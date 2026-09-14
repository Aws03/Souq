# Scaling strategy

> **What this page is:** the order in which Souq should grow, what triggers each step, and what it costs. Every stage past the first is an **option**, not a plan — nothing here is implemented beyond Stage 1.
> **The rule:** scale in this order and stop as soon as the problem is solved. Skipping ahead buys operational cost without buying capacity.
> **Related:** [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) (why the heavy options are not adopted) · [ADR-0012](../11-ADR/0012-service-extraction-strategy.md) (extraction order) · [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) (the per-store database seam) · [Deployment.md](Deployment.md) (how it runs today)

## Stage 0 — Measure first (always)

Before any stage below: know which requests are slow, and why. Today the API logs one line per request with a correlation id, the use-case name and its duration ([ADR-0018](../11-ADR/0018-observability.md)), and `QueryServiceTests` guards against N+1 queries on the main list screens.

**Missing today (do this before scaling):** no metrics backend, no tracing, no alerting, no load test. A capacity decision without a measurement is a guess. Adding application metrics and a load test of checkout is the cheapest first investment.

## Measured: the "best selling" ranking (F-17)

The storefront home page asks for `sortBy=BestSelling` on every anonymous visit, and that ranking sums
quantities across the store's entire delivered order history. It was recorded as needing measurement rather
than a guess. Here are the numbers.

**Method.** The real application over a real SQL Server, on a database created and dropped for the run,
seeded to the stated size; seven requests per sort, median reported, after a warm-up request.
`BestSellingPerformanceTests` is the harness.

| Catalogue | Orders | Order items | Default sort (median) | Best-selling (median) | Ratio |
|---|---|---|---|---|---|
| 200 products | 2,000 | 4,000 | 8.8 ms | 96 ms | 11× |
| 200 products | 20,000 | 40,000 | 24.6 ms | 539 ms | 22× |

**Read the ratio, not the milliseconds.** These runs used an `amd64` SQL Server emulated on an `arm64`
laptop, so the absolute figures are pessimistic and varied between runs (one repetition of the large case
reported a 1.2 s worst sample). The ratio between the two sorts, measured in the same process against the same
data, is the part that is stable — and it grew from 11× to 22× when order history grew ten-fold while the
catalogue stayed the same size. **The cost tracks order history, not catalogue size.**

**Two candidate fixes were measured and rejected, which is what makes the conclusion useful.**

- *A covering index* on `OrderItems (TenantId, ProductId) INCLUDE (OrderId, Quantity)` — the existing index
  lacks both included columns, so this looked like the obvious fix. Measured: no decisive improvement
  (298–414 ms with it against 330–539 ms without, inside the run-to-run noise).
- *Rewriting the LINQ* to pre-aggregate with a `GROUP BY` and join, instead of a correlated subquery per
  product. Measured on an isolated server with `SET STATISTICS IO`: **both shapes produce identical logical
  reads** (OrderItems 146, Orders 47). SQL Server's optimiser already collapses the correlated form, so the
  rewrite would change the source and nothing else.

**What it actually costs.** Stripped of emulation and of the product projection, the ranking is ~195 logical
reads and ~20 ms of CPU at 40,000 order items — a linear scan of the delivered items, once, not once per
product. That is why an index cannot fix it: the work is proportional to how much has been sold, and no
ordering of that data makes the sum cheaper.

**Decision: acceptable for launch, with a defined trigger.** A first store's order history is in the hundreds,
where this is roughly 100 ms on emulated hardware and less on real hardware. It is not worth denormalising the
order lifecycle before a single customer exists, and caching would hide the growth rather than remove it.

**Revisit when any of these becomes true**, whichever comes first:

1. A store passes **~20,000 delivered orders**, or
2. the home page's server time exceeds **200 ms** in production, or
3. `/api/products` starts taking meaningful anonymous traffic (it has no rate-limit policy — see F-18's
   interaction with deep paging).

**The fix when the trigger fires** is a precomputed total — a *UnitsSold* counter per product maintained when
an order reaches Delivered, plus a backfill — which turns the scan into one indexed read. Not Redis: the data
is small, store-scoped, and already lives in the database that must be consulted anyway.

Until then `BestSellingPerformanceTests` guards the shape rather than the timing: it fails if the ranking ever
degrades into a query per product, which is the regression a well-meaning refactor would actually introduce.

## Stage 1 — One instance, one database *(CURRENT)*

What runs today: one API container, one SQL Server, one nginx container serving the built frontend ([Deployment.md](Deployment.md)).

Headroom before anything else is needed:

| Lever | What to do | Notes |
|---|---|---|
| Indexes | Add an index for a slow query; verify with the query plan | Per-store data means most queries filter on `TenantId` first |
| Projections | Narrow a `Select`; stop loading aggregates for reads | Pattern already in place ([CQRS.md](../02-ARCHITECTURE/CQRS.md)) |
| Paging | Keep every list paged; `PagingRules.MaxPageSize` is 100 | Enforced by `PagedQueryValidator` |
| Caching in process | Tenant directory and storefront configuration are cached per process and invalidated on write | Correct while there is one instance — see Stage 2 |
| Container size | More CPU and memory for the API and the database | Cheapest possible step |

## Stage 2 — Several API instances behind a load balancer

- **Trigger:** CPU-bound API with a healthy database; or a need for zero-downtime deploys and failover.
- **Benefit:** horizontal request capacity; rolling restarts.
- **The application is already stateless for requests:** access tokens are JWTs, refresh tokens live in the database, no session affinity is required.
- **Blockers to clear first (all real today):**

  | Blocker | Why it breaks with several instances | Fix |
  |---|---|---|
  | **Uploads on local disk** | An image uploaded on instance A is missing on instance B | Move `IFileStorage` to blob storage (roadmap Phase 23); the port already exists |
  | **In-process caches** | A store's settings change on A; B serves the old branding until it evicts | A short TTL, a shared cache, or an invalidation signal — decide with an ADR |
  | **In-memory rate limits** | Limits become per-instance, so the effective limit multiplies by instance count | A distributed limiter, or enforce at the edge |
  | **Recurring jobs** | Checkout expiry and basket cleanup would run on every instance: safe (each operation is idempotent and guarded) but wasteful | Leader election or a distributed lock (deferred, [ADR-0026](../11-ADR/0026-inventory-reservations.md)) |
  | **Outbox dispatch** | Already safe: rows are claimed under a two-minute lease with a conditional update | None |
- **Cost and risk:** a load balancer pointed at `/health/ready` (which exists, and already reports a stale schema as not ready); deployment becomes rolling; log aggregation becomes necessary rather than nice.

## Stage 3 — Database capacity

- **Trigger:** the database is the bottleneck after indexes and projections are right.
- **Steps, cheapest first:**
  1. Vertical scale (more CPU, memory, faster storage).
  2. Split the connection for reporting reads onto a **read replica** — only for reads with a documented staleness tolerance. Checkout, stock and payment reads must stay on the primary.
  3. Archive cold data (old outbox rows are already purged; orders and audit entries grow forever today).
- **Risk:** a replica silently serving a write-path read produces wrong stock or double spending. Route explicitly, per query service, never globally.

## Stage 4 — A dedicated database for a large or regulated store

- **Trigger:** a contract demanding physical isolation, or one store large enough to distort the shared workload.
- **How it stays possible:** the tenant directory resolves a store before any data access, so a per-store connection can be injected there without touching business code ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md)).
- **Cost:** migrations must run per database; platform-wide reporting must fan out; backup and restore multiply.
- **Requires:** an ADR covering routing, migration orchestration and cross-store reporting.

## Stage 5 — Publish outbox messages to a broker

- **Trigger:** a **second** consumer of the same facts — an extracted service, a data platform, a partner webhook — or dispatch volume the database cannot keep up with.
- **Why it is nearly free when the time comes:** messages already carry references only, with an allow-listed type registry, written inside the business transaction. The dispatcher would publish instead of handling in-process ([Events.md](../02-ARCHITECTURE/Events.md)).
- **Cost:** a broker to run, schema versioning, consumer idempotency, and a second failure mode to operate.
- **Requires:** an ADR. A broker with one consumer is cost without benefit.

## Stage 6 — Extract a module into a service

- **Trigger:** a capability with a genuinely different scaling, reliability or compliance profile, or several teams blocked by one deployment ([ADR-0012](../11-ADR/0012-service-extraction-strategy.md)).
- **Order of candidates:** Notifications first (it only reacts to messages), then Payments (PCI scope), then Search or Media (derived data), then Inventory (needs a saga, since checkout calls it synchronously today).
- **What extraction costs:** the checkout transaction becomes a saga with compensation; cross-module reads become network calls; deployments, tracing and on-call multiply.
- **What keeps it cheap:** module contracts shaped as reserve → commit/release, no cross-module foreign keys, snapshots instead of joins.
- **Requires:** an ADR per service naming its data, its contract and the consistency it gives up.

## What to do first, in practice

1. Add metrics, a trace of checkout, and a load test. Decide from data.
2. Fix the Stage 2 blockers in this order: blob storage for uploads, then cache invalidation, then rate limiting.
3. Only then add instances.

Everything beyond that should be a response to evidence, recorded as an ADR.
