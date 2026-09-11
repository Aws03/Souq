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
| Validation failed (shape, ranges, paging) | 400 |
| Not authenticated / token invalid | 401 |
| Authenticated but not allowed (role or permission) | 403 |
| Resource missing **or owned by someone else** (tenant or user) | **404**. Never 403, which would leak that the resource exists. |
| Business rule violated (invalid transition, insufficient stock, coupon expired) | 400 today → 422 with ProblemDetails (1B) |
| Concurrency conflict or duplicate unique value | **409** |
| Unsupported upload type | 400 (`UnsupportedMediaType` code) |
| Too many requests | 429 (Phase 3) |
| Unexpected | 500, generic message, no internals |

## 4. Errors

- **Today (after 1A):**
  - `{ "error": "<message>", "code": "<StableCode>" }`.
  - Result → HTTP mapping lives in **one** place (`Souq.API/Http/ResultHttpExtensions.cs`) instead of three copies.
  - The middleware maps `ValidationException` → 400, `DomainException` → 400, `ConcurrencyConflictException` → 409, `UniqueConstraintViolationException` → 409, anything else → 500.
- **Target (1B):**
  - RFC 7807 `application/problem+json` with `type`, `title`, `status`, `detail`, `code`, `traceId`, and per-field `errors` for validation.
  - Messages stay human-readable, but the **`code` is the contract**: the frontend translates codes, so English-UI users stop receiving Arabic server text (Phase 0 A9).

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
- **Implementation (1B):** a shared `PageRequest`/`PagedResult<T>` + query-service extensions, so paging is written once.

## 6. Authentication, authorization, tenant resolution

- `Authorization: Bearer <access token>`. From Phase 3 the refresh token travels in an `HttpOnly` cookie.
- The **tenant is never a parameter.** It comes from the Host header, and for authenticated calls it must match the token's `tid` claim ([MultiTenancy.md](MultiTenancy.md)).
- **Customer identity comes from the token, never from the body.** For example `CreateOrderCommand.CustomerId` is overwritten from the claims.
- Authorization today uses `[Authorize(Roles = ...)]`. Target: permission policies (`[HasPermission(...)]`) plus resource ownership checks inside Application ([AuthenticationAndAuthorization.md](AuthenticationAndAuthorization.md)).
- **Automated guard:** an integration test enumerates every admin-only endpoint and asserts anonymous → 401 and customer → 403, so new endpoints can't silently ship unprotected.

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

## 10. Documentation rules

- Every new endpoint appears in Swagger with its auth requirement.
- A module's public HTTP surface is listed in [Modules.md](Modules.md) when that module is rebuilt.
- An endpoint list duplicated in the README must be regenerated, not hand-edited. Target: generated from OpenAPI (Phase 22).
