# Souq: frontend architecture

> **Status:** target adopted 2026-09-11. **Phase 15 delivered the runtime** ([ADR-0035](../11-ADR/0035-white-label-runtime.md)): the store configuration at boot, semantic theming, module gates, four areas and route-level code splitting. **Phase 16 (storefront rebuild)** took both of D-19's adoptions ([ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)): TanStack Query manages storefront server state, and types arrive as `checkJs` plus JSDoc on the `.js` boundaries rather than a TypeScript conversion. The feature-folder move is still outstanding and is the largest remaining item in §6.
> **Stack:** React 18 · Vite 8 · React Router 6 · TanStack Query · i18next · CSS Modules with design tokens · Stripe.js. Tests: Vitest (pure logic since Phase 1A, components and accessibility since Phase 16), ESLint and `tsc --noEmit` over JSDoc-typed `.js`.
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
| Customer account | store host, `/account`, `/account/addresses`, `/orders` | `AccountLayout` inside `CustomerLayout` — one navigation over profile, addresses, orders and the wishlist (Phase 16) | signed-in customer (`ProtectedRoute`) |
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

**Phase 16 (storefront rebuild):**

| Change | Reason |
|---|---|
| **Two routed pages existed only as zero-byte files** — `MyOrders.jsx` and `OrderTracking.jsx`, empty since Phase 9 with their CSS fully written. Both are implemented against the existing contracts | `/orders` and the public tracking link from shipping emails threw at runtime. An empty module builds without complaint |
| **Two more pages crashed on open** — `/offers` and `/wishlist` called `usePageMetadata({ title: t(...) })` a line above `const { t } = ...` | Const temporal dead zone. Found by enabling `no-use-before-define`, which is not in ESLint's recommended set |
| A **linter** (ESLint 9: react-hooks, jsx-a11y, language basics) and `tsc --noEmit` over JSDoc-typed `.js`, both in CI | The frontend had neither. The first thing the linter caught was a JSX parse error Vite had compiled silently |
| **TanStack Query** for storefront server state, with the cache reset on any identity change | [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md). Without the reset, signing out and in as another customer on one device paints the first customer's orders for the second |
| An **account shell** (`AccountLayout`) over profile, addresses, orders and wishlist; `/account/addresses` is new, `/orders` and `/orders/:id` unchanged | The account was three unlinked pages; the order URLs are in emails and notifications |
| A **cart page** at `/cart` beside the drawer | A URL for a basket you can keep, share and come back to |
| **Product URLs by slug** — `/products/:handle` takes either, ids redirect to the canonical form | `api.getProductBySlug` existed and was unused; the module doc recorded the gap |
| Breadcrumbs, a quantity stepper, and **schema.org structured data** with a real price, availability and (only when there are ratings) an aggregate rating | Discovery and a commerce-quality product page |
| **A real 404** and an error boundary that never prints an exception | A silent redirect home hid broken links from visitors and crawlers alike |
| **robots.txt**, tied by test to `PRIVATE_ROUTES` | The app renders in the browser, so a crawler that runs no JavaScript never sees a `noindex` meta tag |
| **Dialog behaviour**: Escape, focus in and back out, scroll lock, named backdrop buttons | Every drawer claimed `role="dialog" aria-modal` while offering none of it |
| **Six dead footer links removed** (FAQ, shipping, returns, contact, privacy, terms) | They pointed at `href="#"`. The platform has no content pages — recorded as TD-42 rather than faked |
| **White-label leaks closed** (TD-27): the invented 3–5 day delivery promise, one store's hero copy on every storefront, `ar-JO` dates, an assumed `JO` country, and the Stripe field's hard-coded font and colours | A white-label storefront that ships one store's identity to every tenant is not white-label |
| The confirmation page reads its order from the server via `?order=`, and its delivery estimate from the order | Refreshing after payment used to send the buyer back to the storefront as if nothing had happened |
| **Component, accessibility and source-invariant tests** — 110 → 249 | The empty files, the crashes and the cache leak were all found by writing tests, not by reading code |

**Store settings and team (Phase 17 screens):**

| Change | Reason |
|---|---|
| `/admin/settings` (`store.settings.manage`): identity per language, logo, favicon and sharing image, colours with live readability checks, typeface, theme preset and mode, opening reveal, languages and time zone, contact, social links, search text and the announcement bar. Currency is shown read-only | The last store-owned configuration that needed Swagger. Currency, domains and modules are the platform's ([WhiteLabel.md](WhiteLabel.md) §2) |
| The allowlists, limits and contrast thresholds come from `GET /api/admin/store/settings/options`; `frontend/src/features/admin/settings/settingsForm.js` (tested) checks before saving | Every settings rule reaches the client as one code (`InvalidTenantOperation`) with an Arabic message; an English-speaking merchant would read "not allowed" for any mistake. A copy of the lists in the frontend would drift |
| The form is built from a full read and sent whole; edits live in a draft over the saved values, so a newer read (an upload, a refocus) never overwrites what was typed | `PUT` replaces the settings document. A form that sent only what it showed would erase social links or SEO text |
| `StorePreview` applies `themeVariables` to its own frame, light and dark, with the store's own texts and no invented products | The preview shows the derivation the storefront will use, without re-theming the admin around an unsaved choice |
| `TenantProvider` exposes `refresh()`: re-reads the configuration without the boot screen | `retry()` replaced the whole app with a loading screen; saving a colour must not throw away the page |
| `/admin/staff` (`store.staff.manage`): server-paged team list, invite with the role explained, resend, enable, disable behind a confirmation. `frontend/src/features/admin/staff/staffView.js` (tested) | Self-disable and the last administrator stay server rules; the screen hides only the action the server is certain to refuse (disabling yourself) |
| `RowActionsMenu` takes a `label`; the trigger had no accessible name in every admin table | Found by axe in a browser |
| Status tokens are derived readable against their own soft surface; the active sidebar link uses `--color-on-panel` text | Both failed contrast on real pages (4.20:1 and 3.39:1). See [DesignSystem.md](DesignSystem.md) §2.2 |
| Browser journeys: `frontend/e2e/store-administration.spec.js`, and both screens added to `frontend/e2e/responsive.spec.js` | One sign-in per file: the auth limiter allows ten a minute, and a reload that aborts an in-flight refresh leaves the browser holding a rotated token |
**Platform store list and provisioning (Phase 18):**

| Change | Reason |
|---|---|
| `/platform/stores`, `/platform/stores/new`, `/platform/stores/:id/setup/:step`, `/platform/stores/:id`, `/platform/stores/:id/settings`; `PlatformLayout` became a shell with navigation and language and theme toggles, and the statistics moved to `PlatformOverview` | Phase 18's exit criterion: provision a client store end to end without the API |
| Creation is the only step that inserts a row; the store is born `Provisioning`, closed to visitors. Every later step saves on its own, the step is in the URL, and the list resumes at the first thing missing (`resumeStepFromSummary`) | An abandoned wizard leaves a closed store in the list, never a half-configured open one. No cross-screen transaction, no rollback that deletes |
| The settings editor moved to `frontend/src/components/settings/StoreSettingsEditor.jsx` and takes a `source` (read, options, save, upload). `/admin/settings` and the platform wrap it with their endpoints | One set of rules, drafts and preview for both paths the server accepts with the same contract. Its existing tests passed unchanged through the move |
| `StorePanels` — readiness, profile, domains, modules, administrator, lifecycle — shared by the wizard and the store page. `frontend/src/features/platform/provisioning.js` (tested) holds identity and host checks against server limits, readiness, resume and lifecycle actions | The wizard is ordering and guidance, not a second copy of the store page |
| Readiness reports and never blocks; activation warns inside its confirmation | The server does not require a domain or an administrator to activate, so the UI invents no such rule |
| `ConfirmDialog`, with typed confirmation for archiving; `Button` gains an outlined `danger` variant | Archiving is permanent; white on the danger colour is 3.9:1 |
| `FormField` wires its label, and its message, to native controls and `PasswordInput` | Sign-in, registration, password reset and invitation acceptance had unnamed inputs. Found by the browser journey on the invitation page |
| Accent buttons use `--color-on-accent`; dark mode derives muted, primary and accent text against the lighter card surface | Faint text on accent and on dark cards, in every store's dark mode. Found by axe run in dark mode |
| Journeys: `frontend/e2e/platform-provisioning.spec.js` (9 steps, needs `SOUQ_API_LOG` to follow the invitation), platform screens in `frontend/e2e/responsive.spec.js` | The administrator's first sign-in on the new store's host is the proof, and it needs two real hosts |

**Back-office completion: confirmations, platform accounts and the activity log (Phase 17–18 remaining scope):**

| Change | Reason |
|---|---|
| The seven store-admin `window.confirm` prompts (delete category, coupon and shipping method; archive product; remove product image; disconnect the Stripe account; disable a team member) use `ConfirmDialog` through `useConfirmAction`. The action runs after confirmation, the dialog stays open while it runs, and a refusal (category in use, coupon used on orders, last administrator) is read inside it | A native prompt carries neither the store's identity nor the server's answer; a toast after the dialog closed was easy to miss. Publishing, unpublishing and restoring still apply at once, as before |
| `useDialog` keeps a stack of open dialogs and reads `onClose` and `locked` from refs | Escape in the image-removal dialog closed the product drawer behind it too; a re-render with a new callback moved focus |
| `/platform/accounts` (`platform.users.manage`): invite a platform administrator or owner, resend, enable, disable behind a confirmation. The team screen's drawer and `frontend/src/features/admin/staff/staffView.js` take the role list | Only what the server offers: no role change for an accepted account, no deletion. Self-disable and the last active owner stay server rules |
| `/platform/audit` (`platform.audit.view`): store, activity, account and date filters in the URL, applied by a button; server paging; each entry's details in a drawer with its recorded metadata, IP address and request id; "all activity in this store / by this account". A store page links to its own activity. `frontend/src/features/platform/audit.js` (tested) | Reading the log writes an audit line, so filtering per keystroke would flood it. The screen shows ids and roles because that is what an entry records, and states what the log never contains (refused or failed requests, sign-ins, shoppers) so an absence is not read as evidence |
| API instants carry `Z` (`UtcDateTimeJsonConverter`) | Found building the viewer: timestamps read from the database were sent without a zone and shown three hours early to a reader in Amman, across every screen that shows a time |
| Journeys: `frontend/e2e/back-office.spec.js` — delete a category (Escape sends nothing, confirming deletes, a category with products is refused inside the dialog), remove a product image from a dialog over the drawer, invite a platform administrator who accepts and cannot reach accounts, disable them behind a confirmation and watch their session fall, find that in the log by activity, account, store and page, then Arabic and dark with axe. Both new platform pages added to the phone check in `frontend/e2e/responsive.spec.js` | Axe runs after animations settle: a drawer measured mid-slide reports a blended colour nobody sees |
| Storefront preview is **not built**: [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md) sets out the owner's decision (D-22) | A preview credential opens a closed store across hosts; who gets one and what it may do are not the engineer's to choose |

## 6. Migration plan: status

**Phase 16 (storefront rebuild) closed four of these and left one open.** Taken in order below; what changed in the storefront itself is in the Phase 16 entry of §5.

1. **Done — `frontend/src/app` introduced without moving features.** It holds `TenantProvider` (the boot from GET `/api/storefront/config`), `frontend/src/app/storeTheme.js` and `frontend/src/app/tenantModel.js` (theme, formatting and module logic, unit-tested), `frontend/src/app/BootScreens.jsx` (closed store, unknown store, retry), `frontend/src/app/StoreBrand.jsx` (the store's logo or name) and `frontend/src/app/PlatformLayout.jsx` (the platform shell).
   The guards stayed in `frontend/src/components/ProtectedRoute.jsx`, which gained `RequireModule` and `PlatformRoute`. The customer account pages still render inside the storefront layout behind `ProtectedRoute`; their own shell comes with the account rebuild (Phase 16).
2. **Still open, and now the largest remaining item — moving features.** Phase 16 rebuilt the storefront screens in place rather than moving them, and that was a deliberate order-of-work choice, not an oversight: the move is pure churn with no behaviour change, and the phase's remaining budget went to defects that only failed in a browser. The bundle argument for it was **measured and found weak**: the admin half of `frontend/src/api/client.js` is 780 bytes gzipped, 0.54% of a 141 KB first load, so splitting the client buys structure, not speed. Each screen still moves with `git mv` when it is rebuilt, so its history survives.
3. **Unblocked, not yet done — splitting `frontend/src/api/client.js`** into per-feature modules. The query layer has arrived and decided their shape ([ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)); the split now travels with item 2. The shared client already normalizes the error contract and refreshes the session silently (Phase 3), and its `ApiError` shape is declared in JSDoc since Phase 16.
4. **Done — theming.** Brand-named tokens were replaced by semantic tokens everywhere. The theme comes from the store, and the visitor theme switcher is gone.
5. **Answered in Phase 16 — types without a conversion** ([ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)). The trigger fired and the answer is `checkJs` with JSDoc at the boundaries, scoped to `.js` because the same check over `.jsx` produced 248 mostly-inferred errors against 52 real ones. `npm run typecheck` runs in CI. Components join when their props are declared.
6. **Done — route-level lazy loading.** Every page except the home and product pages is lazy.
   - Baseline measured in Phase 15: about 381 KB of JavaScript in the first bundle (122 KB gzipped), with about 144 KB loading on demand.
   - **Re-measured in Phase 16: 141.3 KB gzipped across 17 files** (453 KB raw), of which React and React-DOM are 86 KB — 61% of the payload. The rise over the Phase 15 baseline is the query layer (~9 KB) plus the storefront work itself. Making the product page lazy would save 5.4 KB, measured; it stays eager because it is the page search engines land on and a round-trip there costs more than it saves.
   - Budgets will be enforced once a CI pipeline exists (**PLANNED**, Phase 21).

**Also delivered in Phase 15:**
- The store's name, currency, languages, footer content and announcement come from its configuration.
- Wishlist, reviews and coupons are hidden when their module is off.
- `frontend/src/whiteLabel.test.js` fails on any brand, currency or contact literal in the frontend source.

**Still open**, with the evidence for each item in [FrontendGuide.md](FrontendGuide.md) §17: the feature-folder move and the per-feature API modules (item 2), admin list state in the URL, and the admin screens' own migration to the query layer. Closed in Phase 16: the query layer, types, the error boundary, component and route tests, and the store-specific literals the white-label test did not catch (TD-27).
