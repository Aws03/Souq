# Souq: Security Architecture

> **Status:** Adopted 2026-09-11. Identity and permissions are detailed in [AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md); tenant isolation in [MultiTenancy.md](MultiTenancy.md).
> **Principles:** least privilege · secure by default · the server is authoritative · defence in depth · no secrets in git · no sensitive data in logs.

## 1. Assets and threats

| Asset | Main threats | Primary controls |
|---|---|---|
| Tenant data (catalog, customers, orders) | Cross-tenant access, IDOR | Server-side tenant resolution, global query filters + write guard, 404-for-foreign ([MultiTenancy.md](MultiTenancy.md)) |
| Customer accounts | Credential stuffing, reset-link theft, token theft | BCrypt, hashed single-use reset tokens, no secrets in logs, short-lived tokens + rotation (Phase 3), rate limits (Phase 3) |
| Platform control plane | A tenant admin escalating to platform | A separate platform host and audience, platform-only policies, audited platform actions |
| Money flows | Tampered totals, double charge, card data exposure | Server-side pricing, idempotent confirmation, Stripe Elements (card data never reaches us) |
| Storefront visitors | XSS via uploads or content | Content-sniffed uploads with safe extensions, `nosniff` + CSP on uploads, React escaping, no `dangerouslySetInnerHTML` |
| Secrets | Leakage through the repo, logs, or images | user-secrets / environment variables, redacted logging, `.env` git-ignored |

## 2. Authentication (summary)

- **Passwords:** BCrypt (per-password salt, adaptive cost). 8–128 characters with letters and digits.
- **Access token (Phase 3):**
  - An HS256 JWT valid for 15 minutes, held in JavaScript memory only.
  - Validated for issuer, audience, lifetime (30-second clock skew) and signing key.
  - Also bound to the host (`tid`) and to the account's security stamp.
- **Refresh token (Phase 3):**
  - 256-bit random, stored hashed.
  - Rotated on every use, with reuse detection.
  - Carried in an `HttpOnly`/`Secure`/`SameSite=Strict` cookie scoped to `/api/auth`.
- **Account protection:**
  - lockout after 5 failures (15 minutes);
  - a timing-safe path for unknown emails;
  - single-use hashed reset and verification tokens;
  - rate limits.
- Details: [AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md), [ADR-0023](adr/0023-sessions-and-credentials.md).

## 3. Authorization, tenant isolation, IDOR

- **Roles (Phase 3):** PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff and Customer, mapped to permissions in one table (`RolePermissions`).
  - Store roles have no platform permission, and platform roles have no store permission.
  - Endpoints declare the permission they need (`[HasPermission]`), and every endpoint must declare an explicit decision ([ADR-0019](adr/0019-authorization-foundation.md)).
- **Platform vs store:** platform accounts have no store and exist only on platform hosts. A platform token (no `tid`) is rejected on store hosts, and a store token is rejected on platform hosts.
- **IDOR prevention (layers):**
  1. **Tenant isolation (Phase 2, [ADR-0022](adr/0022-tenancy-enforcement.md)):**
     - The store comes from the host only.
     - A named EF filter hides other stores' rows. It **throws** when no store is resolved; it never returns every store's rows.
     - A write guard stamps and verifies `TenantId`.
     - Tenant-scoped composite FKs make a cross-store reference impossible in the database itself.
     - A token's `tid` must match the host.
  2. Ownership checks **inside the use case**, so a customer only sees and confirms their own orders (a 404 otherwise).
  3. Identity always taken from the token through `ICurrentUser`; commands have no customer id or tenant id field to tamper with.
- **Automated proof:**
  - An integration test enumerates all admin endpoints (anonymous → 401, customer → 403).
  - Customer A cannot read or confirm customer B's order (404).
  - `TenantIsolationTests`: store B's admin, customer and visitors, on B's host, get 404 for every endpoint that takes a store-A resource id. Listings exclude A's rows, and writes that reference A's category, parent, product, coupon or order are rejected. A's token on B's host gets 401. The write guard and the missing-tenant filter fail loudly. A completeness test forces every new id-bearing endpoint into the table.
  - `TenancyRuleTests` (architecture) forbid:
    - a business entity without `ITenantOwned` (only identity entities may be store-or-platform);
    - `IgnoreQueryFilters` outside the reviewed platform query type;
    - raw SQL outside migrations;
    - a use case that sets the tenant.
  - `AuthorizationMatrixTests` check role × endpoint over HTTP. `AuthSessionTests` check that platform and store tokens are separated.
  - **Platform area (Phase 4):**
    - `AuthorizationBoundaryTests` proves that every platform endpoint is missing (404) on a store host, even for the store's admin.
    - On the platform host, it rejects anonymous callers and store tokens (401).
    - `PlatformAdministrationTests` covers the full provisioning scenario, and a platform admin without `platform.users.manage` gets 403.
    - A store admin changing settings or staff can only touch their own store: the requests carry no store id, and B's admin gets 404 for A's staff account (`TenantIsolationTests`).
  - **Catalog (Phase 5):**
    - Every admin catalog endpoint requires `catalog.manage`.
    - Draft and archived products, and products in hidden categories, are invisible to anonymous callers, by id and by slug (`CatalogTests`).
    - Image ids are resolved inside a product of the caller's store. Another store's image id under your own product gives 404 on remove and 422 on reorder, and the image is untouched.
    - Slugs and SKUs are unique per store only, so one store cannot probe another store's catalog through a uniqueness conflict (`TenantIsolationTests`).
  - **Inventory (Phase 6):**
    - Stock corrections and thresholds require `inventory.manage` on top of `inventory.view`. Every correction is audited with its delta and reason.
    - Store B's admin gets 404 for A's product on both endpoints, and A's stock and threshold are unchanged (`TenantIsolationTests`).
    - Reservations and ledger rows reference the item through composite tenant-scoped foreign keys, and the expiry sweep runs inside each store's own scope.
  - **Customers (Phase 7, [ADR-0027](adr/0027-customer-profile-and-erasure.md)):**
    - `/api/account` carries no customer id: the profile is always the caller's (`cid`).
    - An address id outside the caller's own book is a 404 on update, delete and both default endpoints, and a checkout with one is `400 AddressNotFound`. A guessed id never reveals or uses someone else's address.
    - Store B's admin gets 404 on every admin customer route for A's customer. B's list and `orders?customerId=` exclude A's customer, and A's customer is unchanged afterwards (`TenantIsolationTests`).
    - Blocking, exporting and erasing need `customers.manage` on top of `customers.view`. They are audited, and so are a customer's own export and erasure.
    - Self-erasure needs the current password. Erasure anonymizes the profile and the login in one save, revokes refresh tokens and forgets the cached security stamp, so existing access tokens stop working immediately.
    - Exports contain no credential material (no password hash, tokens or security stamp).
  - **Basket (Phase 8, [ADR-0028](adr/0028-basket-and-pricing-pipeline.md)):**
    - **The guest cookie** is HttpOnly, Secure, `SameSite=Strict` and scoped to `/api/basket`, so scripts can't read it and cross-site requests don't carry it. It holds 256 random bits; only its SHA-256 is stored, so a database leak doesn't open live guest baskets.
    - **Store isolation:** lookups are filtered by store. A store-A guest token on store B's host opens nothing: an empty basket, the cookie is cleared, and updates and deletes get 404. Store A's product can't be added on store B's host (404). A's basket is unchanged (`TenantIsolationTests`).
    - **No ids on the wire:** requests carry no basket id at all. The basket is the session's or the cookie's, so there is nothing to enumerate.
    - **Rate limits:** coupon codes are priced only through `GET /api/basket/quote`, behind the coupon-preview limit. Writes have their own limit (120 per minute per host and address), because every add from a new guest creates a row.
    - **Amounts:** checkout never trusts one from the client. Prices, discount and total come from the pipeline at order time.
  - **Orders (Phase 9, [ADR-0029](adr/0029-orders-lifecycle.md)):**
    - **Tracking** uses a random 128-bit token (`/api/orders/track/{token}`), not the sequential id, so orders can't be enumerated (B8). It returns status and shipment only: no notes, actors, addresses or amounts.
    - **Bad tokens:** a malformed or unknown token is a 404, and so is a store-A token on store B's host (`TenantIsolationTests`).
    - **Customer cancellation** is owner-only: any other customer gets 404, across stores too. It applies only to unpaid orders, and the gateway is asked first, so a customer can never cancel an order that was just paid.
    - **Order numbers** are per store, so they reveal nothing about other stores' volume.
    - **Visibility:** staff notes and actors are shown only to users with `orders.view`. Customers see their order's statuses and dates.
    - **Accountability:** every status change records the acting staff member's user id on its history row.
  - **Coupons (Phase 10, [ADR-0030](adr/0030-coupon-redemptions.md)):**
    - **Limits hold under concurrency:** a use is taken inside the checkout transaction on a fresh read under the coupon's `rowversion`, so parallel checkouts can't exceed a global or per-customer limit. This closes C1 for coupons and is tested with five concurrent checkouts.
    - **The per-customer limit** counts uses for the signed-in customer, never an id sent by the client.
    - **Redemptions** show order numbers and customer names, so they are listed only with `promotions.manage`, within the host's store. Another store's coupon id is a 404 (`TenantIsolationTests`).
    - **History is kept:** a used coupon can't be deleted (`409 CouponInUse`), so its redemption records keep their coupon.
  - **Payments (Phase 11, [ADR-0031](adr/0031-payments-and-refunds.md)):**
    - **Card data never reaches the server:** Stripe Elements sends it from the browser to Stripe, and we store intent ids only. A test pins the payment tables' columns and scans the whole model for card-like columns.
    - **Store keys at rest:** AES-256-GCM, with a key from the environment (`Secrets:*`). The store id and the kind of secret are authenticated data, so a ciphertext copied to another store's row doesn't decrypt. Keys are write-only: never returned (only the last four characters) and never logged. The audit log records what changed, not the values.
    - **No silent fake payments:** test-mode Stripe keys are refused outside Development and Testing unless `Payments:AllowTestModeStoreAccounts` is set, which logs a startup warning. A store account that can't be decrypted answers 503; payments never fall back to another account silently.
    - **Refunds** require `store.payments.manage` and are audited with the staff member. They can't exceed the payment under concurrency, and an idempotency key means a retry can't refund twice. Another store's order is a 404 (`TenantIsolationTests`).
    - **Webhooks** are verified by signature before anything is read. An event signed by a store's own account is applied only to that store; routing by metadata applies only to deployment-signed events, and applying an event re-asks the gateway.

## 4. Transport, CORS, headers, rate limiting

- **HTTPS** is terminated at the reverse proxy in production. HSTS is set there (Phase 23 checklist).
- **CORS:** an explicit allowlist from `Cors:AllowedOrigins`. The Docker/nginx deployment is same-origin and needs none.
- **Headers:**
  - `/uploads/*` responses carry `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; sandbox` (1A).
  - A full header set (CSP for the SPA, `frame-ancestors`, `Referrer-Policy`) is added in Phase 20.
- **Rate limiting (Phase 3):** the ASP.NET Core limiter, with a fixed window per `host|client IP`.
  - Auth endpoints: 10/min. Refresh: 30/min. Coupon preview: 30/min. All are configurable under `RateLimiting`.
  - Exceeding a limit returns `429 TooManyRequests` with `Retry-After`.
- **Forwarded headers (Phase 3):** `X-Forwarded-For` and `X-Forwarded-Proto` are honoured only from proxies in `ForwardedHeaders:KnownNetworks` (the Docker network by default). A client therefore can't spoof its IP to escape rate limits, or fake `https`.
- **Host header:** the API trusts the host only after it matches `TenantDomains` (an unknown host is a 404). This is what makes host-based email links safe. nginx forwards the original `Host`.

## 5. Input validation, XSS, and uploads

- **Validation:** FluentValidation for shape and ranges (automatic pipeline). Business invariants in the Domain. The database constraints are the final guard.
- **XSS:** React escapes by default, and `dangerouslySetInnerHTML` is forbidden unless the content is sanitized. Since Phase 5, product and category descriptions are **plain text** (up to 4000 characters, rendered escaped). A rich (HTML) description arrives with the storefront (Phase 16); it will be sanitized server-side against an allowlist before storage.
- **Uploads (fixed in 1A, Phase 0 finding B3):**
  1. The file type is detected from **magic bytes**: JPEG, PNG, GIF, WebP for images; MP4, WebM for videos. Anything else (HTML, SVG, scripts, disguised files) is rejected with `UnsupportedMediaType`.
  2. The stored extension is **derived from the detected type**, never from the client's filename. The stored name is a random GUID.
  3. The client `Content-Type` header is ignored for security decisions.
  4. Size limits: 5 MB for images, 50 MB for video (HTTP ceiling plus an Application check).
  5. The static file server serves only the allowlisted media types from the uploads folder, with `nosniff` and a sandboxing CSP, so an unexpected file can never execute as a page on our origin.
  6. SVG is not accepted, because it can carry script.
- **Tenant-prefixed keys (Phase 2):**
  - Files are stored under `tenants/{id}/…`. The resolution middleware serves them only on the owning store's host.
  - Pre-Phase-2 files under `/uploads/{folder}` belong to the default store. They are public catalog media with unguessable names.
- **Product gallery (Phase 5):**
  - Up to 10 images per product, each through the same content-sniffed pipeline, into the store's prefix.
  - Removing an image from the gallery does not delete the file yet (a cleanup job is planned). The file keeps its unguessable name and is still served only on the owning store's host.
- **Target (Phase 23):** cloud blob storage, and optional re-encoding of images (which strips metadata and neutralizes polyglots).

## 6. Secret management

| Secret | Development | Docker / production |
|---|---|---|
| Connection string | `dotnet user-secrets` | environment variable |
| JWT signing key | user-secrets | environment variable (≥ 256-bit random) |
| Stripe secret + webhook secret | user-secrets | environment variable (per-tenant, encrypted, from Phase 11) |
| Email provider keys | user-secrets | environment variable |
| Admin bootstrap credentials | user-secrets (`Seed:AdminEmail`/`Seed:AdminPassword`) | environment variable, set once, removed after first start |
| Platform owner bootstrap (Phase 3) | user-secrets (`Seed:PlatformOwnerEmail`/`Seed:PlatformOwnerPassword`) | environment variable, set once, removed after first start |

- **Fail fast (1B, [ADR-0020](adr/0020-configuration-and-secrets.md)):** settings are typed options validated before the database is touched. A missing connection string, a JWT key shorter than 256 bits, missing JWT issuer/audience, a missing payment provider outside Development, or missing Stripe keys when Stripe is selected stop the startup with a message that names the key and never prints its value.
- **Development conveniences never run implicitly elsewhere:** the fake payment gateway, reset links in the console log, and the development admin work only in Development (and Testing). Anything unsafe for real customers that is enabled explicitly is logged as a warning at every start.
- `appsettings.json` contains **no secrets and no personal data** (the personal email defaults were removed in 1A).
- `.env` is git-ignored; `.env.example` holds placeholders only.
- **Checked at every phase gate:** grep for keys, passwords, and connection strings in the diff.

## 7. Password reset security (fixed in 1A, B2/B6)

- The token is **32 bytes from a CSPRNG**, base64url-encoded, sent only in the email link.
- The database stores **only its SHA-256 hash**. A database read never yields a usable reset token. Tokens have high entropy, so a fast hash is sufficient; no salt or slow hash is needed.
- Tokens are valid for 2 hours, are **single-use**, and are cleared on success.
- Forgot-password always answers 200 with the same message, so accounts can't be enumerated.
- **Logs never contain the link or the token.**
  - The console email adapter prints the link **only in Development**, where no real email is sent.
  - Everywhere else it logs "reset requested" with a masked address.
  - In Production, a missing email provider is logged as a warning at startup.
- **Phase 3:** a successful reset or a password change rotates the security stamp and revokes every refresh token. A password change also issues a fresh session to the caller.
- **Phase 3:** the link points at the host the request came from, so a store's customer returns to that store. An unknown host never reaches the use case (§4).

## 8. Payments

- **PCI scope:** card details go from the browser straight to Stripe (Stripe Elements / Payment Intents). Our servers never receive, store, or log a PAN, CVV, or expiry date. This keeps Souq in the lightest PCI category (SAQ A), as long as the checkout page is served securely.
- **Integrity:**
  - The order total is computed **server-side** from catalog prices and coupon rules. The client's cart price is display only.
  - The payment intent is created **after** the order is saved.
  - Confirmation re-reads the intent status from the provider instead of trusting the client, and it is idempotent.
- **Provider failure:** if intent creation fails, the order is cancelled and its reserved stock released immediately (1A fix for C6).
- **Webhooks:** signature verification lives **inside the payment adapter** (1A; it used to be in the controller). Unsigned or invalid → 400. Missing secret → ignored with a warning.
- **Amount conversion:** the adapter converts `Money` to the provider's minor units (`StripeAmountConverter`, unit-tested).
  - Stripe documents currencies as two-decimal unless listed as zero-decimal. JOD (3 ISO decimals) is therefore sent ×100, rounded away from zero to the nearest 0.01. That matches the previous behaviour, now explicit.
  - ⚠️ **Must be verified before JOD goes live on Stripe.** If the account treats JOD as a three-decimal currency, the multiplier must be 1000. Getting this wrong would charge a tenth of the price. This is listed as an open risk in the roadmap.
- **Fake gateway (1B):** it confirms every payment without money. It used to be selected automatically whenever no Stripe key was set — including in the Production Docker stack, where any order could be marked Paid for free. It is now implicit only in Development/Testing; elsewhere the API refuses to start unless `Payments:Provider=Fake` is set explicitly (demo use), which is logged at every start.
- **Account model:** today there is **one** platform Stripe account configured by environment. The client instance is created per adapter (1A), not through a static global. Per-tenant accounts, or Stripe Connect, is decision D-13, due in Phase 11.

## 9. Logging and sensitive data

**Never log:**
- passwords or hashes;
- tokens (JWT, refresh, reset, API keys);
- reset or verification links;
- full card data (we never have it);
- full provider response bodies on success;
- personal data beyond what is needed.

**Rules applied in 1A:**
- Email adapters log the **masked** recipient (`a***@example.com`), the subject, and the HTTP status. The provider error body is logged on failure only, truncated to 500 characters.
- Configuration diagnostics log only `configured`/`missing`, never values.
- Unexpected exceptions are logged server-side with the stack trace. Clients receive a generic message.

**Rules added in 1B ([ADR-0018](adr/0018-observability.md)):**
- One line per request with method, path **without the query string**, route template, status and duration. Headers (including `Authorization`) and bodies are never logged.
- Every log inside a request carries `CorrelationId` (the W3C trace id, also returned as `X-Correlation-Id` and as `traceId` in errors), `TenantId` (or `Area=Platform` on the platform host, Phase 2) and `UserId`; use-case logs add `UseCase`. A blocked cross-tenant write is logged at **Critical**.
- Use-case logging records name and duration only — never the request payload.
- EF Core SQL text is off by default, and parameter values are never logged.
- Integration tests prove that no password, JWT or `Authorization` value appears in any log.

**Rules added in Phase 3:** refresh-token reuse is logged as a warning with the user id only. Refresh tokens appear only in the `Set-Cookie` header, never in a body or log.

## 10. Audit logging (implemented in Phase 4, [ADR-0024](adr/0024-platform-administration.md))

`AuditEntries` rows record:
- who: the user id and role, and the area (`Platform`, `Store` or `System`);
- which store is affected;
- the action (`tenant.suspended`, `store.staff.invited`, …);
- the target type and id (the natural key for creates);
- metadata as JSON, with the fields the request chose itself;
- the client IP (only from trusted proxies) and the correlation id.

**How rows are written:**
- `AuditBehavior` handles every request that implements `IAuditable`.
- The entry is staged into the current unit of work before the handler runs, so it commits atomically with the change.
- If the handler saved nothing (a query), the entry is flushed after success. On failure it is discarded.
- The request body is never copied, so passwords, tokens and file contents never reach the log.

**What is audited:**
- **Every platform request, reads included.** This is how the Platform Owner's access stays "explicit and audited", and an architecture test enforces it.
- **Store administration:** settings, branding and staff.

Refunds, stock adjustments and catalog commands join as their phases rebuild them.

The write guard rejects any update or delete of an `AuditEntry`. The platform reads the log through `GET /api/platform/audit`, with `platform.audit.view`.

## 11. Phase 0 findings: disposition

| ID | Finding | Status after 1A |
|---|---|---|
| B1 | Default admin `Admin@123` seeded in every environment | ✅ Fixed. **Development** falls back to the documented dev credentials when `Seed:*` isn't set, for convenience. **Every other environment** creates an admin only when `Seed:AdminEmail`/`Seed:AdminPassword` are provided and the password meets the strength policy. Otherwise no admin is created and a warning is logged. An existing admin's password is never overwritten. |
| B2 | Reset links in logs; PII in email logs | ✅ Fixed (§7, §9) |
| B3 | Upload stored XSS | ✅ Fixed (§5) |
| B4 | Long-lived JWT in `localStorage`, no revocation | ✅ Phase 3: a 15-minute access token in memory, the refresh token in an `HttpOnly` cookie, revocation by stamp and by family |
| B5 | No rate limiting | ✅ Phase 3: limits on auth, refresh and coupon preview per `host|IP`, behind trusted forwarded headers |
| B6 | Plaintext reset tokens | ✅ Fixed (hash only) |
| B7 | Ownership checks in controllers; role-only authorization | ✅ 1B: `ICurrentUser`, ownership in use cases, permission policies. ✅ Phase 3: tenant, staff and platform roles |
| New (1B) | Fake payment gateway selected implicitly in Production | ✅ 1B: explicit selection outside Development, startup refusal otherwise |
| B8 | Anonymous tracking by sequential id exposes notes | ✅ Phase 9: tracking by a random token that returns status and shipment only; the id route is removed |
| B9 | Security headers | 🟡 Uploads fixed in 1A; the rest in Phase 20 |
| B10 | App connects as `sa` | ⏳ Phase 23 (least-privilege login) |
| B11 | npm advisories | ✅ Non-breaking fixes applied; the Vite major upgrade (dev server only) is deferred to Phase 15 |
| B12 | Personal email defaults in source | ✅ Removed from code, config, and compose |
