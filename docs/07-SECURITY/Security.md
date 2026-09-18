# Souq: Security Architecture

> **Scope:** why the system is built this way, and what it does and does not protect against. The control-by-control catalog — with implementation paths, tests and gaps — is [SecurityControls.md](SecurityControls.md). Identity and permissions: [AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md). Tenant isolation design: [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md).
> **Principles:** least privilege · secure by default · the server is authoritative · defence in depth · no secrets in git · no sensitive data in logs.
> **Labels:** unlabelled statements describe the code today. **PLANNED** items are scheduled in [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md); **DEFERRED** and **FUTURE** items are not.

## 1. Assets and threats

| Asset | Main threats | Primary controls |
|---|---|---|
| Tenant data (catalog, customers, orders) | Cross-tenant access, IDOR | Host-only tenant resolution, a named query filter plus a write guard, composite foreign keys, 404 for a foreign resource ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md), SEC-TEN-01…11) |
| Customer accounts | Credential stuffing, reset-link theft, token theft | BCrypt, hashed single-use tokens, short access tokens with rotation and reuse detection, rate limits, no secrets in logs (SEC-AUTHN-01…14) |
| Platform control plane | A store admin escalating to the platform | A separate platform host, `tid`-to-host binding, platform-only permissions, every platform request audited (SEC-AUTHZ-07, SEC-AUTHZ-09) |
| Money flows | Tampered totals, double charge, card-data exposure | Server-side pricing, idempotent confirmation and refunds, signature-verified webhooks, Stripe Elements so card data never reaches us (SEC-PAY-01…12) |
| Storefront visitors | XSS through uploads or content | Content-sniffed uploads with server-chosen extensions, `nosniff` and a sandboxing CSP on `/uploads`, plain-text catalog descriptions, React escaping (SEC-UP-01…07) |
| Secrets | Leakage through the repo, logs or images | user-secrets and environment variables, validated at startup, redacted logging, `.env` git-ignored, store payment keys encrypted at rest (SEC-CFG-01…08) |

## 2. Authentication (summary)

- **Passwords:** BCrypt with a per-password salt. 8–128 characters with letters and digits.
- **Access token:**
  - An HS256 JWT valid for 15 minutes (`Jwt:ExpiryMinutes`, accepted range 5–60), held in JavaScript memory only.
  - Validated for issuer, audience, lifetime (30-second clock skew) and signing key.
  - Also bound to the host through `tid`, and to the account's security stamp.
- **Refresh token:**
  - 256-bit random, stored only as a SHA-256 hash.
  - Rotated on every use, with family reuse detection and a 10-second grace for parallel tabs — and the grace requires the family to still hold an active token, so it cannot outlive a password change, a logout or a detected reuse (corrected in M9; before that a just-rotated token survived a password change for ten seconds).
  - Carried in an `HttpOnly` / `Secure` / `SameSite=Strict` cookie scoped to `/api/auth`.
- **Account protection:**
  - lockout after 5 failures for 15 minutes;
  - a timing-safe path for unknown emails, and a forgot-password answer that never differs;
  - single-use hashed reset, verification and invitation tokens;
  - rate limits per host and client address.
- **Known limits:** logout revokes the refresh family but does not rotate the security stamp, so an access token captured before logout works until it expires; the lockout response reveals that an account exists. See SEC-AUTHN-03 and SEC-AUTHN-11.
- Details: [AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md), [ADR-0023](../11-ADR/0023-sessions-and-credentials.md).

## 3. Authorization, tenant isolation, IDOR

- **Roles:** PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff and Customer, mapped to permissions in one table (`RolePermissions`).
  - Store roles hold no platform permission and platform roles no store permission; this is unit-tested.
  - Endpoints declare the permission they need (`[HasPermission]`), and every endpoint must declare an explicit decision ([ADR-0019](../11-ADR/0019-authorization-foundation.md)).
- **Platform vs store:** platform accounts have no store and exist only on platform hosts. A platform token (no `tid`) is rejected on store hosts, and a store token on platform hosts.
- **IDOR prevention, in layers:**
  1. **Tenant isolation** ([ADR-0022](../11-ADR/0022-tenancy-enforcement.md)):
     - the store comes from the host only, with no fallback store in production;
     - a named EF filter hides other stores' rows and **throws** when no store is in scope — it never returns every store's rows;
     - a write guard stamps and verifies `TenantId` on every save;
     - tenant-scoped composite foreign keys make a cross-store reference impossible in the database itself;
     - a token's `tid` must match the host.
  2. **Ownership checks inside the use case**, so a customer only sees and confirms their own orders — a 404 otherwise.
  3. **Identity always from the token** through `ICurrentUser`; commands carry no customer id or tenant id to tamper with.
- **Automated proof** (details and test classes in [SecurityControls.md](SecurityControls.md) §2–§4):
  - `AuthorizationBoundaryTests` enumerates the routing table: every endpoint declares a decision, the anonymous set equals a reviewed list, every declared permission exists, anonymous callers get 401 and customers 403 on every permission-protected endpoint, and every platform endpoint is absent (404) on a store host.
  - `AuthorizationMatrixTests` checks role × endpoint over HTTP, and that staff without a customer profile cannot shop.
  - `TenantIsolationTests` puts store B's admin, customer and visitors on B's host and asks for store A's resources by their real ids: 404 everywhere, empty listings, refused writes that reference A's rows, A's data unchanged afterwards, A's token rejected with 401, the write guard and the missing-tenant filter failing loudly, and A's guest basket token opening nothing. A completeness test forces every new id-bearing endpoint into that table.
  - `TenancyRuleTests` (architecture) forbid a business entity without `ITenantOwned`, `IgnoreQueryFilters` outside the one reviewed platform query type, raw SQL outside migrations, a bulk `ExecuteUpdate`/`ExecuteDelete` outside the reviewed call sites (they never reach `SaveChanges`, so the write guard never sees them), a tenant-owned entity without the filter or the foreign key, a cross-tenant foreign key that omits `TenantId`, and a use case that sets the tenant.
  - `AuthSessionTests` prove platform and store sessions stay apart.
- **What this does not cover:** the architecture tests police the Application layer's module boundaries, not the Domain layer, so a handler that reaches another module's domain repository or entity is not caught by any test.

### Area notes

- **Platform area:** `PlatformAdministrationTests` covers provisioning end to end, and a platform admin without `platform.users.manage` gets 403. A store admin changing settings or staff can only touch their own store: those requests carry no store id, and B's admin gets 404 for A's staff account.
- **Catalog:** every admin catalog endpoint requires `catalog.manage`. Draft and archived products, and products in hidden categories, are invisible to anonymous callers by id and by slug. Image ids are resolved inside a product of the caller's store: an unknown image is 404 on remove, and a foreign id in a reorder is 422, with the image untouched. Slugs and SKUs are unique per store only, so one store cannot probe another's catalog through a uniqueness conflict.
- **Inventory:** corrections and thresholds need `inventory.manage` on top of `inventory.view`, and every correction is audited with its delta and reason. Store B's admin gets 404 for A's product on both endpoints. Reservations and ledger rows reference the item through composite tenant-scoped foreign keys, and the expiry sweep runs inside each store's own scope.
- **Customers** ([ADR-0027](../11-ADR/0027-customer-profile-and-erasure.md)): `/api/account` carries no customer id — the profile is always the caller's (`cid`). An address id outside the caller's own book is 404 on update, delete and both default routes, and `400 AddressNotFound` at checkout. Blocking, exporting and erasing need `customers.manage` on top of `customers.view`, and they are audited, as are a customer's own export and erasure. Self-erasure requires the current password. Erasure anonymizes the profile and the login in one save, revokes refresh tokens and forgets the cached security stamp, so existing access tokens stop working immediately on that instance. Exports contain no credential material.
- **Basket** ([ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)): the guest cookie is `HttpOnly`, `Secure`, `SameSite=Strict` and scoped to `/api/basket`; it holds 256 random bits and only its SHA-256 is stored. Requests carry no basket id at all, so there is nothing to enumerate. A store-A guest token on store B's host opens an empty basket, the cookie is cleared, and updates and deletes are 404. Coupon codes can only be priced through `GET /api/basket/quote`, behind the coupon-preview limit — and only against the caller's own basket, so a code cannot be probed for the discount it would give on an amount the caller invents. The second, anonymous evaluator that accepted a client-sent subtotal (`GET /api/coupons/apply`) was removed in M8 (TD-06); unvalidated and unbounded, it answered a nine-digit subtotal with an eight-digit discount, and it skipped the per-customer limit, so it could promise a discount checkout would refuse; basket writes have their own limit, because every add from a new guest creates a row. Checkout never trusts a client amount.
- **Orders** ([ADR-0029](../11-ADR/0029-orders-lifecycle.md)): tracking uses a random 128-bit token (`GET /api/orders/track/{token}`), not the sequential id, so orders cannot be enumerated (finding B8). It returns the order number, status, history, carrier and tracking number — no identity, address, amounts, lines, notes or actors. A malformed or unknown token is 404, and so is store A's token on store B's host. Customer cancellation is owner-only and applies only to unpaid orders, and the gateway is asked first, so a customer can never cancel an order that was just paid. Order numbers are per store. Staff notes and actors are shown only to callers with `orders.view`. Every status change records the acting user on its history row.
- **Coupons** ([ADR-0030](../11-ADR/0030-coupon-redemptions.md)): a use is taken inside the checkout transaction on a fresh read under the coupon's `rowversion`, so parallel checkouts cannot exceed a global or per-customer limit; this is tested with concurrent checkouts. The per-customer limit counts uses for the signed-in customer, never an id sent by the client. Redemptions show order numbers and customer names, so they are listed only with `promotions.manage`, within the host's store. A used coupon cannot be deleted (`409 CouponInUse`).
- **Payments** ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)): see §8.
- **Shipping** ([ADR-0032](../11-ADR/0032-shipping-methods.md)): tracking links come only from https templates set by staff, with the tracking number URL-encoded into them, so no `javascript:` or plain-http link can reach a customer. The destination country that decides which methods apply is read from the customer's own address book on the server, never from the request.

## 4. Transport, CORS, headers, rate limiting

- **HTTPS and HSTS:** **TLS termination is still the deployment's job** — nginx here listens on port 80 and this repository ships no certificate. HSTS *is* now sent (`UseHsts`, 30 days, configurable via `Security:HstsMaxAgeDays`), but only on requests the API sees as https, which is why the forwarded-scheme fix below matters. Deliberately **no `includeSubDomains` and no `preload`**: stores bring their own domains, and HSTS outlives the relationship with a domain while preload is near-permanent — widening it is a deployment decision made knowingly, not a default. Deliberately **no *UseHttpsRedirection***: the container listens on http behind a terminator, so redirecting in the app would break internal probes; redirect at the edge instead. Note that HSTS is never sent for `localhost`/`127.0.0.1` (ASP.NET's `ExcludedHosts`), which is the usual reason it seems missing in local testing. Automated TLS for custom domains is still **PLANNED** for Phase 23.
- **CORS:** an allowlist from `Cors:AllowedOrigins`. When the key is unset, `CorsOrigins.For` (`src/Souq.API/Http/CorsOrigins.cs`) falls back to `http://localhost:5173` in Development and Testing only, and to no origin at all elsewhere; `ConfigurationTests` covers both cases. The policy does not allow credentials, so a cross-origin session is impossible in any case. The Docker/nginx deployment is same-origin and needs no CORS.
- **Headers:** `SecurityHeadersMiddleware` sets `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options` and `Permissions-Policy` on **every** API response, errors included — it writes them in `OnStarting`, because a header assigned directly is lost when the exception handler rebuilds the response, leaving only the happy paths protected. API responses also carry `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'` — this is a JSON interface, so nothing should ever load from it. `/uploads/*` keeps its stricter `default-src 'none'; sandbox`: the middleware does not overwrite a policy that is already set, because `OnPrepareResponse` runs *before* `OnStarting` and a blind write would have quietly weakened uploads. Swagger (Development only) is exempt, being a real page with scripts.
- **SPA headers** live in `frontend/nginx.conf`, scoped to `location /` only — applying them server-wide would duplicate the API's own, and browsers *intersect* two CSP headers into something stricter than either intended. The SPA's CSP ships as **`Content-Security-Policy-Report-Only`** on purpose: the external origins are known from the source (Google Fonts, injected per store by `storeTheme.js`; Stripe's script and frames at checkout), but runtime behaviour such as the inline styles Stripe Elements injects cannot be proven without a browser session, and an enforced policy that is wrong breaks a store silently *at checkout*. Flipping it to enforcing after one browser pass is the remaining step.
- **Rate limiting:** the ASP.NET Core limiter with a fixed window per `host|client IP`.
  - Auth endpoints 10/min; refresh 30/min; coupon preview 30/min; basket writes 120/min. All configurable under `RateLimiting`.
  - Exceeding a limit returns `429 TooManyRequests` with `Retry-After`, in the standard error contract.
  - The limiter is in-process: with several instances the effective limit multiplies. Checkout, review posting, the webhook and all other authenticated endpoints have no limit.
- **Forwarded headers:** `X-Forwarded-For` and `X-Forwarded-Proto` are honoured only from proxies in `ForwardedHeaders:KnownNetworks` (the compose stack sets the Docker network), so a direct client cannot spoof its address to escape a rate limit or fake `https`. nginx used to overwrite `X-Forwarded-Proto` with **its own** scheme, which is always `http` here — so behind an outer TLS terminator the API believed it was on plaintext, never sent HSTS, and generated `http://` links. It now preserves an incoming value and falls back to its own scheme (verified: the header appears only when the outer proxy says `https`). **If nginx itself is the internet-facing edge, drop that `map` and use `$scheme`** — otherwise you are trusting a client-supplied header. The compose file also publishes the API on port 5201 for diagnosis; a request that bypasses nginx that way may appear to come from inside the trusted range (SEC-HTTP-06).
- **Host header:** the API serves `/api` and `/uploads` only after the host matches `TenantDomains` (or a platform host); an unknown host is 404. That is what makes host-based email links safe. nginx forwards the original `Host`.

## 5. Input validation, XSS, and uploads

- **Validation:** FluentValidation for shape and ranges through the MediatR pipeline; business invariants in the Domain; database constraints as the final guard.
- **XSS:** React escapes by default, and `dangerouslySetInnerHTML` appears nowhere in `frontend/src` — enforced, not just a convention: `frontend/eslint.config.js` sets `react/no-danger` to `error`, and the lint runs in CI (SEC-UP-07). Product and category descriptions are **plain text** (up to 4000 characters, rendered escaped). A sanitized rich HTML description is **DEFERRED** ([ADR-0025](../11-ADR/0025-catalog-model.md)): it needs a server-side sanitizer before storage and a decision, not a lint exception.
- **Uploads** (Phase 0 finding B3, [ADR-0016](../11-ADR/0016-upload-validation.md)):
  1. The type is detected from **magic bytes**: JPEG, PNG, GIF, WebP for images; MP4, WebM for video; ICO for store favicons only. Anything else — HTML, SVG, scripts, disguised files — is rejected with `UnsupportedMediaType`.
  2. The stored extension is **derived from the detected type**, never from the client's filename, and the stored name is a random GUID.
  3. The client's `Content-Type` header is ignored for every security decision.
  4. Size limits: 5 MB images, 50 MB video, 2 MB branding assets in the use case, with HTTP ceilings of 6 MB, 55 MB and 3 MB and `client_max_body_size 55m` in nginx.
  5. The static file server serves only the allowlisted media types from the uploads folder, with `nosniff` and a sandboxing CSP, so an unexpected file can never execute as a page on our origin.
  6. SVG is not accepted, because it can carry script.
- **Tenant-prefixed keys:** files are stored under `tenants/{id}/…` and the resolution middleware serves them only on the owning store's host. Two deliberate exceptions: the platform host serves any store's files, and pre-Phase-2 files under `/uploads/{folder}` belong to the default store and stay public — they are catalog media with unguessable names.
- **Product gallery:** up to 10 images per product, each through the same pipeline, into the store's prefix. **Removing an image does not delete the file** and no cleanup job exists; it was deferred in Phase 5 to "Phase 6 or later" and no later phase schedules it (**DEFERRED**, SEC-UP-06); the file keeps its unguessable name and is still served only on the owning store's host.
- **PLANNED (Phase 23):** cloud blob storage. Re-encoding images to strip metadata and neutralize polyglots was considered in ADR-0016 and is **FUTURE** — no phase schedules it.

## 5a. Database privileges

The application connected as `sa` — full control of every database on the server, which sets the blast radius of every other vulnerability here. Three identities now exist instead (runtime: `db_datareader` + `db_datawriter`; migration: `db_ddladmin` + read/write, used only at startup; administrative: never in an application's configuration), created by `scripts/sql/least-privilege-logins.sql`.

The permissions were **measured**, not assumed: `scripts/verify-least-privilege.sh` runs the real application against them on throwaway infrastructure, greps the log for permission denials (which is how background-job failures surface at all), and then proves the runtime identity is refused when it tries to create a table, drop `Orders`, add itself to `db_owner`, create a database, or read another application's database on the same server.

**Not yet applied anywhere.** The Compose bundle is a demo and still uses `sa`. See [DatabasePrivileges.md](DatabasePrivileges.md) §6 for what remains, and R-12 in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

## 6. Secret management

| Secret | Development | Docker / production |
|---|---|---|
| Connection string | `dotnet user-secrets` | environment variable |
| JWT signing key | user-secrets | environment variable (at least 32 bytes) |
| Deployment Stripe secret and webhook secret | user-secrets | environment variables (`Stripe:SecretKey`, `Stripe:WebhookSecret`) |
| A store's own Stripe keys | entered through the API, encrypted at rest | encrypted at rest with `Secrets:Keys:{id}` / `Secrets:ActiveKeyId` from the environment |
| Email provider keys | user-secrets | environment variable (`Resend:ApiKey`, `Brevo:ApiKey`, `Gmail:AppPassword`) |
| Admin bootstrap credentials | user-secrets (`Seed:AdminEmail` / `Seed:AdminPassword`) | environment variable, set once, removed after the first start |
| Platform owner bootstrap | user-secrets (`Seed:PlatformOwnerEmail` / `Seed:PlatformOwnerPassword`) | environment variable, set once, removed after the first start |

- **Fail fast** ([ADR-0020](../11-ADR/0020-configuration-and-secrets.md)): settings are typed options validated before the database is touched. Startup is refused by a missing connection string; a JWT key shorter than 32 bytes, a missing issuer or audience, or a lifetime outside its range; a missing payment provider outside Development and Testing; missing Stripe keys when Stripe is selected; a malformed secrets key or an active key id that does not exist; a non-absolute storage path; and **a missing email provider outside Development and Testing** unless `Email:Provider=Log` is set explicitly. Messages name the key and never print its value.
- **Store payment keys at rest:** AES-256-GCM with a key from the environment. The store id and the kind of secret are authenticated data, so a ciphertext copied into another store's row does not decrypt. Key ids allow rotation — the old key must stay until every store re-saves its keys, because nothing re-wraps existing ciphertexts. Keys are write-only: only the last four characters are ever returned, and the audit log records what changed, not the values.
- **Development conveniences never run implicitly elsewhere:** the fake payment gateway and the log-only email adapter are implicit in Development and Testing only; reset links in the log and the development admin exist in Development only. Anything unsafe that is enabled explicitly (`Payments:Provider=Fake`, `Email:Provider=Log`, `Payments:AllowTestModeStoreAccounts`, a missing webhook secret) is logged as a warning at every start.
- `src/Souq.API/appsettings.json` contains **no secrets and no personal data**; `.env` is git-ignored and `.env.example` holds placeholders only.
- **Automated in CI:** [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs a secret scan and a check for live payment keys on every run. It blocks a merge only once branch protection is switched on in GitHub, which is a repository setting the owner has to change — and it has not executed at all since before M13, because GitHub Actions is billing-blocked (G-01, TD-31, [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)).

## 7. Password reset security

- The token is **32 bytes from a CSPRNG**, base64url-encoded, and appears only in the email link.
- The database stores **only its SHA-256 hash**, so a database read never yields a usable reset token. The token has 256 bits of entropy, so a fast hash is sufficient; no salt or slow hash is needed.
- Tokens are valid for 2 hours, are **single-use**, and are cleared on success. A successful reset also clears the lockout, confirms the address, rotates the security stamp and revokes every session.
- **The token is generated when the email is dispatched**, not when the request arrives ([ADR-0034](../11-ADR/0034-notifications-outbox.md)): the outbox row holds a user id and the request's origin, never a secret. A retry issues a new token that supersedes the previous one.
- Forgot-password always answers 200 with the same message, so accounts cannot be enumerated there.
- **API logs never contain the link or the token.** The console email adapter prints the link **only in Development**; elsewhere it logs "not sent" with a masked address. Outside Development and Testing the API refuses to start with no email provider at all, unless `Email:Provider=Log` is chosen explicitly.
- **Caveat:** the link itself carries the token in a query string, and the reset page is served by nginx, whose default access log records the request line. Treat the web container's access log as sensitive until that is changed (SEC-LOG-07).
- The link points at the host the request came from, so a store's customer returns to that store. An unknown host never reaches the use case (§4).

## 8. Payments

- **PCI scope:** card details go from the browser straight to Stripe (Stripe Elements / Payment Intents). Our servers never receive, store or log a PAN, CVV or expiry date. This keeps Souq in the lightest PCI category (SAQ A), as long as the checkout page is served securely. A test scans the whole EF model for card-like columns and pins the payment tables' columns.
- **Integrity:**
  - The order total is computed **server-side** from catalog prices, coupon rules and shipping rules; the client's cart total is display only, and the shipping country comes from the customer's own address book.
  - The payment intent is created **after** the order is saved. If intent creation fails, the order is cancelled and its reserved stock and coupon use released immediately, and the caller gets `503 PaymentUnavailable`.
  - Confirmation re-reads the intent's status from the provider instead of trusting the client, and it is idempotent; a customer/webhook race is resolved through the order's `rowversion`.
- **Refunds** need `store.payments.manage`, are audited with the acting user, cannot exceed the payment under concurrency, and carry an idempotency key built from the refund id, so a retry after a timeout cannot refund twice. Another store's order is a 404.
- **Webhooks:** signature verification lives **inside the payment adapter**; an invalid signature is `400 InvalidSignature`, and with no secret configured the event is acknowledged and ignored with a warning. An event signed by a store's own account is applied only to that store; routing by intent metadata applies only to deployment-signed events, and applying an event re-asks the gateway, so metadata alone proves nothing.
- **Per-store accounts:** a store may connect its own Stripe account; its keys are encrypted at rest and bound to the store (§6). Later calls for an intent — confirm, cancel, refund — go through the account **kind** recorded on its payment (`stripe:store` / `stripe:deployment` / `fake`), which for `stripe:store` resolves to the store's **current** account. The payment does not record *which* store account took it, so replacing a store's keys — including the ordinary test→live switch — between charge and refund sends the refund to an account that never took the money, where it fails and cannot be retried. This page claimed the opposite until M6 verified it against the code; the limitation is recorded in [Payments/README.md](../04-MODULES/Payments/README.md) and as TD-50. A store account whose secrets cannot be decrypted answers `503 PaymentsUnavailable`; payments never fall back to the deployment account silently. **D-13 is still open:** whether multi-store production requires every store to connect its own account, or uses Stripe Connect ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)).
- **No silent fake payments:** the fake gateway confirms every payment without money. It is implicit only in Development and Testing; elsewhere the API refuses to start unless `Payments:Provider=Fake` is set explicitly, which is logged at every start. Test-mode Stripe keys for a store account are refused outside Development and Testing unless `Payments:AllowTestModeStoreAccounts` is set, which also logs a warning.
- **Amount conversion:** the adapter converts `Money` to the provider's minor units (`StripeAmountConverter`, unit-tested). Stripe documents currencies as two-decimal unless listed as zero-decimal, so a three-decimal currency is sent ×100, rounded away from zero to the nearest 0.01.
  - **Open risk (P-05):** this must be verified on the real Stripe account before live payments in a three-decimal currency. If the account treats it as three-decimal, the multiplier must be 1000, and getting it wrong would charge a tenth of the price.

## 9. Logging and sensitive data

**Never logged:**
- passwords or hashes;
- tokens (JWT, refresh, reset, verification, invitation, provider API keys);
- reset, verification or invitation links;
- card data (we never have it);
- full provider response bodies on success;
- personal data beyond what is needed.

**How that is achieved:**
- One line per request with method, path **without the query string**, route template, status and duration. Headers (including `Authorization`) and bodies are never logged.
- Every log written inside a request, after authentication, carries `CorrelationId` (the W3C trace id, also returned as `X-Correlation-Id` and as `traceId` in error bodies), the store (`TenantId`, or `Area` on the platform host) and `UserId`; use-case logs add `UseCase`. Use-case logging records the name and duration only, never the payload.
- Email adapters log the message **kind**, the **masked** recipient (`a***@example.com`) and the HTTP status. A provider error body is logged on failure only, truncated to 500 characters with any address inside it masked. The same truncation and masking applies to the error stored on a failed outbox message.
- Configuration diagnostics log which adapters were selected and which settings are unsafe for real customers — never a value.
- EF Core SQL text is off by default (`Microsoft.EntityFrameworkCore` at `Warning`), and parameter values are never logged: *EnableSensitiveDataLogging* appears nowhere in the repository.
- Unexpected exceptions are logged server-side with the stack trace; clients receive a generic message with no type, message or stack.
- A blocked cross-tenant write and a blocked audit mutation are logged at **Critical**. Refresh-token reuse is a warning with the user id only. Refresh tokens appear only in the `Set-Cookie` header, never in a body or a log.
- Integration tests prove that no password, JWT or `Authorization` value appears in any log, and that no reset token or address appears in the logs or in a stored outbox error.

**Secrets that travel in a URL** are handled on both sides of the proxy.

In the API, `SensitivePath.Redact` replaces the value of any sensitive route parameter — today the order
tracking token — with `***`, keeping the route template for aggregation. It redacts by **value**, so the token
cannot survive elsewhere in the path, and it is shared by the request logger and the exception handler because
the first version of this fix redacted only the former and left the error path leaking. It also has a
**pre-routing fallback**: route values do not exist until `UseRouting` has run, so an exception thrown in an
earlier middleware used to reach the exception handler with the raw path, token included.

In nginx, the default `combined` format logs the whole request line including the query string, and the email
links carry their tokens there (`/reset-password?token=…`, `/verify-email?token=…`,
`/accept-invitation?token=…`). The `souq_safe` format now strips the query string, redacts `/track/…`, and
never writes `Referer` — because a same-origin asset request made from `/reset-password?token=…` carries that
full URL in the header, which would reintroduce the leak by another door. It keeps the real path rather than
the rewritten one, so SPA pages remain visible for diagnosis.

**Still yours:** an outer load balancer, CDN or ingress in front of this stack keeps its own access log, and
nothing here can configure it. Apply the same rule wherever TLS terminates.

## 10. Audit logging

[ADR-0024](../11-ADR/0024-platform-administration.md). `AuditEntries` rows record:
- who: the user id and role, and the area (`Platform`, `Store` or `System`);
- which store is affected;
- the action (`tenant.suspended`, `store.staff.invited`, `inventory.adjusted`, …);
- the target type and id (the natural key for creates);
- metadata as JSON, chosen field by field by the request itself;
- the client address (only from trusted proxies) and the correlation id.

**How rows are written:**
- `AuditBehavior` handles every request that implements `IAuditable`.
- The entry is staged into the current unit of work before the handler runs, so it commits atomically with the change.
- If the handler saved nothing (a query), the entry is flushed after success. A failure discards it, unless the handler had already committed it — in which case the trail keeps the attempt.
- The request body is never copied, so passwords, tokens and file contents never reach the log.

**What is audited today:**
- **Every platform request, reads included** — this is what makes the platform owner's access "explicit and audited", and an architecture test enforces it for the Platform and Reporting feature folders.
- **Store administration:** settings and branding, staff invitations and status, store payment accounts, refunds, customer status, export and erasure (including a customer's own), stock adjustments and thresholds, review moderation and review settings, catalog status changes, deletes and gallery changes.

**Not audited** (the gap in SEC-AUTHZ-10): order status changes — the acting user is recorded on the order's own history row instead — coupon create/update/delete, shipping-method create/update/delete, product create and update, and customers' own profile and address changes.

The write guard rejects any update or delete of an `AuditEntry`. The platform reads the log through `GET /api/platform/audit` with `platform.audit.view`; the platform console shows it at `/platform/audit`. There is no store-facing viewer: Phase 4 deferred one to Phase 17, but Phase 17 closed without it and no roadmap phase schedules it now.

## 11. Phase 0 findings: disposition

A historical record of the Phase 0 findings. Open security work is tracked in [SecurityControls.md §12](SecurityControls.md#12-gaps-and-risks) and [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md), and where a row here points there, that page is the one to trust.

| ID | Finding | Status today |
|---|---|---|
| B1 | Default admin `Admin@123` seeded in every environment | **Fixed.** Development falls back to the documented development credentials when `Seed:*` is unset. Every other environment creates an account only from `Seed:AdminEmail`/`Seed:AdminPassword` (and the platform-owner pair), and refuses to start if that password is shorter than 12 characters or equals the development one. Otherwise no account is created and a warning is logged. An existing account's password is never overwritten. The password policy itself (letters and digits) is **not** applied to seeded passwords |
| B2 | Reset links in logs; personal data in email logs | **Fixed** in the API (§7, §9); see the nginx access-log caveat |
| B3 | Upload stored XSS | **Fixed** (§5) |
| B4 | Long-lived JWT in `localStorage`, no revocation | **Fixed:** a 15-minute access token in memory, the refresh token in an `HttpOnly` cookie, revocation by stamp and by family |
| B5 | No rate limiting | **Fixed** for auth, refresh, coupon preview and basket writes, per `host|IP`, behind trusted forwarded headers. Coverage is still narrow (§4, G-08) |
| B6 | Plaintext reset tokens | **Fixed** (hash only) |
| B7 | Ownership checks in controllers; role-only authorization | **Fixed:** `ICurrentUser`, ownership in use cases, permission policies, tenant/staff/platform roles |
| B8 | Anonymous tracking by sequential id exposes notes | **Fixed:** tracking by a random token that returns status and shipment only; the id route is gone |
| B9 | Security headers | **Fixed for the API:** `SecurityHeadersMiddleware` on every response and `UseHsts` outside Development, covered by `SecurityHeadersTests` (§4). **Open for the SPA:** its CSP in `frontend/nginx.conf` is still `Content-Security-Policy-Report-Only`, and TLS is the deployment's (G-02) |
| B10 | The application connects as `sa` | **Mechanism done, deployment open.** `scripts/sql/least-privilege-logins.sql` creates the runtime and migration identities, `ConnectionStrings:Migrations` separates them, and `scripts/verify-least-privilege.sh` measured them (§5a, [DatabasePrivileges.md](DatabasePrivileges.md)). The demo `docker-compose.yml` still uses `sa`, and no real deployment applies it yet (G-09, R-12 in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md)) |
| B11 | npm advisories | **Fixed:** non-breaking fixes applied, then the frontend moved to Vite 8, Vitest 5 and `@vitejs/plugin-react` 6 (G-18, TD-41). CI runs a blocking `npm audit` over shipped dependencies |
| B12 | Personal email defaults in source | **Fixed:** removed from code, configuration and compose |
| New (Phase 1B) | Fake payment gateway selected implicitly in Production | **Fixed:** explicit selection outside Development and Testing, startup refusal otherwise |
