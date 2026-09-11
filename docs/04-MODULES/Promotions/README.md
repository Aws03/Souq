# Promotions module

> **Code:** `src/Souq.Application/Features/Coupons`, `src/Souq.Domain/Entities/Coupon.cs`, `src/Souq.Domain/Entities/CouponRedemption.cs`, `src/Souq.API/Controllers/CouponsController.cs` · **Decisions:** [ADR-0030](../../11-ADR/0030-coupon-redemptions.md), [ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md) (the coupon inside the pricing pipeline), [ADR-0013](../../11-ADR/0013-optimistic-concurrency.md), [ADR-0014](../../11-ADR/0014-money-precision.md) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Promotions owns discount rules: which coupons exist, when they may be used, how many times, by whom, and what each order actually got. It is named after the capability rather than after coupons so that other discount kinds have a home ([Modules.md](../Modules.md)). It is its own module because its hard problem — holding usage limits under concurrent checkouts — is unrelated to pricing or ordering, and because a store can switch the whole capability off.

## Responsibilities

- The coupon lifecycle for store admins: create, update, activate or deactivate, and delete while unused.
- Every coupon rule, inside the `Coupon` entity: type and value, validity window, minimum order, global and per-customer limits, and the discount calculation with its rounding.
- Redemption records: reserve a use at checkout, confirm it at payment, release it on any cancellation (`ICouponRedemptions`).
- Admin read models: the coupon list and the redemptions of one coupon.
- The legacy public preview `GET /api/coupons/apply`.

## Not this module's job

| Concern | Owner |
|---|---|
| Computing basket and order totals, and deciding when a coupon is evaluated | Shopping — `IPricing` stage 3 |
| Freezing the code and the discount on the order | Ordering — `Order.ApplyCoupon` |
| Deciding that an order was placed, paid or cancelled | Ordering — it calls this module at each of those moments |
| Refunding money | Payments |
| Storing and serving module flags | Platform |

## Business concepts

- **Coupon:** a code a customer types to get a discount in one store.
- **Type and value:** a percentage of the subtotal, or a fixed amount.
- **Validity window:** optional start and end instants (UTC).
- **Minimum order:** the subtotal below which the coupon does not apply.
- **Global limit:** how many uses the coupon has in total.
- **Per-customer limit:** how many uses one customer may hold.
- **Used count:** uses held by open or paid orders — not "orders that completed".
- **Redemption:** the record that one order used one coupon; Reserved, Confirmed or Released.
- **Preview:** evaluating a code without taking a use.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `Coupon` | aggregate root | `src/Souq.Domain/Entities/Coupon.cs` | Code required, trimmed and upper-cased; percentage value in (0, 100]; fixed value > 0; `StartsAt` before `ExpiresAt`; `MaxUses` and `MaxUsesPerCustomer` > 0 with the per-customer limit not above the global one; usability (active, window, both limits, minimum order) in `EnsureUsable`; the discount never exceeds the subtotal and is rounded once; `Redeem` takes a use only after all rules pass; `ReleaseUse` never goes below zero |
| `CouponRedemption` | aggregate root, one per order | `src/Souq.Domain/Entities/CouponRedemption.cs` | Coupon, order and customer ids > 0; starts Reserved; `Confirm` is idempotent and refuses a released record; `Release` returns true only the first time |
| `DiscountType` | enum | `src/Souq.Domain/Enums/DiscountType.cs` | `Percentage` = 0, `FixedAmount` = 1, stored as int |
| `CouponRedemptionStatus` | enum | `src/Souq.Domain/Enums/CouponRedemptionStatus.cs` | `Reserved` = 0, `Confirmed` = 1, `Released` = 2; the order is fixed because the values are stored |
| `InvalidCouponException` | domain exception | `src/Souq.Domain/Exceptions/DomainException.cs` | Stable code `InvalidCoupon` for every rule failure |
| `Money` | value object (shared kernel) | `src/Souq.Domain/ValueObjects/Money.cs` | Minimum order and discount amounts, and the single rounding point |

Notes:

- **Aggregate boundaries.** A redemption is a separate aggregate rather than a collection on the coupon: a list on the coupon would grow without bound and would conflict on every checkout ([ADR-0030](../../11-ADR/0030-coupon-redemptions.md)). The coupon's `UsedCount` is expected to match its active redemptions, plus any legacy count carried by the migration.
- **Concurrency token.** `Coupons` carries a `rowversion`. Every reserve and release changes `UsedCount`, so two checkouts on the same coupon can never both commit from the same read.
- **Redemption state machine.** Reserved → Confirmed (payment) and Reserved or Confirmed → Released (cancellation). Released is terminal; a Confirmed redemption becomes Released when an admin cancels a paid order.
- `Coupon` knows nothing about `Order`. `Order.ApplyCoupon` re-checks "discount ≤ subtotal" and the currency, so a corrupt discount is impossible even if a caller misbehaves — defence in depth, not duplication.

## How it works

### The rules

| Rule | Enforced in | Detail |
|---|---|---|
| Code | entity, `CreateCouponValidator`, database | Required, trimmed, upper-cased by the constructor; `CouponRepository.GetByCodeAsync` normalises the same way, so lookups are case-insensitive. At most 50 characters; unique per store (unique `(TenantId, Code)`, plus a pre-check answering `409 DuplicateCode`). It never changes after creation, because customers already have it: to rename, create a new coupon and deactivate the old one |
| Percentage | entity, validators | Value greater than 0 and at most 100 |
| Fixed amount | entity, validators | Value greater than 0; the discount is clamped to the subtotal, so a 20 coupon on a 15 order discounts 15 |
| Window | entity, validators | Optional `StartsAt` and `ExpiresAt`, start strictly before end; usable while `StartsAt ≤ now ≤ ExpiresAt` (UTC) |
| Minimum order | entity | `MinOrderAmount` (store currency) compared against the subtotal **before** the discount |
| Global limit | entity | `MaxUses` greater than 0; refused once `UsedCount ≥ MaxUses` |
| Per-customer limit | entity | `MaxUsesPerCustomer` greater than 0 and not above `MaxUses`; refused once the customer's active redemptions reach it |
| Active | entity | An inactive coupon is refused everywhere |

`Coupon.EnsureUsable` checks in this order: inactive → not started → expired → global limit → per-customer limit → minimum order, and throws on the first failure. Every failure carries the code `InvalidCoupon`; only the message differs, and the message is what the shopper reads.

`Coupon.CalculateDiscount` computes the percentage of the subtotal or the fixed value, clamps it to the subtotal, and rounds **once** through `Money.FromCalculation` to the currency's minor units ([ADR-0014](../../11-ADR/0014-money-precision.md)) — so the amount shown, the amount stored on the order and the amount charged are the same number.

### Preview, reservation, confirmation, release

| Step | Trigger | Path | Effect |
|---|---|---|---|
| **Preview** | `GET /api/basket/quote?couponCode=` | Shopping's `PricingService` → `ICouponRepository.GetByCodeAsync`, `ICouponRedemptionRepository.CountActiveAsync` (only when the caller is a known customer), `Coupon.EnsureUsable`, `Coupon.CalculateDiscount` | Nothing is written; the verdict is reported inside the quote's `coupon` |
| **Preview (legacy)** | `GET /api/coupons/apply?code=&subtotal=` | `ApplyCouponHandler` | Nothing is written; the subtotal comes from the client in the store currency; the per-customer limit is **not** counted; failures are 422 |
| **Reservation** | `POST /api/orders` carrying a coupon | `CreateOrderHandler` → `ICouponRedemptions.ReserveAsync` → `CouponRedemptions` → `Coupon.Redeem` | `UsedCount + 1` and a `CouponRedemption` in state Reserved, inside the order's transaction |
| **Confirmation** | Payment confirmed by the customer, by the gateway webhook, or by a cancellation that discovers the payment succeeded | `OrderPaymentConfirmation.ConfirmAsync` → `ICouponRedemptions.ConfirmAsync` | The redemption becomes Confirmed, saved with the order; no second use is taken |
| **Release** | Every cancellation: failed payment, failed payment start, checkout expiry, customer cancellation, admin cancellation of a pending or paid order | `OrderPaymentConfirmation.CancelAsync`, or `UpdateOrderStatusHandler` for a paid order → `ICouponRedemptions.ReleaseAsync` | The redemption becomes Released and `UsedCount − 1`, exactly once |

At checkout the coupon is evaluated twice on purpose: `CreateOrderHandler` first prices everything through `IPricing` and rejects a bad coupon with `422` before writing anything, and then `ReserveAsync` re-runs every rule on a **fresh read** inside the transaction, because the quote may be seconds old and someone else may have taken the last use.

A use is therefore held by unpaid orders. That is deliberate: counting only at payment let several checkouts pass the same `MaxUses` check and all be paid, which is the bug [ADR-0030](../../11-ADR/0030-coupon-redemptions.md) closes (problem C1 for coupons).

### How the limits hold under concurrency

`CouponRedemptions.SaveWithRetryAsync`, the same pattern Inventory uses:

1. Read the coupon by code (tracked, with its `rowversion`), count the customer's active redemptions, run `Coupon.Redeem` (all rules, then `UsedCount++`), add the `CouponRedemption`, save.
2. If another checkout wrote that coupon in the meantime, the save fails the `rowversion` check (`ConcurrencyConflictException`). `ICouponRedemptionRepository.Reset` detaches every tracked `Coupon` and `CouponRedemption`, and the attempt starts again from a fresh read that sees the winner's count and redemption.
3. The loser either takes a use that is still free or is rejected with `InvalidCoupon` — and because the reservation runs inside the checkout transaction, that rejection rolls back the entire checkout: no order, no stock reservation, no consumed order number.
4. At most `CouponRedemptions.MaxAttempts` (5) attempts; a fifth conflict surfaces as `409 ConcurrencyConflict`.

The per-customer limit is protected by the same row: every reservation updates `UsedCount`, even for a coupon without a global limit, so two checkouts by one customer serialise and the second one counts the first one's redemption.

This runs inside the caller's transaction — `IUnitOfWork.InTransactionAsync` joins an existing transaction rather than opening a second one — and, per the code comment, relies on EF's savepoint per save so a failed attempt does not poison the outer transaction.

**Evidence:** `CouponRedemptionTests` starts five checkouts at once against a single-use coupon: exactly one order is created, four get `422 InvalidCoupon`, `UsedCount` is 1 and there is exactly one redemption.

**Cost:** checkouts using the same coupon wait briefly on its row. ADR-0030's fallback for a contended flash-sale code is an atomic conditional update.

### The module flag

`promotions` (`StoreModules.Promotions`) is enforced in two places:

- **At the endpoint:** `CouponsController` carries `[RequiresModule(StoreModules.Promotions)]`, so every `/api/coupons` route — the public preview *and* the admin routes — answers `404 ModuleDisabled` from `TenantAvailabilityMiddleware`, before authentication.
- **In the use case:** `PricingService` checks `TenantInfo.HasModule(StoreModules.Promotions)`, so the basket quote reports the outcome `ModuleDisabled` and checkout answers `422 ModuleDisabled` (`PlatformAdministrationTests` asserts exactly this pair).

`ICouponRedemptions` does **not** check the flag: orders placed while the module was on are still confirmed and released after it is switched off, which is what keeps counters honest. The SPA hides the coupon field (`useModule('promotions')` in `AddressStep`) and filters the admin navigation and route by module and permission.

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Create a coupon | `CreateCouponCommand` | `CreateCouponHandler` | `promotions.manage` | `POST /api/coupons` |
| Update a coupon, including activate and deactivate | `UpdateCouponCommand` | `UpdateCouponHandler` | `promotions.manage` | `PUT /api/coupons/{id}` |
| Delete an unused coupon | `DeleteCouponCommand` | `DeleteCouponHandler` | `promotions.manage` | `DELETE /api/coupons/{id}` |
| List coupons (active and inactive) | `GetCouponsQuery` | `GetCouponsHandler` | `promotions.manage` | `GET /api/coupons` |
| List one coupon's redemptions | `GetCouponRedemptionsQuery` | `GetCouponRedemptionsHandler` | `promotions.manage` | `GET /api/coupons/{id}/redemptions` |
| Preview a discount (legacy) | `ApplyCouponQuery` | `ApplyCouponHandler` | anonymous, behind the `coupon-preview` rate limit | `GET /api/coupons/apply` |
| Reserve, confirm or release a use | `ICouponRedemptions` (not a MediatR request) | `CouponRedemptions` | Ordering only | — |

Validators: `CreateCouponValidator`, `UpdateCouponValidator`, `DeleteCouponValidator`, `GetCouponsQueryValidator`, `GetCouponRedemptionsQueryValidator`.

`UpdateCouponCommand` is a full replacement: fields the client omits become null, and the controller forces the route id onto the command. The code is not part of it.

## Public contracts

| Contract | Path | Implementation | Callers |
|---|---|---|---|
| `ICouponRedemptions` | `src/Souq.Application/Features/Coupons/Contracts/CouponRedemptionContracts.cs` | `CouponRedemptions` | `CreateOrderHandler` (`ReserveAsync`), `OrderPaymentConfirmation` (`ConfirmAsync`, `ReleaseAsync`), `UpdateOrderStatusHandler` (`ReleaseAsync`) |

- `ReserveAsync` and `ReleaseAsync` save with retry; `ConfirmAsync` deliberately does not save, so the caller's unit of work commits it with the order — the confirmation and the payment succeed or fail together.
- `ICouponQueries` is the module's own read port, implemented by `CouponQueries` in Infrastructure.
- There is **no evaluation contract.** The discount is computed by Shopping's pipeline straight from the entity, through Promotions' domain repositories. [Modules.md](../Modules.md) says "the pipeline is the only evaluator", and the planned *ICouponEvaluator* was never built; the legacy `ApplyCouponHandler` is in fact a second evaluator. See *Dependencies*.

## Dependencies

- **Uses:** only the shared kernel (`Money`, `ITenantContext`, `IUnitOfWork`, `TimeProvider`, `Result` / `Error`) and its own repositories. It calls no other module's contracts — `AllowedContracts` has no entry for Promotions.
- **Used by:** Ordering, through `ICouponRedemptions` (an allowed contract).
- **Boundary leak into this module:** Shopping's `PricingService` uses `ICouponRepository`, `ICouponRedemptionRepository`, `Coupon` and `InvalidCouponException` directly — domain types, not a contract, and no test catches it.
- **Cross-module reads in Infrastructure:** `CouponQueries` reads `Orders` (order number) and `Customers` (full name) for the redemptions list, and `CouponRedemptionConfiguration` has composite foreign keys to `Orders` and `Customers`. Both are read-side or referential coupling, not Application-layer dependencies.
- **Enforced vs convention:** `ModuleAndContractRuleTests` allows Ordering → Promotions contracts and would fail any reference from Promotions into another feature folder. It cannot see domain repositories, so Shopping's use of `Coupon` passes. Modules.md's "Promotions → Catalog" arrow is reserved for scoped coupons, which are not built.

## Data ownership

| Table | EF configuration | Tenant-owned | Concurrency | Constraints and indexes that encode rules |
|---|---|---|---|---|
| `Coupons` | `CouponConfiguration` | yes | **`rowversion`** | Unique `(TenantId, Code)` — the same code in two stores is two independent coupons; alternate key `(TenantId, Id)` so redemptions can use composite foreign keys; `MinOrderAmount` and its currency as owned columns; `Value` stored with money precision so a fixed amount is not truncated |
| `CouponRedemptions` | `CouponRedemptionConfiguration` | yes | through the coupon's `rowversion` | Unique `(TenantId, OrderId)`: one redemption per order; `(TenantId, CouponId, CustomerId, Status)` serves the per-customer count; composite foreign keys to coupon, order and customer, all Restrict, because this is a reporting record that must outlive nothing |

- Migration `Phase10Coupons` is additive with a backfill: each existing order that used a coupon still present gets a redemption (Pending → Reserved; Paid, Shipped or Delivered → Confirmed; Cancelled → none), counters keep their value and gain the pending orders. `Down()` subtracts the reserved uses before dropping the table, and `MigrationRehearsalTests` checks the backfill against Phase 1-shaped data.
- Orders keep their own text snapshot of the code and the discount amount; that belongs to Ordering.
- Data of other modules that this module reads: `Orders` and `Customers`, in `CouponQueries` only.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/coupons/apply?code=&subtotal=` | anonymous; `coupon-preview` rate limit | `promotions` | legacy preview |
| GET | `/api/coupons?page=&pageSize=` | `promotions.manage` | `promotions` | list, newest first |
| POST | `/api/coupons` | `promotions.manage` | `promotions` | create, 201 with the id |
| PUT | `/api/coupons/{id}` | `promotions.manage` | `promotions` | update, 204 |
| GET | `/api/coupons/{id}/redemptions?page=&pageSize=` | `promotions.manage` | `promotions` | redemptions with order number, customer, discount and status |
| DELETE | `/api/coupons/{id}` | `promotions.manage` | `promotions` | delete, 204 |

Coupons also travel through two endpoints owned by other modules: `GET /api/basket/quote` (Shopping) and `POST /api/orders` (Ordering).

Frontend: `getCoupons`, `getCouponRedemptions`, `createCoupon`, `updateCoupon`, `deleteCoupon` in `frontend/src/api/client.js`; screens in `frontend/src/pages/admin/Coupons.jsx`, `frontend/src/pages/admin/CouponFormDrawer.jsx` and `frontend/src/pages/admin/CouponRedemptionsDrawer.jsx`; the pure form logic (`couponToForm`, `buildCouponPayload`, which never sends the code when editing, and `couponFormProblem`) in `frontend/src/features/admin/coupons/couponForm.js`.

## Security and permissions

- `promotions.manage` is granted to the store administrator role only; store staff do not have it (`RolePermissions`, checked by `RolePermissionsTests`).
- **Coupon guessing** is the module's main abuse surface. A code can only be tried through the basket quote or the legacy preview, both behind the `coupon-preview` fixed window (30 per 60 s per host and client address by default, `RateLimiting:CouponPreview`). Trying a code at checkout requires a signed-in customer and creates an order.
- The legacy preview trusts a client-sent subtotal, but it only computes; it never writes and never takes a use.
- Coupon changes are **not audited**: no request in this module implements `IAuditable`, unlike the platform area.
- Every route is store-scoped: another store's coupon id is a 404, and its code is `CouponNotFound`.

## Tenant behaviour

- `Coupon` and `CouponRedemption` are `ITenantOwned`: the query filter scopes reads, the write guard stamps the store, and composite foreign keys keep a redemption's coupon, order and customer in the same store.
- The same code in two stores is two coupons with independent counters; a store-A code used on store B's host is `CouponNotFound` (`TenantIsolationTests`).
- Amounts are in the store currency: `CreateCouponHandler` and `UpdateCouponHandler` build `MinOrderAmount` from `ITenantContext`, and the preview ignores any client-sent currency.

## Events and background work

- No domain events, no outbox messages and no hosted service belong to this module.
- Releases run inside Ordering's cancellation transactions, including the one driven by the checkout-expiry sweep (`ExpireStaleCheckoutsHandler`), so an abandoned checkout gives its use back without anything in Promotions running on a timer.

## External integrations

None.

## Tests

| Layer | Class | What it covers |
|---|---|---|
| Domain | `CouponRuleMatrixTests` | every combination of active, window, global limit, per-customer limit and minimum order (64 cases) through the real entity; percentage rounding per currency; window and limit guards on create and update; release never below zero and an idempotent redemption lifecycle |
| Domain | `CouponTests` | upper-cased codes; rejected percentages and fixed values; discount calculation, rounding and clamping; each `EnsureUsable` refusal; a rejected update leaving the entity unchanged |
| Domain | `DomainExceptionCodeTests` | the `InvalidCoupon` code |
| Application | `CouponRedemptionsTests` | a reservation takes a use and records it Reserved; an exhausted per-customer limit is rejected without saving; a concurrency conflict retries from a fresh read that sees the winner; release gives the use back once and confirmation takes none; an order without a coupon confirms and releases nothing |
| Application | `CouponHandlersTests` | duplicate code; create and update; the window and per-customer validators; a used coupon is deactivated, not deleted |
| Application | `ApplyCouponHandlerTests` | the preview computes in the store currency and rejects an amount with too many decimals |
| Application | `PricingServiceTests` | the coupon as an outcome, including the per-customer limit for a known customer and `ModuleDisabled` |
| Application | `CreateOrderHandlerTests`, `ConfirmOrderPaymentHandlerTests` | reserve at checkout, apply before the payment intent, confirm at payment |
| Integration | `CouponRedemptionTests` | five concurrent checkouts on a single-use coupon: one order, four `422 InvalidCoupon`; the per-customer limit held by an unpaid order, released by customer and by admin cancellation, confirmed by payment; a coupon that has not started; the admin redemptions list; `409 CouponInUse` |
| Integration | `PlatformAdministrationTests` | the disabled module: 404 at the endpoint and `422 ModuleDisabled` at checkout |
| Integration | `TenantIsolationTests`, `ErrorContractTests`, `MigrationRehearsalTests`, `BasketTests` | cross-store codes and ids; the `CouponNotFound` problem shape; the Phase 10 backfill; basket total equal to order total with a coupon |
| Frontend | `couponForm.test.js`, `basketModel.test.js` | payload and validation of the admin form; the rejected-coupon message in the shopper's language |

Not covered: five consecutive conflicts surfacing as `409 ConcurrencyConflict`; a refund (without cancellation) keeping the use; the legacy preview ignoring the per-customer limit.

## Failure modes

| Situation | Code | HTTP | How it is handled |
|---|---|---|---|
| Create with an existing code | `DuplicateCode` | 409 | pre-check in `CreateCouponHandler` |
| Two creates of the same code at once | `DuplicateValue` | 409 | unique index → `UniqueConstraintViolationException` |
| Value, limits or window rejected by the validator | `ValidationFailed` | 400 | FluentValidation |
| Values the entity rejects (per-customer above global, start not before end, non-positive value) | `InvalidCoupon` | 422 | `Coupon` throws |
| Minimum order with more decimals than the currency allows | `InvalidMoney` | 422 | `Money` throws |
| Coupon not found, or belongs to another store (update, delete, redemptions) | `NotFound` | 404 | handler result |
| Delete after any use or redemption | `CouponInUse` | 409 | `DeleteCouponHandler`; deactivate instead, so the history survives |
| Unknown code in a quote or at checkout | `CouponNotFound` | 200 in the quote / 422 at checkout | `PricingService` |
| Any rule failure | `InvalidCoupon` | 200 in the quote / 422 at checkout | `Coupon.EnsureUsable` |
| Last use lost to a concurrent checkout | `InvalidCoupon` | 422 | `Coupon.Redeem` on the retry's fresh read; the whole checkout rolls back |
| Five consecutive `rowversion` conflicts | `ConcurrencyConflict` | 409 | `CouponRedemptions.MaxAttempts` exhausted |
| Module disabled | `ModuleDisabled` | 404 on `/api/coupons`, 422 at checkout, an outcome in the quote | middleware and `PricingService` |
| Confirming a redemption that was released | `InvalidCoupon` | 422 | `CouponRedemption.Confirm`; defensive, unreachable through the normal order flow |
| Preview or quote flooded | `TooManyRequests` | 429 | `coupon-preview` limiter with `Retry-After` |

## Common change scenarios

See [ChangeGuide.md](ChangeGuide.md): add a coupon type; scope coupons to products or categories; add or change a rule or a limit; give the use back on a refund; add automatic promotions; deal with a contended "hot" coupon; retire the legacy preview endpoint.

## Known limitations

- **An unpaid order holds its use** until it is paid, cancelled or expired by the checkout sweep, so a limited coupon can read as used up while checkouts are open.
- **A refund without a cancellation keeps the use.** Only cancellation releases it; ADR-0030 left the refund question open and Phase 11 did not answer it in code.
- **No product or category scope, no automatic promotions, no stacking:** one code per order.
- **Two evaluators.** `GET /api/coupons/apply` evaluates outside the pricing pipeline, trusts a client subtotal and ignores the per-customer limit. It contradicts the "one pipeline" rule; the SPA does not use it, but it is still public.
- **Currency is not re-checked on the rules.** `EnsureUsable` compares the minimum order by amount only, and a fixed value is a bare decimal applied in the basket's currency, so a coupon created as "5 JOD" becomes "5 USD" if the store's currency changes.
- **The entity's percentage message says "between 1 and 100"** while the check accepts any value above 0 (0.5 is valid).
- **Admin coupon changes leave no audit trail.**
- **Contention:** checkouts on one popular code serialise on its row.

## Future evolution

- **FUTURE:** product- or category-scoped coupons — ADR-0030 rejected building them speculatively; revisit when a store asks and a rule for mixed baskets is agreed.
- **FUTURE:** automatic promotions and stacking — the roadmap records them as not planned; they need a new aggregate and an evaluation contract.
- **FUTURE:** an atomic conditional update for a contended coupon, ADR-0030's named fallback.
- **DEFERRED:** releasing a use when an order is refunded rather than cancelled — a product decision, not a technical gap.
- **FUTURE:** retiring `GET /api/coupons/apply` in favour of the basket quote, leaving one evaluator.
