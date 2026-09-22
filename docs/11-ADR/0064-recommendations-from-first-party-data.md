# ADR-0064: Recommendations from data the store already owns

- **Status:** Accepted 2026-09-22, implemented the same day, closing `C10`. Answers `C-09` in **portfolio mode** (and, this record argues, for any deployment).
- **Date:** 2026-09-22
- **Related modules:** Catalog, Ordering
- **Related ADRs:** [ADR-0050](0050-behavioural-event-foundation.md) (the behavioural foundation this deliberately does *not* depend on), [ADR-0008](0008-cqrs-strategy.md) (read services and projection)

## Context

`FindRelatedProductsAsync` has existed since Phase 5 with a route and a frontend surface. It returned products from the same category ordered by best-selling, padded with the newest from other categories, and it said nothing about *why*.

`C10` was blocked on `C-09` — may behavioural data be pooled across tenants — and on `C9`'s capture having accumulated data. Behavioural capture ships **off** in this repository and stays off: no lawful basis is claimed for a public demonstration, so nothing accumulates, so anything waiting on it waits forever.

That is the trap worth naming. A recommendation feature designed around behavioural data is, in this deployment, a feature that never runs.

## Problem

1. What can be recommended using only data the store legitimately owns?
2. How is a weak signal kept from being presented as a strong one?
3. What does the shopper see when there is not enough data?

## Options considered

### A — Wait for behavioural capture

**Rejected.** It is waiting for a decision that portfolio mode answers in the negative. It also mistakes where the strongest signal is: a delivered order is money paid and goods received, which is a far better statement of "these go together" than a click.

### B — Content similarity only (attributes, category, price band)

**Reasonable, and it is what the plan proposes first**, because it is correct on day one for a brand-new tenant. But a store *with* order history has something better and would be ignoring it.

### C — Co-purchase from delivered orders, then content, then newest — each labelled

**Chosen.** It uses the strongest available signal when it exists, degrades honestly when it does not, and tells the shopper which is which.

## Decision

### Three layers, strongest first, each carrying its reason

1. **`BoughtTogether`** — products appearing in the same **delivered** orders. Not paid, not shipped: delivered. It is the most conservative status available and the only one where the transaction is actually finished.
2. **`SameCategory`** — best-selling first, the previous behaviour, now named.
3. **`NewArrival`** — what remains for a young store, and it says *new arrival* rather than *recommended for you*.

`RecommendationReason` is a closed enum for the same reason every other allowlist in this repository is closed: the value reaches the shopper, and free text there becomes a claim no calculation supports.

### A minimum support of two, which is the whole difference between a signal and a coincidence

One order containing two products does not make them related — someone bought a phone case and a kettle. Requiring two independent delivered orders is a low bar that nonetheless excludes the single-coincidence case, and it is mutation-checked: lowering it to one makes a test fail.

This threshold is deliberately a constant rather than a setting. A per-store knob would be a per-store claim about statistical confidence, which nobody is in a position to tune.

### Cross-tenant pooling is refused structurally, not by a predicate

The co-purchase aggregate reads `_db.Orders`, which carries the tenant query filter. **It cannot see another store's orders** — not because a `WHERE` clause was remembered, but because the read is inside the filter. That is `C-09`'s answer ("never pool") enforced by construction.

This record argues that answer is right beyond portfolio mode: it is correct by construction, simplest to put in a contract, and every tenant starts cold under either answer anyway.

### The reason travels on the product, and is shown

`ProductDto.Reason` is null everywhere except a recommendation, so its absence means "this is not a recommendation list" rather than "the data is missing". The card renders a muted caption when it is present.

## Consequences

**Good**

- Recommendations work on day one, on a store's own data, with behavioural capture off — which is its permanent state here.
- The shopper is told *why*, and the three reasons are honestly different in strength.
- A new store degrades to "new arrival" instead of claiming a recommendation it cannot support.
- No new table, no rollup, no migration. One query method changed and no client contract broke.

**Costs, honestly**

- **Co-purchase is a count, not a model.** There is no lift, no normalisation for popularity, and a bestseller will co-occur with everything. With a real catalogue this is where the next iteration is needed, and the plan's "rescaled measure" is exactly that.
- Ordering by raw co-occurrence means the most popular partner wins ties by id, which is stable but arbitrary.
- The Catalog module reads `Orders` directly. That boundary crossing is **pre-existing and documented** — `BestSellingFirst` has done it since Phase 5 — and this change leans on it rather than fixing it. It belongs behind an Ordering contract, and it is still not.
- No click-through measurement, because that needs the behavioural surface that is off. The slot id and impression list the plan asks for would be written and never read.

## Deliberately out of scope

- **Cross-tenant pooling**, refused above.
- **Attribute-level similarity** (shared options, price band, brand). The category is a coarse proxy; the finer version needs a similarity measure and a way to evaluate it, and evaluating it needs traffic this deployment does not have.
- **A per-tenant readiness state.** With three layers that always return something, there is no state where the feature is unavailable — the reason label already communicates the weak case.
