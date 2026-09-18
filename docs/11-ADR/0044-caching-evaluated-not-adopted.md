# ADR-0044: No caching layer is added — the measurement says the database saturates on writes and aggregates a cache would not serve

- **Status:** Accepted, 2026-09-18. Implements the M16 scope item that caching be "evaluated with evidence … **built only if a measurement justifies it**" ([SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md) §2). Supersedes nothing.
- **Date:** 2026-09-18
- **Related modules:** Catalog, Platform, Reporting — the three the roadmap names as cache candidates
- **Related ADRs:** [ADR-0008](0008-cqrs-strategy.md) for the read ports a cache would sit in front of; [ADR-0022](0022-tenancy-enforcement.md), because any cache key in this system is a tenancy question first; [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §5 for the standing rule against infrastructure whose benefit is not measured

## Context

[ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) Phase 21 names two caching candidates — tenant configuration and catalogue reads — and [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) Stage 1 records that both **already have an in-process cache**: the tenant directory and the storefront configuration are cached per process and invalidated on write.

The question M16 asks is whether anything further is justified: a distributed cache (Redis), or a response cache in front of the catalogue.

## Problem

1. Is there a measured read bottleneck a cache would remove?
2. What is actually saturating under load?
3. What would a cache cost in this system specifically?

## Decision

**No caching layer is added.** The existing in-process caches stay; nothing is introduced beyond them.

### 1. The measured bottleneck is not read latency

Steady-state latency on the container stack, after warm-up, at concurrency 1 — the full table is in [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md):

| Route | p50 | p95 | Target |
|---|---|---|---|
| Storefront catalogue | 56 ms | 221 ms | 300 ms |
| Storefront search | 59 ms | 144 ms | 500 ms |
| Merchant dashboard (16 aggregates) | 43 ms | 156 ms | 3000 ms |

Every route meets its target with room. A cache is a fix for latency that hurts; this latency does not. And the one route that *was* pathological — M13's search insights at 25,375 logical reads a call — was fixed by reordering a single index to 107, which is the cheaper and more durable instrument. Reaching for a cache there would have hidden the wrong index behind it rather than correcting it.

### 2. Under load the database saturates, and a cache does not address the reason

At concurrency 16 the API container sat at **0.54% CPU** while SQL Server ran at **47%**. The reads doing that are not the ones a cache would serve:

- The **most-written table in the system is `SearchQueryLogs`**, one row per keyword search. A read cache does nothing for a write load.
- The **dashboard's sixteen aggregates** are per-store, per-window, and change with every order — a cache with a short enough TTL to be correct has close to no hit rate, and one with a long enough TTL to hit shows a merchant stale money.
- The **catalogue** is already the cheapest route and is per-store, per-page, per-filter, per-sort, per-language. That key space is wide, so the hit rate is low exactly when the store is busy enough to matter.

The lever the measurement actually points at is Stage 1's last one and Stage 3: **database capacity**. That is also the cheapest.

### 3. What a cache would cost here specifically

- **Every cache key is a tenancy question.** A key that forgets `TenantId` serves one store's catalogue to another — a data leak with a latency benefit. M16's own scope names this ("confirm a caching layer does not leak one tenant's data into another's cache key space"), and the honest reading is that it introduces a whole new surface where isolation must be proven, alongside the query filter and the write guard that already prove it.
- **Invalidation becomes a correctness problem with no test.** The in-process caches invalidate on write within one process. A distributed cache has to invalidate across processes, and the failure mode is a merchant editing a price and not seeing it — which reads as a bug in the editor, not in the cache.
- **Redis is a new runtime dependency**, and [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) requires a measurement before one. There is none to offer.

## Consequences

- The system keeps two in-process caches and no others. No new dependency, no new key space, no new invalidation path.
- The next performance investment is written down instead: **database capacity first** (Stage 1/Stage 3), and the uncovered `ORDER BY CreatedAt DESC` index on `Products` as the next index to reach for if catalogue latency ever becomes the complaint.
- The absolute figures above come from an emulated, memory-capped stack and are pessimistic. That weakens the case for *adding* infrastructure, not for skipping it: if latency looks acceptable on a deliberately slow stack, it is unlikely to be the first thing that hurts on a real one.

**This decision is revisited when any of these is measured, not anticipated:**

1. A read route misses its p95 target on representative hardware with the database already scaled.
2. The catalogue's cache key space narrows enough for a hit rate to be plausible — for example, if an unfiltered first page becomes the overwhelming majority of storefront traffic, which is measurable from the request log today.
3. Stage 2 arrives (several API instances), at which point the in-process caches become per-instance and their behaviour needs re-deciding anyway — that is the natural moment to ask this question again.

## Alternatives considered

- **A response cache in front of the catalogue.** Rejected for now: the key space is wide, the measured latency is already inside target, and a per-store response cache is a tenancy surface with no test behind it yet.
- **Redis for the tenant directory and storefront config.** Rejected: both are already cached in process and neither appears in the measured cost. This becomes a real question at Stage 2, not before.
- **Materialising the dashboard aggregates.** Rejected: the dashboard is 43 ms at p50 and a merchant opens it a few times a day. Materialisation would trade a measured non-problem for a staleness problem in the numbers a merchant trusts most.
