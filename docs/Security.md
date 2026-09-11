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

- **Passwords:** BCrypt (per-password salt, adaptive cost).
- **Access token:** HS256 JWT validated for issuer, audience, lifetime, and signing key, with a 30-second clock skew.
- **Target (Phase 3):**
  - 15-minute access tokens;
  - rotating refresh tokens stored hashed, with reuse detection, in an `HttpOnly`/`Secure`/`SameSite` cookie;
  - a security stamp for revocation;
  - lockout;
  - email verification;
  - platform and tenant token audiences.

## 3. Authorization, tenant isolation, IDOR

- **Roles today:** `Customer`, `Admin`.
- **Target roles:** PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff, Customer, mapped to **permissions** in code ([AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md)).
- **IDOR prevention (three layers):**
  1. The tenant filter, so another tenant's rows are invisible.
  2. Ownership checks, so a customer only sees their own orders (a 404 otherwise).
  3. Identity always taken from the token, never from the request body.
- **Automated proof:**
  - An integration test enumerates all admin endpoints (anonymous → 401, customer → 403).
  - Customer A cannot read or confirm customer B's order (404).
  - Tenant isolation tests are added in Phase 2.

## 4. Transport, CORS, headers, rate limiting

- **HTTPS** is terminated at the reverse proxy in production. HSTS is set there (Phase 23 checklist).
- **CORS:** an explicit allowlist from `Cors:AllowedOrigins`. The Docker/nginx deployment is same-origin and needs none.
- **Headers:**
  - `/uploads/*` responses carry `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; sandbox` (1A).
  - A full header set (CSP for the SPA, `frame-ancestors`, `Referrer-Policy`) is added in Phase 20.
- **Rate limiting (Phase 3):** the ASP.NET Core limiter on login, register, forgot-password, reset-password, and coupon preview, partitioned by IP and tenant.

## 5. Input validation, XSS, and uploads

- **Validation:** FluentValidation for shape and ranges (automatic pipeline). Business invariants in the Domain. The database constraints are the final guard.
- **XSS:** React escapes by default, and `dangerouslySetInnerHTML` is forbidden unless the content is sanitized. Rich product descriptions (Phase 5) are sanitized server-side against an allowlist before storage.
- **Uploads (fixed in 1A, Phase 0 finding B3):**
  1. The file type is detected from **magic bytes**: JPEG, PNG, GIF, WebP for images; MP4, WebM for videos. Anything else (HTML, SVG, scripts, disguised files) is rejected with `UnsupportedMediaType`.
  2. The stored extension is **derived from the detected type**, never from the client's filename. The stored name is a random GUID.
  3. The client `Content-Type` header is ignored for security decisions.
  4. Size limits: 5 MB for images, 50 MB for video (HTTP ceiling plus an Application check).
  5. The static file server serves only the allowlisted media types from the uploads folder, with `nosniff` and a sandboxing CSP, so an unexpected file can never execute as a page on our origin.
  6. SVG is not accepted, because it can carry script.
- **Target (Phase 5):** tenant-prefixed storage keys, cloud blob storage, optional re-encoding of images (which strips metadata and neutralizes polyglots).

## 6. Secret management

| Secret | Development | Docker / production |
|---|---|---|
| Connection string | `dotnet user-secrets` | environment variable |
| JWT signing key | user-secrets | environment variable (≥ 256-bit random) |
| Stripe secret + webhook secret | user-secrets | environment variable (per-tenant, encrypted, from Phase 11) |
| Email provider keys | user-secrets | environment variable |
| Admin bootstrap credentials | user-secrets (`Seed:AdminEmail`/`Seed:AdminPassword`) | environment variable, set once, removed after first start |

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
- **Phase 3:** a password change revokes existing sessions through the security stamp.

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

**Phase 1B:** structured scopes (`TenantId`, `UserId`, `CorrelationId`) on every log entry.

## 10. Audit logging (Phase 4)

`AuditLog` rows record:
- who (user id, role, platform or tenant);
- which tenant;
- the action;
- the target type and id;
- metadata as JSON (changed fields, without secrets);
- IP and timestamp.

They are written by a MediatR behavior for commands marked `IAuditableCommand`. Platform actions, permission changes, refunds, stock adjustments, and tenant configuration changes are always audited. The table is append-only.

## 11. Phase 0 findings: disposition

| ID | Finding | Status after 1A |
|---|---|---|
| B1 | Default admin `Admin@123` seeded in every environment | ✅ Fixed. **Development** falls back to the documented dev credentials when `Seed:*` isn't set, for convenience. **Every other environment** creates an admin only when `Seed:AdminEmail`/`Seed:AdminPassword` are provided and the password meets the strength policy. Otherwise no admin is created and a warning is logged. An existing admin's password is never overwritten. |
| B2 | Reset links in logs; PII in email logs | ✅ Fixed (§7, §9) |
| B3 | Upload stored XSS | ✅ Fixed (§5) |
| B4 | Long-lived JWT in `localStorage`, no revocation | ⏳ Phase 3 |
| B5 | No rate limiting | ⏳ Phase 3 |
| B6 | Plaintext reset tokens | ✅ Fixed (hash only) |
| B7 | Ownership checks in controllers; role-only authorization | ⏳ 1B (`ICurrentUser`) / Phase 3 (permissions) |
| B8 | Anonymous tracking by sequential id exposes notes | ⏳ Phase 9 (tracking tokens) |
| B9 | Security headers | 🟡 Uploads fixed in 1A; the rest in Phase 20 |
| B10 | App connects as `sa` | ⏳ Phase 23 (least-privilege login) |
| B11 | npm advisories | ✅ Non-breaking fixes applied; the Vite major upgrade (dev server only) is deferred to Phase 15 |
| B12 | Personal email defaults in source | ✅ Removed from code, config, and compose |
