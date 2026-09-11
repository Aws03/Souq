# Souq: Frontend Architecture

> **Status:** Target adopted 2026-09-11. **Phase 15 ✅** delivered the runtime ([ADR-0035](../11-ADR/0035-white-label-runtime.md)): the store configuration at boot, semantic theming, module gates, four areas and route-level code splitting (§6). Existing screens move into `features/*` in the phases that rebuild them (16–17). D-19 is deferred with a trigger.
> **Stack:** React 18 · Vite 5 · React Router 6 · i18next · CSS Modules + design tokens · Stripe.js. Tests: **Vitest**, introduced in 1A for pure logic.

## 1. Assessment: evolve, don't rewrite

The current frontend is **78 modules** with real routing, separate storefront and admin layouts, a reusable UI kit, i18n with RTL/LTR, and URL-driven catalog state. That is a sound base.

What it lacks:
- feature-based organization;
- four clearly separated areas;
- a tenant/theme runtime;
- a consistent data-fetching layer;
- types and tests.

Rewriting it would throw away working, accessible components for no business gain. The plan is to **move and refine**, not to recreate.

## 2. The four areas

| Area | Served on | Layout | Guard (UX only; the server enforces) |
|---|---|---|---|
| Public storefront | tenant domain | `StorefrontLayout` (announcement, nav, category bar, footer, cart drawer) | none |
| Customer account | tenant domain `/account/*` | `AccountLayout` | signed-in customer |
| Tenant admin | tenant domain `/admin/*` | `TenantAdminLayout` (sidebar, mobile tab bar) | tenant admin/staff + permission per route |
| Platform owner | platform host `/platform/*` | `PlatformLayout` | platform owner/admin |

One build serves all four areas, split with **route-level code splitting**. Visitors never download the admin bundles.

**On which host the platform area mounts:** the SPA asks `GET /api/storefront/config` at boot, and the response tells it whether it is on a tenant host or the platform host. The browser can never *choose* the mode in a way that grants access, because every API call is authorized by the server for that host.

## 3. Target folder structure

```
frontend/src/
├── app/              App root, provider composition, error boundary, router assembly
├── routes/           one route module per area (lazy), guards: RequireAuth, RequirePermission, RequireModule
├── layouts/          StorefrontLayout, AccountLayout, TenantAdminLayout, PlatformLayout, AuthLayout
├── features/         vertical slices; each has api.js, hooks.js, pages/, components/, (model.js for pure logic)
│   ├── auth/  catalog/  product/  cart/  checkout/  orders/  account/  wishlist/  reviews/
│   ├── admin/        dashboard, products, categories, inventory, orders, customers, coupons, reviews, shipping, settings, staff
│   └── platform/     dashboard, tenants (identity, branding, domains, modules, admins), users, settings, audit
├── components/       design-system UI kit: Button, DataTable, Drawer, FormField, Pagination, StateViews,
│                     ConfirmDialog, Skeleton, Toast … (today's components/common/*)
├── contexts/         AuthProvider, TenantProvider, ThemeProvider, ToastProvider
├── api/              http core only: base URL, credentials, silent refresh, ProblemDetails → typed error
├── hooks/  utils/  config/  types/
├── i18n/             i18next setup + per-feature namespaces
└── styles/           tokens.css (semantic), reset.css
```

## 4. Boundaries and rules

| Concern | Lives in | Rules |
|---|---|---|
| **Pages** | `features/*/pages` | Compose components and hooks. No `fetch`; no business rules (prices, stock, permissions). |
| **Feature components** | `features/*/components` | Presentational plus local UI state |
| **UI kit** | `components/` | Knows nothing about business or API; styled only with semantic tokens |
| **API clients** | `features/*/api.js` over `api/http.js` | One file per feature. The shared http core handles auth headers, token refresh, and error normalization. |
| **Server state** | `features/*/hooks.js` | Target: TanStack Query (caching, deduplication, retries). D-19 was **deferred** in Phase 15 ([ADR-0035](../11-ADR/0035-white-label-runtime.md)); the trigger is Phase 16. Until then, the existing hooks pattern. |
| **Client state** | contexts or local state | Auth session, tenant config, theme, toasts. The cart becomes *server* state in Phase 8. |
| **Authentication** | `contexts/AuthProvider` (today `context/AuthContext.jsx` + `api/client.js`, Phase 3 ✅) | Access token in module memory; refresh through the `HttpOnly` cookie with a single-flight silent refresh. What the UI shows follows `user.permissions`. Route guards are UX only. |
| **Tenant context** | `app/TenantProvider.jsx` (Phase 15 ✅) | Populated from the config endpoint, read-only: `useTenant`, `useStoreConfig`, `useModule`. Components never send a tenant id to the API. |
| **Theme/branding** | `app/storeTheme.js` + `app/tenantModel.js` + the tokens in `styles.css` (Phase 15 ✅) | Semantic CSS variables set from the store's config ([WhiteLabel.md](WhiteLabel.md)). There is no visitor theme choice. |
| **Forms** | feature components + `FormField` | Client validation is for UX; server validation is authoritative and displayed as returned |
| **Tables** | `DataTable` + server paging | Page, sort, and filter state in the URL for admin lists |
| **Loading, error, empty** | `Skeleton`, `ErrorBanner`, `EmptyState` | Every data view handles all three |
| **Pure logic** | `features/*/model.js` | Tested with Vitest (payload builders, query builders, formatting) |

**The frontend is never the authority** for:
- tenant authorization;
- permissions;
- prices;
- stock;
- coupon validity.

It displays the server's decisions and handles its errors.

## 5. Changes made in Phase 1A (minimal)

| Change | Reason |
|---|---|
| Admin product list sends `categoryIds` (from `features/admin/products/productQuery.js`, tested) | Phase 0 C8: the category filter was silently ignored |
| The product form sends stock only when the admin changed it, together with `expectedStockQuantity` (`productPayload.js`, tested) | Phase 0 C4: stale forms were overwriting sales |
| The admin UI shows the server's 409 message ("stock changed since you opened the form") | Concurrency feedback |
| Inventory history knows the new `Cancellation` movement type (ar/en) | Phase 0 C2/C3 ledger fix |
| Vitest added (`npm test`) | The first frontend tests, for pure logic |
| Unused `hooks/useProducts.js` removed | Dead code (E7) |
| `npm audit fix` (non-breaking) | B11 |

**Phase 3 (authentication, minimal adaptation):**

| Change | Reason |
|---|---|
| `api/client.js` holds the access token in memory. On a 401 it runs one shared silent refresh and retries once; a rejected refresh emits `session-expired`. Tested in `client.test.js` | B4: no token in `localStorage`; rotation makes parallel refreshes look like theft |
| `AuthProvider` restores the session on load (`loading` state) and exposes `can()` and `canManageStore` | No stored user; permissions come from the server |
| `AdminRoute`, the navbar and the admin navigation follow permissions, not the role name | Staff share the dashboard with admins but not every page |
| New `/verify-email` page; new error codes translated in both languages | Email verification |
| Coupon preview no longer sends a currency | The server uses the store currency (Phase 2) |

**Phase 4 (administration API, minimal adaptation):**

| Change | Reason |
|---|---|
| `/accept-invitation` reuses the reset-password page in "invitation" mode (same API, different text) | Invited administrators set their password on their store's host |
| New error codes translated (`ModuleDisabled`, `DomainTaken`, `TenantSlugTaken`, `TenantHasNoDomain`, `CannotDisableSelf`, `LastAdministrator`) | Server decisions shown in both languages |
| The SPA does not consume `/api/storefront/config` yet | That is the Phase 15 white-label runtime (TenantProvider/ThemeProvider); the platform UI is Phase 18 |

**Phase 5 (catalog, adaptation to the new contract):**

| Change | Reason |
|---|---|
| `features/catalog/catalogText.js` (tested) reads a product's or category's name and description from `translations` in the UI language. It falls back to the store's default text, and to cart items saved before Phase 5 (`nameAr`/`nameEn`) | Per-language catalog texts (D-10) |
| The admin product form has per-language fields, SKU, compare-at price, slug, brand, low-stock threshold and, on create, the status. Gallery tiles offer "make main" and "remove". The payload comes from `productPayload.js` (tested), with stock compare-and-set unchanged | The new catalog contract ([ADR-0025](../11-ADR/0025-catalog-model.md)) |
| The admin products table reads `/api/admin/products` (every status). It has a status filter, and publish / draft / archive / restore actions. `productQuery.js` sends `categoryId` and `status` (tested) | C7: archived products stay manageable |
| The categories page shows the tree order with indentation, sort order, a visibility toggle and per-language names. Parent options exclude the category and its descendants (`categoryForm.js`, tested) | Tree rules (the server also rejects cycles and depth > 5) |
| The storefront shows localized product and category names, the product gallery, and a struck-through compare-at price | Offers and localization |
| New error codes translated (`ProductSlugTaken`, `SkuTaken`, `DefaultTranslationRequired`, `InvalidCategory`) | Server decisions shown in both languages |

**Phase 6 (inventory):**

| Change | Reason |
|---|---|
| The inventory page shows on hand, reserved and available, and colours rows by available stock. Users with `inventory.manage` get an adjustment drawer: a delta with a reason, plus the threshold | Stock is corrected by deltas; a concurrent sale is never overwritten (C4) |
| The product form sets stock and the threshold only when a product is created; on edit it shows a read-only summary pointing to the inventory page. `productPayload.js` (tested) never sends stock on edit | The product aggregate no longer owns stock ([ADR-0026](../11-ADR/0026-inventory-reservations.md)) |
| `StockChanged` translation removed; `InvalidInventoryOperation` added | The compare-and-set path no longer exists |

**Phase 7 (customers):**

| Change | Reason |
|---|---|
| `/account` ("My account"; the link shows only when the session has a `customerId`): a profile form, the address book with an add/edit drawer and default actions, a data download, and account deletion with password confirmation followed by a local sign-out | Self-service profile and data rights ([ADR-0027](../11-ADR/0027-customer-profile-and-erasure.md)) |
| Checkout lists saved addresses with the default shipping address preselected, and sends only `shippingAddressId`. "Use a different address" keeps the free-text field. `features/checkout/shippingChoice.js` (tested) | The server snapshots the caller's own address; the client never sends address text it didn't type |
| `/admin/customers` (`customers.view`): search, status filter, order count, spend and last order. A detail drawer shows addresses, recent orders (`orders.view`), and block / export / erase (`customers.manage`; erase asks for explicit confirmation). `features/admin/customers/customerActions.js` (tested) mirrors the entity rules | Admin customer management |
| `features/account/addressForm.js` (tested) builds the address payload: trimmed, upper-case country, empty optional fields as `null` | One address shape for the account page and the admin views |
| The admin mobile tab bar truncates labels, so seven tabs fit on a 360px screen | The new Customers tab |
| New error codes translated (`CustomerBlocked`, `AddressNotFound`, `InvalidCustomerData`) | Server decisions shown in both languages |

**Phase 8 (basket):**

| Change | Reason |
|---|---|
| `CartContext` is backed by `/api/basket`. It reads after the session is restored and again whenever the user changes (the server merges the guest basket at the first read after sign-in). Every action replaces the state with the basket the server returns | The cart survives a refresh and a device switch (C12). The server is the only source of prices and totals ([ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)) |
| `features/basket/basketModel.js` (tested) maps server lines to the item shape the components already render, flags lines that block checkout (no longer available, more than available), bounds quantities, and words a rejected coupon | One place for basket rules on the client |
| The drawer and checkout show the server's subtotal, shipping and total. Checkout previews a coupon through `/api/basket/quote` (the pipeline that creates the order) and re-quotes when the basket changes. Checkout is disabled while a line blocks it | Basket totals equal checkout totals |
| Add-to-cart waits for the server: the "added" toast appears only on success, and failures (for example, not enough stock) show once from the cart context | The server decides availability |
| `api.applyCoupon` removed (its only caller was checkout); `InvalidBasketOperation` translated | No second pricing path |

**Phase 9 (orders):**

| Change | Reason |
|---|---|
| `/orders/:id` is now the customer's order page (protected): lines and totals as placed, both addresses, the timeline, a copyable tracking link, and "Cancel order" while unpaid | Customer order detail and self-service cancellation ([ADR-0029](../11-ADR/0029-orders-lifecycle.md)) |
| `/track/:token` is the public tracking page; it shows status and shipment only | Tracking by random token instead of the sequential id (B8) |
| Order numbers replace database ids wherever a customer or admin reads them: My orders, confirmation, the admin list and drawers | Per-store numbers from 1001 |
| The admin order list has status and search filters (order number, customer name or email). Its detail drawer shows lines, addresses and the status history with who acted and the notes. The drawer's action buttons come from the server's `allowedActions`, so the JavaScript copy of the transition rules is gone | One transition table |
| Checkout sends no lines (the server reads the basket), and after payment it reloads the basket instead of clearing it locally | Checkout from the basket; the server removes what was bought |
| `features/orders/orderView.js` (tested): tracking URL, actor labels, admin filter query | Pure helpers |
| New error codes translated (`BasketEmpty`, `OrderAlreadyPaid`, `PaymentProcessing`) | Server decisions shown in both languages |

**Phase 10 (coupons):**

| Change | Reason |
|---|---|
| The coupon form has a start date and a per-customer limit. `features/admin/coupons/couponForm.js` (tested) turns a coupon into form state, builds the request body, and names the first problem that blocks saving (the window's order, a per-customer limit above the total) | Rules shown before the server rejects them ([ADR-0030](../11-ADR/0030-coupon-redemptions.md)) |
| The coupon list shows the validity window and the per-customer limit. A row action opens a redemptions drawer: order number, customer, discount and status | Admins see who used a coupon |
| New error code translated (`CouponInUse`) | Deleting a used coupon says what to do instead |

**Phase 11 (payments):**

| Change | Reason |
|---|---|
| `/admin/payments` (sidebar and mobile tab bar, `store.payments.manage`): the store's account status; connect or update Stripe keys; disconnect. The secret fields are write-only: left empty they keep the saved key, and the page shows only its last four characters | Per-store accounts ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)); secrets never reach the browser |
| The admin order drawer has a payment section: status, refunded amount, each refund with its status and reason, a refund form (empty amount = everything left) when the server allows it (`canRefund`), and "Retry refund" for a pending one. Cancelling a paid order warns that the payment will be refunded | Refunds from the order they belong to |
| The customer's order page shows the refunded amount | Customers see what came back |
| `features/admin/payments/paymentView.js` (tested): key modes, the account form's first problem, the request body (empty secrets aren't sent), the refund amount check with the currency's minor units | Pure helpers |
| New error codes translated (`RefundExceedsPayment`, `NothingToRefund`, `PaymentNotRefundable`, `RefundNotPending`, `InvalidPaymentKeys`, `TestKeysNotAllowed`, `SecretsNotConfigured`, `PaymentsUnavailable`) | Server decisions shown in both languages |

**Phase 12 (shipping):**

| Change | Reason |
|---|---|
| Checkout has a delivery step: the methods that serve the chosen address's country, with price and estimate. It re-quotes when the address, the method or the basket changes, and keeps the chosen method while it's offered. A typed address without a country shows only methods without country limits, with a hint to use a saved address | Shipping is chosen where it's priced ([ADR-0032](../11-ADR/0032-shipping-methods.md)) |
| The order summary shows shipping ("choose a method" until one is chosen). The cart drawer says "at checkout" when a choice will be needed | Totals match what will be charged |
| `/admin/shipping` (sidebar and tab bar, `store.shipping.manage`): the methods table and its form drawer | Store-defined rates |
| The customer order page, the admin drawer and the public tracking page show the method and a carrier tracking link | Carriers and tracking |
| `features/checkout/shippingOptions.js` and `features/admin/shipping/shippingForm.js` (tested) | Pure helpers |
| New error codes translated (`ShippingMethodRequired`, `ShippingMethodUnavailable`, `ShippingNotAvailable`, `InvalidShippingMethod`) | Server decisions shown in both languages |

## 6. Phase 15 migration plan: status

1. ✅ **`app/` introduced without moving features.** It holds:
   - `TenantProvider`: the boot from `GET /api/storefront/config`;
   - `storeTheme` and `tenantModel`: the theme, formatting and module logic, unit-tested;
   - `BootScreens`: store closed, unknown store, retry;
   - `StoreBrand`: the store's logo or name;
   - `PlatformLayout`: the platform shell.

   The guards stay in `components/ProtectedRoute.jsx`, which gained `RequireModule` and `PlatformRoute`. The customer account pages still render inside the storefront layout behind `ProtectedRoute`; their own `AccountLayout` comes with the account rebuild (Phase 16).
2. ⏸ **Deferred to Phases 16–17:** moving features with `git mv`. Each screen moves when it is rebuilt, so its history is preserved and the diff stays reviewable.
3. ⏸ **Deferred with D-19:** splitting `api/client.js` into per-feature `api.js` files. The query layer decides their shape. The shared client already normalizes ProblemDetails and refreshes the session silently (Phase 3).
4. ✅ **Theming.** Brand-named tokens were replaced by semantic tokens everywhere. The theme comes from the store, and the visitor theme switcher is gone.
5. ⏸ **TypeScript:** D-19 is deferred with a trigger ([ADR-0035](../11-ADR/0035-white-label-runtime.md)).
6. ✅ **Route-level lazy loading.** Every page except the home and product pages is lazy.
   - Baseline: the first bundle is about 381 KB of JavaScript (122 KB gzipped), and about 144 KB loads on demand.
   - Budgets will be enforced once a CI pipeline exists.

**Also delivered in Phase 15:**
- The store's name, currency, languages, footer content and announcement come from its configuration.
- Wishlist, reviews and coupons are hidden when their module is off.
- `whiteLabel.test.js` fails on any brand, currency or contact literal in the frontend source.
