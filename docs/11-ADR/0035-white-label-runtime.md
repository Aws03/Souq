# ADR-0035: White-label runtime: the SPA boots from the host's store configuration, with semantic tokens, module gates, four areas with route-level splitting, and D-19 deferred

- **Status:** Accepted (implemented in Phase 15), 2026-09-11. Completes the frontend half of **D-12**. Defers **D-19** with a trigger.
- **Builds on:**
  - [ADR-0006](0006-tenant-resolution.md): the store comes from the host, resolved by the server.
  - [ADR-0022](0022-tenancy-enforcement.md): module flags enforced on the server (`404 ModuleDisabled`).
  - [ADR-0024](0024-platform-administration.md): store settings and branding, and the public storefront config endpoint with an ETag (Phase 4).
  - [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) and [FrontendArchitecture.md](../08-FRONTEND/FrontendArchitecture.md).
- **Date:** 2026-09-11
- **Related modules:** Platform (the public storefront configuration); Cross-cutting (the whole frontend)
- **Related ADRs:** completes the frontend half of [ADR-0011](0011-white-label-architecture.md); builds on [ADR-0006](0006-tenant-resolution.md), [ADR-0022](0022-tenancy-enforcement.md) and [ADR-0024](0024-platform-administration.md); the module flags it hides are enforced on the server by [ADR-0024](0024-platform-administration.md) and in use cases by [ADR-0033](0033-review-moderation-and-wishlist.md); money is formatted with the minor units of [ADR-0014](0014-money-precision.md)

## Context

Before Phase 15 the frontend belonged to one store:
- **The brand name** was hard-coded in the navbar, the sign-in screens, the admin sidebar, `index.html`, and the locale files (the footer text, contact details and announcement).
- **The currency was hard-coded:** `JOD` in the coupon screens and the empty basket, plus a three-decimal rule.
- **A visitor theme switcher** offered four palettes and stored the choice in `localStorage`, so a visitor could re-skin the store.
- **Design tokens had brand names.**
- **There was one bundle.** Visitors downloaded the admin, checkout and payment code.
- **The storefront config endpoint** (Phase 4) existed, but the frontend never called it.

## Problem

How can one build render any store's identity — its name, colours, fonts, currency, languages and optional modules — without a build or a fork per store? And what should the application show before the first paint, when the store named by the host is closed, unknown, or cannot be reached?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Where the store's identity comes from | **`GET /api/storefront/config` at boot**, resolved by the server from the host and cached with an ETag. Nothing renders until it answers. It supplies the name, logo, colours, fonts, languages, currency, modules, SEO text, contact details and announcement | A build per store: a fork by another name. Values injected into `index.html` at deploy time: the same problem |
| Boot failures | **An explicit screen for each case:** <br>• `503 StoreUnavailable` → the store is closed; <br>• `404 StoreNotFound` → no store at this address; <br>• any other 404 on the platform host → the platform area; <br>• a network error → a retry screen | Rendering a default brand: shows the wrong store |
| Colours | **Semantic tokens written onto `<html>`** from the store's branding: <br>• primary, with strong and on-primary variants; <br>• secondary; <br>• accent, with soft and on-accent variants; <br>• background, surface and surface-alt; <br>• text, muted text and border. <br>Derived colours are computed. Muted text keeps a contrast of at least 4.5:1, and text on the primary or accent colour is black or white, whichever reads better. Status colours (success, info, danger) are the same for every store | A full palette entered by each store: more fields, and easy to make unreadable. Custom CSS: ruled out in WhiteLabel §4 (XSS, upgrades) |
| Fonts | **The store's typography preset**, from the server's allow-list (`BrandPresets.Typography`), loaded from Google Fonts at runtime. The English UI stays in Inter | Arbitrary font URLs: a third-party request chosen by a store |
| Visitor theme switcher | **Removed.** The identity is the store's decision. The store's theme preset is exposed as `data-preset` on `<html>`, the hook for Phase 16's layout variations. The four old palettes aren't kept as visitor options; colour presets belong in the branding editor (Phases 17–18) | Keeping it: a visitor could replace the store's brand |
| Money | **`Intl.NumberFormat`** with the store currency's minor units (from Intl, per ISO 4217) and Latin digits. A price without an explicit currency uses the store's | A currency table in the frontend: duplicates ISO data, and the server's table is already authoritative |
| Languages | **The visitor's language is kept only if the store enables it.** A single-language store shows no language toggle | Always offering both languages |
| Optional modules | **`useModule` and `RequireModule`.** A disabled module's UI (wishlist, reviews, coupons) is hidden, its routes redirect, and no request is sent. The server still enforces the flag | Hiding the UI but still calling the API: requests that are bound to fail |
| Areas | **One build with four areas:** storefront, customer account, store admin, and platform. The boot answer picks the storefront or the platform routes. Routes are split with `React.lazy`, so only the home and product pages are in the first bundle | Separate apps: a duplicated shell and duplicated auth |
| Keeping brand and currency literals out | **Two tests.** <br>• A Vitest check reads every frontend source file and `index.html`. <br>• An architecture test reads the backend code and the committed `appsettings*` files. <br>Two files are exempt on purpose: the seeder (the demo store's data) and the ISO currency table | Code review only: a regression is one line away |
| The development default store | **`localhost` serves the seeder's store** (`DbSeeder.DefaultTenantSlug`) in Development and Testing, unless `Tenancy:LocalDefaultTenant` names another store | A slug in committed `appsettings.json`: product configuration naming a store |
| Moving features into `features/*` | **Deferred.** `app/` holds the new runtime: providers, boot screens, the store brand and the platform shell. Existing screens move with `git mv` in the phases that rebuild them (16–17) | Moving everything now: a large diff with no change in behaviour, and merge pain for the storefront rebuild |
| D-19: TypeScript and TanStack Query | **Deferred with a trigger** (below) | Adopting both now |

## Decision

1. **`TenantProvider`** fetches the store configuration before anything else renders. It sets the store currency and a supported language first, then exposes `{ mode, config, retry }` through `useTenant`, `useStoreConfig` and `useModule`.
2. **`applyStoreTheme`** is the runtime theme provider. It writes the tokens, the preset, the title, the meta description, the favicon and the font stylesheet onto the document, and re-applies them when the language changes. The logic is in `tenantModel.js`, a pure module that is unit-tested.
3. **Components read only semantic tokens.** The brand-named tokens are gone everywhere.
4. **Store-specific content comes from the configuration:**
   - the store name or logo;
   - the footer description, contact details and social links;
   - the announcement bar (none when the store has none);
   - the page title.
5. **Route guards stay UX only.** `RequirePermission`, `RequireModule` and `PlatformRoute` redirect, but the server remains the authority for the store, permissions and modules.
6. **The platform host gets a shell:** sign-in, invitation acceptance and a placeholder area for platform accounts. Its screens come in Phase 18.

## D-19: why TypeScript and TanStack Query are deferred

- **Both are major dependency and toolchain changes.** The project's rule is to propose such changes and get approval before adopting them.
- **Phase 15's exit criteria don't need them.** The same build renders two stores, and no literals remain.
- **The storefront and admin rebuilds (Phases 16–17) rewrite most data-fetching screens anyway.** Introducing the query layer there avoids rewriting the same screens twice.
- **Trigger:** the start of Phase 16, or adding a CI type-check, whichever comes first.
- **Recommended path when approved:**
  - TypeScript: a `tsconfig` with `allowJs`; new files in `.ts`/`.tsx`; the pure `model.js` files converted first.
  - TanStack Query: used first for the new catalog and basket screens. The existing hooks pattern stays until a screen is touched.

## Consequences

- **The same build renders any store.** A unit test renders two stores with different branding, currency, languages and modules.
- **Code splitting:** the first bundle is about 381 KB of JavaScript (122 KB gzipped). About 144 KB loads on demand: admin, checkout, account, authentication and platform. Enforced budgets need a CI pipeline, which doesn't exist yet.
- **Boot cost:** one small cacheable request (with an ETag) before the first paint. Until it answers, the page shows neutral colours and a loading state.
- **SEO:** the title and description are set at runtime. Heads that crawlers can see are injected per host in Phase 16.
- **Development setups need nothing new:** `localhost` still serves the demo store, and `{slug}.localhost` any other.

## Revisit when

- Phase 16 starts: the D-19 trigger, and the feature-folder moves for storefront screens.
- A store needs a font or colour outside the presets.
- SEO needs server-rendered heads.
