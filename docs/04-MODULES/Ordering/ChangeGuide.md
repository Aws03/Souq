# Ordering: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

1. An order's total always equals its lines minus the discount plus shipping, and after `Order.Place` none of those may change.
2. Every status change goes through `OrderTransitions` and writes exactly one history row with its actor. There is no other way to reach a status.
3. Stock and coupon uses are reserved at checkout, committed at payment, released on every cancellation path — exactly once each. `Cancel` on an already-cancelled order is refused for this reason.
4. No database transaction stays open across a gateway call ([ADR-0021](../../11-ADR/0021-transaction-boundaries.md)). The shape is save → call → save, with compensation or an idempotent re-read on failure.
5. Confirmation is idempotent: the client and the webhook may both arrive, in any order, and the effects happen once.
6. Identity comes from `ICurrentUser`, never from a request body, and "not yours" answers 404.
7. Money is `Money`: the currency is the order's, and rounding happens only in `Money.FromCalculation`.

**Files to read first:** `src/Souq.Domain/Entities/Order.cs`, `src/Souq.Domain/Entities/OrderTransitions.cs`, `src/Souq.Application/Features/Orders/OrderPaymentConfirmation.cs`, `src/Souq.Application/Features/Orders/Commands/CreateOrderHandler.cs`, `src/Souq.Application/Features/Orders/Commands/UpdateOrderStatusCommand.cs`.

**Tests that guard the module:** `tests/Souq.Domain.Tests/OrderLifecycleTests.cs` and `OrderTests.cs`; everything in `tests/Souq.Application.Tests/Orders`; `tests/Souq.IntegrationTests/OrderLifecycleTests.cs`, `InventoryAndOrderTests.cs`, `PaymentsAndRefundsTests.cs`, `CouponRedemptionTests.cs`, `NotificationTests.cs`, `TenantIsolationTests.cs`, `AuthorizationBoundaryTests.cs`; `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`; `frontend/src/features/orders/orderView.test.js`.

---

## I need to add a new order status

For example a *Processing* or *Ready for pickup* state between Paid and Shipped.

- **Inspect:** `OrderStatus`, `OrderTransitions`, `Order` (the transition methods), `OrderStatusAction` and `OrderStatusActions` in `UpdateOrderStatusCommand.cs`, `OrderConfiguration` (the status index), `OrderStatusChangedHandler` and `OrderEmailHandler`, `frontend/src/features/orders/orderView.js` (`ORDER_STATUSES`), the admin drawer and the i18n keys it reads.
- **Rules to respect:** enum values are stored as integers, so **append** the new member and never renumber existing ones. Every state needs an entry in the `Allowed` dictionary — `TargetsFrom` indexes it directly and will throw for a missing key. Decide the customer rule explicitly: `CanMove` allows a customer only the Pending → Cancelled move, so a new state is staff-only unless you change that rule too. Decide what the new state means for stock: today only Pending and Paid hold stock that a cancellation must release.
- **Steps:**
  1. Add the enum member (highest number).
  2. Add its row and its incoming edges in `OrderTransitions.Allowed`.
  3. Add a transition method on `Order` that calls `MoveTo` with a refusal message.
  4. Expose it as an `OrderStatusAction` member and map it in `OrderStatusActions.ByTarget`, then handle it in `UpdateOrderStatusHandler`'s switch — the admin UI reads `allowedActions` from that map, so nothing else is needed server-side.
  5. Decide the notification and email behaviour in `OrderStatusChangedHandler.WantsEmail` and `OrderEmailHandler`, and add a template if you send mail.
  6. Add the status to the frontend list and to both locale files.
- **Tests:** extend the 5×5 matrix in `tests/Souq.Domain.Tests/OrderLifecycleTests.cs` (it enumerates the enum, so a new member changes the case count); add a handler case in `tests/Souq.Application.Tests/Orders/UpdateOrderStatusHandlerTests.cs`; extend the integration lifecycle test if the state is user-visible.
- **API:** `status` strings are the enum names in `OrderDto`, `OrderSummaryDto`, `OrderTrackingDto` and the `status` filter; adding a value widens a response contract that the frontend switches on. Old clients will show an unknown badge — check the admin list and the customer timeline.
- **Database:** no migration for the enum itself. A new index only if the new state gets its own filtered list.
- **Security:** if customers may trigger it, change `OrderTransitions.CanMove` deliberately and re-check `CustomerCanCancel` callers; permissions otherwise stay `orders.manage`.
- **Docs and ADR:** update [README.md](README.md) (state machine table) and [ADR-0029](../../11-ADR/0029-orders-lifecycle.md) if the lifecycle itself changes; a genuinely new lifecycle stage deserves a short ADR of its own. Note that [ADR-0031](../../11-ADR/0031-payments-and-refunds.md) explicitly rejected a *Refunded* status — reopen that decision rather than quietly adding it.

---

## I need to change the cancellation rules

For example letting customers cancel a paid order that hasn't shipped, with an automatic refund.

- **Inspect:** `Order.Cancel`, `OrderTransitions.CanMove` and `CustomerCanCancel`, `CancelMyOrderHandler`, `OrderPaymentConfirmation.CancelUnpaidAsync` and `CancelAsync`, `UpdateOrderStatusHandler`, `GetOrderByIdHandler` (the `CanCancel` flag), `frontend/src/pages/OrderDetail.jsx`.
- **Rules to respect:** cancellation must release stock and the coupon use exactly once, and settle the payment — `CancelAsync` is the single path that does all three, so route any new rule through it rather than writing a second one. An unpaid order must ask the gateway to cancel the intent **before** cancelling locally, or a payment landing at that moment leaves money on a cancelled order. A paid order must be refunded **after** the cancellation commits ([ADR-0031](../../11-ADR/0031-payments-and-refunds.md)), never before.
- **Steps:**
  1. Change the rule in `OrderTransitions` (the table and, for customers, the second clause of `CanMove`) — not in the handler.
  2. Adjust `Order.Cancel`'s refusal message, which currently distinguishes "paid, cancel through the store" from "shipped or delivered".
  3. In the handler, reuse `CancelUnpaidAsync` for Pending orders; for a paid order follow the admin path: cancel, commit, then `IOrderPayments.RefundAsync(orderId, amount: null, …)`.
  4. Re-check who is recorded as the actor: a customer-initiated refund still needs `OrderActor.Customer`, and `OrderStatusChangedHandler.WantsEmail` sends mail for a cancellation that was paid.
  5. Update `CanCancel` in `GetOrderByIdHandler` — the UI shows the button from that flag alone.
- **Tests:** `tests/Souq.Domain.Tests/OrderLifecycleTests.cs` (the customer rule), `tests/Souq.Application.Tests/Orders/CancelMyOrderHandlerTests.cs` (each gateway outcome), `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` and `PaymentsAndRefundsTests.cs` (stock, coupon and money all move).
- **API:** no new route; `canCancel` and the error codes `InvalidOrderOperation`, `PaymentProcessing`, `OrderAlreadyPaid` are the contract the frontend reacts to.
- **Database:** none.
- **Security:** letting customers cause refunds is a money decision — today only `store.payments.manage` may refund directly, and only `orders.manage` can cancel a paid order. Make the new capability explicit rather than inheriting it.
- **Docs and ADR:** [ADR-0029](../../11-ADR/0029-orders-lifecycle.md) chose "customers cancel while Pending" deliberately, and deferred paid cancellation to refunds. Changing it needs an ADR amendment.

---

## I need to change what happens when a payment succeeds

For example issuing an invoice number, or writing a ledger entry.

- **Inspect:** `OrderPaymentConfirmation.ConfirmAsync`, `Order.MarkAsPaid`, `IInventoryReservations.CommitAsync`, `ICouponRedemptions.ConfirmAsync`, `IOrderPayments.MarkSucceededAsync`, `IBasketCheckout.ConsumeAsync`, `AppDbContext.SaveChangesAsync` (outbox capture), `OrderStatusChangedHandler`.
- **Rules to respect:** everything that must be true together goes inside the one `InTransactionAsync` block — the save plus the reservation commit. Anything that talks to the outside world goes **after** the commit, and the way to do that is an outbox message, not a direct call ([ADR-0034](../../11-ADR/0034-notifications-outbox.md)). The method must stay idempotent: a second confirmation returns success without redoing anything, and the rowversion loser re-reads and returns success too. Never add a side effect that would run twice in that race.
- **Steps:**
  1. If the new effect is data in our database, add it to the same unit of work before `SaveChangesAsync` — the tracked changes commit together.
  2. If it calls anything external, raise a domain event from the entity (or enqueue through `INotificationOutbox`) and add a handler; register it in `NotificationMessageTypes` and in `src/Souq.Application/DependencyInjection.cs`.
  3. If it belongs to another module, add or use that module's contract rather than its repository; check the allowed list in `ModuleAndContractRuleTests`.
  4. Re-read the conflict branch: after `ConcurrencyConflictException` the method returns an idempotent success, so your effect must already have been applied by the winner.
- **Tests:** `tests/Souq.Application.Tests/Orders/ConfirmOrderPaymentHandlerTests.cs` has both race directions and asserts what is called once; add your effect there and in `tests/Souq.IntegrationTests/OrderLifecycleTests.cs`.
- **API:** `OrderConfirmedDto` is small on purpose; widen it only if the browser needs the value immediately.
- **Database:** a migration if you add a table; keep it tenant-owned and give it a composite foreign key to the order.
- **Security:** the webhook path has no user behind it, so anything you add must not assume `ICurrentUser`.
- **Docs and ADR:** update [README.md](README.md)'s transition table; an ADR if you add a second unit of work or an external call in this path.

---

## I need to add a field to the order snapshot

For example a gift message, a tax breakdown, or a structured address.

- **Inspect:** `Order`, `OrderItem`, `OrderConfiguration`, `OrderItemConfiguration`, `PersistenceConventions`, `CreateOrderHandler` (where snapshots are taken), `OrderQueries`, `OrderDto`/`OrderSummaryDto`/`OrderTrackingDto`, `CreateOrderCommand` and `CreateOrderValidator`.
- **Rules to respect:** a snapshot is written **before** `Place` and is immutable after it — `EnsureOpen` enforces that for lines, coupon and shipping, and a new setter should use the same guard. Money fields use `PersistenceConventions.MoneyColumnType` and the order's currency. Data that belongs to another module must arrive through a contract (the shipping snapshot arrives through the price quote), not by reading that module's tables.
- **Steps:**
  1. Add the property with a private setter and a guarded mutator on `Order` (or on `OrderItem` with an `internal` one).
  2. Configure it in `OrderConfiguration`: length, column type, and an index only if something filters on it.
  3. Fill it in `CreateOrderHandler` before `order.Place(...)`.
  4. Carry it into the read model: `OrderQueries.FindAsync`, the DTO, and only then the pages that show it.
  5. Decide whether the anonymous tracking projection may expose it — by default it must not; that view is deliberately minimal.
- **Tests:** a domain test that it can't change after placement (`tests/Souq.Domain.Tests/OrderLifecycleTests.cs` has the pattern); `tests/Souq.Application.Tests/Orders/CreateOrderHandlerTests.cs` for the snapshot being taken; `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` for the frozen value surviving a later change at the source.
- **API:** additive fields on `OrderDto` are safe; new request fields need validation in `CreateOrderValidator`.
- **Database:** a migration is required. Nullable, or with a default, so existing rows stay valid; a backfill only if the field must be meaningful for old orders — `MigrationRehearsalTests` is where a backfill is proven. `Down()` should drop the column.
- **Security:** order snapshots are personal data. If the field is personal, check the customer export and the erasure path in [Customers](../Customers/README.md), and keep it out of the public tracking view.
- **Docs and ADR:** [README.md](README.md) data ownership table, and [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md).

---

## I need to change the checkout expiry

For example a shorter hold during a sale, or a different sweep cadence.

- **Inspect:** `InventorySettings` (`ReservationMinutes`, `SweepIntervalSeconds`, `ReservationLifetime`), the options validation in `src/Souq.Infrastructure/DependencyInjection.cs`, `ReservationExpiryService`, `StoreSweepService`, `ExpireStaleCheckoutsHandler`, `IInventoryReservations.FindExpiredAsync`.
- **Rules to respect:** the lifetime belongs to Inventory's settings, because the reservation carries the expiry; the *decision* about the order belongs here. The sweep must keep asking the gateway before cancelling: a succeeded intent confirms the order, a processing one is left for the next pass. Every reference is settled independently — one failure must not stop the rest. The values are validated at startup (5–1440 minutes; the interval is 0 or 10–3600 seconds), so an out-of-range value fails the boot, not a request.
- **Steps:**
  1. Change the configuration value (`Inventory:ReservationMinutes` and/or `Inventory:SweepIntervalSeconds`) per environment; adjust the validation bounds only if the new value falls outside them.
  2. If the *policy* must vary per store rather than per deployment, that is a new decision: the settings are a deployment singleton today, and a per-store lifetime would need a store setting plus a change in `InventoryReservations.ReserveAsync`.
  3. Keep `ExpireStaleCheckoutsCommand.Max` in mind: the sweep settles at most 50 references per store per pass, so a very short lifetime with many abandoned checkouts needs a larger batch or a shorter interval.
- **Tests:** `tests/Souq.Application.Tests/Orders/ExpireStaleCheckoutsHandlerTests.cs` covers every settlement case; `tests/Souq.IntegrationTests/InventoryAndOrderTests.cs` drives the expiry end to end. The integration host sets the interval to `0` and sends the command directly, so changing the interval does not change the tests.
- **API:** none. The customer sees it only as a checkout that stops working after the hold ends.
- **Database:** none.
- **Security:** none, but note the operational effect: a longer hold means stock is unavailable for longer, a shorter one means a slow payer loses their order.
- **Docs and ADR:** [ADR-0026](../../11-ADR/0026-inventory-reservations.md) states the 30-minute default and the 60-second sweep; update it and [Configuration.md](../../09-OPERATIONS/Configuration.md) if the default changes.

---

## I need to make a failed confirmation stop cancelling orders that can still be paid

This fixes limitation 1 in [README.md](README.md): a declined card can cancel an order whose intent is still alive at the gateway.

- **Inspect:** `OrderPaymentConfirmation.ConfirmAsync`, `PaymentIntentState`, `IPaymentService.CancelIntentAsync`, `StripeGateway.ConfirmAsync` (which returns success only for `succeeded`), `ExpireStaleCheckoutsHandler` and `CancelUnpaidAsync` — both already ask the gateway first.
- **Rules to respect:** the order may only be cancelled when the money certainly cannot arrive. "Not succeeded yet" and "will never succeed" are different answers, and `PaymentConfirmationResult` cannot currently express the difference. Whatever you do must stay idempotent for a repeated webhook.
- **Steps:**
  1. Widen the confirmation result so the adapter can distinguish a final failure from a retryable state (the adapter already maps those statuses in `StripeGateway.StateOf`).
  2. In `ConfirmAsync`, cancel the intent through `CancelIntentAsync` before cancelling the order, and treat `PaymentIntentState.Succeeded` as a confirmation and `Processing` as "leave it alone", exactly as the cancel and sweep paths do.
  3. Leave the order Pending when the payment can still be retried; the expiry sweep is the backstop that eventually settles it.
  4. Decide what a `payment_intent.payment_failed` webhook should do at all — arguably nothing but a log, since the sweep and the customer's retry both handle it.
- **Tests:** add cases to `tests/Souq.Application.Tests/Orders/ConfirmOrderPaymentHandlerTests.cs` and `ProcessPaymentWebhookHandlerTests.cs`: a declined card followed by a successful retry of the same intent must end Paid, not Cancelled.
- **API:** `PaymentFailed` stays the code for a real failure; a retryable state should not cancel, so the client sees the order still Pending.
- **Database:** none.
- **Security:** none.
- **Docs and ADR:** this refines [ADR-0031](../../11-ADR/0031-payments-and-refunds.md)'s gateway-first rule to cover the confirmation path; record it there.

---

## I need to give another module order data without lending it the repository

Today Reviews and Notifications both take `IOrderRepository`, and Notifications' email handler reads an order without its lines, which is how the wrong-total defect happens.

- **Inspect:** `CreateReviewHandler`, `OrderStatusChangedHandler`, `OrderEmailHandler`, `IOrderRepository`, `IOrderQueries`, the `AllowedContracts` map in `ModuleAndContractRuleTests`.
- **Rules to respect:** a contract exposes what the consumer needs, not the aggregate: values, not entities (`ModuleAndContractRuleTests` forbids domain entities in request and response types, and the same spirit applies to contracts). Adding an arrow to `AllowedContracts` must not create a cycle — the test checks that. Notifications consumes events, so prefer putting the value **in the event** over letting the consumer read back.
- **Steps:**
  1. Create a *Features/Orders/Contracts* folder with a narrow interface — *IOrderHistory* for the review check, and for the email either the frozen totals or extra fields on `OrderStatusChanged`.
  2. Implement it in Ordering over `IOrderQueries` or the repository.
  3. Register it in `src/Souq.Application/DependencyInjection.cs`, add the module pair to `AllowedContracts`, and delete the consumer's `IOrderRepository` dependency.
  4. While you are in the email handler: use `PlacedTotal` (or load with `GetWithItemsAsync`) so the total is right and a discounted order stops throwing.
- **Tests:** `tests/Souq.Application.Tests/Reviews/CreateReviewHandlerTests.cs` and `OrderNotificationHandlersTests` (in `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs`) change shape; add an assertion on the email's total — no test asserts it today; `ModuleAndContractRuleTests` proves the new arrow is the only one.
- **API:** none.
- **Database:** none.
- **Security:** keep the contract minimal — the email handler needs a number and a total, not an address.
- **Docs and ADR:** [ModuleBoundaries.md](../../02-ARCHITECTURE/ModuleBoundaries.md) lists *IOrderHistory* as the contract that would close the Reviews → Ordering crossing; update it, TD-02 and both module pages when it exists.
