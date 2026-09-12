# Souq: frontend architecture

> **Status:** target adopted 2026-09-11. **Phase 15 delivered the runtime** ([ADR-0035](../11-ADR/0035-white-label-runtime.md)): the store configuration at boot, semantic theming, module gates, four areas and route-level code splitting. Existing screens move into feature folders in the phases that rebuild them (16–17). D-19 was **decided in Phase 17** ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md)): a query library is the target for server state, adopted at the first screen rebuilt rather than installed as its own migration; TypeScript waits for a CI pipeline that can enforce it.
> **Stack:** React 18 · Vite 5 · React Router 6 · i18next · CSS Modules with design tokens · Stripe.js. Tests: Vitest, introduced in Phase 1A for pure logic.
>
> **This page is the target shape and the history.** For what exists today and how to change it — structure, boot, routing, state, API, i18n, styling, testing, debt — read [FrontendGuide.md](FrontendGuide.md). Branding and what a store may customize are in [WhiteLabel.md](WhiteLabel.md).

## 1. Assessment: evolve, don't rewrite

The Phase 0 audit found a frontend of **78 modules** with real routing, separate storefront and admin layouts, a reusable UI kit, i18n with RTL/LTR, and URL-driven catalog state. That was judged a sound base. (The tree has since grown to 119 JavaScript and JSX modules, excluding tests.)

What it lacked:
- feature-based organization;
- four clearly separated areas;
- a tenant and theme runtime;
- a consistent data-fetching layer;
- types and tests.

Areas, the tenant runtime and the first tests arrived in Phases 1A–15. Feature-based organization and the data-fetching layer are still open (§6).

Rewriting would have thrown away working, accessible components for no business gain. The plan is to **move and refine**, not to recreate.

## 2. The four areas

| Area | Served on | Layout today | Guard (UX only; the server enforces) |
|---|---|---|---|
| Public storefront | store host | `CustomerLayout` in `frontend/src/App.jsx` (announcement, nav, category bar, footer, cart drawer) | none |
| Customer account | store host, `/account`, `/orders` | `CustomerLayout` — a dedicated account shell is **PLANNED** for Phase 16 | signed-in customer (`ProtectedRoute`) |
| Store admin | store host, `/admin/*` | `AdminLayout` (sidebar, mobile tab bar) | `AdminRoute`, then a permission per route and a module flag where the page is optional |
| Platform owner | platform host, `/platform/*` | `PlatformLayout` (shell only; screens **PLANNED** for Phase 18) | `PlatformRoute` (platform account) |

Authentication screens (`/login`, `/register`, `/forgot-password`, `/reset-password`, `/verify-email`, `/accept-invitation`) render in `AuthLayout` outside all four layouts, on both host kinds; the platform host offers no self-registration.

One build serves every area, split with **route-level code splitting**. Visitors never download the admin bundles.

**How the area is chosen:** the SPA asks GET `/api/storefront/config` at boot. A store answers with its configuration; the platform host has no such endpoint and answers `404`, which selects the platform routes. The browser can never *choose* the mode in a way that grants access, because every API call is authorized by the server for that host.

## 3. Target folder structure

**TARGET**, not today's tree. Names in italics do not exist yet; today's layout and its rules are mapped in [FrontendGuide.md](FrontendGuide.md) §2.

```
frontend/src/
├── app/              App root, provider composition, error boundary, router assembly
├── routes/           one route module per area (lazy), guards
├── layouts/          one layout per area, plus the authentication layout
├── features/         vertical slices; each with its own api, hooks, pages, components and pure model
│   ├── auth/  catalog/  product/  cart/  checkout/  orders/  account/  wishlist/  reviews/
│   ├── admin/        dashboard, products, categories, inventory, orders, customers, coupons, reviews, shipping, settings, staff
│   └── platform/     dashboard, tenants (identity, branding, domains, modules, admins), users, settings, audit
├── components/       design-system UI kit (today `frontend/src/components/common`)
├── contexts/         the providers, including the tenant provider that today lives in `frontend/src/app`
├── api/              http core only: base URL, credentials, silent refresh, ProblemDetails → typed error
├── hooks/  utils/  config/  types/
├── i18n/             i18next setup and per-feature namespaces
└── styles/           semantic tokens and reset
```

Differences from today worth naming, because they are the work items: guards live in `frontend/src/components/ProtectedRoute.jsx` rather than a routes folder; the tenant provider lives in `frontend/src/app` while the other providers live in `frontend/src/context`; layouts are defined inside `frontend/src/App.jsx` and `frontend/src/pages/admin`; `frontend/src/features` holds pure logic only, with screens still under `frontend/src/pages`; the token sheet is the single `frontend/src/styles.css`; and there is no error boundary.

## 4. Boundaries and rules

| Concern | Lives in | Rules |
|---|---|---|
| **Pages** | today `frontend/src/pages`, target *features/…/pages* | Compose components and hooks. No `fetch`; no business rules (prices, stock, permissions) treated as authority. |
| **Feature components** | today `frontend/src/components`, target *features/…/components* | Presentational plus local UI state |
| **UI kit** | `frontend/src/components/common` | Knows nothing about business or API; styled only with semantic tokens |
| **API clients** | today the single `frontend/src/api/client.js`; target one module per feature over an http core | The shared client handles the auth header, token refresh and error normalization. |
| **Server state** | today each screen's own `useEffect` and `useState`; target a query layer | TARGET: TanStack Query for caching, de-duplication and retries. D-19 is **DECIDED** ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md)): it is adopted at the first screen rebuilt or newly written, with the test environment, not as a separate migration. Until then every touched screen guards its effects with a local `active` flag. |
| **Client state** | contexts or local state | Session, tenant config, toasts. The cart became *server* state in Phase 8. |
| **Authentication** | `frontend/src/context/AuthContext.jsx` with `frontend/src/api/client.js` | Access token in module memory; refresh through the `HttpOnly` cookie with a single-flight silent refresh. What the UI shows follows `user.permissions`. Route guards are UX only. |
| **Tenant context** | `frontend/src/app/TenantProvider.jsx` | Populated from the config endpoint, read-only: `useTenant`, `useStoreConfig`, `useModule`. Components never send a tenant id to the API. |
| **Theme and branding** | `frontend/src/app/storeTheme.js` with `frontend/src/app/tenantModel.js` and the tokens in `frontend/src/styles.css` | Semantic CSS variables set from the store's configuration ([WhiteLabel.md](WhiteLabel.md)). There is no visitor theme choice. |
| **Forms** | feature components with `FormField` | Client validation is for UX; server validation is authoritative and displayed as returned |
| **Tables** | `DataTable` with server paging | TARGET: page, sort and filter state in the URL for admin lists. Today only the storefront catalog keeps its state in the URL; admin lists hold it in component state. |
| **Loading, error, empty** | `Skeleton`, `ErrorBanner`, `EmptyState` | Every data view handles all three |
| **Pure logic** | `frontend/src/features` | Tested with Vitest (payload builders, query builders, formatting, view models) |

**The frontend is never the authority** for:
- tenant authorization;
- permissions;
- prices;
- stock;
- coupon validity;
- module availability.

It displays the server's decisions and handles its errors.

## 5. Change log by phase

Historical record: each table describes what changed **in that phase**, not necessarily what is true now. Where a later phase superseded an entry, it is marked.

**Phase 1A (minimal):**

| Change | Reason |
|---|---|
| The admin product list sends `categoryIds` (from `frontend/src/features/admin/products/productQuery.js`, tested) | Phase 0 C8: the category filter was silently ignored. *Superseded in Phase 5:* the admin endpoint binds a single `categoryId` |
| The product form sends stock only when the admin changed it, together with an expected quantity (`frontend/src/features/admin/products/productPayload.js`, tested) | Phase 0 C4: stale forms were overwriting sales. *Superseded in Phase 6:* the form never sends stock on edit |
| The admin UI shows the server's `409` message ("stock changed since you opened the form") | Concurrency feedback. *Superseded in Phase 6* with the move to adjustments |
| Inventory history knows the `Cancellation` movement type (Arabic and English) | Phase 0 C2/C3 ledger fix |
| Vitest added (`npm test`) | The first frontend tests, for pure logic |
| The unused *hooks/useProducts.js* removed | Dead code (E7) |
| `npm audit fix` (non-breaking) | B11 |

**Phase 3 (authentication, minimal adaptation):**

| Change | Reason |
|---|---|
| `frontend/src/api/client.js` holds the access token in memory. On a `401` it runs one shared silent refresh and retries once; a rejected refresh emits a session-expired event. Tested in `frontend/src/api/client.test.js` | B4: no token in `localStorage`; rotation makes parallel refreshes look like theft |
| `AuthProvider` restores the session on load (`loading` state) and exposes `can()` and `canManageStore` | No stored user; permissions come from the server |
| `AdminRoute`, the navbar and the admin navigation follow permissions, not the role name | Staff share the dashboard with administrators but not every page |
| New `/verify-email` page; new error codes translated in both languages | E-mail verification |
| Coupon preview no longer sends a currency | The server uses the store currency (Phase 2) |

**Phase 4 (administration API, minimal adaptation):**

| Change | Reason |
|---|---|
| `/accept-invitation` reuses the reset-password page in invitation mode (same API, different text) | Invited administrators set their password on their store's host |
| New error codes translated (`ModuleDisabled`, `DomainTaken`, `TenantSlugTaken`, `TenantHasNoDomain`, `CannotDisableSelf`, `LastAdministrator`) | Server decisions shown in both languages |
| The SPA did not consume `/api/storefront/config` yet | *Superseded in Phase 15:* `TenantProvider` now boots from it |

**Phase 5 (catalog, adaptation to the new contract):**

| Change | Reason |
|---|---|
| `frontend/src/features/catalog/catalogText.js` (tested) reads a product's or category's name and description from `translations` in the UI language, falling back to the store's default text and to cart items saved before Phase 5 | Per-language catalog texts (D-10) |
| The admin product form gained per-language fields, SKU, compare-at price, slug, brand, low-stock threshold and, on create, the status. Gallery tiles offer "make main" and "remove" | The new catalog contract ([ADR-0025](../11-ADR/0025-catalog-model.md)) |
| The admin products table reads `/api/admin/products` (every status), with a status filter and publish / draft / archive / restore actions | C7: archived products stay manageable |
| The categories page shows the tree order with indentation, sort order, a visibility toggle and per-language names; parent options exclude the category and its descendants (`frontend/src/features/admin/categories/categoryForm.js`, tested) | Tree rules (the server also rejects cycles and depth > 5) |
| The storefront shows localized names, the product gallery and a struck-through compare-at price | Offers and localization |
| New error codes translated (`ProductSlugTaken`, `SkuTaken`, `DefaultTranslationRequired`, `InvalidCategory`) | Server decisions shown in both languages |

**Phase 6 (inventory):**

| Change | Reason |
|---|---|
| The inventory page shows on hand, reserved and available, and colours rows by available stock. Users with `inventory.manage` get an adjustment drawer: a delta with a reason, plus the threshold | Stock is corrected by deltas; a concurrent sale is never overwritten (C4) |
| The product form sets stock and the threshold only on create; on edit it shows a read-only summary pointing to the inventory page | The product aggregate no longer owns stock ([ADR-0026](../11-ADR/0026-inventory-reservations.md)) |
| The *StockChanged* translation was removed; `InvalidInventoryOperation` added | The compare-and-set path no longer exists |

**Phase 7 (customers):**

| Change | Reason |
|---|---|
| `/account` ("My account"; the link shows only when the session has a customer id): a profile form, the address book with an add/edit drawer and default actions, a data download, and account deletion with password confirmation followed by a local sign-out | Self-service profile and data rights ([ADR-0027](../11-ADR/0027-customer-profile-and-erasure.md)) |
| Checkout lists saved addresses with the default shipping address preselected and sends only an address id; "use a different address" keeps the free-text field (`frontend/src/features/checkout/shippingChoice.js`, tested) | The server snapshots the caller's own address; the client never sends address text it didn't type |
| `/admin/customers` (`customers.view`): search, status filter, order count, spend and last order. A detail drawer shows addresses, recent orders (`orders.view`) and block / export / erase (`customers.manage`) | Admin customer management |
| `frontend/src/features/account/addressForm.js` (tested) builds the address payload: trimmed, upper-case country, empty optional fields as `null` | One address shape for the account page and the admin views |
| The admin mobile tab bar truncates labels so seven tabs fit on a 360px screen | The new customers tab |
| New error codes translated (`CustomerBlocked`, `AddressNotFound`, `InvalidCustomerData`) | Server decisions shown in both languages |

**Phase 8 (basket):**

| Change | Reason |
|---|---|
| `CartContext` is backed by `/api/basket`. It reads after the session is restored and again whenever the user changes (the server merges the guest basket at the first read after sign-in). Every action replaces the state with the basket the server returns | The cart survives a refresh and a device switch (C12). The server is the only source of prices and totals ([ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)) |
| `frontend/src/features/basket/basketModel.js` (tested) maps server lines to the item shape the components render, flags lines that block checkout, bounds quantities, and words a rejected coupon | One place for basket rules on the client |
| The drawer and checkout show the server's subtotal, shipping and total. Checkout previews a coupon through `/api/basket/quote` — the pipeline that creates the order — and re-quotes when the basket changes | Basket totals equal checkout totals |
| Add-to-cart waits for the server: the "added" toast appears only on success, and failures show once from the cart context | The server decides availability |
| The client-side coupon call was removed; `InvalidBasketOperation` translated | No second pricing path |

**Phase 9 (orders):**

| Change | Reason |
|---|---|
| `/orders/:id` is the customer's order page (protected): lines and totals as placed, both addresses, the timeline, a copyable tracking link, and "cancel order" while unpaid | Customer order detail and self-service cancellation ([ADR-0029](../11-ADR/0029-orders-lifecycle.md)) |
| `/track/:token` is the public tracking page; it shows status and shipment only | Tracking by random token instead of the sequential id (B8) |
| Order numbers replace database ids wherever a customer or an administrator reads them | Per-store numbers from 1001 |
| The admin order list has status and search filters; its detail drawer shows lines, addresses and the status history with actor and notes, and its action buttons come from the server's allowed actions | One transition table, on the server |
| Checkout sends no lines (the server reads the basket) and reloads the basket after payment | Checkout from the basket; the server removes what was bought |
| `frontend/src/features/orders/orderView.js` (tested): tracking URL, actor labels, admin filter query | Pure helpers |
| New error codes translated (`BasketEmpty`, `OrderAlreadyPaid`, `PaymentProcessing`) | Server decisions shown in both languages |

**Phase 10 (coupons):**

| Change | Reason |
|---|---|
| The coupon form has a start date and a per-customer limit. `frontend/src/features/admin/coupons/couponForm.js` (tested) turns a coupon into form state, builds the request body, and names the first problem that blocks saving | Rules shown before the server rejects them ([ADR-0030](../11-ADR/0030-coupon-redemptions.md)) |
| The coupon list shows the validity window and the per-customer limit; a row action opens a redemptions drawer | Administrators see who used a coupon |
| New error code translated (`CouponInUse`) | Deleting a used coupon says what to do instead |

**Phase 11 (payments):**

| Change | Reason |
|---|---|
| `/admin/payments` (`store.payments.manage`): the store's account status, connect or update Stripe keys, disconnect. Secret fields are write-only: left empty they keep the saved key, and the page shows only the last four characters | Per-store accounts ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)); secrets never reach the browser |
| The admin order drawer gained a payment section: status, refunded amount, each refund with status and reason, a refund form when the server allows it, and a retry for a pending refund | Refunds from the order they belong to |
| The customer's order page shows the refunded amount | Customers see what came back |
| `frontend/src/features/admin/payments/paymentView.js` (tested): key modes, the account form's first problem, the request body, the refund amount check with the currency's fraction digits | Pure helpers |
| New error codes translated (`RefundExceedsPayment`, `NothingToRefund`, `PaymentNotRefundable`, `RefundNotPending`, `InvalidPaymentKeys`, `TestKeysNotAllowed`, `SecretsNotConfigured`, `PaymentsUnavailable`) | Server decisions shown in both languages |

**Phase 12 (shipping):**

| Change | Reason |
|---|---|
| Checkout gained a delivery step: the methods that serve the chosen address's country, with price and estimate, re-quoted when address, method or basket changes | Shipping is chosen where it is priced ([ADR-0032](../11-ADR/0032-shipping-methods.md)) |
| The order summary shows shipping ("choose a method" until one is chosen); the cart drawer says "at checkout" when a choice will be needed | Totals match what will be charged |
| `/admin/shipping` (`store.shipping.manage`): the methods table and its form drawer | Store-defined rates |
| The customer order page, the admin drawer and the public tracking page show the method and a carrier tracking link | Carriers and tracking |
| `frontend/src/features/checkout/shippingOptions.js` and `frontend/src/features/admin/shipping/shippingForm.js` (tested) | Pure helpers |
| New error codes translated (`ShippingMethodRequired`, `ShippingMethodUnavailable`, `ShippingNotAvailable`, `InvalidShippingMethod`) | Server decisions shown in both languages |

**Phase 13 (reviews and wishlist)** added the product review section and form, the `/admin/reviews` moderation queue with its policy toggle, and a wishlist that is local for a guest and server-side for a customer, merged at sign-in (`frontend/src/features/reviews/ratingSummary.js`, `frontend/src/features/admin/reviews/reviewModeration.js`, `frontend/src/features/wishlist/wishlistModel.js`, all tested) — [ADR-0033](../11-ADR/0033-review-moderation-and-wishlist.md).

**Phase 14 (notifications)** added `NotificationBell` in both shells: an unread count polled every minute, a panel fetched on open, and wording built from the notification kind and its parameters so it follows a language switch (`frontend/src/features/notifications/notificationView.js`, tested) — [ADR-0034](../11-ADR/0034-notifications-outbox.md).

**Phase 15 (white-label runtime)** is summarized in §6 and detailed in [ADR-0035](../11-ADR/0035-white-label-runtime.md).

## 6. Phase 15 migration plan: status

1. **Done — `frontend/src/app` introduced without moving features.** It holds `TenantProvider` (the boot from GET `/api/storefront/config`), `frontend/src/app/storeTheme.js` and `frontend/src/app/tenantModel.js` (theme, formatting and module logic, unit-tested), `frontend/src/app/BootScreens.jsx` (closed store, unknown store, retry), `frontend/src/app/StoreBrand.jsx` (the store's logo or name) and `frontend/src/app/PlatformLayout.jsx` (the platform shell).
   The guards stayed in `frontend/src/components/ProtectedRoute.jsx`, which gained `RequireModule` and `PlatformRoute`. The customer account pages still render inside the storefront layout behind `ProtectedRoute`; their own shell comes with the account rebuild (Phase 16).
2. **Deferred to Phases 16–17 — moving features.** Each screen moves with `git mv` when it is rebuilt, so its history is preserved and the diff stays reviewable.
3. **Waits on the query layer — splitting `frontend/src/api/client.js`** into per-feature modules. The query layer decides their shape, and D-19 now names when it arrives ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md)). The shared client already normalizes the error contract and refreshes the session silently (Phase 3).
4. **Done — theming.** Brand-named tokens were replaced by semantic tokens everywhere. The theme comes from the store, and the visitor theme switcher is gone.
5. **Deferred — TypeScript**, with a trigger that can now actually fire: a CI pipeline exists to enforce the type-check ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md)).
6. **Done — route-level lazy loading.** Every page except the home and product pages is lazy.
   - Baseline measured in Phase 15: about 381 KB of JavaScript in the first bundle (122 KB gzipped), with about 144 KB loading on demand.
   - Budgets will be enforced once a CI pipeline exists (**PLANNED**, Phase 21).

**Also delivered in Phase 15:**
- The store's name, currency, languages, footer content and announcement come from its configuration.
- Wishlist, reviews and coupons are hidden when their module is off.
- `frontend/src/whiteLabel.test.js` fails on any brand, currency or contact literal in the frontend source.

**Still open**, with the evidence for each item in [FrontendGuide.md](FrontendGuide.md) §17: the feature-folder move, the per-feature API modules, the query layer and types, an error boundary, component and route tests, admin list state in the URL, and a handful of store-specific literals that the white-label test does not catch.
