# Notifications: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

1. **No request path may wait on an email provider.** `IEmailSender` stays confined to this module — `ModuleAndContractRuleTests` fails otherwise.
2. A message is enqueued in the **same unit of work** as the change that caused it: no notification without the change, no change without the notification.
3. Outbox payloads are **references only** — ids, enum names, a link origin. Never a token, a rendered email, or personal data.
4. Tokens are issued **at dispatch**, their hash saved before the provider call.
5. Every message type is in the `NotificationMessageTypes` allow-list; the stored type name is resolved from that list alone.
6. Handlers run inside their message's store (or platform) scope, in a fresh DI scope.
7. Delivery is at least once, so every handler must tolerate being run twice.
8. Logs and `LastError` never contain a token, a link or a full email address.
9. Outside Development and Testing, the app refuses to start without a provider unless `Email:Provider=Log` is explicit.

**Files to read first**

`src/Souq.Application/Common/Notifications/Outbox.cs`, `src/Souq.Application/Common/Notifications/Email.cs`, `src/Souq.Application/Features/Notifications/` (all four files), `src/Souq.Infrastructure/Persistence/Outbox/OutboxProcessor.cs`, `src/Souq.Infrastructure/BackgroundJobs/OutboxDispatcherService.cs`, `src/Souq.Infrastructure/Notifications/EmailComposer.cs`, `src/Souq.Infrastructure/DependencyInjection.cs` (the email and notifications sections), `src/Souq.Infrastructure/Persistence/AppDbContext.cs` (domain-event capture), and [ADR-0034](../../11-ADR/0034-notifications-outbox.md).

**Tests that guard the module**

`OutboxPolicyTests`, `IdentityEmailHandlersTests`, `OrderNotificationHandlersTests`, `NotificationUseCasesTests` (all in `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs`), `DomainEventTests`, `NotificationTests`, `TenantIsolationTests`, `ModuleAndContractRuleTests`, and `frontend/src/features/notifications/notificationView.test.js`.

---

## I need to… add a new notification or email type

*Example: tell staff a review is waiting; tell a customer their refund was issued.*

- **Inspect:** `NotificationMessageTypes` (the allow-list), the message records in `src/Souq.Application/Common/Notifications/Outbox.cs`, an existing handler to copy — `StockBecameLowHandler` for a pure in-app fan-out, `OrderEmailHandler` for an email — `NotificationKinds`, `EmailTemplate` and `EmailComposer`, the registrations in `src/Souq.Application/DependencyInjection.cs`, and `describeNotification` in `frontend/src/features/notifications/notificationView.js`.
- **Rules to respect:**
  - Decide the trigger shape first: a **domain event** when an aggregate states a fact with more than one consumer, or a plain **outbox message** when one use case wants one side effect. Events are raised by the entity and captured automatically; messages are enqueued explicitly.
  - The payload carries ids and small values only. Read everything else live in the handler, and give up quietly when the state no longer warrants a message (the identity handlers show the pattern).
  - A new in-app `kind` is a contract with the frontend: add it to `NotificationKinds` and render it in `notificationView.js`, whose default branch shows a generic text.
  - A new `EmailTemplate` needs **both** Arabic and English entries in `EmailComposer` — a missing language would throw at send time and turn into a dead message.
  - Keep in-app rows and the email request in one save, and send the email as its **own** message, so a provider failure retries the email alone (ADR-0034's fan-out rule).
- **Steps:** message record → add it to the allow-list → handler implementing `INotificationMessageHandler<T>` → register it → enqueue from the use case (or raise the event from the aggregate) → notification kind and/or email template → frontend text and link.
- **Tests:** a handler test in `tests/Souq.Application.Tests/Notifications/NotificationHandlersTests.cs`, an `OutboxPolicyTests` case if you touched the allow-list, a `DomainEventTests` case for a new event, and an end-to-end case in `NotificationTests` that dispatches and asserts the row or the captured email. Add the frontend case in `notificationView.test.js`.
- **API:** none — the bell and `/api/notifications` carry any kind.
- **Database:** none; both tables already exist.
- **Security:** no tokens, no free text from users, no personal data in the payload. If the message needs a link, pass the **origin** captured at request time, and build the URL with `StorefrontLinks` at dispatch.
- **Docs and ADR:** README's message table; an ADR only if you introduce a new channel or change the outbox contract.

## I need to… add a channel: SMS or push (FUTURE)

- **Inspect:** `IEmailSender` and its four adapters as the shape to copy, `NotificationEmails` (identity plus composition), `AddEmail` and `AddNotifications` in `src/Souq.Infrastructure/DependencyInjection.cs`, `InfrastructureStartupReport`.
- **Rules to respect:** a channel is a **port in Application, adapter in Infrastructure** (repository rule 3). Reuse the outbox: a new channel is another handler, not a new delivery mechanism. Keep the architecture rule intact — if the new port must also stay out of request paths, extend the test in `ModuleAndContractRuleTests` that confines `IEmailSender` so it covers the new port too. Decide the "no silent fallback" behaviour up front: either the app refuses to start without the provider, or the channel is explicitly optional and says so in the startup report. A phone number is personal data — it belongs to Customers, and consent belongs with marketing preferences.
- **Steps:** port and message types → adapter with a timeout and a redacted logger, throwing a delivery exception on failure → provider selection and validated options in the Infrastructure DI, recorded in the startup report → handlers → per-channel templates.
- **Tests:** a capturing double like `CapturingEmailSender`; handler tests; an integration test that the request path never blocks on the new provider.
- **API:** possibly a preference endpoint; otherwise none.
- **Database:** only if the channel needs stored recipient data — that belongs in Customers.
- **Security:** the same redaction rules; a phone number is masked in logs exactly as an address is.
- **Docs and ADR:** an ADR is required — ADR-0034 lists a second channel as a revisit trigger.

## I need to… change the retry policy

- **Inspect:** `OutboxRetryPolicy` (`MaxAttempts` and the delay table), `OutboxProcessor` (the lease, the claim and `RecordFailureAsync`), `NotificationSettings` and the `Notifications:*` validation in `AddNotifications`.
- **Rules to respect:** the schedule exists to survive a provider outage of hours without looping forever on a poison message; keep both ends of that trade-off in view. The lease (two minutes) must stay comfortably longer than the slowest handler — a provider timeout is 15 seconds today — or two instances will process one message. `NextAttemptAt` and `Attempts` are updated by conditional bulk updates; keep them that way rather than loading entities. A dead message must stay dead: nothing retries it automatically.
- **Steps:** change the delays or `MaxAttempts` → check the delay table still covers `MaxAttempts - 1` entries (the policy clamps to the last delay, so a longer schedule needs more entries to be meaningful) → if you make the interval or lease configurable, add it to `NotificationSettings` with `Validate` bounds, as the existing settings do.
- **Tests:** `OutboxPolicyTests` asserts the exact schedule and the death point — update it deliberately, it is the specification. `NotificationTests` asserts the second attempt is at least ~30 seconds away; adjust if you change the first delay.
- **API:** none.
- **Database:** none.
- **Security:** none, but a longer schedule means a failed reset email can arrive much later — the token is issued at dispatch, so it is still valid, which is the point.
- **Docs:** README's dispatcher section and [Configuration.md](../../09-OPERATIONS/Configuration.md).

## I need to… make templates editable per store (DEFERRED)

- **Inspect:** `EmailComposer` (the whole template table lives there), `EmailContent`, `EmailBranding`, `NotificationEmails.SendAsync`, Platform's store settings.
- **Rules to respect:** ADR-0034 rejected this as content management, so start with an ADR. Templates would become **Platform-owned store configuration**, not Notifications data. Whatever the storage, keep every substituted value HTML-encoded and the subject free of control characters — a store-authored template is untrusted input as far as injection is concerned. Keep a code-level fallback for every template and language: a store that edited only Arabic must still be able to send English. Placeholders become a public contract; validate them when the template is saved, not when the email is sent, so a typo never produces a dead message.
- **Steps:** ADR → storage and validation on the Platform side → a template resolver behind an interface in Application, with the current `EmailComposer` as the fallback implementation → admin UI → migration.
- **Tests:** composer tests for encoding and fallback; a handler test proving an invalid stored template falls back rather than throwing.
- **API:** new store-settings endpoints (`store.settings.manage`).
- **Database:** a new table or settings document; additive, with the fallback meaning no backfill is required.
- **Security:** encoding, and audit every template change.
- **Docs and ADR:** required.

## I need to… honour a customer language preference (DEFERRED)

- **Inspect:** `NotificationEmails` (it decides the culture from `Tenant.DefaultCulture`), `EmailComposer`'s culture lookup and Arabic fallback, `EmailContent.Culture`.
- **Rules to respect:** the preference is Customers' data, not Notifications' — see [../Customers/ChangeGuide.md](../Customers/ChangeGuide.md). Read it live in the handler, never from the payload. Keep the fallback chain explicit: customer preference → store default → Arabic. In-app notifications already follow the visitor's language, because the server stores data and not text — do not break that by rendering text on the server.
- **Steps:** field and endpoint in Customers → the handler passes the culture into `NotificationEmails.SendAsync` → `EmailComposer` already selects by culture and falls back.
- **Tests:** handler test per language; composer fallback test.
- **API:** a profile field.
- **Database:** an additive column on `Customers`.
- **Security:** none beyond normal profile data.
- **Docs and ADR:** ADR-0034 lists this as a revisit trigger; amend it and both module READMEs.

## I need to… investigate a message that never arrived

A runbook, in order:

1. **Is a provider configured?** Check the startup log line from `src/Souq.API/Program.cs` ("Adapters selected: … email …") and its warnings. `Log` means nothing was ever sent; in Development the action link is printed in the log instead.
2. **Is the dispatcher running?** `Notifications:DispatchIntervalSeconds` = 0 disables it (that is the test configuration). With it disabled, rows pile up unsent.
3. **Find the row.** Query `OutboxMessages` by `Type` and a fragment of `Payload` (the ids are in it — `NotificationTests` does exactly this). Then read the state:
   - `ProcessedAt` set ⇒ the handler ran and the provider accepted it; the problem is downstream (spam folder, provider suppression list, wrong address on the account).
   - `ProcessedAt` null, `FailedAt` null, `NextAttemptAt` in the future ⇒ it is waiting for its next attempt; `Attempts` and `LastError` say why.
   - `FailedAt` set ⇒ **dead**. It will never be retried automatically.
   - `LockedUntil` in the future ⇒ another instance holds the lease right now.
   - No row at all ⇒ the message was never enqueued: the use case failed, the save rolled back (a `rowversion` loser detaches its rows deliberately), or the code path does not enqueue anything.
4. **Read `LastError`** — it holds the exception type and a truncated, address-masked message. Provider rejections surface here as the provider's own status code.
5. **Check the handler's silent exits.** Several are by design and leave no trace: an inactive or already-confirmed account (identity emails), an invitation that is no longer pending, an erased customer (order email and in-app row), a missing order, or a cancellation that does not qualify for email.
6. **Check the recipient list for staff messages:** only Active accounts with a usable password hash and a role granting the permission receive them.
7. **To retry a dead or waiting message** there is no UI (DEFERRED). Operationally you can set `NextAttemptAt` to the past and clear `FailedAt` on that row, exactly as `NotificationTests` does to make a message due. Treat that as a manual, audited database intervention — and prefer re-triggering the use case (for example, requesting a new password reset), because tokens are issued at dispatch and a re-run produces a fresh, valid one.
8. **What you will not find:** the recipient address, the link or the token in any log — that is deliberate. Correlate by message id, type and account id instead.

## I need to… stop notifications for a store or an environment

- **Inspect:** `NotificationSettings`, `AddEmail`'s provider selection, `docker-compose.yml`'s `EMAIL_PROVIDER`.
- **Options:** set `Email:Provider=Log` to keep the app running while sending nothing (a startup warning records the choice); set `Notifications:DispatchIntervalSeconds` to 0 to stop processing entirely while messages keep accumulating safely.
- **Rules to respect:** there is **no per-store switch** — the module has no flag in `StoreModules`, and adding one would mean deciding what happens to messages already queued for that store. Silence the outbound side, never the enqueue side: dropping messages at enqueue would break the "no change without its notification" guarantee.
- **Tests:** `NotificationTests` pins that the log fallback is impossible outside Development without the explicit setting.
- **Docs:** [Configuration.md](../../09-OPERATIONS/Configuration.md) and [Troubleshooting.md](../../09-OPERATIONS/Troubleshooting.md).
