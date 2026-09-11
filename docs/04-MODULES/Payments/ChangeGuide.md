# Payments: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

1. Refunded plus pending never exceeds the captured amount, under concurrency. The guard lives on `Payment`, and the payment's `RowVersion` is what makes it hold when two refunds race.
2. No database transaction is open while the gateway is called ([ADR-0021](../../11-ADR/0021-transaction-boundaries.md)). A refund is exactly three steps: reserve and save, call, record and save.
3. Every gateway mutation that can be retried carries an idempotency key derived from our own row (`souq-refund-{tenantId}-{refundId}`), so a retry can never move money twice.
4. Calls about an existing intent go to the account that created it, resolved from the payment's `Gateway`. A store account that can't be used is a `503`, never a silent fallback to the deployment account.
5. Card data never reaches our servers or our database. `PaymentDataRulesTests` fails the build on a card-like column anywhere in the model.
6. Store secrets exist only as ciphertext bound to their store, are never returned by an API, and never enter the audit log.
7. Payments never calls Ordering.

**Files to read first:** `src/Souq.Domain/Entities/Payment.cs`, `src/Souq.Application/Features/Payments/OrderPayments.cs`, `src/Souq.Infrastructure/Payments/PaymentGatewayRouter.cs`, `src/Souq.Infrastructure/Payments/StripeGateway.cs`, `src/Souq.Application/Common/Interfaces/IPaymentService.cs`.

**Tests that guard the module:** `tests/Souq.Domain.Tests/PaymentTests.cs` and `StorePaymentAccountTests.cs`; `tests/Souq.Application.Tests/Payments/OrderPaymentsTests.cs` and `tests/Souq.Application.Tests/Stores/StorePaymentAccountEditorTests.cs`; `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs`, `tests/Souq.IntegrationTests/PaymentAdapterTests.cs`, `PaymentDataRulesTests.cs`, `StripeAmountConverterTests.cs`, `ConfigurationTests.cs`; `frontend/src/features/admin/payments/paymentView.test.js`.

---

## I need to add a payment provider

- **Inspect:** `IPaymentGateway`, `StripeGateway`, `FakeGateway`, `DeploymentPaymentGateway`, `PaymentGatewayRouter`, `PaymentProviderSelector`, `AddPayments` in `src/Souq.Infrastructure/DependencyInjection.cs`, `StripeAmountConverter`, `frontend/src/pages/checkout/stripeClient.js` and `CardPaymentForm.jsx`.
- **Rules to respect:** everything provider-specific stays in Infrastructure — `DependencyRuleTests` fails if Domain, Application or a controller references a provider SDK. The adapter serves **one account** and receives its keys; choosing the account is the router's job. `Name` must be a stable string, because it is written on every payment row and later decides where refunds go — and the router currently treats any name other than `stripe:store` as the deployment account, which is the first thing to revisit for a second provider. Webhook signature verification belongs in the adapter and nowhere else.
- **Steps:**
  1. Implement `IPaymentGateway` for the provider, including `ParseWebhook` returning a `GatewayWebhookEvent` with the order reference and the store id from the intent's metadata, and minor-unit conversion for its API.
  2. Extend `PaymentProvider` and `PaymentProviderSelector.Select` so the provider is chosen explicitly, and add its settings class with an `IValidateOptions<T>` that fails the boot on a missing key.
  3. Register it in `AddPayments` as the `DeploymentPaymentGateway`, and decide whether a *store* account for it exists: if so, `PaymentGatewayRouter.StoreAsync` and `StorePaymentAccount` need a provider dimension — today `Provider` is set to `StorePaymentAccount.Stripe` and never varies, and `PaymentKeyRules` hard-codes Stripe key shapes.
  4. Teach `ForIntentAsync` to route by the recorded name instead of "store versus everything else".
  5. Front end: `GET /api/payments/config` returns only a publishable key today; a provider needing more will change `PaymentClientConfig` and the checkout page.
- **Tests:** an adapter test beside `FakeGatewayTests` in `tests/Souq.IntegrationTests/PaymentAdapterTests.cs`; extend `tests/Souq.IntegrationTests/ConfigurationTests.cs` for the selection rules; `tests/Souq.Application.Tests/Payments/OrderPaymentsTests.cs` should still pass untouched — if it doesn't, the abstraction leaked.
- **API:** no new route; `/api/payments/webhook` stays one endpoint, and the adapter decides whether a payload is its own.
- **Database:** a migration only if store accounts gain a provider column or provider-specific fields.
- **Security:** new secrets follow the existing path — configuration for the deployment account, `ISecretProtector` with a new purpose for store accounts; add the key shapes to the rules so a wrong key is refused before it is stored.
- **Docs and ADR:** [ADR-0031](../../11-ADR/0031-payments-and-refunds.md) lists "a second payment provider" as a revisit trigger; write the ADR.

---

## I need to change the refund rules

For example a refund window, a maximum, or an approval step.

- **Inspect:** `Payment.RequestRefund`, `Payment.CompleteRefund`, `Payment.FailRefund`, `Refund`, `OrderPayments.RefundAsync` / `RetryRefundAsync` / `SendAsync` / `ApplyAsync`, `RefundOrderCommand` and its validator, `OrderRefundsController`, `frontend/src/features/admin/payments/paymentView.js` (`refundProblem`).
- **Rules to respect:** the amount rule belongs on `Payment`, not in the handler — that is what makes it hold when two refunds race. Keep the reserve-before-call order: the amount must be held on the payment before the gateway is asked, or a crash between the two loses the accounting. Keep results idempotent: `Complete` on an already-succeeded refund is a no-op by design, because a retry after a successful call must not count twice. A rule that is a *policy* rather than an invariant (a 30-day window, an approval) belongs in the Application layer, where it can be a `Result` failure with a code instead of an exception.
- **Steps:**
  1. Invariants → the entity, with a domain exception and, if the frontend must distinguish it, an explicit code.
  2. Policies → `OrderPayments.RefundAsync` before `RequestRefund`, returned as `Error.BusinessRule` with a new code.
  3. Keep the "everything left" path returning a result, not throwing: `UpdateOrderStatusHandler` calls it after a cancellation that has already committed and must not be undone.
  4. Mirror the rule in the admin form's `refundProblem` so the UI fails fast — the server still decides.
- **Tests:** `tests/Souq.Domain.Tests/PaymentTests.cs` for an invariant; `tests/Souq.Application.Tests/Payments/OrderPaymentsTests.cs` for a policy, including the concurrency retry; `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs` for the end-to-end and the five-parallel-refunds case; the frontend test for the form rule.
- **API:** new codes are additive; the outcome shape (`RefundOutcome`) should not change — the UI branches on `status`.
- **Database:** a migration only if a refund gains a field (an approver, a window anchor).
- **Security:** `store.payments.manage` gates the routes, and both commands are audited. If you add an approval step, audit the approval too. Remember that cancelling a paid order refunds it with only `orders.manage`.
- **Docs and ADR:** [ADR-0031](../../11-ADR/0031-payments-and-refunds.md) records the three-step design; amend it if the steps change.

---

## I need to support a new currency, or change minor-unit handling

This is where P-05 lives; read it in [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md) first.

- **Inspect:** `CurrencyInfo`, `Money`, `StripeAmountConverter`, `PersistenceConventions.MoneyColumnType`, `tests/Souq.IntegrationTests/StripeAmountConverterTests.cs`, `tests/Souq.Domain.Tests/MoneyTests.cs`, and the frontend's currency decimals helper used by `paymentView.js`.
- **Rules to respect:** there are **two** currency tables and they must agree in intent: `CurrencyInfo` decides what an amount may look like in our database, `StripeAmountConverter` decides what the provider is charged. When they disagree, the customer is charged something other than the order total — that is exactly P-05 for three-decimal currencies, and the `MGA` mismatch noted in [README.md](README.md). Storage is `decimal(19,4)`, which covers every ISO-4217 exponent; rounding happens once, in `Money.FromCalculation`.
- **Steps:**
  1. Add the currency's exponent to `CurrencyInfo.Exceptions` only if it differs from 2.
  2. Decide the provider's multiplier for it, from the provider's own documentation, and reflect it in the converter; a three-decimal currency the provider treats as three-decimal needs a real branch, not the current ×100 default.
  3. Add both cases to the converter's theory data and to `MoneyTests`, including a value whose last digit would be lost.
  4. Check the store settings path: a store's currency is validated as an ISO code and snapshotted onto each order, so existing orders keep their currency and are unaffected.
  5. Before enabling live payments in a three-decimal currency, confirm the multiplier against the real account — a wrong multiplier charges a tenth or ten times the price.
- **Tests:** `StripeAmountConverterTests` is the table that documents the decision; `InventoryAndOrderTests` already proves JOD's three decimals survive storage and arithmetic.
- **API:** currency is a string in every money-bearing DTO; nothing changes structurally.
- **Database:** none — the column type already covers four decimals.
- **Security:** none, but this is a money-correctness change: review it as one.
- **Docs and ADR:** [ADR-0014](../../11-ADR/0014-money-precision.md) owns the precision rule and already carries the JOD warning; update it and close or re-scope P-05 in the roadmap.

---

## I need to change how a store's gateway account is configured (D-13)

For example adopting Stripe Connect, or requiring every store to connect its own account.

- **Inspect:** `StorePaymentAccount`, `PaymentKeyRules`, `StorePaymentAccountEditor` (in the **Platform** folder, `src/Souq.Application/Features/Stores/StorePaymentAccounts.cs`), `src/Souq.Application/Features/Platform/TenantPaymentAccounts.cs`, `PaymentGatewayRouter`, `AesGcmSecretProtector`, `SecretPurposes`, `StorePaymentsController`, `frontend/src/pages/admin/Payments.jsx`.
- **Rules to respect:** the editor is one implementation used by both the store path and the platform path, and the platform path runs **inside the target store's scope** so encryption, the write guard and the tenant filter all apply ([ADR-0024](../../11-ADR/0024-platform-administration.md)). Secrets are write-only. A connected-account model (Connect) replaces keys with an account id but must keep the same router seam — Application must not learn which model is in use.
- **Steps:**
  1. Decide D-13 and record it before writing code: the answer changes who the merchant of record is.
  2. For Connect: add an adapter that talks to the platform account on behalf of a connected account, give `StorePaymentAccount` the account id, and make `PaymentGatewayRouter.StoreAsync` build that adapter. The key-shape rules and the secret-protection path become unused for those stores — leave them working for stores that still hold keys.
  3. For mandatory store accounts: checkout must fail clearly when no account is connected, instead of falling back to the deployment account. That is a new error path in the router and a new store-readiness rule; check what [Platform](../Platform/README.md) considers a store ready to open.
  4. Either way, decide what happens to payments already taken by the old arrangement — today `Gateway` records only "store" or "deployment", so old intents follow the store's *current* account.
- **Tests:** `tests/Souq.Application.Tests/Stores/StorePaymentAccountEditorTests.cs`; `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs` (the store-account test covers encryption, audit and the platform path); add a routing test for the new model — note that no test names the router today.
- **API:** the account DTO is the contract for both admin pages; adding fields is additive, removing the key fields is not.
- **Database:** a migration for new columns; a data migration if keys are replaced by account ids.
- **Security:** this is the highest-value secret in the system. Keep the purpose binding, keep secrets out of responses and audit metadata, and keep the startup warnings that tell an operator when no encryption key is configured.
- **Docs and ADR:** amend [ADR-0031](../../11-ADR/0031-payments-and-refunds.md) or write a successor; update the D-13 row in the roadmap and the decision log.

---

## I need to handle a new webhook event

For example refund events, or disputes.

- **Inspect:** `StripeGateway.ParseWebhook`, `GatewayWebhookEvent`, `PaymentWebhookEvent`, `PaymentGatewayRouter.ParseWebhookAsync`, `ProcessPaymentWebhookHandler` and `ApplyPaymentEventHandler` (both in [Ordering](../Ordering/README.md)), `FakeGateway.ParseWebhook` and `FakeGateway.Sign`.
- **Rules to respect:** the event's payload is never trusted on its own — the current design re-asks the gateway for the truth, which is what makes store-id metadata harmless. Verification stays in the adapter; routing between stores stays in the handler. A store-signed event must never be applied to another store. Unknown events are acknowledged with 200, so the gateway stops retrying.
- **Steps:**
  1. Widen `GatewayWebhookEvent` (or add a sibling) so the event kind survives the adapter, and keep returning `null` for events we don't care about.
  2. For a refund event, the natural home is a new Payments use case that settles the matching `Refund` by provider refund id — it is the missing half of "a pending refund needs a manual retry". Keep `Payment.CompleteRefund` and `FailRefund` as the only way to change the amounts, since both are idempotent.
  3. Keep the handler's tenant routing intact: resolve the store, then run the work in its scope.
  4. Add the event to the fake gateway so tests can sign and send it.
- **Tests:** `tests/Souq.Application.Tests/Orders/ProcessPaymentWebhookHandlerTests.cs` for routing and rejection; `tests/Souq.IntegrationTests/PaymentsAndRefundsTests.cs` for a signed event applied end to end; `FakeGatewayTests` for the signature.
- **API:** the endpoint and its 200-on-unknown behaviour stay as they are.
- **Database:** none for refunds; a new concept (a dispute) needs a table and an owner module.
- **Security:** never widen the endpoint's trust: no branch may act on the payload without verification, and the webhook stays anonymous-but-signed in the reviewed public list in `AuthorizationBoundaryTests`.
- **Docs and ADR:** [ADR-0031](../../11-ADR/0031-payments-and-refunds.md) lists refund webhooks as deferred — record the change there.

---

## I need to rotate or recover the store-secret encryption key

- **Inspect:** `SecretsSettings`, `SecretsSettingsValidator`, `AesGcmSecretProtector`, `SecretPurposes`, `PaymentGatewayRouter.StoreAsync`, the `Secrets__*` variables in `docker-compose.yml` and `.env.example`.
- **Rules to respect:** the ciphertext format carries the key id, so old keys keep working for decryption while `Secrets:ActiveKeyId` decides what new writes use. Removing a key that rows still reference turns those stores' payments into a `503`, loudly — that is the intended failure, not a silent fallback. The purpose string binds a ciphertext to its store; changing the purpose format invalidates every stored secret.
- **Steps:**
  1. Add the new key alongside the old one in `Secrets:Keys` and point `Secrets:ActiveKeyId` at it; startup validation refuses a mismatch or a key that isn't 32 base64-encoded bytes.
  2. Re-save each store's secret (the editor re-encrypts with the active key) before removing the old key — there is no bulk re-encryption job.
  3. Only then remove the old key.
  4. If a key is lost, the honest recovery is to reconnect each store's account: nothing can decrypt those rows again.
- **Tests:** `tests/Souq.IntegrationTests/PaymentAdapterTests.cs` covers rotation, a removed key, tampering and startup validation.
- **API:** none.
- **Database:** none. The ciphertext column already holds the key id.
- **Security:** the key is an environment secret like the JWT key; it never goes into `appsettings` in the repository.
- **Docs and ADR:** [ADR-0020](../../11-ADR/0020-configuration-and-secrets.md) and [Configuration.md](../../09-OPERATIONS/Configuration.md).
