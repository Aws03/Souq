# ADR-0031: Payments: a payment record per order, refunds as reserve–call–record, per-store gateway accounts with encrypted keys, and webhooks routed to the right store

- **Status:** Accepted (implemented in Phase 11), 2026-09-11.
- **Decides part of D-13 (payment tenancy):** gateways are resolved per store and store secrets are encrypted at rest. A store may connect its own Stripe account; stores without one keep using the deployment's account. **The choice between tenant-owned keys and Stripe Connect remains open** (see "D-13" below).
- **Builds on:**
  - [ADR-0013](0013-optimistic-concurrency.md): `rowversion`.
  - [ADR-0014](0014-money-precision.md): money.
  - [ADR-0020](0020-configuration-and-secrets.md): explicit configuration; no fake payments selected silently outside development.
  - [ADR-0021](0021-transaction-boundaries.md): no network call inside a transaction.
  - [ADR-0024](0024-platform-administration.md): platform writes go through the target store's scope.
  - [ADR-0026](0026-inventory-reservations.md) and [ADR-0030](0030-coupon-redemptions.md): retry from a fresh read.
- **Date:** 2026-09-11
- **Related modules:** Payments; Ordering; Platform (per-store gateway accounts and their secrets)
- **Related ADRs:** builds on [ADR-0013](0013-optimistic-concurrency.md), [ADR-0014](0014-money-precision.md), [ADR-0020](0020-configuration-and-secrets.md), [ADR-0021](0021-transaction-boundaries.md), [ADR-0024](0024-platform-administration.md), [ADR-0026](0026-inventory-reservations.md) and [ADR-0030](0030-coupon-redemptions.md); the amount it charges and refunds includes the shipping added by [ADR-0032](0032-shipping-methods.md); its failures use the error shape of [ADR-0017](0017-error-contract.md)

## Context

Before Phase 11:
- **One account:** a single Stripe account for the whole deployment, from configuration. Every store's money went into it.
- **No record, no refunds:**
  - Nothing recorded a payment except `Order.PaymentIntentId`.
  - Refunds didn't exist. An admin could cancel a paid order and the money stayed where it was; Phase 9 deferred refunds to this phase.
- **Lost webhooks:** events arrived at one URL. An event for another store's order was acknowledged and dropped. That order was confirmed later by the customer's browser or by the expiry sweep.

## Problem

What should record a payment, so that refunds have somewhere to live, and how is a refund called without holding a transaction over the network or refunding twice? And how does a store take money into its own gateway account, with its secrets protected and its webhook events reaching the right store?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| What records a payment | **One `Payment` per order:** the account that created the intent, the provider's intent id, amount, status, and refunded and pending-refund amounts. `Refund` rows are children of the payment. `rowversion` on the payment | Only `Order.PaymentIntentId`: no place for refund state. Payment state on the order: mixes two modules |
| Refund flow | **Three steps, none holding a transaction over the network:** <br>1. Reserve the amount on the payment and add a Pending refund. This is one save, guarded by `rowversion`; a concurrent refund is retried from a fresh read and rejected if it would exceed the payment. <br>2. Call the gateway outside any transaction, with an idempotency key made from the refund id. <br>3. Record the result in a second save. <br>A gateway that doesn't answer leaves the refund Pending; retrying sends the same key, so the money can't go back twice | One transaction around the gateway call: breaks ADR-0021. No idempotency key: a retry after a timeout could refund twice |
| Cancelling a paid order | **The cancellation commits first** (stock and coupon use go back), **then the remaining amount is refunded.** A refused or unanswered refund shows on the order's payment and is retried from there; the cancellation stands | Refund first, then cancel: the refund could succeed for an order that then can't be cancelled (shipped meanwhile). Blocking the cancellation on the gateway |
| Cancelling an unpaid order (staff) | **The customer's gateway-first path:** the intent is cancelled at the gateway first. If the payment has just succeeded, it is confirmed and the cancellation refused (`OrderAlreadyPaid`) | Cancelling locally (before this phase): a customer paying at that moment left money on a cancelled order, which the later confirmation then ignored |
| Order status after a refund | **Unchanged.** Refunds live on the payment; the order shows its payment's status and refunded amount | A new `OrderStatus.Refunded`: touches the transition table and every consumer for information the payment already holds |
| Which account takes a payment | **A router behind the unchanged Application port** (`IPaymentService`): the store's own account if it has one, otherwise the deployment account. Later calls for an intent (confirm, cancel, refund) go through the account recorded on its payment | Reading the current setting on every call: after a store switches accounts, refunds of earlier payments would go to an account that never took the money |
| Store secrets at rest | **AES-256-GCM** with a key from configuration (`Secrets:Keys:{id}`, `Secrets:ActiveKeyId`). The purpose (store id + kind of secret) is authenticated data, so a ciphertext copied to another store's row doesn't decrypt. Key ids allow rotation. Secrets are write-only: never returned, and the audit log records what changed, not the keys | ASP.NET Data Protection: its key ring defaults to the container's disk and is lost with it, taking every stored secret along. Plain text. A secrets vault: later, at deployment scale |
| Test-mode keys | Accepted in Development and Testing. Elsewhere only with an explicit `Payments:AllowTestModeStoreAccounts`, which logs a startup warning | Always accepted: on a real server, test cards would "pay" real orders — fake payments, silently |
| Webhook routing | **The intent's metadata carries the store id.** An event is verified with the host store's own webhook secret if it has one, otherwise with the deployment's. <br>• A deployment-signed event naming another store is applied in that store's scope (`ITenantScopeRunner`). <br>• A store-signed event is applied only to that store. <br>Applying an event re-asks the gateway, so the metadata claim alone changes nothing | Dropping events for other stores (before). Trusting metadata without a signature |
| A store account that can't be used | `503 PaymentsUnavailable`, never a silent fallback to the deployment account | Fallback: collects a store's money in someone else's account |
| JOD minor units (P-05) | **Unchanged: ×100** (two decimals). This matches Stripe's currency documentation, checked on 2026-09-11: JOD is neither zero-decimal nor a listed special case | — (still to verify against the real account; see Consequences) |
| Card data | **Never stored.** Stripe Elements sends card details from the browser to Stripe; we keep intent ids only. A test guards the model against card-like columns | — |

## Decision

The options marked "Chosen" above.

The migration is additive. Each existing order with a payment intent gets a `Payment`:
- **Account:** intents starting with `pi_fake_` → the fake gateway; any other → the deployment account (there were no store accounts before this phase).
- **Amount:** the order's placed total, in its currency.
- **Status:**
  - Pending → Pending;
  - Paid, Shipped or Delivered → Succeeded;
  - Cancelled → Succeeded if its history shows it was paid first, so an admin can refund that kept money now; otherwise Cancelled.

`Down()` drops the new tables (development only). `MigrationRehearsalTests` checks the backfill.

### D-13

This phase builds the mechanism both D-13 options need:
- per-store resolution;
- encrypted per-store configuration;
- per-payment account routing;
- per-store webhook secrets.

It implements tenant-owned keys as an option. It does not decide whether multi-store production must require each store to connect its own account (the merchant-of-record question) or should use Stripe Connect. Connect would be another adapter behind the same router, holding a connected-account id instead of secret keys.

## Consequences

- **Positive:**
  - Refunds exist, are idempotent, and can't exceed the payment, even under concurrency.
  - Cancelling a paid order gives the money back.
  - A store can be paid into its own Stripe account.
  - Store secrets are encrypted, bound to their store, never returned and never logged.
  - Webhook events reach the store that created the intent.
  - The Application layer doesn't know which provider or account is in use; swapping the gateway means adding an adapter.
- **Negative / limits:**
  - A store without its own account is still paid into the deployment account. That is the behaviour before this phase, and the merchant-of-record question is D-13.
  - Removing a store's account blocks refunds of payments it took until it is reconnected.
  - A Pending refund (gateway silent) needs a manual retry. There is no automatic reconciliation and no refund webhooks yet.
  - Stripe limits the number of webhook endpoints per account, so stores sharing the deployment account share one endpoint (any store's host). Routing by metadata handles that.
  - Only Stripe and the fake gateway are implemented.
  - A refund doesn't give a coupon use back ([ADR-0030](0030-coupon-redemptions.md)); only a cancellation does.
  - **P-05:** JOD totals with three decimals are rounded to 0.01 at the gateway, so the charge can differ from the order total by up to 0.005 JOD. Live JOD payments are not production-ready until the minor-unit behaviour is confirmed on the real Stripe account.

## Revisit when

- D-13 is decided.
- A second payment provider is needed.
- Refund volume calls for reconciliation (refund webhooks, a sweep for Pending refunds).
- A secrets vault becomes available to the deployment.

## Verification

- **Domain:**
  - `PaymentTests`: settling is idempotent; refunds never exceed the payment; a refused refund gives its amount back; results aren't counted twice; only a succeeded payment is refunded; currency and sign.
  - `StorePaymentAccountTests`: key formats and modes; the first link needs a secret; editing without a secret keeps it; switching mode needs a new secret.
  - `DomainExceptionCodeTests`.
- **Application:**
  - `OrderPaymentsTests`: save, gateway, save, in that order, with no transaction; refund of the remainder; refusal; an unanswered gateway then a retry with the same key; concurrency retried from a fresh read; results instead of exceptions when there is nothing to refund.
  - `StorePaymentAccountEditorTests`: encryption bound to the store; the test-key policy; format rules; secrets never read back.
  - `ProcessPaymentWebhookHandlerTests` / `ApplyPaymentEventHandlerTests`: routing to the host store, to another store in its scope, ignoring store-signed events for other stores.
  - `CreateOrderHandlerTests`, `ConfirmOrderPaymentHandlerTests`, `UpdateOrderStatusHandlerTests`, `GetOrderByIdHandlerTests`.
- **Integration:**
  - `PaymentsAndRefundsTests`: payment recorded and settled; partial and full refunds; five concurrent refunds (two succeed); cancelling a paid order refunds it; a webhook on another store's host applied in the order's store and a forged one rejected; a store account stored encrypted, never returned, audited, and edited by the platform in the store's scope.
  - `PaymentAdapterTests`: AES-GCM round trip, purpose binding, rotation, tampering, startup validation; the fake gateway's idempotent refunds and signed webhooks.
  - `PaymentDataRulesTests`: no card-like column anywhere; the payment tables have their reviewed columns only.
  - `TenantIsolationTests`: refund routes.
  - `MigrationRehearsalTests`: the backfill.
