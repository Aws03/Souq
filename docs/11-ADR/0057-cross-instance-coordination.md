# ADR-0057: Cross-instance coordination — a lease table and a shared generation, not a lock server

- **Status:** Accepted 2026-09-22, and **implemented by `C4`** on the same day — both halves: the sweep lease and cross-instance cache invalidation. Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Platform, Billing, Reporting, Inventory, Shopping
- **Related ADRs:** [ADR-0034](0034-notifications-outbox.md) (the outbox's per-message lease, whose mechanism this generalises), [ADR-0049](0049-tenant-quota-enforcement.md) (why a read-then-write is not a guard), [ADR-0022](0022-tenancy-enforcement.md) (no raw SQL outside migrations), [ADR-0047](0047-commercial-control-plane.md) (the three table shapes), [ADR-0056](0056-platform-invoices-and-manual-collection.md) (`C6`'s dunning sweep is the caller that makes this urgent)

## Context

Everything in this repository has assumed a single API instance. `StoreSweepService` said so in its own header — "a second instance is safe but does duplicate work" — and that was true while the sweeps only expired reservations and cleaned baskets.

`C5` changed the stakes and `C6` will change them again. [CommercialPlatformArchitecture.md](../12-ROADMAP/CommercialPlatformArchitecture.md) §4.18 puts the ordering plainly: **cross-instance invalidation and a distributed lock must land before automated dunning**, because dunning is the first place the platform takes an irreversible action against a paying customer without a human in the loop.

The half-truth in that old header is worth naming, because it is what actually decided this. The sweeps *are* idempotent — running one twice corrupts nothing. But `QuotaReconciliationService` logs a **warning** whenever it corrects drift, and that warning means "some path is not reporting its usage". Two instances counting the same rows at the same moment produce that warning with no drift behind it. Duplicate work does not corrupt the data; it corrupts **the signal**, and a false alarm in a quota counter is chased for days.

## Problem

1. How does exactly one instance run a periodic job, without a coordination service?
2. What happens when the instance holding that right dies mid-job?
3. How is the guard kept from becoming the read-then-write race it exists to prevent?

## Options considered

### A — A leader election across instances (one node runs all background work)

**Rejected.** It needs either a consensus protocol or a coordination service, and it couples unrelated jobs: the leader runs every sweep, so a slow reconciliation delays basket cleanup on a platform that has no reason to serialise them. It also makes the failure mode worse — losing the leader stops all background work at once rather than one job.

### B — `sp_getapplock`

**Rejected, though it would work.** SQL Server's own application locks are exactly this primitive, with real blocking semantics and automatic release on disconnect. They are also **raw SQL**, which `لا_SQL_خام_في_Infrastructure_خارج_الهجرات` forbids outside migrations ([ADR-0022](0022-tenancy-enforcement.md)) — and that rule is right: a lock expressed as a string is invisible to the tenant filter and to everything that reads the code. Reaching for a documented exception to get a lock, when a table does the job with the mechanism this repository already trusts, is a bad trade.

### C — Redis, or any coordination service

**Rejected, and the record already says why.** A distributed cache is an explicit non-goal ([ExplicitNonGoals.md §9](../02-ARCHITECTURE/ExplicitNonGoals.md)), as is a message broker (§3). Introducing an operational dependency for one lock buys nothing a four-column table does not.

### D — A lease table, claimed by a conditional update

**Chosen.** It is the outbox's mechanism ([ADR-0034](0034-notifications-outbox.md)), generalised — and that mechanism has been in production use here since Phase 14 and was audited in Phase 17.

## Decision

### 1. The claim is one atomic statement, never a read followed by a write

```sql
UPDATE DistributedLeases SET Owner = @me, AcquiredAt = @now, ExpiresAt = @until
WHERE  Name = @work AND (ExpiresAt IS NULL OR ExpiresAt <= @now OR Owner = @me)
```

One row updated means the lease is ours. The predicate and the write are in the same statement, so there is no window between "it looked free" and "I took it" — decomposing it into a read, a decision and a write would reproduce exactly the race the lock exists to prevent, which is the mistake [ADR-0049](0049-tenant-quota-enforcement.md) was written about.

`Owner = @me` in the predicate is what makes renewal free: the holder extends its own lease with the same statement, and a non-holder cannot extend what it does not hold.

First use has no row. The caller inserts one, and a concurrent insert loses to the **unique index on `Name`** — caught as `UniqueConstraintViolationException` and retried. That exception type is deliberate and must not be widened to `DbUpdateException`: `AppDbContext` translates 2601/2627 into a type deriving from `Exception` directly, and getting this wrong is precisely the defect `OrderNumbers` carried.

### 2. Leases expire, and expiry is the recovery mechanism

An instance that dies holding a lease never releases it. If the lease were permanent, that job would stop until a human intervened. So every lease has a duration, the holder renews it while it works, and expiry frees it with no intervention. `DistributedLease` therefore has no "held forever" state and `IDistributedLock` exposes no blocking acquire.

**`TryAcquireAsync` never waits.** A caller that does not get the lease returns and tries on the next tick. Periodic work does not need a queue; it needs not to run twice.

### 3. One lease per job, not one lease for all background work

The lease name is derived from the sweep's own name (`sweeps.store.basket-cleanup`), so quota reconciliation cannot block search-log purging. They contend for nothing. What must be prevented is *the same* sweep running in two instances.

### 4. The holder renews between stores, and stops if it loses the lease

A sweep over many stores can outlast its lease. `RenewAsync` returns `bool` rather than throwing, because losing a lease is an ordinary event — a slow cycle, an expiry, another instance taking over — and the correct response is to **stop**, not to continue. Ignoring that return value is exactly how a job runs twice while believing it is guarded.

### 5. Instance identity is a fresh GUID per process

Not the hostname, not the process id. Two containers can share a hostname and a restarted process can reuse a pid; two instances that believe they are the same holder is worse than having no lock at all.

## Consequences

- **`C6` is unblocked.** Its dunning sweep was the reason for the ordering, and the last dependency it was waiting on is now in place.
- **One additive table**, `DistributedLeases`, with a unique index on `Name`. Nothing else changes shape.
- **The outbox is deliberately left alone.** Every message already carries its own lease, so it is safe with two dispatchers by construction; putting it behind one lock would turn two dispatchers into one and slow delivery for safety it already has.
- **`Souq.Infrastructure` now exposes internals to `Souq.IntegrationTests`**, so the test can construct the lock with two different instance identities — which cannot be done through DI, because identity is a singleton per process. Widening the type to public to serve a test would have been worse; `Souq.Domain` sets the precedent.
- **The race is tested on real SQL Server and mutation-checked.** Ten simulated instances contend for a lease whose row does not exist yet, released through a barrier so they genuinely start together, and exactly one wins. Deleting the predicate makes four of the five tests fail — so the suite measures the guard rather than the happy path, which is the obligation [ADR-0049](0049-tenant-quota-enforcement.md) §4 records.

## Decision, part two: cache invalidation is a shared generation counter

Both per-process caches already invalidate the same way: `TenantDirectoryCache` and `SessionStampCache` each hold a **generation counter** that is part of every cache key, and "invalidate" means incrementing it — `MemoryCache` cannot enumerate its keys, so discarding what cannot be counted is done by *orphaning* it rather than deleting it.

That existing design is what made this cheap. Making invalidation cross-instance does not need a new mechanism; it needs the generation to be **shared**.

### 6. A `CacheSignals` row per cache, incremented atomically, polled by every instance

`UPDATE CacheSignals SET Version = Version + 1 WHERE Name = @n` — one statement, for the same reason the lease claim is one statement. A read-then-write would let two instances invalidating at the same moment write the same value, losing one bump; a third instance that saw the intermediate value would then never learn the second change happened, which is exactly the staleness this exists to end.

`CacheSignalWatcher` polls every **5 seconds** (`Coordination:CacheSignalPollSeconds`, 0 disables it) and bumps the matching local generation when a version has grown. Its first tick records a baseline and invalidates nothing: an instance starts with an empty cache, so invalidating it would only make every deployment begin with a pointless query storm.

**The watcher is the one background job that must run on every instance, and therefore takes no lease.** What it does is purely local — updating the memory of its own process. Putting it behind a lock would mean one instance refreshes and the rest stay stale forever, which is the opposite of the goal.

### 7. Invalidation happens locally first, then publishes

`ITenantDirectory.InvalidateAsync` and `ISessionValidator.ForgetAllAsync` became asynchronous because publishing is a database write. Both bump the local generation **before** publishing: publishing first would leave a window in which the instance that made the change still serves a snapshot it knows is stale — and it is the one instance that knows for certain.

A failed publish is allowed to propagate rather than be swallowed. The call happens after the change is committed, so silence would leave every other instance on the old value until TTL with nobody aware.

### 8. Per-user session forgetting stays local, deliberately

`Forget(userId)` — password change, reset, account disable — is **not** published. A global signal per password change would discard every account's cached stamp on every instance to serve one account. The window on other instances stays the cache's own 30 seconds, and that is written where the method is declared so it is known rather than discovered.

`ForgetAllAsync` **is** published, because its only caller is mass revocation when a store is archived or suspended — precisely the case where a session surviving 30 seconds on another instance is a security difference rather than a performance one, and precisely what `C-17` = B promises.

### 9. The rate limiter is a counter, not a cache — and it is not fixed here

[CommercialPlatformArchitecture.md](../12-ROADMAP/CommercialPlatformArchitecture.md) §4.18 lists the rate limiter as the third per-process cache. **That classification is wrong, and the correction matters**: invalidation makes instances agree on what they have *read*; a rate limit is a counter they would have to *share*, and sharing it means a read and a write in a common store on the path of every request. A relational database is the wrong tool for that, and a distributed cache is a recorded non-goal.

What is offered instead is an honest approximation: `RateLimiting:InstanceCount` divides each configured limit, floored at 1 so a bad value can never close an endpoint. It defaults to **1**, so nothing changes for anyone running a single instance — which is every deployment today. With an even load balancer the aggregate approaches the intended limit; with an uneven one a request may be refused that should have passed. That is a trade recorded, not a problem solved.

## What this does not do

- **It does not make the rate limiter exact across instances** — see §9. It approximates, and says so.
- **It does not close the staleness window; it shrinks it** from 60 seconds to about 5 for the tenant directory, and from 30 to about 5 for mass session revocation. Closing it entirely would mean a read per request, which buys nothing here.
- **It does not publish per-user session forgets** — see §8.
- **It does not move the sweeps off `StoreSweepService`.** `C6`'s dunning is platform-scope by its own design and will take a lease directly.
- **It does not deliver blob storage**, the third part of `C4`, which stays blocked on `D-18`.

## Migration

`20260922034133_DistributedLeases` and `20260922040528_CacheSignals` — both additive: one table and one unique index each. No column dropped or narrowed, no row deleted.
