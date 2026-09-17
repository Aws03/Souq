# Souq: API Architecture and Conventions

> **Status:** Conventions adopted 2026-09-11; kept current. **The endpoint inventory is generated from the code:** [Endpoints.md](Endpoints.md) lists every endpoint with its authorization, host, module flag, rate limit and the use case it sends. Interactive docs: Swagger at `/swagger` (Development only).

## 1. Style

- **REST over HTTPS with JSON.** Resources are nouns; HTTP methods are the verbs. Enums travel as strings (`"Shipped"`).
- **Every instant in a response is UTC and says so** (`"2026-09-17T12:39:47.758192Z"`). Storage is UTC, but a `DateTime` read from the database has no kind, and without the `Z` a browser reads the text in its own time zone — every order time, sign-in and audit line was shifted by the reader's offset. `UtcDateTimeJsonConverter` (`src/Souq.API/Http/UtcDateTimeJsonConverter.cs`) writes the suffix. Request bodies and query strings are read as before; send an instant with `Z` or an explicit offset. Proven by `PlatformAuditViewerTests`.
- Commands that don't map to CRUD become **sub-resource actions**, for example `POST /api/orders/{id}/confirm-payment` and `PUT /api/orders/{id}/status`. Invented verb endpoints are avoided — the distinction is that the action hangs off the resource it changes, rather than becoming a verb of its own.
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
| Storefront | `/api/storefront` (`/config` since Phase 4); still on shared routes: `/api/products` (with `onSale`), `/api/products/{id}`, `/api/products/by-slug/{slug}` (Phase 5), `/api/categories`, `/api/coupons/apply`, `/api/products/{id}/reviews` | required | anonymous/customer |
| Basket | `/api/basket` (Phase 8): `GET`, `GET /quote?couponCode=`, `POST /items`, `PUT` and `DELETE /items/{productId}`, `PUT` and `DELETE /items/variants/{variantId}`, `DELETE` | required | anonymous (guest cookie) or customer — only the caller's own basket |
| Customer account | `/api/account` (Phase 7): `/profile`, `/addresses` (plus `/{id}/default-shipping` and `/{id}/default-billing`), `/export`, `/erase`; orders are still at `/api/orders/mine` and `/api/orders/{id}` | required | customer (own data) |
| Tenant back-office | `/api/admin`: `/api/admin/inventory` (on hand, reserved, available; Phase 6 adds `POST /{productId}/adjustments` and `PUT /{productId}/threshold`, `inventory.manage`; ADR-0039 adds the same per variant under `/variants/{variantId}`), `/api/admin/store/*` and `/api/admin/staff` (Phase 4), `/api/admin/products` (every status, full detail, `/{id}/status`, `/{id}/images/order`, `/{id}/images/{imageId}`) and `/api/admin/categories` (Phase 5), `/api/admin/customers` (Phase 7: list and detail with `customers.view`; `/{id}/status`, `/{id}/export` and `/{id}/erase` also need `customers.manage`), plus admin writes still on shared routes (`POST/PUT/DELETE /api/products`, `/api/categories`) | required | tenant admin/staff + permission |
| Platform | `/api/platform/tenants`, `/api/platform/users`, `/api/platform/stats`, `/api/platform/audit` (Phase 4) | none | platform owner/admin, platform host only, every request audited |
| Webhooks | `/api/payments/webhook` (target `/api/webhooks/{provider}`) | from provider metadata | signature |
| Health | `/health/live`, `/health/ready` | none (outside `/api`, so no store is resolved) | anonymous — restrict at the network edge |

Routes move into these areas in the phase that rebuilds each module. The frontend API client changes in the same commit, so there are no long-lived compatibility shims (we own the only client).

## 3. Methods and status codes

| Situation | Code |
|---|---|
| Read OK | 200 |
| Created | 201 + `Location` (or `{ id }`) |
| Updated or deleted with no body | 204 |
| Validation failed (shape, ranges, paging, unreadable JSON, unsupported upload type, a compare-at price not above the price, no text in the store's default language `DefaultTranslationRequired`, a checkout address id that isn't in the caller's own book `AddressNotFound`, checkout from an empty basket `BasketEmpty`) | 400 |
| Not authenticated, token invalid, wrong credentials | 401 |
| Authenticated but lacking the permission; a staff account on a customer use case (`CustomerAccountRequired`); a blocked customer placing an order or writing a review (`CustomerBlocked`) | 403 |
| Resource missing **or owned by someone else** (tenant or user) | **404**. Never 403, which would leak that the resource exists. |
| No store on this host (`StoreNotFound`); a platform endpoint on a store host or the reverse (`NotFound`); a module disabled for this store (`ModuleDisabled`) | **404** |
| Conflict with the current state: concurrent write (`ConcurrencyConflict`), duplicate (`DuplicateValue`, `EmailTaken`, `SlugTaken`, `ProductSlugTaken`, `SkuTaken`, `TenantSlugTaken`, `DomainTaken`), delete blocked (`CategoryInUse`, `CategoryHasChildren`), database reference rejected (`ReferenceConflict`) | **409** |
| Business rule violated (invalid transition, insufficient stock, coupon unusable, too many decimals, unreadable colour palette, `CannotDisableSelf`, `LastAdministrator`, `TenantHasNoDomain`, `ModuleDisabled` at checkout, catalog values the entity rejects, such as a malformed slug or SKU or an unsupported language (`InvalidProductData`, `InvalidCategory`), a category cycle or a tree deeper than 5 levels (`InvalidParent`), an image order that does not list every image once, a stock correction below what open orders reserve (`InvalidInventoryOperation`), profile or address values the entity rejects, such as a malformed phone, a country that isn't a 2-letter code, or a 21st address (`InvalidCustomerData`), a basket quantity above 99 or a 51st basket line (`InvalidBasketOperation`), a basket quantity above what is available now (`InsufficientStock`), a basket or order line, or a product-keyed basket or stock route, that doesn't say which variant of a product with several is meant (`VariantRequired`), a product option or variant rule ([ADR-0040](../11-ADR/0040-product-option-model.md): `TooManyOptions`, `TooManyOptionValues`, `TooManyVariants`, `OptionNameRequired`, `OptionNameTooLong`, `OptionNameInvalid`, `DuplicateOptionName`, `DuplicateOptionValue`, `OptionValuesRequired`, `OptionNotFound`, `OptionValueNotFound`, `OptionValueInUse`, `OptionRemovalCollides`, `ExistingVariantsValueRequired`, `OptionsRequired`, `IncompleteVariantCombination`, `DuplicateVariantCombination`, `DuplicateVariantSku`, `DefaultVariantMustBeActive`, `DefaultVariantCannotBeDeactivated`, `VariantNotFound`), a price or SKU change through the product form on a product with options (`ProductHasVariants`), a basket line whose variant ran out between the product page and the add (`InsufficientStock`), a customer cancelling a paid order (`InvalidOrderOperation`), a cancellation that lost the race to payment (`OrderAlreadyPaid`) or met a payment still in flight (`PaymentProcessing`)) | **422** |
| Too many requests | 429 (Phase 3) |
| Payment intent could not be created at checkout, so the order was cancelled and its stock released (`PaymentUnavailable`, from `CreateOrderHandler`); a store payment account whose keys cannot be decrypted (`PaymentsUnavailable`, from `GlobalExceptionHandler`); no secrets key configured to save a store's payment keys (`SecretsNotConfigured`); store suspended, archived, or still provisioning (`StoreUnavailable`) | 503 |
| Unexpected | 500, generic message, no internals |

## 4. Errors ([ADR-0017](../11-ADR/0017-error-contract.md))

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
- **Framework errors use the same shape.** When an error reaches the client without a code of our own — from authentication, routing, model binding, Kestrel or the rate limiter — `ProblemDetailsConventions.DefaultCode` fills one in from the status:

  | Status | Default `code` |
  |---|---|
  | 400 | `ValidationFailed` when there are field errors (unreadable JSON too, without internal type names), otherwise `BadRequest` |
  | 401 | `Unauthenticated` |
  | 403 | `Forbidden` |
  | 404 | `NotFound` (an unknown route too) |
  | 405 | `MethodNotAllowed` |
  | 409 | `Conflict` |
  | 413 | `PayloadTooLarge` |
  | 415 | `UnsupportedMediaType` |
  | 422 | `BusinessRule` |
  | 429 | `TooManyRequests` |
  | 503 | `ServiceUnavailable` |
  | any other 5xx | `ServerError` |
  | anything else | `Error` |

  Unexpected exceptions are 500 `ServerError` with a generic message; the exception exists only in the log.
- **Where codes come from:** use cases return `Result.Failure(Error.X(code, message))` for outcomes they decide; entities throw a `DomainException` whose `Code` is stable (`InsufficientStock`, `InvalidOrderOperation`, `InvalidCoupon`, `InvalidMoney`, `InvalidReview`, `InvalidProductData`, `ResetTokenExpired`). One table in `Souq.API/Http/ProblemDetailsConventions.cs` maps the kind to the status.
- **Frontend:** `frontend/src/api/problem.js` turns every error into one `Error` with `message`, `code`, `status`, `traceId`, `fieldErrors`. Translations live under `errors.codes` in `frontend/src/i18n/locales/*.json` (a test keeps Arabic and English keys identical).

## 5. Lists: pagination, filtering, sorting, search

- **Paging:**
  - `page` (≥ 1, default 1) and `pageSize` (1–100, default per endpoint).
  - They are validated by FluentValidation on **every** list query (1A). Before 1A, `page=0` produced a SQL error and a 500.
  - **Depth is capped too:** `(page − 1) × pageSize` may not exceed `PagingRules.MaxOffset` (10,000 rows), so `page=100000&pageSize=100` is `400 ValidationFailed` on `page` instead of a scan that reads and discards millions of rows (F-18). Past that depth, narrow the filters rather than paging further.
- **Response shape:**

  ```json
  { "items": [...], "pageNumber": 1, "pageSize": 12, "totalCount": 58, "totalPages": 5, "hasNext": true, "hasPrevious": false }
  ```
- **Filtering:**
  - Explicit, typed query parameters (`categoryIds=1&categoryIds=2`, `minPrice`, `status`).
  - Repeated keys for arrays. Comma-separated lists are not used.
- **Sorting:** a `sortBy` enum per resource (an allowlist). Raw column names from the client are never accepted.
- **Search:** `keyword`, matched on the stored **normalized** form of catalog text since M3 ([ADR-0042](../11-ADR/0042-local-search-engine.md)). Every word in the keyword must match, in a product name, its description or its category name, in any language. `sortBy=Relevance` ranks server-side; it falls back to `Newest` when there is no keyword.
  - `GET /api/products` adds one optional parameter and one optional response field, both additive:
    - **`exact=true`** — do not attempt typo recovery, even if the query matches nothing. Sent when the shopper has been shown a correction and explicitly insisted on their own words.
    - **`search`** — present only when the server did something worth saying. `null` otherwise, including whenever results were found for the shopper's own words:

      ```json
      { "items": [...], "totalCount": 3, "pageNumber": 1,
        "search": { "term": "مكلسة", "searchedInstead": "مكنسه", "category": null } }
      ```

      `searchedInstead` means **these results belong to that word, not the one typed** — the storefront must say so rather than swap the words silently. When nothing matched at all, `searchedInstead` is `null` and `category` may carry `{ "id", "slug", "name" }` to offer instead of a dead end.
  - **`GET /api/products/suggestions?q=…&limit=8`** (anonymous) — suggestions while typing: visible products first, then active categories, ranked by the same expression the results page uses. Each entry is `{ "kind": "product" | "category", "id", "slug", "name", "imageUrl" }`; `kind` is a string, not an enum, so a third kind can be added without breaking a client. Fewer than 2 normalized characters returns `[]` without querying; `limit` outside 1–10 is `400 ValidationFailed`. It does **not** correct typos — the shopper is still typing; recovery belongs to the executed search.
  - Recovery runs only for queries of at most 3 words, each at least 3 characters: the endpoint is anonymous and has no rate-limit policy, so the bound is what stops a nonsense query from repeatedly costing a vocabulary read plus edit-distance work.
- **Every *paged* list carries the same envelope and the same limits** — including `/api/orders/mine` (default 20) and the admin inventory lists (inventory 50, low stock 20, stock movements 50). A badge that only needs a count asks for `pageSize=1` and reads `totalCount`.
- **Five lists are deliberately not paged, and two exports are unbounded.** Corrected in Phase 17 — this page previously claimed every list was paged, which was never true:
  - bounded by a domain rule, so paging would add nothing: `/api/wishlist` (200 items per customer) and `/api/account/addresses` (20).
  - **unbounded today:** `/api/categories` and `/api/admin/categories` (depth is capped at 5, breadth is not), `/api/admin/shipping-methods`, and both `/export` endpoints, which return every order a customer ever placed with all of its lines plus every review. See F-21 in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).
- **Implementation (1B):**
  - The query implements `IPagedQuery`; its validator inherits `PagedQueryValidator<T>` (one place for the limits).
  - The use case passes typed criteria and a `PageRequest` to the module's query service (`ICatalogQueries`, `IOrderQueries`, …).
  - The query service (Infrastructure) ends with `ToPageAsync(projection, page)`, which only accepts an ordered query and an explicit projection. Every sort ends with an `Id` tiebreaker, so rows never repeat or vanish between pages.
  - `IQueryable` never leaves Infrastructure (architecture test).

## 6. Authentication, authorization, tenant resolution

- `Authorization: Bearer <access token>`, valid for 15 minutes.
  - The refresh token travels only in the `souq_refresh` cookie (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`). It never appears in a body.
  - Session endpoints and their contract: [AuthenticationAndAuthorization.md §5](../07-SECURITY/AuthenticationAndAuthorization.md#5-endpoints).
- The **tenant is never a parameter.** It comes from the Host header, and for authenticated calls it must match the token's `tid` claim ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md), [ADR-0022](../11-ADR/0022-tenancy-enforcement.md)). Implemented in Phase 2:
  - **Resolution:**
    - `TenantResolutionMiddleware` maps the host to a store through `TenantDomains`.
    - An unknown host gets `404 StoreNotFound`. There is no fallback store in Production.
    - Platform hosts (`Tenancy:PlatformHosts`) have no store.
  - **Development/Testing only:**
    - `localhost` serves the seeded default store, or the store named by `Tenancy:LocalDefaultTenant`, and `{slug}.localhost` serves that store.
    - The `X-Tenant: <slug>` header overrides both.
    - `admin.localhost` is the platform host.
  - **Availability:**
    - `Suspended`/`Archived` stores answer `503 StoreUnavailable` for every endpoint **except the five marked `AvailableWhenStoreClosedAttribute`**: `GET /api/storefront/config` (so the SPA can render the store's own branded "unavailable" screen) and `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me` (so the store's administrators can sign in and see why). Registration, password reset and every permission-protected endpoint stay `503`.
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
- **Customer account (Phase 7, [ADR-0027](../11-ADR/0027-customer-profile-and-erasure.md)):**
  - `/api/account` acts on the caller's own profile. An address id outside the caller's book is a 404 (update, delete, defaults); at checkout it is `400 AddressNotFound`.
  - `POST /api/orders` takes either `shippingAddressId` or a typed `shippingAddress`. The order stores a single-line snapshot, so editing an address later never changes a past order.
  - `GET /api/account/export` and `GET /api/admin/customers/{id}/export` return the data as a JSON attachment.
  - `POST /api/account/erase` requires the current password. Erasure revokes every session at once: the old access token gets 401 on the next request.
- **Basket (Phase 8, [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)):**
  - **Guests:** the server sets `souq_basket` on the first write. It is an HttpOnly, Secure, `SameSite=Strict` cookie scoped to `/api/basket`, holding a random token; the client never sees or sends it explicitly. Customers use their session instead.
  - **Merge:** a customer's first basket request that still carries a guest cookie merges that basket and deletes the cookie. A token that matches nothing (expired, already merged, or from another store) reads as an empty basket, and the cookie is deleted.
  - **Responses:** every response is the whole basket, priced by the same pipeline that creates orders: lines with `sellable` and `available`, then `subtotal`, `discount`, `shipping`, `tax`, `total`, `coupon` and `readyForCheckout`.
  - **Quotes:** `GET /api/basket/quote?couponCode=` reports a coupon that can't be used inside `coupon` (`applied: false`, `errorCode`, `message`) instead of failing. It shares the coupon-preview rate limit.
  - **Errors:** a product that isn't sellable in the host's store is a 404 on add. A line that isn't in the caller's basket is a 404 on update and delete.
  - **Checkout:** `POST /api/orders` prices its lines with the same pipeline, so its subtotal, discount and total equal the basket's for the same lines and coupon.
- **Orders (Phase 9, [ADR-0029](../11-ADR/0029-orders-lifecycle.md)):**
  - **Checkout from the basket:** `POST /api/orders` without `items` creates the order from the caller's basket (`400 BasketEmpty` if it is empty). The purchased quantities leave the basket when payment is confirmed, never before. `billingAddressId` picks a billing address from the book; otherwise the default billing address is used, else the shipping address.
  - **Order details:** orders carry a per-store `orderNumber` from 1001. `GET /api/orders/{id}` also returns `billingAddress`, the `trackingToken` for sharing, `history`, `allowedActions` (for `orders.manage`) and `canCancel` (for the owner). Customers don't receive history notes or actors.
  - **Customer cancellation:** `POST /api/orders/{id}/cancel`, owner only, unpaid orders only. The gateway is asked first. The errors are `422 InvalidOrderOperation` for a paid order, `422 OrderAlreadyPaid` if the gateway reports the payment succeeded first, and `422 PaymentProcessing` while a payment is in flight.
  - **Tracking:** `GET /api/orders/track/{token}` is the only anonymous order endpoint. It returns status, dates, tracking number and carrier. `GET /api/orders/{id}/tracking` is removed.
  - **Admin filters on `GET /api/orders`:** `status`, `search` (an order number such as `1042` or `#1042`, or part of a customer's name or email), and `from` and `to` on creation time (`to` is exclusive).
- **Coupons (Phase 10, [ADR-0030](../11-ADR/0030-coupon-redemptions.md)):**
  - **Create and update** accept `startsAt` and `maxUsesPerCustomer`, both optional. `startsAt` must be before `expiresAt`, and the per-customer limit can't exceed `maxUses`.
  - **Usage:** `POST /api/orders` with a coupon takes one use inside the checkout transaction. If no use is left, or the customer has reached their limit, it answers `422 InvalidCoupon` and creates no order, even under concurrency. Cancelling the order gives the use back. The basket quote reports the same outcome in `coupon` without failing.
  - **Redemptions:** `GET /api/coupons/{id}/redemptions?page=&pageSize=` (`promotions.manage`) lists the orders that used the coupon. Each entry has `orderId`, `orderNumber`, `customerId`, `customerName`, `discount`, `currency`, `status` (`Reserved`, `Confirmed` or `Released`) and `createdAt`. Another store's coupon is a 404.
  - **Delete:** `DELETE /api/coupons/{id}` answers `409 CouponInUse` once the coupon has been used. Deactivate it with `PUT` instead.
- **Payments and refunds (Phase 11, [ADR-0031](../11-ADR/0031-payments-and-refunds.md)):**
  - **Order detail** includes `payment` (`status`, `amount`, `refundedAmount`, `refundable`, `currency`, `refunds[]`) and `canRefund`. Customers see the status and the refunded amount only; the refund list and reasons are for staff.
  - **Refund:** `POST /api/orders/{id}/refunds` `{ amount?, reason? }`, with `store.payments.manage`. Without `amount`, everything left is refunded.
    - The answer is the refund's outcome: `Succeeded`; `Failed` (the gateway refused, with `failureReason`); or `Pending` (the gateway didn't answer).
    - Errors: `422 RefundExceedsPayment`, `422 NothingToRefund`, `422 PaymentNotRefundable`. Another store's order is a 404.
  - **Retry:** `POST /api/orders/{id}/refunds/{refundId}/retry` re-sends a pending refund with the same idempotency key (`422 RefundNotPending` otherwise).
  - **Cancelling a paid order** (`PUT /api/orders/{id}/status` with `Cancel`) refunds the remaining amount after the cancellation commits.
  - **Cancelling an unpaid order as staff** asks the gateway first, like the customer's cancellation. It answers `422 OrderAlreadyPaid` if the payment has just succeeded (the order is confirmed instead; cancel it again to refund it), or `422 PaymentProcessing` while a payment is in flight.
  - **Store payment account:** `GET`, `PUT` and `DELETE /api/admin/store/payments` (`store.payments.manage`), and `/api/platform/tenants/{id}/payments` on the platform host.
    - `PUT` takes `publishableKey`, `secretKey` and `webhookSecret`; empty secrets keep the saved ones.
    - Responses never include a secret: only `secretKeyHint` (the last four characters) and `hasWebhookSecret`.
    - Errors: `422 InvalidPaymentKeys`, `422 TestKeysNotAllowed`, `503 SecretsNotConfigured`.
  - **`GET /api/payments/config`** returns the publishable key of the host store's account, or of the deployment's.
  - **Webhook** (`POST /api/payments/webhook`): verified with the host store's webhook secret, else the deployment's. An event for another store sharing the deployment account is applied in that store.
  - **A store account that can't be used** (its keys can't be decrypted) answers `503 PaymentsUnavailable`.
- **Shipping (Phase 12, [ADR-0032](../11-ADR/0032-shipping-methods.md)):**
  - **Methods:** `GET` and `POST /api/admin/shipping-methods`, `PUT` and `DELETE /api/admin/shipping-methods/{id}` (`store.shipping.manage`).
    - The body has `name`; `price` (store currency); `freeOverAmount?`; `minDays?` and `maxDays?` (both or neither); `carrier?`; `trackingUrlTemplate?` (https, containing `{number}`); `countries?` (ISO codes; empty = everywhere); `isActive`; `sortOrder`.
    - A rule violation answers `422 InvalidShippingMethod`. Another store's method is a 404.
  - **Quote:** `GET /api/basket/quote?couponCode=&shippingMethodId=&country=` adds `shippingMethods` to the basket.
    - It holds `options[]` (`methodId`, `name`, `cost`, `carrier`, `minDays`, `maxDays`) for the country, `selectedMethodId`, `required`, and `errorCode`/`message` (`ShippingMethodRequired`, `ShippingMethodUnavailable`, `ShippingNotAvailable`).
    - `shipping` and `total` include the selected method.
  - **Checkout:** `POST /api/orders` takes `shippingMethodId`.
    - When the store has active methods, one that serves the address is required.
    - The country comes from the chosen `shippingAddressId`. A typed address has none, so only methods without country limits apply.
    - Errors: `422 ShippingMethodRequired`, `422 ShippingMethodUnavailable`, `422 ShippingNotAvailable`.
    - The response includes `shippingCost`, and `totalAmount` includes it.
  - **Order detail and tracking:** the detail adds `shippingMethod`, `shippingCost`, `shippingMinDays`, `shippingMaxDays`, `shippingCountry` and `trackingUrl`. The public tracking response adds `trackingUrl`.
- **Rate limits (Phase 3; basket writes Phase 8):** auth, refresh, coupon-preview and basket-write endpoints answer `429 TooManyRequests` with `Retry-After` when a limit is exceeded. The policies and their defaults are in `src/Souq.API/Security/RateLimiting.cs`; which endpoint uses which is in [Endpoints.md](Endpoints.md).
- **Platform area (Phase 4, [ADR-0024](../11-ADR/0024-platform-administration.md)):**
  - Endpoints are marked `[PlatformEndpoint]` and are served only on platform hosts, behind `platform.*` permissions.
  - These are the only requests that carry a store id (`/api/platform/tenants/{id}/…`), enforced by an architecture test.
  - Every platform request writes an audit entry.
- **Optional modules (Phase 4):**
  - Endpoints of a module disabled for the host's store answer `404 ModuleDisabled` before authentication. Examples: `[RequiresModule("promotions")]` on coupons, `"reviews"` on reviews.
  - Use cases that touch a module check it too.
- **Store configuration (Phase 4):**
  - `GET /api/storefront/config` is public and serves presentation data only: branding, locale, currency decimals, contact, SEO, modules.
  - It sends a content-hash `ETag` with `Cache-Control: no-cache`, so a revalidation answers 304.
  - Unlike every other storefront endpoint, it is served while the store is suspended, archived or provisioning (`AvailableWhenStoreClosedAttribute`), so the SPA can render the store's own unavailable screen. It exposes presentation data only.
  - Settings are edited through `PUT /api/admin/store/settings` (store admin) or `PUT /api/platform/tenants/{id}/settings` (platform), with the same body and the same validation.
- **Authorization (1B, [ADR-0019](../11-ADR/0019-authorization-foundation.md)):** endpoints declare `[HasPermission(Permissions.X.Y)]`, `[Authorize]` or `[AllowAnonymous]` — explicitly, every one. Resource ownership is checked inside the use case (404 for someone else's resource).
- **Automated guards:** integration tests enumerate every endpoint and assert that each declares its decision, that the public surface equals a reviewed list, that every declared permission exists, and that permission-protected endpoints answer anonymous → 401 and customer → 403.

## 7. Versioning

- **No URL versioning yet (YAGNI).** The only consumer is our own SPA, deployed together with the API.
- **Rule:** additive changes only (new fields, new endpoints). A breaking change means changing the SPA in the same release.
- **Revisit** when a third-party or public API consumer exists. Then use `/api/v1` for the public surface only.

## 8. Idempotency and concurrency

| Operation | Guarantee |
|---|---|
| Payment confirmation (client and webhook) | Idempotent by order state: a second confirmation returns the current status with no side effects. A concurrent race is resolved by `rowversion` plus a re-read (1A). |
| Webhooks | Signature-verified (the host store's secret, else the deployment's); idempotent by the same rule; routed to the store named in the intent (Phase 11); unknown events → 200 (ignored) |
| Refunds (`POST /api/orders/{id}/refunds`, `…/retry`) | The amount is reserved on the payment under `rowversion`, so concurrent refunds can't exceed it. The gateway receives an idempotency key per refund, so a retry after a timeout returns the first refund instead of refunding twice (Phase 11) |
| Checkout (`POST /api/orders`) | The order and its stock reservation are written in one transaction; the loser of the last unit gets `422 InsufficientStock`, because inventory conflicts are retried from a fresh read (Phase 6). **Not idempotent today:** a duplicate submission creates a second order with its own reservation and payment intent (no double charge; the unpaid duplicate expires), measured by `CheckoutIdempotencyTests`. Whether a duplicate should replay or be rejected is owner decision F-8 ([OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)); no *Idempotency-Key* header exists yet |
| Coupon use at checkout | Taken in the order's transaction, on a fresh read under the coupon's `rowversion`. The loser of the last use gets `422 InvalidCoupon` and its checkout rolls back. Releasing a use is idempotent by redemption status (Phase 10) |
| Reservation commit, release and restock | Idempotent by reservation status: committing twice, or cancelling an already cancelled order, changes nothing (Phase 6) |
| Updates to shared rows | Optimistic concurrency → 409 with a message to reload |
| Stock corrections (`POST /api/admin/inventory/{productId}/adjustments`) | A delta with a reason, applied to the current value, so a concurrent sale is never overwritten. Going below what open orders reserve → `422 InvalidInventoryOperation`. The product form carries no stock (Phase 6; replaces the 1A compare-and-set) |

## 9. Uploads

- `multipart/form-data`, field `file`.
- The controller checks presence and the size ceiling (HTTP concerns).
- The **Application layer** validates the actual content by magic bytes, and the stored file extension is derived from the detected type, never from the client's filename or `Content-Type` ([Security.md §5](../07-SECURITY/Security.md#5-input-validation-xss-and-uploads)).

## 10. Correlation ([ADR-0018](../11-ADR/0018-observability.md))

- Every response carries `X-Correlation-Id`: the request's W3C trace id (32 hex characters). It is also the `traceId` in error bodies and the `CorrelationId` in the server logs.
- An incoming W3C `traceparent` header is honoured (for gateways or services in front of the API). Arbitrary client-chosen ids are not accepted.
- The header is exposed to browsers through CORS so the UI can show it on error screens.

## 11. Documentation rules

- Every new endpoint appears in Swagger with its auth requirement.
- A module's public HTTP surface is listed in [Modules.md](../04-MODULES/Modules.md) when that module is rebuilt.
- An endpoint list duplicated in the README must be regenerated, not hand-edited. The inventory in [Endpoints.md](Endpoints.md) is already generated from the compiled API by `tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`, which fails when the committed file drifts. A full API reference generated from OpenAPI remains **PLANNED** (Phase 22).
