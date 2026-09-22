# ADR-0057: One instance does the sweeping — a lease table, not a lock server

- **Status:** Accepted 2026-09-22, and **implemented by `C4`** on the same day for the lock half. The cache-invalidation half is the same phase and is recorded separately below. Supersedes nothing.
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

## What this does not do

- **It does not make the rate limiter correct across instances.** That is a shared-counter problem on a request-hot path, not an invalidation problem, and it is recorded in §Related work below rather than pretended away.
- **It does not invalidate the per-process caches.** That is the other half of `C4` and lands separately.
- **It does not move the sweeps off `StoreSweepService`.** `C6`'s dunning is platform-scope by its own design and will take a lease directly.

## Migration

`20260922034133_DistributedLeases` — additive: one table, one unique index. No column dropped or narrowed, no row deleted.
