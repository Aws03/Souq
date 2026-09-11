# Souq: Frontend Architecture

> **Status:** Target adopted 2026-09-11. The restructuring happens in **Phase 15**; until then only minimal, targeted changes are made.
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
| **Server state** | `features/*/hooks.js` | Target: TanStack Query (caching, deduplication, retries; decision D-19, Phase 15). Until then, the existing hooks pattern. |
| **Client state** | contexts or local state | Auth session, tenant config, theme, toasts. The cart becomes *server* state in Phase 8. |
| **Authentication** | `contexts/AuthProvider` | Access token in memory, refresh via cookie (Phase 3). Route guards are UX only. |
| **Tenant context** | `contexts/TenantProvider` | Populated from the config endpoint, read-only. Components never send a tenant id to the API. |
| **Theme/branding** | `contexts/ThemeProvider` + `styles/tokens.css` | Semantic CSS variables set from tenant config ([WhiteLabel.md](WhiteLabel.md)) |
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

## 6. Phase 15 migration plan

1. Introduce `app/`, `routes/`, `layouts/`, and `contexts/` without moving features. The app keeps working.
2. Move one feature at a time with `git mv` (history preserved): catalog → cart → checkout → orders → auth → admin features → platform features.
3. Split `api/client.js` into per-feature `api.js` files over a shared `http.js`.
4. Replace brand-named tokens with semantic tokens; add ThemeProvider and TenantProvider.
5. Add TypeScript incrementally (`.ts` for new files and `model.js` → `model.ts`), if D-19 is approved.
6. Add route-level lazy loading and set bundle budgets.
