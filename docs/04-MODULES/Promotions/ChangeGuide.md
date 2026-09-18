# Promotions: change guide

> Read [README.md](README.md) first. This page lists the changes engineers actually make in this module and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive every change**

1. **Rules live in `Coupon`.** A handler, the pricing pipeline or a controller may ask the entity, never re-implement it.
2. **Every rule is checked twice:** once in the quote (informational) and again at reservation, on a **fresh read inside the checkout transaction**.
3. **Any rule that counts something must be protected by the coupon's `rowversion`** — that is, the reservation must write the coupon row. A count checked without touching that row can be passed by two concurrent checkouts.
4. **A use is taken at checkout, confirmed at payment, released on every cancellation,** and release is idempotent.
5. **Round once,** in `Money.FromCalculation`, so the shown, stored and charged amounts match ([ADR-0014](../../11-ADR/0014-money-precision.md)).
6. **A used coupon is never deleted** — the redemptions are the store's history.
7. **No cycle.** Ordering depends on Promotions, so Promotions must never call Ordering (or Shopping). Facts it needs are passed in by the caller.
8. **The module flag is enforced on the server,** at the endpoint and inside the pricing use case.

**Files to read first:** `src/Souq.Domain/Entities/Coupon.cs`, `src/Souq.Domain/Entities/CouponRedemption.cs`, `src/Souq.Application/Features/Coupons/Redemptions/CouponRedemptions.cs`, `src/Souq.Application/Features/Coupons/Contracts/CouponRedemptionContracts.cs`, `src/Souq.Application/Features/Baskets/Pricing/PricingService.cs`, `src/Souq.Application/Features/Orders/Commands/CreateOrderHandler.cs`, `src/Souq.Application/Features/Orders/OrderPaymentConfirmation.cs`.

**Tests that guard the module:** `CouponRuleMatrixTests`, `CouponTests`, `CouponRedemptionsTests`, `CouponHandlersTests`, `PricingServiceTests`, `CreateOrderHandlerTests`, `ConfirmOrderPaymentHandlerTests`, `CouponRedemptionTests`, `PlatformAdministrationTests`, `MigrationRehearsalTests`, and the frontend `couponForm.test.js`.

---

## I need to add a coupon type

- **Inspect:** `DiscountType`, `Coupon.Validate` and `Coupon.CalculateDiscount`, `CreateCouponValidator` and `UpdateCouponValidator`, `CouponDto` and `CouponQueries` (the type is projected as a string), `PricingService` stage 3 (and stage 4 if the new type touches shipping), `Order.ApplyCoupon`, and the frontend form (`couponForm.js`, `CouponFormDrawer.jsx`).
- **Rules to respect:** enum values are stored as integers — append a new member, never renumber. The type's arithmetic belongs in `CalculateDiscount`, clamped to the subtotal and rounded once. A type that does **not** reduce the subtotal (free shipping, for example) does not fit stage 3 at all: it must reach stage 4, which means changing the pipeline and `PriceQuote`, and deciding what the order snapshot records.
- **Steps:**
  1. Add the enum member and its validation in `Coupon.Validate`.
  2. Implement the arithmetic in `CalculateDiscount` (or, for a shipping-affecting type, pass the accepted coupon into stage 4 and adjust the shipping cost there).
  3. Extend the validators, the admin form's type options and its labels.
  4. Check `Order.ApplyCoupon` still holds: the order stores one code and one discount amount.
- **Tests:** add the type to `CouponRuleMatrixTests` and `CouponTests` (arithmetic, clamping, rounding per currency); `PricingServiceTests` for the quote; `CouponHandlersTests` for create and update; an integration case in `CouponRedemptionTests` if reservation behaviour differs.
- **API:** `type` accepts a new value; existing clients are unaffected. Errors keep the `InvalidCoupon` code.
- **Database:** no migration for a new enum value; new parameters (a cap, for example) mean nullable columns on `Coupons` — additive and reversible.
- **Security:** none specific.
- **Docs and ADR:** this README's rules table, [ApiDocumentation.md](../../05-API/ApiDocumentation.md), and an ADR if the type changes what the pipeline means (a shipping discount does).

## I need product- or category-scoped coupons (FUTURE)

- **Inspect:** `PricingLine` and `PricedLine` (the pipeline's line shapes), `PricingService.DiscountAsync` (it receives only the subtotal today), `Coupon`, `Order.ApplyCoupon` and `OrderItem` (there is no per-line discount), and `AllowedContracts` in `ModuleAndContractRuleTests`.
- **Rules to respect:** [ADR-0030](../../11-ADR/0030-coupon-redemptions.md) declined to build this until a store asks **and** the mixed-basket rule is decided: which lines the discount applies to, what the minimum order is measured on, and what happens when an eligible line is removed after the preview. Promotions may depend on Catalog (Modules.md allows it) but never on Shopping or Ordering.
- **Steps:**
  1. Take the product decision, then write an ADR.
  2. Add a scope table (coupon to product or category) with composite foreign keys, owned by this module.
  3. Give `Coupon` a method that computes the discount from **eligible** lines, keeping the clamp and the rounding.
  4. Pass the priced lines (with their category) into stage 3; add Catalog to `AllowedContracts["Promotions"]` and use a Catalog contract to resolve categories — do not read Catalog tables from a handler.
  5. Decide whether the order needs a per-line breakdown; a single order-level discount is enough unless refunds must allocate it.
- **Tests:** entity tests for eligibility and clamping; `PricingServiceTests` for mixed baskets; `CreateOrderHandlerTests`; an integration test where an eligible line is removed between quote and checkout.
- **API:** create and update gain a scope; the quote's `discount` stays a single amount.
- **Database:** a new table plus its indexes; additive.
- **Docs and ADR:** a new ADR, this README, [Modules.md](../Modules.md).

## I need to add or change a coupon rule or limit

- **Inspect:** `Coupon.EnsureUsable` (the order of checks is the order of messages), `Coupon.Validate`, `CreateCouponValidator` / `UpdateCouponValidator`, `CouponRedemptions.ReserveAsync`, `ICouponRedemptionRepository.CountActiveAsync`, `PricingService.DiscountAsync`.
- **Rules to respect:** the rule must run in the quote *and* at reservation. If it counts anything (uses, orders, redemptions), the reservation must still write the coupon row so the `rowversion` serialises concurrent checkouts. Rules that depend on the customer only work when a customer is known — in the quote a guest counts as zero. A rule that needs Ordering data (a "first order only" coupon) must **not** call Ordering: pass the fact in as a parameter from the caller, or derive it from this module's own redemptions.
- **Steps:** add the property to `Coupon` with its validation; add the check to `EnsureUsable` in the right position; extend the validators; add the field to the commands, the DTO, `CouponQueries` and the admin form.
- **Tests:** extend the `CouponRuleMatrixTests` matrix (it is the module's safety net); `CouponRedemptionsTests` for the reservation path; `PricingServiceTests` for the quote; `CouponHandlersTests` for validation.
- **API:** `PUT /api/coupons/{id}` replaces the whole coupon, so an older admin client that does not send the new field will silently clear it. Either ship the frontend with the API, or treat the field as add-only.
- **Database:** a nullable column on `Coupons`; additive and reversible.
- **Security:** a rule keyed to the customer must use the server's customer id, never one from the request body.
- **Docs and ADR:** the rules table in this README, [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md); an ADR only if it changes when a use is taken.

## I need a refund to give the coupon use back

- **Inspect:** `ICouponRedemptions.ReleaseAsync` and `CouponRedemption.Release` (it already accepts a Confirmed record), `UpdateOrderStatusHandler` (an admin cancelling a paid order releases first and refunds afterwards), and the Payments refund path (`IOrderPayments`).
- **Rules to respect:** this is a product decision first — partial refunds, and refunds of an order that stays Delivered, need an answer. Payments must not reference Promotions (it is not in `AllowedContracts`), so the release has to be triggered from the Ordering-side use case that finalises the refund. Release stays idempotent, so a refund after a cancellation must not double-count.
- **Steps:** decide the policy; call `ReleaseAsync` from the Ordering use case that records a full refund; leave partial refunds alone unless the policy says otherwise.
- **Tests:** an application test that a full refund releases exactly once and a second refund attempt changes nothing; an integration test in `CouponRedemptionTests` style covering refund-then-cancel.
- **API:** no shape change; the redemption status a store sees changes.
- **Database:** none.
- **Docs and ADR:** amend [ADR-0030](../../11-ADR/0030-coupon-redemptions.md) (it explicitly left this open) and this README's limitations.

## I need automatic promotions (FUTURE)

- **Inspect:** `PricingService.DiscountAsync` (one code, one discount), `CouponOutcome` and `PriceQuote.Discount`, `Order.ApplyCoupon` (one code, one amount), and the unique `(TenantId, OrderId)` index on `CouponRedemptions`.
- **Rules to respect:** the roadmap records automatic promotions and stacking as not planned, so start with an ADR covering precedence, stacking and whether a coupon can combine with an automatic rule. The total discount must still be clamped to the subtotal and rounded once; every limited promotion needs its own concurrency guard in the checkout transaction; each result is still an outcome, never an exception.
- **Steps:** new aggregate in this module, with its own rules; an evaluation contract Shopping can call (this also removes the existing leak); stage 3 becomes a list of applied discounts; Ordering snapshots the applied promotions and a redemption row per limited promotion.
- **Tests:** a rule matrix for the new aggregate; pipeline tests for combinations; concurrency tests per promotion; migration rehearsal.
- **API:** `BasketDto` gains a list of applied promotions (additive); `discount` stays the total.
- **Database:** new tables, plus order snapshot columns; the unique "one redemption per order" index must be revisited.
- **Docs and ADR:** a new ADR, [Modules.md](../Modules.md), both module READMEs.

## I need to handle a contended ("hot") coupon

- **Inspect:** `CouponRedemptions.SaveWithRetryAsync` and `CouponRedemptions.MaxAttempts`, the `rowversion` in `CouponConfiguration`, and `TenancyRuleTests` (raw SQL is forbidden in Infrastructure outside migrations).
- **Rules to respect:** measure before changing anything — ADR-0030's stated trigger is a coupon whose contention shows in checkout latency. The fallback it names (an atomic conditional update) moves the rule out of the aggregate and cannot check the per-customer limit in the same statement, so it is only acceptable for coupons that have a global limit and no per-customer limit.
- **Steps:** first try raising `MaxAttempts` or shortening the transaction; if that is not enough, add a set-based update (EF's `ExecuteUpdate`, not raw SQL) guarded by `UsedCount < MaxUses`, keeping the redemption insert and every other rule in the aggregate path.
- **Tests:** a concurrency test with more parallel checkouts than `MaxAttempts`; assert that no coupon ever exceeds its limit and that the 409 path is reachable only when the retries are exhausted.
- **API:** the loser's status code may change from 422 to 409 — state it in [ApiDocumentation.md](../../05-API/ApiDocumentation.md).
- **Database:** none.
- **Docs and ADR:** amend ADR-0030 with the measurement and the choice.

## ~~I need to retire the legacy preview endpoint~~ — done in M8

This recipe was followed and the endpoint is gone (TD-06 closed). Kept here for the record, because two of its own
assumptions turned out to be wrong and a third step was deliberately not taken:

- **What was done:** confirmed the SPA never called it (`client.js` had no entry; the checkout's "Apply" button
  re-quotes the basket); re-pointed the three integration tests that used the route as a convenient anonymous
  probe onto other specimens — the problem+json contract onto `POST /api/orders` with an unknown code, module
  gating onto `GET /api/coupons` (the gate runs before authentication, so an anonymous request still reads
  `ModuleDisabled`), tenant isolation onto `GET /api/basket/quote`; then deleted the action, the query and the
  handler, and its two unit tests, whose rules both live in Domain and are tested there.
- **The DEPRECATED-for-one-release step was skipped, on purpose.** Its reason was that removal breaks consumers
  outside this repository. There are none: the platform has not launched, there are no releases to deprecate
  across, and no external client exists to warn. Deprecating would have been ceremony, and the master plan's M8
  entry authorised deleting outright once nothing was found to depend on it. **Reinstate this step the moment
  there is a published API consumer** — the reasoning above expires at launch, not the rule.
- **Two stale assumptions in the old recipe, corrected:** there is no coupon route in
  `AuthorizationBoundaryTests`' public list, so there was nothing to remove there; and TD-06's claim that the
  endpoint skipped the module check was wrong — `RequiresModule` was on the controller all along.
- **Security:** one fewer anonymous surface. The `coupon-preview` limit now guards the basket quote alone, which
  is the only place a code can be priced — and only against the caller's own basket, so a code can no longer be
  probed for what it would give on an amount the caller invents.
