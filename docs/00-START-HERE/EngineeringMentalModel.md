# The engineering mental model

> **What this page is:** how to *think* about a change in this repository, before you think about code. It gives you the chain from a business request to a commit, the vocabulary with real Souq examples, and the test for deciding where a piece of code belongs.
> **Read after:** [SystemOverview.md](SystemOverview.md). **Read with:** [HowToAddAFeature.md](HowToAddAFeature.md), [DDD.md](../03-DOMAIN/DDD.md).

## 1. The chain

Every change walks the same chain. Skipping a link is how systems rot.

```
Business requirement            "a customer should not be able to cancel an order that already shipped"
      ↓
Capability / module             which module owns this concept? (Ordering)
      ↓
Business rule                   state it as an invariant: "cancellation is only allowed from Pending or Paid"
      ↓
Domain model                    the rule lives in the aggregate that owns the state (Order and its transition table)
      ↓
Use case                        the command/query that exercises it (CancelMyOrderCommand + handler)
      ↓
Contracts / ports               what it needs from other modules or the outside world (stock release, payments)
      ↓
Infrastructure adapter          how that is actually done (EF repository, Stripe gateway, email sender)
      ↓
API contract                    the endpoint, its authorization, its error codes
      ↓
Frontend                        what the user sees and what the UI hides (never what it decides)
      ↓
Tests                           a test at the level of the rule, plus one through the API
      ↓
Documentation / ADR             the module doc, the business rule, and an ADR if the decision was architectural
```

The chain also works **backwards**, and that is how you debug: a wrong number on a screen → which endpoint → which use case → which aggregate method → which rule.

## 2. Four kinds of logic, and the test that separates them

| Kind | Question it answers | Lives in | Souq examples |
|---|---|---|---|
| **Business logic** | What is *true* regardless of UI, database or provider? | `Souq.Domain` | An order can't be cancelled after shipping; available stock never goes negative; a discount never exceeds the subtotal; money rounds to the currency's minor units |
| **Application logic** | How is one use case *carried out*? | `Souq.Application` | Load the basket, price it, reserve stock, redeem the coupon, create the order, start payment — in one transaction |
| **Infrastructure logic** | How does a *technology* do it? | `Souq.Infrastructure` | The EF mapping and query, the Stripe call and its minor-unit conversion, the email provider, BCrypt, JWT issuing |
| **Presentation logic** | How is it *shown* or *translated*? | `Souq.API`, `frontend/` | HTTP status codes, route binding, React components, formatting money for a locale |

**The test:** *if we replaced the database and the payment provider tomorrow, would this code have to change?*
- No, and it states a rule → Domain.
- No, but it coordinates steps → Application.
- Yes → Infrastructure.
- It only formats or routes → API/frontend.

A second test, for the frontend: *could a malicious user change this in their browser and gain something?* If yes, the decision belongs on the server; the UI may only reflect it.

## 3. The building blocks, in Souq's own terms

### Domain

| Block | What it is | In Souq | Smell that it is in the wrong place |
|---|---|---|---|
| **Entity** | Identity + lifecycle; changes only through guarded methods | `Order`, `Product`, `InventoryItem`, `Coupon`, `User`, `Tenant` | A public setter, or a handler mutating fields directly |
| **Value object** | Immutable, equal by value, carries rules | `Money` (amount + currency, currency-aware rounding), `PostalAddress`, `CatalogText`, `OrderActor` | A `decimal` passed around with a separate currency string |
| **Aggregate** | A cluster that changes as one unit, entered through its root | `Order` + lines + status history; `Product` + variants + images + translations; `Customer` + addresses | Loading two aggregates and enforcing a rule "between" them in a handler |
| **Aggregate root** | The only entry point; other aggregates are referenced **by id** | `Order`; an order line stores `ProductId` + a price snapshot, never a `Product` | A navigation property to another aggregate |
| **Invariant** | What must hold before and after every change | "available = on hand − reserved ≥ 0"; "lines only while Pending" | A rule enforced in the UI or only in the database |
| **Domain event** | A fact the model produces, for more than one consumer | `OrderStatusChanged`, `StockBecameLow` | An event with exactly one consumer that could be a direct call |
| **Domain service** | An I/O-free rule spanning aggregates | **None today.** Pricing needs lookups, so it is an *application* service (`PricingService` behind `IPricing`) | Calling a repository from something you call a "domain service" |

### Application

| Block | What it is | In Souq |
|---|---|---|
| **Command** | A request that changes state | `CreateOrderCommand`, `AdjustStockCommand` |
| **Query** | A request that only reads | `GetInventoryQuery`, `GetOrderByIdQuery` |
| **Handler** | Executes one command or query; orchestrates, never decides business rules | `CreateOrderHandler` |
| **Validator** | Shape and range checks before the handler | FluentValidation classes next to each request |
| **Port** | An interface the core owns, implemented outside | `IPaymentService`, `IEmailSender`, `IFileStorage`, `IPasswordHasher`, `ICurrentUser`, `ITenantContext` |
| **Contract** | A module's public in-process API | `IInventoryReservations`, `ICouponRedemptions`, `IPricing`, `IShippingRateProvider`, `IOrderPayments` |
| **Repository** | The write-side port for an aggregate | `IOrderRepository`, `IProductRepository` (in `Souq.Domain.Interfaces`) |
| **Unit of work** | The transaction boundary the use case owns | `IUnitOfWork`, with `InTransactionAsync` when several saves must be atomic |
| **DTO** | A shape for crossing a boundary | Every request and response type; never an entity |
| **Application service** | A concrete collaborator with one implementation and no variation point | `PricingService`, `CustomerErasure`, `OrderPaymentConfirmation`, `BasketResolver` |

### Infrastructure and delivery

| Block | What it is | In Souq |
|---|---|---|
| **Adapter** | A port implemented with a technology | `StripeGateway`, the email senders, `LocalFileStorage`, EF repositories |
| **Query service / projection** | A read implemented as SQL straight into a DTO | `CatalogQueries`, `OrderQueries`, `CustomerQueries` (internal; reached through their ports) |
| **Outbox** | Messages written in the business transaction, delivered later | `OutboxMessage` + `OutboxProcessor` + `OutboxDispatcherService` ([Events.md](../02-ARCHITECTURE/Events.md)) |
| **Controller** | HTTP translation only | Binds, authorizes, sends one request, maps the result |

## 4. Where does this code go? A decision table

| You are writing… | It goes… | Because |
|---|---|---|
| "An order may not be cancelled after it ships" | `Order` (Domain) | True regardless of UI, storage or provider |
| "The coupon must still be valid *today*" | `Coupon` with an injected time (Domain) | A rule; the clock is a port, not `DateTime.UtcNow` |
| "This slug is already taken in this store" | Handler (Application) | Needs a lookup, so it cannot live in the aggregate |
| "Reserve stock, then create the order, then redeem the coupon" | Handler (Application) | Orchestration across modules in one unit of work |
| "Convert the total to Stripe's minor units" | `StripeAmountConverter` (Infrastructure) | A provider's representation, not a business rule |
| "Return 409 when a row changed underneath" | `GlobalExceptionHandler` / result mapping (API) | HTTP translation of a domain outcome |
| "Hide the wishlist icon when the module is off" | React (`useModule`) **and** the server (`[RequiresModule]`) | The UI hides; the server enforces |
| "Send the customer an email when the order ships" | A message + handler in Notifications (via the outbox) | Side effects leave after the commit, never inside it |

## 5. Worked example: "add a gift message to an order"

1. **Requirement:** a customer may attach a short gift message at checkout; staff see it on the order.
2. **Module:** Ordering owns the order record. (Not Shopping: the basket carries intent, but the message becomes part of the commercial record.)
3. **Rules:** at most 200 characters; only while the order is Pending; it is part of the frozen snapshot once placed.
4. **Domain:** add the field to `Order` behind a guarded method that enforces the length and the status; add a domain test for both rules.
5. **Application:** extend the checkout command and its validator; no new port is needed.
6. **Infrastructure:** an EF configuration change and a **migration** (additive, nullable column).
7. **API:** the field joins the existing checkout request and the order response; no new endpoint, no new permission.
8. **Frontend:** a field in the checkout step; the admin order drawer shows it. Both translated.
9. **Tests:** domain rules; a handler test; an integration test that places an order with a message and reads it back as staff; a cross-tenant check is already covered by the order endpoints.
10. **Docs:** the Ordering module document (business concepts and data ownership), [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md), regenerate the inventories. No ADR — this changes no boundary.

Now a variant that *does* need an ADR: "let each store define its own order statuses". That changes the transition table from code to data, touches every consumer of `OrderStatus`, and affects notifications and reporting. Decision first, code after.

## 6. Anti-patterns this codebase actively resists

| Anti-pattern | Why it hurts here | What to do instead |
|---|---|---|
| **Anemic domain** — entities as property bags, rules in handlers | The same rule gets re-implemented in the next handler, slightly differently | Put the rule in the aggregate; the handler calls one method |
| **Fat handler** — 200 lines orchestrating six concerns | Untestable, and the business rule is invisible | Push rules into the domain, extract collaborators, keep the handler a sequence of intentions |
| **Entity as DTO** | Leaks internals (password hashes, rowversion) and freezes the model | Project into a DTO; a test enforces this |
| **Business logic in React** | A user can change it; two implementations drift | Server decides, UI displays |
| **`IQueryable` escaping Infrastructure** | Callers compose SQL without the tenant filter or paging | Return a `PaginatedList` of DTOs from a query service |
| **A "Service" that does everything** | Nobody can tell what owns what | Name it after the use case, or make it an application service with one responsibility |
| **A generic abstraction for one use** | Indirection that hides the only implementation | Wait for the second real case |
| **Cross-module reach into entities or repositories** | Silently couples modules and blocks extraction | Ask the module for a contract; if there is none, add one (and record the leak until then) |

## 7. How to name things

- Commands and queries read like intentions: `CancelMyOrderCommand`, `GetLowStockQuery`.
- Aggregate methods are business verbs: `Place`, `MoveTo`, `Reserve`, `Redeem` — not `SetStatus`.
- Contracts say what they promise: `IInventoryReservations`, `IShippingRateProvider`.
- Error codes are stable, specific and client-facing: `InsufficientStock`, `CouponInUse`, `StoreUnavailable`.
- Folder = feature; namespace = module; the module name is the business capability, not the entity ([Modules.md](../04-MODULES/Modules.md)).
