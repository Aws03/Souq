# Souq: API Architecture and Conventions

> **Status:** Conventions adopted 2026-09-11. The current endpoint inventory is in [ArchitectureAssessment.md §5](ArchitectureAssessment.md#5-current-api-map). Interactive docs: Swagger at `/swagger` (Development).

## 1. Style

- **REST over HTTPS with JSON.** Resources are nouns; HTTP methods are the verbs. Enums travel as strings (`"Shipped"`).
- Commands that don't map to CRUD become **sub-resource actions**, for example `POST /api/orders/{id}/confirm-payment` and `PUT /api/admin/orders/{id}/status`. Invented verb endpoints are avoided.
- **Controllers are thin.** A controller:
  - binds the request;
  - applies authorization attributes;
  - sends one MediatR command or query;
  - maps the Result to an HTTP response.

  It holds no business rules, no database access, and no provider SDK calls. The last three are enforced by `Souq.ArchitectureTests`.

## 2. Areas and route prefixes

| Area | Prefix | Tenant context | Who |
|---|---|---|---|
| Auth | `/api/auth` | host's tenant (or platform host) | anonymous + token flows |
| Storefront | `/api/storefront` *(target)*; today `/api/products`, `/api/categories`, `/api/coupons/apply`, `/api/products/{id}/reviews` | required | anonymous/customer |
| Customer account | `/api/account` *(target)*; today `/api/orders/mine`, `/api/orders/{id}` | required | customer (own data) |
| Tenant back-office | `/api/admin` (today: `/api/admin/inventory` + admin actions on shared routes) | required | tenant admin/staff + permission |
| Platform | `/api/platform` | none | platform owner/admin, platform host only |
| Webhooks | `/api/payments/webhook` (target `/api/webhooks/{provider}`) | from provider metadata | signature |
| Health | `/health/live`, `/health/ready` (Phase 23) | — | infrastructure |

Routes move into these areas in the phase that rebuilds each module. The frontend API client changes in the same commit, so there are no long-lived compatibility shims (we own the only client).

## 3. Methods and status codes

| Situation | Code |
|---|---|
| Read OK | 200 |
| Created | 201 + `Location` (or `{ id }`) |
| Updated or deleted with no body | 204 |
| Validation failed (shape, ranges, paging, unreadable JSON, unsupported upload type) | 400 |
| Not authenticated, token invalid, wrong credentials | 401 |
| Authenticated but lacking the permission | 403 |
| Resource missing **or owned by someone else** (tenant or user) | **404**. Never 403, which would leak that the resource exists. |
| No store on this host (`StoreNotFound`); a platform endpoint on a store host or the reverse (`NotFound`) | **404** |
| Conflict with the current state: concurrent write (`ConcurrencyConflict`), duplicate (`DuplicateValue`, `EmailTaken`, `SlugTaken`), stale edit (`StockChanged`), delete blocked (`CategoryInUse`), database reference rejected (`ReferenceConflict`) | **409** |
| Business rule violated (invalid transition, insufficient stock, coupon unusable, too many decimals) | **422** |
| Too many requests | 429 (Phase 3) |
| Required external provider unavailable (`PaymentUnavailable`); store suspended, archived, or still provisioning (`StoreUnavailable`) | 503 |
| Unexpected | 500, generic message, no internals |

## 4. Errors ([ADR-0017](adr/0017-error-contract.md))

Every error is RFC 7807 `application/problem+json`:

```json
{
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "الكمية المطلوبة (3) من \"سماعات\" غير متوفرة. المتاح: 1",
  "code": "InsufficientStock",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

- **`code` is the contract.** Clients branch on it and translate it. `detail` is for humans and may change.
- **`traceId`** equals the `X-Correlation-Id` response header and the request's log scope. Support asks for it.
- **Validation (400)** adds `errors`, keyed by the JSON path the client sent: `{ "errors": { "items[0].quantity": ["…"] } }`.
- **Framework errors use the same shape:** 401 `Unauthenticated`, 403 `Forbidden`, 404 `NotFound` (unknown route too), 400 `ValidationFailed` (unreadable JSON, without internal type names). Unexpected errors are 500 `ServerError` with a generic message; the exception exists only in the log.
- **Where codes come from:** use cases return `Result.Failure(Error.X(code, message))` for outcomes they decide; entities throw a `DomainException` whose `Code` is stable (`InsufficientStock`, `InvalidOrderOperation`, `InvalidCoupon`, `InvalidMoney`, `InvalidReview`, `InvalidProductData`, `ResetTokenExpired`). One table in `Souq.API/Http/ProblemDetailsConventions.cs` maps the kind to the status.
- **Frontend:** `frontend/src/api/problem.js` turns every error into one `Error` with `message`, `code`, `status`, `traceId`, `fieldErrors`. Translations live under `errors.codes` in `frontend/src/i18n/locales/*.json` (a test keeps Arabic and English keys identical).

## 5. Lists: pagination, filtering, sorting, search

- **Paging:**
  - `page` (≥ 1, default 1) and `pageSize` (1–100, default per endpoint).
  - They are validated by FluentValidation on **every** list query (1A). Before 1A, `page=0` produced a SQL error and a 500.
- **Response shape:**

  ```json
  { "items": [...], "pageNumber": 1, "pageSize": 12, "totalCount": 58, "totalPages": 5, "hasNext": true, "hasPrevious": false }
  ```
- **Filtering:**
  - Explicit, typed query parameters (`categoryIds=1&categoryIds=2`, `minPrice`, `status`).
  - Repeated keys for arrays. Comma-separated lists are not used.
- **Sorting:** a `sortBy` enum per resource (an allowlist). Raw column names from the client are never accepted.
- **Search:** `keyword`, bilingual `LIKE` today. A search port will abstract it if a search engine arrives.
- **Every list endpoint is paged** — including `/api/orders/mine` (default 20) and the admin inventory lists (inventory 50, low stock 20, stock movements 50). A badge that only needs a count asks for `pageSize=1` and reads `totalCount`.
- **Implementation (1B):**
  - The query implements `IPagedQuery`; its validator inherits `PagedQueryValidator<T>` (one place for the limits).
  - The use case passes typed criteria and a `PageRequest` to the module's query service (`ICatalogQueries`, `IOrderQueries`, …).
  - The query service (Infrastructure) ends with `ToPageAsync(projection, page)`, which only accepts an ordered query and an explicit projection. Every sort ends with an `Id` tiebreaker, so rows never repeat or vanish between pages.
  - `IQueryable` never leaves Infrastructure (architecture test).

## 6. Authentication, authorization, tenant resolution

- `Authorization: Bearer <access token>`, valid for 15 minutes.
  - The refresh token travels only in the `souq_refresh` cookie (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`). It never appears in a body.
  - Session endpoints and their contract: [AuthenticationAndAuthorization.md §5](AuthenticationAndAuthorization.md#5-endpoints).
- The **tenant is never a parameter.** It comes from the Host header, and for authenticated calls it must match the token's `tid` claim ([MultiTenancy.md](MultiTenancy.md), [ADR-0022](adr/0022-tenancy-enforcement.md)). Implemented in Phase 2:
  - **Resolution:**
    - `TenantResolutionMiddleware` maps the host to a store through `TenantDomains`.
    - An unknown host gets `404 StoreNotFound`. There is no fallback store in Production.
    - Platform hosts (`Tenancy:PlatformHosts`) have no store.
  - **Development/Testing only:**
    - `localhost` serves `Tenancy:LocalDefaultTenant`, and `{slug}.localhost` serves that store.
    - The `X-Tenant: <slug>` header overrides both.
    - `admin.localhost` is the platform host.
  - **Availability:**
    - `Suspended`/`Archived` stores answer `503 StoreUnavailable`, except endpoints marked `[AvailableWhenStoreClosed]`.
    - `Provisioning` stores also serve auth and admin endpoints.
    - `[PlatformEndpoint]` endpoints exist only on platform hosts; every other endpoint exists only on store hosts (404 otherwise).
  - **Tokens:**
    - A store token carries `tid` and is valid only on that store's host.
    - A platform token has no `tid` and is valid only on platform hosts (Phase 3).
    - A mismatch fails authentication, which is a 401 on protected endpoints. So does an outdated security stamp: password changed or reset, or refresh reuse detected.
  - **Uploads:** `/uploads/tenants/{id}/…` is served only on that store's host.
  - **Money:** amounts are in the store currency. `GET /api/coupons/apply` ignores any `currency` parameter.
- **Customer identity comes from the token, never from the body.**
  - Use cases read it from `ICurrentUser` (the `cid` claim); commands have no customer id field at all.
  - A staff account has no customer profile, so customer use cases answer `403 CustomerAccountRequired`.
- **Rate limits (Phase 3):** auth, refresh and coupon-preview endpoints answer `429 TooManyRequests` with `Retry-After` when a limit is exceeded.
- **Authorization (1B, [ADR-0019](adr/0019-authorization-foundation.md)):** endpoints declare `[HasPermission(Permissions.X.Y)]`, `[Authorize]` or `[AllowAnonymous]` — explicitly, every one. Resource ownership is checked inside the use case (404 for someone else's resource).
- **Automated guards:** integration tests enumerate every endpoint and assert that each declares its decision, that the public surface equals a reviewed list, that every declared permission exists, and that permission-protected endpoints answer anonymous → 401 and customer → 403.

## 7. Versioning

- **No URL versioning yet (YAGNI).** The only consumer is our own SPA, deployed together with the API.
- **Rule:** additive changes only (new fields, new endpoints). A breaking change means changing the SPA in the same release.
- **Revisit** when a third-party or public API consumer exists. Then use `/api/v1` for the public surface only.

## 8. Idempotency and concurrency

| Operation | Guarantee |
|---|---|
| Payment confirmation (client and webhook) | Idempotent by order state: a second confirmation returns the current status with no side effects. A concurrent race is resolved by `rowversion` plus a re-read (1A). |
| Webhooks | Signature-verified; idempotent by the same rule; unknown events → 200 (ignored) |
| Checkout (`POST /api/orders`) | Target (Phase 9): an `Idempotency-Key` header, so a network retry doesn't create a second order |
| Updates to shared rows | Optimistic concurrency → 409 with a message to reload |
| Product stock edits (admin form) | Compare-and-set: the client sends `stockQuantity` with `expectedStockQuantity`, and a mismatch → 409 (1A) |

## 9. Uploads

- `multipart/form-data`, field `file`.
- The controller checks presence and the size ceiling (HTTP concerns).
- The **Application layer** validates the actual content by magic bytes, and the stored file extension is derived from the detected type, never from the client's filename or `Content-Type` ([Security.md §5](Security.md#5-input-validation-xss-and-uploads)).

## 10. Correlation ([ADR-0018](adr/0018-observability.md))

- Every response carries `X-Correlation-Id`: the request's W3C trace id (32 hex characters). It is also the `traceId` in error bodies and the `CorrelationId` in the server logs.
- An incoming W3C `traceparent` header is honoured (for gateways or services in front of the API). Arbitrary client-chosen ids are not accepted.
- The header is exposed to browsers through CORS so the UI can show it on error screens.

## 11. Documentation rules

- Every new endpoint appears in Swagger with its auth requirement.
- A module's public HTTP surface is listed in [Modules.md](Modules.md) when that module is rebuilt.
- An endpoint list duplicated in the README must be regenerated, not hand-edited. Target: generated from OpenAPI (Phase 22).
