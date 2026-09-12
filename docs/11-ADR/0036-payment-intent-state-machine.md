# ADR-0036: A failed confirmation reads the intent's state, and money captured after an order closes is recorded rather than swallowed

- **Status:** Accepted (implemented in Phase 17), 2026-09-12.
- **Date:** 2026-09-12
- **Related modules:** Ordering; Payments
- **Related ADRs:** corrects the confirmation path of [ADR-0031](0031-payments-and-refunds.md); keeps the gateway call outside every transaction per [ADR-0021](0021-transaction-boundaries.md); the confirmation race it must not disturb is [ADR-0013](0013-optimistic-concurrency.md); the cancellation compensations are [ADR-0026](0026-inventory-reservations.md) and [ADR-0030](0030-coupon-redemptions.md); failures use the error shape of [ADR-0017](0017-error-contract.md)

## Context

[ADR-0031](0031-payments-and-refunds.md) made every **cancellation** path ask the gateway before cancelling an order: `OrderPaymentConfirmation.CancelUnpaidAsync` and the expiry sweep both call `CancelIntentAsync` first, so a customer who paid one second earlier gets a confirmed order instead of a cancelled one with money attached.

The **confirmation** path never got the same treatment. `OrderPaymentConfirmation.ConfirmAsync` asked `IPaymentService.ConfirmAsync`, which answered a single boolean, and treated everything that was not "succeeded" as final: it cancelled the order, released the stock reservation and the coupon use, and marked the `Payment` as `Failed` — without cancelling the intent at the gateway.

That is a misreading of how a card decline works. A declined Stripe PaymentIntent is **not** terminal: it returns to `requires_payment_method`, which is precisely the state that lets the shopper try another card on the same client secret. Stripe also emits `payment_intent.payment_failed` for that decline, and Souq's webhook applies it through the same confirmation path.

## Problem

A shopper's card is declined. Stripe emits `payment_intent.payment_failed`; the webhook cancels the order server-side, releases the stock and the coupon, and marks the payment `Failed`. The shopper — still on the checkout page, still holding a live client secret — enters a working card. Stripe captures the money. The `payment_intent.succeeded` webhook arrives, finds an order that is no longer `Pending`, and returns an idempotent success without doing anything.

The result is a cancelled order, a `Failed` payment, and captured money. `Payment.RequestRefund` refuses anything that is not `Succeeded`, so **no refund path in the product accepts it**: the money can only be returned by hand from the Stripe dashboard, and nothing in Souq says it is there.

Two questions follow. What should a *non-succeeded* confirmation do? And what should happen when money is confirmed for an order that can no longer accept it?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| What does the port report | **The intent's state**, not a boolean: `PaymentConfirmationResult` carries a `PaymentIntentState`, which gains a `Retryable` member for the `requires_*` family. `Succeeded` remains as a derived convenience | A boolean: it is exactly what made a retryable decline indistinguishable from a dead intent. A provider-specific status string in Application: leaks Stripe's vocabulary past the adapter (ADR-0003) |
| A decline whose intent is still alive (`Retryable`) | **Leave the order `Pending`** and answer `PaymentFailed`. The shopper retries on the same intent; if they abandon it, the existing expiry sweep settles it — asking the gateway first, as it already does | Cancel the order (the behaviour being fixed): leaves a live intent that can still capture against a cancelled order. Cancel the *intent* on every decline: destroys retry, which is the whole point of Stripe's model, and turns one mistyped CVC into a lost sale |
| An intent the gateway reports `canceled` | **Cancel the order** and release stock and coupon, as before | Nothing changes here: a cancelled intent cannot capture, so this was always safe |
| An intent still `processing` | **Neither cancel nor confirm**; answer `PaymentProcessing` and let the next webhook or the sweep settle it | Cancelling it: the money may still land. This matches what `CancelUnpaidAsync` already does for the same state |
| Money confirmed for an order that is already `Cancelled` | **Record it.** `Payment.MarkCapturedAfterClose` moves the payment to `Succeeded`, the save is logged at error level, and the existing refund path — which requires `Succeeded` — now accepts it | Keep returning a silent idempotent success (the behaviour being fixed): the money stays invisible and unrefundable. Reopen the order: it was cancelled for a reason, its stock and coupon were already released, and resurrecting it would oversell |
| Refunding that money | **Not automatically.** Staff refund it from the order screen under `store.payments.manage`, as for any other refund | An automatic refund: moving money without a person deciding is a commercial choice this phase is not entitled to make, and the cancel-a-paid-order path already refunds deliberately |
| Creating the intent | **An idempotency key** `souq-intent-{tenantId}-{orderReference}` on `CreateIntentAsync` | Without one, a timeout after Stripe created the intent but before its response arrived left an orphan intent whose id we never learned |

## Decision

The options marked "Chosen" above.

`PaymentIntentState` is now the single vocabulary for "what is this intent doing": `Cancelled`, `Succeeded`, `Processing`, `Retryable`. `StripeGateway` maps its statuses to it in one place and answers `Retryable` for anything that is not a known terminal state — the conservative direction, because `Retryable` never destroys anything. `CancelIntentAsync` never answers `Retryable`: it has just cancelled whatever was.

`Payment.MarkCapturedAfterClose` is the only door that moves a settled payment back to `Succeeded`. It exists because the gateway, not Souq, is the authority on whether money moved; refusing to record reality is what stranded it. It is idempotent, so duplicate late webhooks record once.

**No schema change.** The reconciliation signal an operator looks for is a `Payment` that is `Succeeded` on an `Order` that is `Cancelled` — a query over data that already exists, not a new column.

## Consequences

- **Positive:**
  - A declined card no longer destroys the order: the shopper retries on the same intent, which is what Stripe's flow is designed for.
  - No path cancels an order while leaving an intent that can still capture.
  - Money captured against a closed order becomes visible and refundable through the normal, permissioned path instead of being stranded.
  - A retry of a lost `CreateIntentAsync` returns the original intent instead of creating a second one.
- **Negative / limits:**
  - A declined order now holds its stock reservation until the checkout expiry window elapses, instead of releasing it at once. That is the mechanism abandoned checkouts already use, and the window is `Inventory:ReservationMinutes`.
  - `PaymentCapturedOnCancelledOrder` is a state a human must resolve; nothing sweeps for it. The log line names the order, and the register records it.
  - The webhook still answers 200 for it, because the gateway must stop retrying; the signal is the log line and the payment row, not the HTTP status.
  - **Unverified against a real account.** Every state in this ADR is exercised against `FakeGateway` and reasoned from Stripe's documented intent lifecycle. The mapping of a real declined intent to `Retryable`, and of a real late capture, has not been observed on a live Stripe account — see [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

## Revisit when

- A real Stripe account is available to confirm the status mapping end to end.
- A second payment provider arrives whose intent lifecycle does not fit these four states.
- `PaymentCapturedOnCancelledOrder` happens often enough to deserve an automatic reconciliation sweep instead of a log line.

## Verification

- **Domain:** `PaymentTests` — a capture after the payment was closed as `Failed`, `Cancelled` or `Pending` makes it `Succeeded` and refundable, and a repeat records nothing.
- **Application:** `ConfirmOrderPaymentHandlerTests` — a retryable decline leaves the order `Pending` and touches neither stock, coupon nor payment; a cancelled intent still cancels the order; a processing intent does neither; a late success on a cancelled order records the capture and answers `PaymentCapturedOnCancelledOrder`; a cancelled order whose intent did not capture stays untouched.
- **Application:** `CancelMyOrderHandlerTests`, `UpdateOrderStatusHandlerTests`, `ExpireStaleCheckoutsHandlerTests` — the gateway-first cancellation paths are unchanged.
- **Integration:** `PaymentsAndRefundsTests` — an order cancelled by its customer, then a signed `payment_intent.succeeded` for its intent: the order stays cancelled, the payment becomes `Succeeded`, the money is refunded through the normal endpoint, and a duplicate webhook adds nothing.
