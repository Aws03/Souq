# ADR-0050: Behavioural events are captured now, with the fields that cannot be reconstructed later — and the identity link is kept separate from the start

- **Status:** Accepted as the design, 2026-09-20. **Not implemented**, and gated on one owner decision (`C-08`: whether a visitor identifier is stored for signed-out shoppers) which changes what may be captured but not the shape. Supersedes nothing.
- **Date:** 2026-09-20
- **Related modules:** Catalog, Reporting, Ordering, Shopping
- **Related ADRs:** [ADR-0042](0042-local-search-engine.md) (the search engine whose analytics this generalises), [ADR-0034](0034-notifications-outbox.md) (why this is *not* the outbox), [ADR-0044](0044-caching-evaluated-not-adopted.md) (the measurement discipline this follows), [ADR-0008](0008-cqrs-strategy.md) (read models only when reporting demands them), [ADR-0047](0047-commercial-control-plane.md) (why these rows are store-owned, not platform-owned)

## Context

M13 shipped `SearchQueryLog` and, with it, a write path whose reasoning is written into the code and is correct:
`ISearchLog.Record` returns `void`, never throws and takes no `Task` — because a signature returning `Task`
invites an `await` on the shopper's search path, and a slow table would then slow search. It deposits into a
bounded channel that **drops** rather than waits, counts and logs every drop, and a background writer batches
each store's rows **inside that store's tenant scope** so the write guard stamps the right owner. The channel is
a singleton and the recorder is scoped, because a scoped service resolved from the root provider has already
broken this codebase's Development boot twice.

That table deliberately holds **no personal data by construction** — no customer id, no IP, no session, no
visitor token — and that is precisely why its 90-day retention could be an engineering decision while TD-16's
retention question waits for a legal answer elsewhere.

It is also a deliberate dead end for everything else. There is no product view, no click, no add-to-cart, no
purchase event, no session and no attribution field anywhere in the system. Related products, recommendations,
funnel analysis and conversion reporting all start from nothing.

Research across three independent vendors' specifications found that the e-commerce event vocabulary has
converged — search, list impression, list click, item view, cart add/remove, checkout begin/step, purchase,
refund — so adopting it couples Souq to none of them. It also found the field that everything depends on, and it
is not the event name: it is **the position an item occupied in the list it was shown in, plus the identity of
that list**. Without it, click-through rate, ranking evaluation and every "customers also viewed" signal are
uncomputable. And a **search-result-set identifier must be minted at query time and echoed back** on the click,
the add-to-cart and the purchase — attribution cannot be recovered later by timestamp proximity.

## Problem

1. What must be captured **now**, because it cannot be reconstructed afterwards?
2. How is it captured without putting a write on the shopper's critical path?
3. What happens to the legal position that made `SearchQueryLog` simple, once an identifier makes events
   joinable?

## Options considered

| Option | Verdict |
|---|---|
| **Do nothing until a feature needs it** | Rejected. Every other item in the commercial plan can be built later at the same cost. This one cannot: a purchase that happened before the event existed can never be attributed to the search that produced it. |
| **Ride the transactional outbox** | Rejected. The outbox exists for at-least-once delivery of facts that *must* arrive; it caps payloads at 4000 characters and **throws inside `SaveChanges`** on an oversized or unregistered message, which would fail the business transaction. Behavioural volume is an order of magnitude larger and loss-tolerant. |
| **Write synchronously to a table** | Rejected for the reason `ISearchLog` already records: it puts an insert on the shopper's path, and measuring something must not slow it. |
| **Send to an external analytics vendor** | Rejected as the system of record. It is a cross-border transfer of personal data, which in the target jurisdiction needs a legal basis Souq does not yet have; and a vendor cannot be removed later if it holds the only copy. A vendor may be added later as an *adapter* over the same events. |
| **Extend `SearchQueryLog`** | Rejected. An integration test asserts that table's exact property set precisely so a personal-data column cannot be added quietly. That guard is working as designed and should not be weakened; a behavioural event is a new table. |
| **Generalise the `ISearchLog` pattern into a new store-owned event store** | **Chosen.** |

## Decision

### 1. One port, the same guarantees, a new table

An Application-owned sink with the same contract as `ISearchLog`: `void`, never throws, no `Task`, bounded
channel with drop-on-full, drops counted and logged, background writer batching **inside each store's tenant
scope**. The rows are **store-owned** (shape A of [ADR-0047](0047-commercial-control-plane.md)) because they are
the merchant's own data, read by the merchant's own dashboards.

Two flaws in the existing writer are fixed while generalising it, not inherited: a failure while saving one
store's batch currently discards **every remaining store's** rows in that cycle, because one try/catch wraps the
whole loop; and the recorder silently records nothing at platform scope.

**One exception is explicit:** metering for billing may **not** use this path. Sampled or dropped telemetry is
unfit for billing, so billable events are durable rows with a deterministic idempotency key.

### 2. An envelope plus a versioned payload

The envelope is stable forever — event id, tenant, name, schema version, occurred-at, received-at, visitor,
session, user, correlation/search-execution id, surface, culture — and the payload is versioned JSON that
consumers read tolerantly. Adding an optional field is not a breaking change; changing a type is, and old rows
keep their old version. **Historical rows are never rewritten.**

### 3. Capture what cannot be recovered

- **Position in list and list identity**, on every impression.
- **A search-execution id** minted at query time, returned with the results, echoed back on every downstream
  event.
- **Denormalise at write time**: price, currency, category, stock status, rank, applied synonyms, the ranking
  version and any experiment variant are copied onto the row, because joining to `Product` at analysis time
  returns today's values and not what the shopper saw.
- **Money follows the Domain** — amount plus currency at the right minor-unit scale, never a float. A float
  would put fils into binary floating point and make a merchant's analytics disagree with their own orders.

### 4. Sessionise at write time

A server-minted session id using the 30-minute inactivity convention that two unrelated vendors independently
landed on. Deriving sessions later from timestamps becomes wrong the moment the definition changes or rows are
purged.

### 5. The identity link is a separate table from day one

Events carry an opaque, server-minted visitor id. A **separate, access-controlled table** maps visitor to
customer. This is the difference between an affordable erasure story and an expensive one: retrofitting
separation onto an append-only table that already embeds customer ids is the documented failure mode.

### 6. Roll up before you purge, and keep the rollups

Raw rows carrying an identifier get a bounded life; identifier-free aggregates — term × day, product × day
impressions/clicks/conversions, item-pair co-occurrence — are kept indefinitely. **The rollup jobs must exist
before the first purge**, because the aggregate cannot be recomputed from rows that are gone.

### 7. Storage starts simple

A single append-only rowstore table keyed by (tenant, occurred-at, event id) with the minimum number of indexes,
each one paid for in write amplification. When dashboard scans start to hurt, add a nonclustered columnstore
over the same table before considering a second datastore. Two indexes from day one, as `SearchQueryLogs`
already demonstrates: one for the read shape, one for the purge.

### 8. The legal position changes, and that is the owner's call

`SearchQueryLog` is free of personal data *by construction*, which is why its retention was engineering's to
decide. **A visitor identifier ends that.** The design does not smuggle one in: `C-08` asks explicitly whether
one is stored for signed-out shoppers, and if the answer is no, the consequence — no funnel analysis, no
search-to-purchase attribution, no behavioural recommendation, ever — is recorded rather than left open.

Worth stating plainly either way: "no personal data" is a claim about **columns**, not content. The existing
`Term` column stores raw shopper text verbatim for 90 days, and a shopper can type an email address into a
search box.

## Consequences

**Good.** The capability to measure and to recommend is preserved at its cheapest moment. The write path cannot
slow or break a shopper's request, and that guarantee is in the contract rather than in an implementation. The
merchant's data stays the merchant's, which is also the answer at offboarding. Two real defects in the existing
writer are fixed rather than copied.

**Costs.** A second event pipeline beside the outbox, with a different guarantee (at-most-once) that must be
documented so nobody bills from it. A new personal-data class, with consent, retention, residency and erasure
obligations that today's search log deliberately avoids. Write amplification on every index. The rollup jobs are
real work that must precede the first purge rather than follow it.

**Revisit when** event volume makes a single rowstore table the wrong answer — the trigger is a measured
conflict between analytics scans and the transactional workload, not a row count — or when a merchant contract
requires exporting or erasing behavioural data on a timetable the rollups cannot meet.
