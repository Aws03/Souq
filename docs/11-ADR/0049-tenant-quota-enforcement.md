# ADR-0049: A per-tenant quota is enforced by a counter row under an explicit lock — not by the write-first-then-recount pattern this repository already uses

- **Status:** Accepted as the design, 2026-09-20. **Not implemented.** It also decides how TD-68 closes, because the existing administrator guard and any future quota share one invariant. Supersedes nothing.
- **Date:** 2026-09-20
- **Related modules:** Platform, Identity, Catalog, Shopping — every module that would carry a countable limit
- **Related ADRs:** [ADR-0013](0013-optimistic-concurrency.md) (rowversion on contended aggregates), [ADR-0021](0021-transaction-boundaries.md) (the use case owns the unit of work), [ADR-0007](0007-database-strategy.md) (EF Core as the only schema source), [ADR-0047](0047-commercial-control-plane.md) (why the counter is a store-owned table)

## Context

A commercial plan carries numeric limits — products, staff seats, storage, orders per month. Enforcing one means
answering "is this store at its limit?" correctly while two requests race.

The repository already contains one count guard that is correct, and three that are not, and the difference
between them is subtle enough that copying the wrong one is the likely failure.

**The correct one** is `AccountStatusChanger.SetActiveAsync` (the F-7 fix). Its order is load-bearing and is the
reverse of the obvious: it **disables the account, saves, and only then re-counts inside the same transaction**,
throwing a private exception to roll everything back. That works because the write takes the exclusive lock
first, so the racer's count must wait for it and therefore reads committed truth rather than a stale snapshot.
Its own comment records why `SERIALIZABLE` was rejected (a range scan then an exclusive request — a deadlock
ending in a server error instead of a clean refusal) and why a named application lock was rejected (a new
concept to solve what the transaction already solves). The deadlock it does provoke is translated into a clean
business refusal.

**The incorrect ones** are `SearchSynonym.MaxPerStore` and `WishlistItem.MaxItemsPerCustomer`, which count and
then insert with no transaction and no re-count, so two concurrent creates at the limit both pass. They are soft
caps where that does not matter. `Basket.MaxLines` and `Product.MaxVariants` are not analogues either: they are
protected by an aggregate root's `rowversion`, and a per-tenant quota has no such root.

## Problem

1. Which pattern should a commercial quota use?
2. TD-68 records that the correct pattern depends on an isolation-level invariant nothing asserts. Does that
   matter enough to change the pattern, or is asserting the invariant sufficient?

## Options considered

### A — Copy the write-first-then-recount guard

**Rejected, on three findings.**

1. **It depends on an invariant nothing asserts, and the invariant is not universal.** It is safe only because
   the default isolation level is *locking* read-committed. Under Read Committed Snapshot Isolation the re-count
   reads a snapshot, never blocks, and **both racers pass**. A repository-wide search for
   `READ_COMMITTED_SNAPSHOT` returns exactly one hit — the comment in that guard. Nothing sets it, no migration
   asserts it, no startup check reads it, and it is **on by default** on a managed database this repository's own
   backup documentation names as a possible target. The failure is silent and open: no exception, no log line,
   and a green suite.
2. **Its test does not exercise it.** `LastAdministratorConcurrencyTests` seeds the store with one administrator,
   creates two more, signs in as the seeded one and disables the other two. The issuer cannot disable himself, so
   the count can only fall from three to one; the in-transaction re-count tests for zero and can never fire. The
   assertion passes by arithmetic and would stay green with the entire guarded block deleted. What the test does
   prove is narrower and still useful — that the concurrent pair never yields a 500.
3. **It deadlocks by design.** That is acceptable for a rare administrative action translated into a refusal; it
   is a poor foundation for a check on a hot creation path.

### B — `SERIALIZABLE` transactions

Rejected for the reason already recorded in the existing guard: the count scans a range, so two racers take
shared range locks and then request exclusive ones. The deadlock rate is the point of the pattern rather than an
edge case, and the caller gets a server error instead of a clean refusal.

### C — A named application lock

Rejected. It is engine-specific, it introduces a new concept to solve what a transaction already solves, and its
release-on-failure behaviour becomes a second thing to reason about.

### D — Optimistic concurrency on the tenant row

Rejected. EF Core's concurrency exception is generally not raised when *adding* entities, so a rowversion on
`Tenant` does not serialise two concurrent creations of a child row. (The counter's own rowversion *would* work,
because incrementing it is an update — which is option E by another name.)

### E — A per-tenant counter row, incremented under an explicit lock inside the caller's transaction

**Chosen.**

## Decision

**A per-tenant counter row, taken with an explicit lock inside the caller's transaction, behind one
Application-owned port.**

|  | Write-first-then-recount | **Counter row (chosen)** |
|---|---|---|
| What serialises racers | the lock on the row just written | the lock on one per-tenant counter row |
| Depends on the isolation level? | **Yes** — RCSI defeats it silently | **No** — an update takes an exclusive lock regardless |
| Deadlock class | by design, translated into a refusal | none; contenders queue on one row in one order |
| Failure mode when wrong | fails **open**, no error, green tests | drift, if deletions do not decrement |

`OrderNumbers` is the existing proof that this shape works here — a single per-tenant row incremented inside the
caller's transaction, where the exclusive lock is held to commit so a concurrent order in the same store waits a
moment and a different store is untouched.

**Four obligations come with it, and none may be skipped.**

1. **One port, one implementation, one test.** *ITenantQuotaGuard* in Application with a single Infrastructure
   implementation, plus an architecture test in the existing shape forbidding any other count-then-insert. The
   present defect is not that one guard is wrong; it is that nothing asserts the rule.
2. **The counter must be kept truthful.** `OrderNumbers` explicitly tolerates gaps because it only ever counts
   up; a quota must count **down** on deletion, archival and erasure. That needs a reconciling sweep, and the
   sweep is part of the phase, not a follow-up.
3. **The bulk-write allow-list grows by one, deliberately.** `ExecuteUpdate` bypasses `SaveChanges` and
   therefore both the tenant write guard and the audit-timestamp interceptor. A counter type must be added to
   the reviewed list by name with a written reason, and — because it is a store-owned table — the tenant filter
   is what keeps the statement safe.
4. **A test that can actually fail.** The concurrency test must arrange exactly enough holders that a broken
   guard reaches the forbidden state, with a third actor issuing the requests. The existing administrator test is
   the counter-example, not the template.

**TD-68 closes here too, and not only with the counter.** The existing administrator guard still depends on the
isolation level whether or not quotas do, so a startup check reads the database's own
`READ_COMMITTED_SNAPSHOT` setting and warns loudly — in the shape `DatabasePrivileges` already uses to report
the runtime identity from the database at every boot, rather than a test that could only ever assert the test
container.

## Consequences

**Good.** Quota correctness stops depending on a database setting that a managed host enables by default. There
is no deadlock class on a creation path. The mechanism is one named port with one implementation and a rule that
fails the build, which is what the repository lacked. Closing TD-68 becomes a by-product rather than a separate
task.

**Costs.** A counter is state that can drift, and the reconciling sweep is real work that the re-count pattern
does not need. Every quota check becomes a write, so a read-only page that merely *displays* remaining quota
must read the counter without taking the lock. A quota guard adds an explicit-transaction site, which matters to
F-14: enabling retry-on-failure later requires every such site to be wrapped in an execution strategy, and none
of the existing ones is. A commit-time deadlock is still untranslated — `InTransactionAsync` wraps only the
work, not the commit — so any design that defers conflict detection to commit inherits a 500.

**Revisit when** a quota needs to be enforced across instances without a shared database, or when a limit must
be checked far more often than it is consumed, at which point a read-through cache in front of the counter is
the next question — and a cached quota is a correctness question, not an optimisation.
