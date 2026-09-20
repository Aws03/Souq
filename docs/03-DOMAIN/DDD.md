# Domain-driven design in Souq: what is used, and what is not

> **In one sentence:** Souq uses DDD's *strategic* idea (boundaries around capabilities) everywhere, and its *tactical* patterns only where invariants are rich enough to pay for them.
> **Related:** [ADR-0009](../11-ADR/0009-ddd-usage.md) (the decision) · [EngineeringMentalModel.md](../00-START-HERE/EngineeringMentalModel.md) (how to apply it) · [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) · [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md)

This page is deliberately honest about the gaps: claiming "full DDD" when the code does something simpler is how documentation stops being trusted.

## 1. Strategic: modules are bounded contexts

Each of the fourteen modules is a boundary inside which a word has one meaning. "Customer" means a commercial profile in Customers and a `CustomerId` reference in Ordering; "product" means the sellable definition in Catalog and a frozen line snapshot in an order. Modules communicate through contracts and ids, never by sharing a model ([Modules.md](../04-MODULES/Modules.md)).

What is **not** done strategically: there is no context map with translation layers, and no per-module ubiquitous-language glossary beyond [Glossary.md](../00-START-HERE/Glossary.md). With one team and one deployment, the module catalog does that job.

## 2. Tactical patterns that are used

### Entities and aggregates

An aggregate is a cluster that changes as one unit and is entered only through its root. Souq keeps them **small**: an aggregate holds what must stay consistent within one transaction, and references everything else by id.

| Aggregate root | Contains | Key invariants | Concurrency |
|---|---|---|---|
| `Tenant` (Platform) | domains, settings, module flags | status lifecycle; exactly one primary domain; a host is unique platform-wide | `rowversion` |
| `User` (Identity) | refresh tokens | normalized email unique per store; platform accounts have no store; lockout; token rotation with reuse detection | `rowversion` |
| `Product` (Catalog) | options with values, variants with their option values, images, translations | slug and SKU unique per store; exactly one default variant, always active; at most 3 options, 20 values, 100 variants; each variant a complete, unique combination; a used value never removed; a variant sold only within its own product, deactivated never deleted; status lifecycle; price in the store currency | `rowversion`, forced on option and variant edits (`GuardConcurrentEdit`) |
| `Category` (Catalog) | translations | no cycles; bounded depth; slug unique per store | — |
| `InventoryItem` (Inventory) | — (movements are separate append-only rows) | `available = on hand − reserved ≥ 0`; every change writes exactly one ledger row | `rowversion` (hot row) |
| `Basket` (Shopping) | lines | quantity within bounds; one basket per customer or guest token | last write wins (low value) |
| `Order` (Ordering) | items, status history | the transition table; items only while Pending; snapshots immutable after placement; discount ≤ subtotal | `rowversion` |
| `Coupon` (Promotions) | — (redemptions are separate rows) | value ranges; validity window; global and per-customer limits | `rowversion` |
| `Payment` (Payments) | refunds | refunded ≤ captured; idempotency key unique | `rowversion` |
| `Customer` (Customers) | addresses | one profile per account per store; at most one default shipping and billing address; blocked customers cannot order | none today |
| `Review` (Reviews) | — | rating range; verified purchase; one per customer per product; moderation states | — |

**Why `Order` stays small:** it owns its lines and its own history, and nothing else. Payments, shipments, reviews and the customer are separate aggregates referenced by id. Putting "everything about an order" inside it would make every payment webhook and every review lock the order row — the classic oversized-aggregate mistake.

**Where Souq bends the textbook, deliberately:** one transaction may modify several aggregates. Checkout writes an order, a coupon redemption and a stock reservation in one unit of work. In a monolith that is the correct trade: correctness now, at the cost of a future saga if a module is extracted ([ADR-0021](../11-ADR/0021-transaction-boundaries.md), [ADR-0012](../11-ADR/0012-service-extraction-strategy.md)).

### Value objects

Immutable, compared by value, and they carry rules — that is the test for creating one:

| Value object | Rules it carries |
|---|---|
| `Money` | amount + currency together; arithmetic refuses to mix currencies; rounding follows the currency's minor units |
| `PostalAddress` | the parts of an address and how they render to a single line |
| `CatalogText` | one product or category text per language |
| `OrderActor` | who caused a transition (customer, staff, system, gateway) and their id |
| `CurrencyInfo` | how many minor digits a currency has |

`CatalogSlug` is a static helper, not a value object: it normalizes and validates a string that is stored as a string. That is honest — wrapping it would add a type without adding a rule.

### Domain events

Two, and only where a fact has more than one consumer: `OrderStatusChanged` and `StockBecameLow`. Aggregates raise them; `SaveChanges` writes them to the outbox in the same transaction ([Events.md](../02-ARCHITECTURE/Events.md)). One consumer and a required outcome stays a direct call.

### Repositories

One port per aggregate, in `Souq.Domain.Interfaces`, returning aggregates for the write side. Reads do **not** go through repositories: they go through query services that project into DTOs ([CQRS.md](../02-ARCHITECTURE/CQRS.md)). A repository here is a collection of aggregates, not a query API.

## 3. Patterns that are deliberately *not* used

| Pattern | Status | Why |
|---|---|---|
| **Domain services** | **None exist.** | The one candidate — pricing — needs catalog, coupon and shipping lookups, so it lives in the Application layer as `PricingService` behind `IPricing`. Calling it a "domain service" while it queries repositories would be a label, not a design |
| **Factories** | Not used | Constructors and static creators on the aggregates are enough |
| **Specifications** | Not used | Query services express filters directly; a specification layer would add indirection without a second consumer |
| **Event sourcing** | Not used | State is the truth; history is kept where the business needs it (order status history, the stock ledger) — see [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) |
| **A ubiquitous-language document per context** | Not used | One glossary, one team |
| **Anti-corruption layers between modules** | Not needed yet | Contracts already speak in ids and snapshots |

## 4. Where the model is deliberately lightweight

Not everything deserves ceremony. These are plain entities with rules enforced in their handlers or by the database, and that is a decision, not an oversight:

- `Category` — a tree with a depth limit and no cycles; no aggregate boundary to defend.
- `WishlistItem`, `Notification`, `StockMovement`, `CouponRedemption`, `OrderStatusHistory` — records that are created and read, rarely mutated.
- `StoreSettings` and its branding parts — a settings document, validated on write, with no lifecycle.
- `AuditEntry` — append-only, written by a pipeline behaviour.

## 5. Deciding whether something should be an aggregate

Ask, in order:

1. **Does it have an invariant that spans more than one field or row?** If not, it is an entity or a record, not an aggregate.
2. **Must that invariant hold at every commit?** If eventual is fine, they are two aggregates.
3. **Would including it make an unrelated write contend for the same row?** If yes, split and reference by id.
4. **Is it loaded and saved as a whole?** If callers only ever want a slice, you have a query, not an aggregate.

If you promote something to an aggregate, say so in the module document and give it a concurrency token if it is contended.

## 6. Known weaknesses in the model today

Recorded rather than hidden; each is in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) or [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md):

- **`Customer` has no concurrency token**, so "at most 20 addresses" and "exactly one default" are enforced in memory only.
- **Some rules live outside the aggregate that owns the concept**: sellability is checked in the pricing service rather than in `Product`.
- **A coupon's fixed discount carries no currency.** `Coupon` itself refuses a minimum-order comparison across currencies, but `Coupon.Value` is a bare `decimal` and a fixed discount takes the basket's currency when it is calculated. The store currency lock makes that unreachable today ([RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md), R-09).
- **Cross-module domain access** (74 crossings) means aggregates from one module are loaded inside another module's handler — the boundary is documented and counted, not enforced.
- **The order's shipping address is a single-line snapshot**, not the structured `PostalAddress` the model can produce; the destination country is stored separately.
