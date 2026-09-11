# ADR-0034: Notifications: a transactional outbox with a background dispatcher, domain events for order and stock facts, branded localized email, and in-app notifications

- **Status:** Accepted (implemented in Phase 14), 2026-09-11. Resolves **D-14**.
- **Builds on:**
  - [ADR-0021](0021-transaction-boundaries.md): no transaction held across a network call.
  - [ADR-0022](0022-tenancy-enforcement.md): the tenant filter and write guard.
  - [ADR-0023](0023-sessions-and-credentials.md): tokens are stored as hashes only.
  - [ADR-0024](0024-platform-administration.md): store settings and branding.
  - D-15: background work runs in .NET hosted services.
- **Date:** 2026-09-11
- **Related modules:** Notifications; Ordering and Inventory (the facts it reacts to); Identity (account email); Platform (store branding)
- **Related ADRs:** builds on [ADR-0021](0021-transaction-boundaries.md), [ADR-0022](0022-tenancy-enforcement.md), [ADR-0023](0023-sessions-and-credentials.md) and [ADR-0024](0024-platform-administration.md); implements the outbox promised by [ADR-0001](0001-target-architecture.md) and [ADR-0012](0012-service-extraction-strategy.md) and the domain events deferred by [ADR-0009](0009-ddd-usage.md); replaces the missing-provider warning of [ADR-0020](0020-configuration-and-secrets.md) with a startup refusal; its events come from the transitions of [ADR-0029](0029-orders-lifecycle.md) and the stock rules of [ADR-0026](0026-inventory-reservations.md)

## Context

Before Phase 14:
- **Email was sent inline**, after the commit, by registration, forgot-password, resend-verification, invitations and payment confirmation.
  - The request waited on the provider (a 15-second timeout).
  - A provider failure was logged and swallowed, so the message was lost.
- **Templates** were hard-coded in Arabic with the "Marka" brand.
- **Without a configured provider**, non-local environments silently logged "not sent". The only trace was a warning in the startup report.
- **There were no in-app notifications.**

## Problem

How is a side effect of a committed change delivered without the request waiting on a provider, and without the message being lost when the provider fails? And how do those messages carry each store's brand and language instead of one hard-coded identity?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Delivery | **A transactional outbox** (`OutboxMessages`) written in the same `SaveChanges` as the change that caused it, processed by a hosted dispatcher | Inline sending: waits on the provider and loses messages. A message broker: too much operational weight for a modular monolith. Hangfire: D-15 keeps hosted services until scheduling needs grow |
| What the outbox stores | **References only:** <br>• an account or order id; <br>• the request origin for links; <br>• enum names, not numbers. <br>Payload ≤ 4000 characters; the stored type name must come from a fixed allow-list | Rendered emails or links: a reset, verification or invitation token stored raw in a table would undo hashing tokens at rest, and would copy personal data |
| Tokens | **Issued at dispatch.** The handler issues the token and saves its hash, then calls the provider. A retry issues a new token that supersedes the previous one | A token issued at request time and carried in the payload: a plaintext secret at rest. Encrypting payloads with the secret protector: key management for every email |
| Side effects of domain facts | **Domain events** (`OrderStatusChanged`, `StockBecameLow`) raised by the aggregates. `AppDbContext.SaveChanges` writes them to the outbox in the same transaction. When the save fails, the added rows are detached and the events cleared | Notification calls from each handler: six transition paths (webhook, expiry sweep, admin, customer…) and one is easy to forget. An EF interceptor: can't cleanly detach on failure without shared state |
| Delivery semantics | **At least once.** A two-minute lease (`LockedUntil`) is claimed with a conditional update, so two instances never process the same row; a crashed worker's lease expires | Exactly once: needs a distributed transaction with the provider |
| Retries | **8 attempts:** 30 s, 2 min, 10 min, 30 min, 1 h, 3 h, 6 h, then dead (`FailedAt`, kept for diagnosis). An unknown type is dead at once. Processed rows are purged after 14 days | Unlimited retries: a poison message loops forever |
| Fan-out and retries | **The event handler writes the notification rows and the follow-up email request in one save.** The email is its own outbox message and is retried alone | One handler doing both: an email failure would retry it and duplicate the notifications |
| Sender identity | **From name:** the store's display name in its default language. **Address:** the deployment's verified sender. **Reply-To:** the store's contact email | Per-store sending domains: each needs SPF/DKIM verification. Revisit with custom domains (Phase 23) |
| Templates | **Localized (Arabic, English)** by the store's default language, with a branded header (logo or name, primary colour) and a plain-text alternative. Every value is HTML-encoded, and the subject has no control characters | Templates each store can edit: content management, later |
| Links in order emails | **The public tracking page on the store's primary domain**, with the scheme and port of the frontend URL | The request host: a webhook or the expiry sweep has no customer-facing host |
| In-app notifications | **One `Notification` row per recipient account** (store-owned, tenant-filtered): <br>• customer ⇐ changes to their order's status; <br>• staff with `orders.view` ⇐ new paid orders; <br>• staff with `inventory.view` ⇐ low stock. <br>A row holds a kind and small data, which the frontend renders in the visitor's language. The bell polls the unread count every minute | Server-rendered text: fixed to one language. WebSocket/SignalR push: infrastructure for a small gain |
| Which order changes email the customer | **Paid, Shipped, Delivered; Cancelled only when the order was paid or the store cancelled it** | Every change: a customer who cancels, or an expired checkout, needs no email |
| No silent fallback | **Outside Development/Testing the API refuses to start without an email provider**, unless `Email:Provider=Log` is set explicitly (warned in the startup report). docker-compose passes `EMAIL_PROVIDER` | A warning only (the old behavior) |
| The request never waits on the provider | **Only notification handlers depend on `IEmailSender`**; an architecture test forbids it anywhere else in Application | Convention |

## Decision

The options marked "Chosen" above. Migration `Phase14Notifications` is additive: the `Notifications` and `OutboxMessages` tables with their indexes. No existing data changes.

## Consequences

- **Positive:**
  - No request waits on the email provider, and no message is lost to a provider outage.
  - Reset, verification and invitation tokens are never stored raw.
  - Emails carry the store's name, look and language.
  - Customers and staff see in-app notifications.
  - A transition path can't forget its notification side effects.
- **Negative / limits:**
  - At least once: a crash between the provider accepting a message and the row being marked processed sends it again (the newest link supersedes).
  - A reset link now arrives a few seconds after the request; the dispatcher polls every 5 seconds by default.
  - One polling dispatcher per instance. Heavy volume would need tuning or a broker.
  - Email language is the store's default language; customers have no language preference yet.
  - The bell polls every minute; there is no push.
  - Staff hear about new orders at payment, not at placement, because unpaid orders aren't actionable.
  - Low-stock alerts fire when a reservation or adjustment crosses the threshold; changing the threshold doesn't alert.
  - Dead messages have no operator screen yet (Phase 17/23).

## Revisit when

- Customers get a language preference, or stores need editable templates.
- A second channel (SMS, push) is needed: another handler behind the same outbox.
- Stores get their own sending domains (Phase 23).
- Volume outgrows one polling dispatcher.

## Verification

- **Domain:** `DomainEventTests`:
  - order transitions raise events with from, to and the actor; unsaved or refused transitions don't;
  - the low-stock event fires once per drop below the threshold;
  - notification guards, and a notification is read once.
- **Application:**
  - `OutboxPolicyTests`: the retry schedule, dead after the last attempt, the allow-list and enum names.
  - `IdentityEmailHandlersTests`: the token is issued at dispatch and its hash saved first; changed accounts get nothing; invitation renewal with the inviter's name; provider failures reach the dispatcher.
  - `OrderNotificationHandlersTests`: customer and staff fan-out, the email rules per cancellation, an erased customer, low stock, and the order email's template and tracking link.
  - `NotificationUseCasesTests`.
  - The auth, account and payment-confirmation tests now assert the outbox message or the domain event instead of an inline email.
- **Architecture:** only the Notifications module depends on `IEmailSender`, and Notifications is a registered module.
- **Integration:**
  - `NotificationTests`:
    - the request succeeds while the provider is blocked;
    - a failure is retried with backoff, then delivered; a message is dead after its last attempt;
    - no token or email address appears in the logs or in the stored error;
    - the order lifecycle produces notifications and branded emails; a customer's own cancellation sends no email;
    - a save that loses a `rowversion` race writes no event;
    - there is no silent console fallback outside Development.
  - `TenantIsolationTests`: the notification route.
  - The existing auth, staff and platform-invitation tests read their links after one dispatch pass.
- **Frontend (Vitest):** `notificationView`.
