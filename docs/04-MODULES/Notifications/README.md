# Notifications module

> **Code:** `src/Souq.Application/Features/Notifications/`, `src/Souq.Application/Common/Notifications/`, `src/Souq.Infrastructure/Persistence/Outbox/`, `src/Souq.Infrastructure/BackgroundJobs/OutboxDispatcherService.cs`, `src/Souq.Infrastructure/Notifications/`, `src/Souq.API/Controllers/NotificationsController.cs` · **Decisions:** [ADR-0034](../../11-ADR/0034-notifications-outbox.md) (resolves D-14), [ADR-0021](../../11-ADR/0021-transaction-boundaries.md), [ADR-0023](../../11-ADR/0023-sessions-and-credentials.md) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Telling people what happened — in the store's voice, through the right channel, without ever making a customer's request wait for an email provider and without losing a message when that provider is down. It is a module of its own because it reacts to facts rather than deciding them: it owns a delivery mechanism (a transactional outbox with a background dispatcher), the email templates, and the in-app notification inbox, and it makes no business decisions at all. That reactive shape is also why Modules.md calls it the strongest candidate for extraction into a service.

## Responsibilities

- The **transactional outbox**: `OutboxMessages`, written in the same `SaveChanges` as the change that caused it, and a hosted dispatcher that processes it under a lease with bounded retries and a retention purge.
- Capturing **domain events** raised by aggregates into that outbox (the mechanism lives in `AppDbContext`).
- **Email**: composing localized, branded messages and handing them to the configured provider; issuing reset, verification and invitation tokens at dispatch time so no raw token is ever stored.
- **In-app notifications**: one row per recipient account, and the API the bell polls.

## Not this module's job

| Concern | Owner |
|---|---|
| Deciding that an order was paid, shipped or cancelled | Ordering (it raises the event) |
| Deciding that stock is low | Inventory |
| Who may sign in, token lifetimes and hashing rules | Identity (`User` issues and hashes its own tokens; this module only asks it to) |
| Store name, logo, colours, contact email, default language | Platform (read from the `Tenants` row at send time) |
| Customer contact details and whether a profile is erased | Customers |
| Any business rule | every other module — a handler only reads state and delivers |

## Business concepts

- **Outbox message** — a durable intent to notify, stored as a reference (ids plus a link origin), never as rendered content or a secret.
- **Domain event** — a fact an aggregate raised (`OrderStatusChanged`, `StockBecameLow`), captured into the outbox by the same save.
- **Lease** — the two-minute claim that keeps two app instances from processing one message.
- **Dead message** — one that failed its last allowed attempt; kept for diagnosis, never retried automatically.
- **In-app notification** — a row addressed to one account, holding a kind and small data, rendered by the frontend in the visitor's language.
- **Sender identity** — the store's display name, the deployment's verified address, and the store's contact email as Reply-To.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `Notification` | aggregate root | `src/Souq.Domain/Entities/Notification.cs` | a recipient account id > 0; kind non-empty and ≤ `Notification.KindMaxLength` (40); data non-null and ≤ `Notification.DataMaxLength` (1000); `MarkRead` is once-only — a second read keeps the first timestamp |
| `NotificationKinds` | constants | same file | the fixed contract with the frontend: `order.status`, `order.new`, `stock.low` |
| `InvalidNotificationException` | domain exception | `src/Souq.Domain/Exceptions/InvalidNotificationException.cs` | code `InvalidNotification` |
| `IDomainEvent`, `OrderStatusChanged`, `StockBecameLow` | domain events | `src/Souq.Domain/Events/DomainEvents.cs` | ids and small values only; the consumer reads live state, so no personal data sits in the outbox |
| `OutboxMessage` | technical building block, **not** a domain entity | `src/Souq.Infrastructure/Persistence/Outbox/OutboxMessage.cs` | the type name must come from the allow-list; payload ≤ `OutboxMessage.PayloadMaxLength` (4000) or the enqueue throws; `LastError` ≤ 500 |

**Why the recipient is a bare account id.** `Notification.RecipientUserId` has no foreign key: accounts are never deleted (they are disabled or anonymized), and an account's `TenantId` is nullable, which would force an awkward composite key.

**Why events carry no text.** The handler reads the order, product and customer live at dispatch. That keeps personal data out of the outbox and means a message dispatched minutes later reflects reality — including a customer erased in the meantime.

**Concurrency.** `Notifications` has no `rowversion`; `MarkRead` is naturally idempotent. Outbox state moves through conditional bulk updates (`ExecuteUpdateAsync`) rather than tracked entities, which is what makes two instances safe.

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| My notifications | `ListMyNotificationsQuery` (+ `ListMyNotificationsValidator`) | `ListMyNotificationsHandler` | any signed-in account in a store | `GET /api/notifications` |
| Unread badge | `CountMyUnreadNotificationsQuery` | `CountMyUnreadNotificationsHandler` | any signed-in account | `GET /api/notifications/unread-count` |
| Mark one read | `MarkNotificationReadCommand` | `MarkNotificationReadHandler` | recipient only | `POST /api/notifications/{id}/read` |
| Mark all read | `MarkAllNotificationsReadCommand` | `MarkAllNotificationsReadHandler` | caller's own rows | `POST /api/notifications/read-all` |

The rest of the module is **not** MediatR: outbox handlers implement `INotificationMessageHandler<TMessage>` and are invoked by the dispatcher, not by a request.

| Message | Handler | What it does |
|---|---|---|
| `PasswordResetRequested` | `PasswordResetEmailHandler` | active account only; `User.GenerateResetToken`, save the hash, then send |
| `EmailVerificationRequested` | `EmailVerificationEmailHandler` | skips an already-confirmed or inactive account; issues and saves, then sends |
| `AccountInvited` | `InvitationEmailHandler` | pending invitation only; `User.RenewInvitation`, save, then send with the inviter's name |
| `OrderStatusChanged` | `OrderStatusChangedHandler` | in-app row for the customer, `order.new` for staff with `orders.view` on payment, and an `OrderEmailRequested` message when the change deserves an email — all in one save |
| `StockBecameLow` | `StockBecameLowHandler` | `stock.low` rows for staff with `inventory.view`, with the product name in the store's default culture |
| `OrderEmailRequested` | `OrderEmailHandler` | the customer's order email with the store's branding and a tracking link |

## Public contracts

| Contract | Path | Consumers |
|---|---|---|
| `INotificationOutbox` | `src/Souq.Application/Common/Notifications/Outbox.cs` | Identity (`ForgotPassword.cs`, `Register.cs`, `VerifyEmail.cs`), `src/Souq.Application/Common/Accounts/Accounts.cs`, and this module's own `OrderStatusChangedHandler` |
| Message records `PasswordResetRequested`, `EmailVerificationRequested`, `AccountInvited`, `OrderEmailRequested` | same file | the enqueuing use cases |
| `INotificationMessageHandler<TMessage>` | same file | implemented here, resolved by `OutboxProcessor` |
| `NotificationMessageTypes`, `OutboxRetryPolicy`, `NotificationMessageDispatch` | same file | the processor and the tests |
| `IEmailSender`, `EmailMessage`, `EmailDeliveryException`, `EmailTemplate`, `EmailBranding`, `EmailContent`, `ComposedEmail`, `IEmailComposer`, `IStoreOrigins` | `src/Souq.Application/Common/Notifications/Email.cs` | **this module only** — an architecture test enforces it |
| `INotificationQueries` | `src/Souq.Application/Features/Notifications/NotificationUseCases.cs` | this module's in-app queries |
| `IOutboxProcessor` | `src/Souq.Infrastructure/Persistence/Outbox/OutboxProcessor.cs` | the hosted dispatcher and the integration tests |

Domain events are the other half of the inbound contract: any aggregate may raise one, and this module decides what it means.

## Dependencies

**Uses** (handlers read live state through other modules' domain ports — accepted by ADR-0034, which says handlers read state through repositories):

| What | Used by | Note |
|---|---|---|
| Identity's `IUserRepository` and `User` token methods | the three identity handlers, plus staff fan-out through `ListActiveIdsByRolesAsync` | the handlers **write** the Identity aggregate (issuing and saving a token hash) — the deepest cross-module reach in the system, and the price of "no raw token at rest" |
| Ordering's `IOrderRepository` | `OrderStatusChangedHandler`, `OrderEmailHandler` | |
| Customers' `ICustomerRepository` | both order handlers (recipient account, contact email, erased check) | |
| Catalog's `IProductRepository` | `StockBecameLowHandler` (product name) | |
| Platform's `ITenantRepository`, `ITenantDirectory` | `NotificationEmails` branding; the processor resolving a message's store | |
| `Permissions`, `RolePermissions` | staff fan-out by permission, not by role name | shared kernel |
| `IStorefrontLinks` / `StorefrontLinks`, `IStoreOrigins` | link building at request time and at dispatch time | |

**Used by:** Identity and the shared account-invitation service enqueue messages; Ordering and Inventory reach this module only by raising domain events — neither knows it exists.

**Enforced vs convention.** `ModuleAndContractRuleTests` contains a rule written for this module: **nothing outside `Features/Notifications` and `Common/Notifications` may depend on `IEmailSender`**, so no request path can wait on a provider. The same suite maps Notifications to its feature folder and forbids references to other feature folders — which holds, because everything this module touches is a **domain** port, and those are invisible to the tests. That the handlers write another module's aggregate is convention, documented here.

## Data ownership

| Table | EF configuration | Tenant | Concurrency | Indexes that encode rules |
|---|---|---|---|---|
| `Notifications` | `NotificationConfiguration` | `ITenantOwned` (query filter + write guard) | none | `(TenantId, RecipientUserId, ReadAt)` for the unread badge; `(TenantId, RecipientUserId, CreatedAt)` for the inbox |
| `OutboxMessages` | `OutboxMessageConfiguration` | `TenantId` nullable, **no query filter and no foreign key** — the dispatcher deliberately reads across every store, and platform messages have no store | none (conditional updates instead) | filtered index on `NextAttemptAt` for rows still waiting (it does not grow with processed rows); filtered index on `ProcessedAt` for the purge |

Other modules' data read at dispatch: `Users`, `Orders`, `Customers`, `Products` with translations, `Tenants` and `TenantDomains`. Other modules' data **written**: the token hash and expiry on `Users` (see Dependencies). Migration `Phase14Notifications` is additive: both tables and their indexes, no existing data touched.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/notifications` | `[Authorize]` | — | list (optional `unreadOnly`, paged) |
| GET | `/api/notifications/unread-count` | `[Authorize]` | — | badge count |
| POST | `/api/notifications/{id}/read` | `[Authorize]` | — | mark one read |
| POST | `/api/notifications/read-all` | `[Authorize]` | — | mark all read |

No route accepts an account id: the recipient is always the caller. There is no admin or platform view of the outbox (**DEFERRED**). On a platform host these routes answer 404, because `TenantAvailabilityMiddleware` refuses a non-platform endpoint in platform scope — platform accounts have no in-app inbox.

**Frontend.** `frontend/src/components/notifications/NotificationBell.jsx`, mounted in `frontend/src/components/layout/Navbar.jsx` (storefront) and `frontend/src/pages/admin/AdminLayout.jsx` (store admin). It polls the unread count every `POLL_MS` (60 s) while a session exists, loads ten rows when opened, marks a row read on click and navigates to the link from `describeNotification` in `frontend/src/features/notifications/notificationView.js` — which builds the text from kind + data in the visitor's language, so switching language re-renders old notifications correctly.

## Security and permissions

- **No raw secret is ever stored.** The outbox holds ids and a link origin; the reset, verification and invitation tokens are generated inside the handler, their hashes saved **before** the provider call, and a retry issues a fresh token that supersedes the old one (`User.HashToken`, ADR-0023/0034).
- **Redacted logging** (`LogRedaction`): senders log the template kind, a masked recipient and the HTTP status; a provider error body is logged only on failure, with every address inside it masked and the text truncated to 500 characters. The processor logs message id, type, attempt count and exception type — never a recipient, link or token. The stored `LastError` goes through the same truncation. `NotificationTests` greps every captured log line and the stored error for the token and the address.
- **Links only in development.** With no provider configured, `ConsoleEmailService` prints the action link **only** when `ConsoleEmailOptions.IncludeLinksInLog` is set, which happens in Development alone; elsewhere it logs a warning that nothing was sent.
- **No silent fallback outside local environments.** `AddEmail` throws at startup when no provider key is set unless `Email:Provider=Log` is given explicitly, and then records a startup warning. The API simply does not boot — the alternative was a production store where password resets vanish unnoticed.
- **Header safety:** `EmailSenders.CleanName` strips control characters, angle brackets and quotes from the store-chosen sender name, and `EmailComposer` strips control characters from the subject; every value is HTML-encoded into the body, and the logo URL is only rendered when it parses as an absolute http(s) URI.
- **Ownership:** a notification id belonging to another account (or another store) is a 404; mark-all touches only the caller's rows.
- **Staff fan-out is by permission** (`RolePermissions.RolesGranting`), and only Active accounts with a usable password hash receive rows — pending invitations and erased accounts are skipped.

## Tenant behaviour

Every message is processed **inside its store's scope**: `OutboxProcessor` resolves the store through `ITenantDirectory` and runs the handler in a brand-new DI scope via `TenantScopes.RunAsync`, so the tenant filter and write guard behave exactly as in an HTTP request. A message with no store (platform account invitations, platform password resets) runs through `TenantScopes.RunPlatformAsync`, where only platform accounts are visible. Branding, language and the link origin are per store: `NotificationEmails` reads the `Tenants` row at send time (so a rename applies to the next message), and `StoreOrigins` builds the link from the store's primary domain with the scheme and port of the configured frontend URL. `ITenantDirectory.FindByIdAsync` does **not** filter by store status, so queued messages for a suspended or archived store are still delivered.

## Events and background work

- **Capture:** `AppDbContext.SaveChangesAsync` calls `CaptureDomainEvents` before saving, turning pending events on tracked entities into `OutboxMessages` rows in the same transaction; on failure the rows are detached and the events cleared, so a change that lost a `rowversion` race publishes nothing.
- **Dispatcher:** `OutboxDispatcherService` (hosted) ticks every `Notifications:DispatchIntervalSeconds` (default 5, `0` disables it) and purges processed rows older than `Notifications:RetentionDays` (default 14) once an hour. Failures are logged and never stop the service.
- **Processing:** batches of 50 due rows, each claimed with a conditional update setting a two-minute lease; handlers run outside any transaction (ADR-0021); success marks the row processed; failure schedules the next attempt — 30 s, 2 min, 10 min, 30 min, 1 h, 3 h, 6 h — and the eighth failure (`OutboxRetryPolicy.MaxAttempts`) marks it dead. An unknown message type is dead immediately.
- **Delivery is at least once**; handlers are written so a repeat is harmless.

Full step-by-step traces of the outbox cycle and the order-status fan-out: [FeatureMaps.md](../FeatureMaps.md).

## External integrations

| Provider | Adapter | Selection | Secrets |
|---|---|---|---|
| Resend (HTTP API) | `ResendEmailService` + `ResendOptions` | first choice when `Resend:ApiKey` is set | `Resend:ApiKey`; `Resend:From` is the verified sending address (not secret) |
| Brevo (HTTP API) | `BrevoEmailService` + `BrevoOptions` | when Resend is absent and `Brevo:ApiKey` is set | `Brevo:ApiKey`; sender address from `Brevo:SenderEmail`, falling back to `Gmail:Username` / `Gmail:SenderEmail` |
| Gmail SMTP (MailKit) | `GmailEmailService` + `GmailSmtpOptions` | when neither API key is set and `Gmail:AppPassword` is | `Gmail:AppPassword`; port 465 means implicit TLS, otherwise STARTTLS |
| Log only | `ConsoleEmailService` + `ConsoleEmailOptions` | Development and Testing automatically, elsewhere only with `Email:Provider=Log` | — |

The two HTTP providers get a 15-second timeout from their `IHttpClientFactory` client (which also rotates connections, so a DNS change is honoured); the SMTP adapter sets no explicit timeout. All of them throw `EmailDeliveryException` on failure, so the dispatcher retries. The chosen provider is recorded in `InfrastructureStartupReport` and logged once by `src/Souq.API/Program.cs`, together with any warnings. `docker-compose.yml` passes `EMAIL_PROVIDER`, `RESEND_API_KEY`, `BREVO_API_KEY`, `GMAIL_APP_PASSWORD`, `GMAIL_USERNAME`, `BREVO_SENDER_EMAIL`, `GMAIL_SMTP_PORT` and `FRONTEND_URL`; `.env.example` documents them.

**Templates** (`EmailComposer`, Infrastructure): seven templates (`EmailTemplate`) in Arabic and English, chosen by the store's default language with Arabic as the fallback, each with an HTML body (branded header, button, note, footer) and a plain-text alternative. Shipping details (tracking number, carrier) are appended for the shipped template when present. Order emails additionally carry an itemised block — each `EmailLine` with its purchased name, quantity and line total, then the subtotal, discount, shipping and total. The composer renders whatever it is handed: the handler sends the order's **frozen** figures and omits the discount and shipping keys when there are none, so the template never decides a commercial question.

## Tests

| Level | Class | Coverage |
|---|---|---|
| Domain | `DomainEventTests` | which transitions raise events and which do not, one low-stock event per crossing, notification guards, read-once |
| Domain | `DomainExceptionCodeTests` | the `InvalidNotification` code |
| Application | `OutboxPolicyTests` | the retry schedule, death at the last attempt, the allow-list, enum names in payloads |
| Application | `IdentityEmailHandlersTests` | token issued at dispatch with its hash saved first, store branding, accounts that changed since the request get nothing, invitation renewal, provider failure reaching the dispatcher |
| Application | `OrderNotificationHandlersTests` | customer and staff fan-out in one save, the email matrix per cancellation, an erased customer, low stock, the order email's template and tracking link |
| Application | `NotificationUseCasesTests` | marking own notification read, another account's is 404, mark-all scoped to the caller |
| Integration | `NotificationTests` | the request succeeds while the provider is blocked; backoff then delivery; dead after the last attempt and never retried; no token or address in logs or `LastError`; the order lifecycle end to end with branded email and a tracking link on the store's host; a customer's own cancellation sends no email; a `rowversion` loser writes no event; no silent console fallback outside Development |
| Integration | `TenantIsolationTests` | another store's notification id is a 404 and stays unread |
| Architecture | `ModuleAndContractRuleTests` | `IEmailSender` confined to this module; the module is mapped |
| Frontend | `frontend/src/features/notifications/notificationView.test.js` | text and link per kind, unknown-kind fallback, badge capped at 99+ |

Test infrastructure: `SouqApiFactory` disables the dispatcher (`Notifications:DispatchIntervalSeconds` = 0) and swaps in `CapturingEmailSender`, which can block or fail on demand; tests call `DispatchNotificationsAsync` to run exactly one cycle.

**Test gaps:** no test covers a purge run, a lease expiring and being re-claimed, or two processors racing; the Brevo, Gmail and Resend adapters have no tests of their own (`CapturingEmailSender` replaces them).

## Failure modes

| Situation | Exception or error code | HTTP | Handling |
|---|---|---|---|
| No email provider configured outside Development/Testing | `InvalidOperationException` from `AddEmail` | — | the API refuses to start; `Email:Provider=Log` is the explicit opt-out |
| `Email:Provider=Log` in a real environment | — | — | nothing is sent; a startup warning is logged |
| Provider rejects or times out | `EmailDeliveryException` | — | retried per `OutboxRetryPolicy`, then dead |
| Payload over 4000 characters at enqueue | `InvalidOperationException` | 500 `ServerError` | fails the causing request loudly rather than dropping the message |
| Unregistered message type at dispatch | dead immediately, type recorded in `LastError` | — | |
| No handler registered for a message | `InvalidOperationException` in `NotificationMessageDispatch` | — | normal retry path, then dead |
| Store row missing for a message's `TenantId` | `InvalidOperationException` | — | retried |
| Notification data over 1000 characters | `InvalidNotificationException` | — | the message retries and eventually dies; `NotificationData.Short` prevents it for product names |
| Notification id of another account or store | `Error.NotFound` | 404 `NotFound` | |
| Guest calling a notification route | `[Authorize]` | 401 `Unauthenticated` | |
| Notification routes on a platform host | `TenantAvailabilityMiddleware` | 404 `NotFound` | |
| Dispatcher cycle throws | logged | — | the loop continues on the next tick |
| Two instances processing | conditional lease claim | — | exactly one wins per row |

## Common change scenarios

Add a notification or email type · add a channel · change the retry policy · make templates store-editable · honour a customer language preference · investigate a message that never arrived. Details in [ChangeGuide.md](ChangeGuide.md).

## Known limitations

- **At least once, never exactly once.** A crash between the provider accepting a message and the row being marked processed re-sends it.
- **Latency floor:** a reset link arrives seconds after the request (5-second poll by default).
- **One polling dispatcher per instance**, with no distributed lock beyond the per-row lease; volume beyond that needs tuning or a broker.
- **Dead messages have no operator screen** and no alert — they are found only by querying `OutboxMessages`.
- **In-app notification rows are never purged.** Only processed outbox rows are; `Notifications` grows without bound, and erasing a customer leaves their rows behind.
- **Email language is the store's default**, not the reader's; there is no customer preference.
- **Templates are code**, identical for every store apart from name, logo and colours.
- **One sending address for the whole deployment**; per-store domains need SPF/DKIM (ADR-0034 points at a later phase).
- **Email only.** There is no SMS or push channel, and the bell polls instead of receiving push.
- **The outbox is the only visibility.** There is no delivery log of what was sent, and processed rows disappear after the retention window; Modules.md's claim that this module owns "the delivery log" describes an intent, not the code.
- **Handlers write Identity's aggregate** to issue tokens — powerful, and worth remembering before extracting this module.
- **Messages for suspended or archived stores are still delivered.**

## Future evolution

- **A second channel (SMS, push)** — **FUTURE**: another handler behind the same outbox, per ADR-0034's revisit list.
- **Per-store editable templates** — **DEFERRED**: it is a content-management feature.
- **Customer language preference** — **DEFERRED**; the field would live on `Customer`.
- **Push instead of polling (WebSocket/SignalR)** — **DEFERRED** as infrastructure for a small gain.
- **An operator screen for dead messages** — **DEFERRED** (roadmap points at Phase 17/23).
- **Per-store sending domains** — **DEFERRED** until custom domains (each needs SPF/DKIM verification).
- **Moderation notifications for Reviews** — **DEFERRED**, now unblocked by this module's outbox.
- **Extraction into a service** — **FUTURE**: outbox rows become broker messages; the module consumes events only, which is what keeps this possible.
