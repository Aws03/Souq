# Storefront preview — decision required (D-22)

> **Status:** not built, deliberately. **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`. Phase 18 asks for a *storefront preview using a preview token*. This page is the brief the owner needs to decide it. It is written against the code as it stands; the names of things that don't exist yet are in *italics*.
> **Why it stopped here:** [AGENTS.md §9](../../../AGENTS.md#9-when-to-stop-instead-of-guessing) — a security decision with real-world consequences. A preview token is a new credential that lets a request through the store-status gate, on a new public endpoint, carried from one host to another. Every one of those is a choice about who can see a closed store, and the repository records no rule for any of them.

## 1. What exists today

- **Status gating is one decision in one place.** `TenantAvailabilityMiddleware` answers `503 StoreUnavailable` for every store-host endpoint of a store that is not `Active`, unless the endpoint is marked `AvailableWhenStoreClosedAttribute`: the storefront configuration (so the closed page can be branded) and sign-in, refresh, sign-out and the current user (so a closed store's staff keep their session). During `Provisioning` only, the rest of the authentication controller (`AvailableDuringProvisioningAttribute`) is open too, so an administrator can accept an invitation and prepare the store. Administrative endpoints (`HasPermissionAttribute`) are open during `Provisioning` only. A preview has to open **visitor** endpoints — catalogue, categories, product pages — which today nothing opens.
- **Tokens are bound to their host.** `AccessTokenValidation` refuses a token on any host other than the one it was issued for, so the platform owner's session is refused on every store host (proven by `ProvisioningBoundaryTests` and `frontend/e2e/platform-provisioning.spec.js`). A preview therefore cannot reuse the owner's session. It needs its own credential that works on the store's host.
- **The platform already shows a preview inside its own page.** The settings editor renders the store's derived colours, type and dark mode in a frame (`frontend/src/components/settings/StoreSettingsEditor.jsx`). That is a *look* preview. It is not the real storefront with the real catalogue, and the roadmap is right that it is no substitute.
- **There is a precedent for a browser credential.** The guest basket cookie is `HttpOnly`, `Secure`, `SameSite=Strict`, scoped to one path, holds 256 random bits and is stored only as its SHA-256 ([ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md)). **Two kinds of credential do travel in a URL today, and the earlier claim that none did was wrong (corrected in M11):** the order-tracking token is a 128-bit random value in the *path* of a deliberately shareable, unauthenticated link, and every invitation, password-reset and email-verification link carries `?token=`. Both are single-purpose and one of them is meant to be pasted to a courier, so neither is a precedent for a *preview* grant — but the recommendation below has to stand on its own reasoning (a fragment is not sent to the server, and is not written to logs or `Referer`) rather than on a claim that URLs are never used. Nothing is ever put in browser storage.

## 2. The decisions

Each has a recommendation. None is implemented until the owner chooses.

| # | Question | Options | Recommendation |
|---|---|---|---|
| 1 | **Who may start a preview?** | (a) Platform accounts with `platform.tenants.manage` only · (b) also the store's own administrators (`store.settings.manage`) before opening · (c) anyone holding a link, so a client can approve their store before launch | **(a)** now. (b) is useful but lets a store administrator see a *suspended* store, which the platform may have closed for billing or abuse. (c) turns a preview into a shareable key to a closed store. |
| 2 | **Which states can be previewed?** | `Provisioning` only · `Provisioning` and `Suspended` · every non-`Active` state | **`Provisioning` and `Suspended`.** An archived store is terminal, and an active store needs no preview. |
| 3 | **What can a preview do?** | Read-only browsing · browsing plus a basket · anything a visitor can do | **Read-only.** Registration, sign-in, basket, checkout, reviews and wishlist stay refused. A closed store must not collect customers, orders or personal data through a preview. |
| 4 | **Lifetime and revocation** | A signed, short-lived token that can't be revoked · a server-stored grant that can be revoked (a new table and migration) | **A server-stored grant**: single-use exchange code (about 60 seconds), a session of 30–60 minutes, a "revoke previews" action on the store page, and each grant audited. A preview of a suspended store that can't be withdrawn is the wrong default. |
| 5 | **How the credential travels from the platform host to the store host** | (a) A one-time code in the URL **fragment**, exchanged on the store host for an `HttpOnly`, `Secure`, `SameSite=Strict` cookie scoped to that host · (b) a bearer token kept in `sessionStorage` and sent as a header · (c) a token in the query string | **(a).** A fragment never reaches a server, proxy or request log. The code works once, and the cookie is invisible to scripts. (b) exposes the credential to any script on the page. (c) writes it into access logs and browser history. |
| 6 | **Caches and search engines** | — | Every preview response carries `Cache-Control: private, no-store` and `X-Robots-Tag: noindex`, and the storefront shows a persistent "Preview — this store is closed" bar. This matters more once a CDN arrives (Phase 23): a cached preview page would open the closed store to everyone. |

## 3. What building it would involve

Once the owner decides, the work is contained but touches the security boundary. It needs an ADR, because it touches tenant isolation and authentication ([ADR index §1](../../11-ADR/README.md#1-how-to-use-this-index)).

- **Backend.**
  - A platform use case that mints a grant (audited like every platform request). On option 4, it also needs a *StorePreviewGrant* table and migration.
  - A store-host exchange endpoint: anonymous, rate-limited, and open while the store is closed.
  - An explicit allowlist attribute (*AvailableInPreview*) on the read-only visitor endpoints.
  - One change to `TenantAvailabilityMiddleware.IsOpen`: a closed store's endpoint is open only if it is on that list **and** the request carries a valid grant **for this store**.
  - **Three things M11 verified about that change, since "one change" understates it slightly.** `IsOpen` is a
    pure static (`internal static bool IsOpen(TenantStatus, Endpoint)`) with no access to the request, so
    reading a grant forces a signature change and an `async` call site — still cheap, because it has exactly
    one caller and no test caller, and the middleware already takes its dependencies by method injection.
    **The gate runs before authentication** (`Program.cs`: routing → availability → rate limiter → CORS →
    authentication), which means the grant must be readable without the auth pipeline — the recommended
    `HttpOnly` cookie satisfies that, but by luck rather than by analysis until now — and it also means a
    grant lookup would sit **ahead of the rate limiter** on every request to a closed store, which the
    rate-limiting requirement below does not cover. And the status gate fires **before** the module gate, so
    on a closed store a disabled-module endpoint answers `503`, not `404`; a preview allowlist inherits that
    order.
- **Frontend.**
  - A "Preview storefront" action on the platform store page, which opens the store host with the code in the fragment.
  - The store host reads the fragment once, removes it from the address bar, exchanges it, and shows the preview bar.
  - Nothing is kept in browser storage.
- **Tests that must exist before it ships.**
  - A grant for store A presented on store B's host is ignored.
  - An expired or replayed code is refused.
  - Every write endpoint still answers `503` in preview.
  - A preview grants no customer capability: registration, basket and checkout still answer `503`.
  - An `Active` store is unaffected.
  - The platform owner's token is still refused on store admin endpoints.
  - Minting is audited, and revocation takes effect on the next request.
  - A browser journey covers all of the above, including the fragment disappearing from history.

## 4. What does not depend on this decision

The rest of Phase 18's scope does not wait for it — except platform-wide settings, which wait on a separate owner decision (P-07, [OwnerDecisions.md](../../09-OPERATIONS/OwnerDecisions.md)):
- Platform accounts, the activity log and the styled confirmation dialogs are delivered.
- The in-frame settings preview stays as it is.
- The provisioning wizard's readiness checklist already tells the owner what a visitor will see while the store is closed.
