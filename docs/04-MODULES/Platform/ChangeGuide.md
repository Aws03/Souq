# Platform: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change:**

1. The store is decided by the server from the `Host` header. No request body, query string or client header outside Development/Testing may influence it, and only requests under `Features.Platform` may carry a `TenantId` at all (`ModuleAndContractRuleTests`).
2. Every request in the platform area is audited (`ModuleAndContractRuleTests`), and audit rows are append-only (`TenantWriteGuardInterceptor`).
3. Cross-store reads happen only in `PlatformQueries`, with an explicit `TenantId` predicate or an aggregate count (`TenancyRuleTests`).
4. Platform *writes* inside a store go through `ITenantScopeRunner`, never by bypassing the write guard.
5. Business rules live in the `Tenant` aggregate. Handlers coordinate; they do not validate.
6. Every change to a store invalidates the tenant directory (`ITenantDirectory.Invalidate`), or stale hosts, statuses, modules, entitlements and branding are served for up to 60 seconds.
7. Settings are validated on write and rebuilt from storage **without** validation, so tightening a rule never breaks a store that saved its settings earlier.

**Files to read first:** `src/Souq.Domain/Platform/Tenant.cs`, `src/Souq.Domain/Platform/StoreSettings.cs`, `src/Souq.Application/Features/Platform/TenantAdministration.cs`, `src/Souq.Application/Features/Stores/StoreSettingsModels.cs`, `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs`, `src/Souq.API/Tenancy/TenantAvailability.cs`, `src/Souq.Infrastructure/Tenancy/TenantDirectory.cs`.

**Tests that guard the module:** `tests/Souq.Domain.Tests/TenantTests.cs`, `tests/Souq.Domain.Tests/TenantSettingsTests.cs`, `tests/Souq.Domain.Tests/StoreSettingsTests.cs`, `tests/Souq.Application.Tests/Platform/TenantAdministrationTests.cs`, `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs`, `tests/Souq.IntegrationTests/StoreAdministrationTests.cs`, `tests/Souq.IntegrationTests/TenantResolutionTests.cs`, `tests/Souq.ArchitectureTests/TenancyRuleTests.cs`, `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.

---

## I need to add a store setting

- **Inspect:** `src/Souq.Domain/Platform/StoreSettings.cs` (the value objects and `StoreSettings.With`), `Tenant.UpdateStorefront` / `Tenant.UpdateBranding`, `src/Souq.Infrastructure/Persistence/Configurations/StoreSettingsJson.cs`, `src/Souq.Application/Features/Stores/StoreSettingsModels.cs`.
- **Rules to respect:** the new field is validated in the Domain, not in the DTO; per-language text goes through `LocalizedText.Normalize` so only supported cultures and bounded lengths are stored; a URL or file path never comes from the client — the server generates it; the settings object stays immutable (add a parameter to `With`, never a setter).
- **Steps:**
  1. Add the property to the right value object and validate it in its factory (`StoreContact.Create`, `SeoSettings.Create`, `BrandColors.Create`) or in `Tenant.UpdateStorefront`.
  2. Extend `StoreSettings.With` and the `internal` constructor.
  3. Add the field to the storage document in `StoreSettingsJson` (both `Serialize` and `Deserialize`), giving it a **default for missing values** so older documents still load.
  4. Add it to `StoreSettingsInput` and to `StoreSettingsDto`, and map it in `StoreSettingsMapper.ToDto`. Decide deliberately whether it belongs in `StorefrontConfigDto` (it is public if it does).
  5. Apply it in `StoreSettingsEditor.Apply`, which both edit paths (platform and store) share.
  6. Add shape-only rules to `StoreSettingsInputValidator` if the value can be structurally absent; leave meaning to the Domain.
- **Tests:** a Domain test in `TenantSettingsTests` or `StoreSettingsTests` for the rule and its rejection; extend the settings body in `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` (`SettingsBody`) if the field is required; assert it in the storefront config if you exposed it.
- **API:** `StoreSettingsInput` is a request contract — adding an optional field is backwards compatible, making one required is not. A field added to `StorefrontConfigDto` changes the ETag of every store's config.
- **Database:** no migration. The JSON column absorbs it. Old rows simply lack the key.
- **Security:** never accept a client-supplied URL or HTML; check the length bound; decide whether the value may appear in the public config.
- **Docs and ADR:** update [README.md](README.md) and [WhiteLabel.md](../../08-FRONTEND/WhiteLabel.md). No ADR unless the field changes who controls what (platform vs store).

## I need to add a module flag

- **Inspect:** `src/Souq.Domain/Platform/StoreModules.cs`, `src/Souq.Domain/Platform/Entitlements.cs`, `src/Souq.Infrastructure/Tenancy/TenantDirectory.cs`, `src/Souq.API/Tenancy/TenantAvailability.cs`, `src/Souq.Application/Common/Tenancy/TenantInfo.cs`, and an existing gated controller such as `src/Souq.API/Controllers/WishlistController.cs`.
- **Rules to respect:** module flags are **server-enforced**, not a UI toggle; the endpoint check and the use-case check are both needed whenever the feature can be reached indirectly (a coupon at checkout is the precedent); `StoreModules.Parse` must stay tolerant of unknown keys (tolerant, but reporting them through `Unknown` so the drift is logged rather than silent); and the key space is shared with [Billing](../Billing/README.md)'s entitlements on purpose — `Entitlements.All` **is** `StoreModules.All`, so a new key is a new entitlement and must never acquire a second catalogue.
- **Steps:**
  1. Add the constant to `StoreModules` and to `StoreModules.All`.
  2. Put `RequiresModuleAttribute` with your new key on the controllers that belong to it, the way `StoreModules.Reviews` is used today.
  3. If another module can reach the feature indirectly, check `TenantInfo.HasModule` in that use case and fail with a `ModuleDisabled` business error, as `PricingService` does for coupons.
  4. Decide the default for existing stores, remembering that **two** inputs must say yes and neither grants the key by itself. `Tenants.EnabledModules` has a database default of the empty string since C1, and adding a key to `All` does not grant it to existing rows either, because their stored string was written earlier: if the switch should be on by default, write a migration that appends the key. No existing plan grants it either, because a plan names what it grants — so a key that should be part of what stores already pay for needs a **new plan version** granting it, assigned deliberately; a published plan is frozen and must never be edited ([Billing](../Billing/README.md)).
- **Tests:** extend `StoreSettingsTests` (module parsing) and `TenantSettingsTests` (replacement and unknown keys); add an integration case to `PlatformAdministrationTests` showing the endpoint answering `404 ModuleDisabled`. If the key is meant to reach existing stores, prove it end to end in `CommercialControlPlaneTests` — the switch alone will not do it.
- **API:** `PUT /api/platform/tenants/{id}/modules` replaces the whole list — a client that sends a stale list silently removes the new flag. `GET /api/storefront/config` exposes the **effective** list (the intersection, from the request's tenant snapshot), not the column, so a key granted by only one input never reaches the frontend.
- **Database:** a data migration only if existing stores must get the new key.
- **Security:** a disabled module answers 404, not 403 — the feature's existence is not revealed.
- **Docs and ADR:** [README.md](README.md), [Modules.md](../Modules.md), [Billing/README.md](../Billing/README.md), and [ADR-0024](../../11-ADR/0024-platform-administration.md) if the enforcement model changes — or [ADR-0047](../../11-ADR/0047-commercial-control-plane.md) and [ADR-0053](../../11-ADR/0053-entitlement-resolution.md) if the *resolution* model does.

## I need to add a platform endpoint

- **Inspect:** `src/Souq.API/Controllers/PlatformControllers.cs`, `src/Souq.Application/Features/Platform/PlatformModels.cs`, `src/Souq.Application/Common/Auditing/Auditing.cs`.
- **Rules to respect — the audit rule is enforced by a test:** every request type under `Features.Platform` **must** implement `IAuditable`, or `ModuleAndContractRuleTests` fails the build. Queries included: reading a store's data from the platform is itself an event worth recording.
  - The action name must match `tenant.created`-style lowercase dotted words (`AuditEntry` rejects anything else at runtime).
  - Pass the affected store as `AuditRecord.TenantId` — on a platform host there is no ambient store to fall back to.
  - Put only safe, chosen fields in `Metadata`. Never the request body: it may hold keys or passwords.
  - For a create, the target is the natural key (slug, email); the id does not exist yet when the line is staged.
- **Steps:**
  1. Add the request, its validator and its handler under `src/Souq.Application/Features/Platform`, implementing `IAuditable`.
  2. Read through `IPlatformQueries` if you need data across stores; add the method to `PlatformQueries` with an explicit `TenantId` predicate. Do not add a new `IgnoreQueryFilters` caller — `TenancyRuleTests` has an allowlist of exactly one type.
  3. Write inside a store only through `ITenantScopeRunner`.
  4. Add the action to the controller with `[PlatformEndpoint]` and a `platform.*` permission (a class-level `[HasPermission]` counts).
  5. Call `ITenantDirectory.Invalidate()` if the change affects a host, status, modules or settings.
- **Tests:** `AuthorizationBoundaryTests` picks the endpoint up automatically (404 on store hosts, 401 for anonymous and for store tokens); add a scenario to `PlatformAdministrationTests`, and assert the audit action there.
- **API:** platform routes live under `/api/platform`; they exist only on platform hosts, so they never appear in a storefront's surface.
- **Database:** only if you add state.
- **Security:** platform permissions are never granted to store roles (`RolePermissions`, unit-tested). Anything that ends up inside a store must run in that store's scope.
- **Docs and ADR:** [README.md](README.md) and [Endpoints.md](../../05-API/Endpoints.md). An ADR if you introduce a new kind of cross-store access.

## I need to change the store status rules

- **Inspect:** `Tenant.Activate`, `Tenant.Suspend`, `Tenant.Archive`, `src/Souq.Domain/Platform/TenantStatus.cs`, `TenantAvailabilityMiddleware.IsOpen`, `ChangeTenantStatusHandler`.
- **Rules to respect:** transitions are guarded in the aggregate, and availability is decided in exactly one middleware — never with a status check inside a controller or handler; `Archived` is terminal; the change only takes effect elsewhere after `Invalidate`. Since C3, `Suspended` and `Archived` are **separate branches** of `IsOpen` (owner decision `C-17` = B) — collapsing them again reopens an archived store's admin.
- **Steps:**
  1. Change the transition guard in `Tenant` (and `TenantLifecycleAction` plus its audit action name if you add a transition).
  2. Change `TenantAvailabilityMiddleware.IsOpen` if the new state serves a different set of endpoints, and mark the endpoints that must stay open with the corresponding availability attribute.
  3. Consider what the frontend should show, and keep it matching the server: `bootOutcome` maps `503 StoreUnavailable` and `404 StoreNotFound` to boot screens, `bootModeForConfig` decides whether the app **mounts at all** for that status, and `storefrontIsOpen` decides whether the shopping pages render or the branded notice does (all in `frontend/src/app/tenantModel.js`, with the notice in `frontend/src/app/StoreClosed.jsx`). A status the server still serves must mount, or the browser silently denies what the API allows — which is what kept a suspended merchant out of their own admin until C3.
- **Tests:** `TenantTests` for the transition guard; `TenantResolutionTests` for what each state serves; `PlatformAdministrationTests` for the end-to-end effect.
- **API:** the status strings appear in `TenantSummaryDto`, `TenantDetailDto` and `StorefrontConfigDto`; adding a value changes a public contract.
- **Database:** `TenantStatus` is stored as an `int`; never renumber existing values — add new ones at the end.
- **Security:** a closed store must not leak data. Check that any endpoint you open while closed exposes only public, branded information.
- **Docs and ADR:** [README.md](README.md), [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md) (§3 currently disagrees with the code here — fix it in the same change), and [ADR-0022](../../11-ADR/0022-tenancy-enforcement.md) §6 if the gating model changes.

## I need to open an endpoint while a store is closed

**Done for the storefront config (R-08, Phase 12) and for order tracking (C3).** This section used to describe the config as unbuilt and named two tests that asserted the opposite of today's behaviour; both statements were stale and are corrected here.

- **Inspect:** `AvailableWhenStoreClosedAttribute`, `AvailableWhenStoreSuspendedAttribute` and `TenantAvailabilityMiddleware.IsOpen` in `src/Souq.API/Tenancy/TenantAvailability.cs`.
- **Rules to respect:** anything readable by a closed store must be public and branded — no secrets, no administrative email, no internal ids — and a closed store must still not serve catalogue, basket or order-creation endpoints. Choose the **narrower** attribute: `AvailableWhenStoreSuspended` for something a temporarily-suspended store should still answer, `AvailableWhenStoreClosed` only when an **archived** store should answer it too, which is a much stronger claim.
- **Steps:** mark the single action, not the controller; then make the frontend render the page it now can, and check `storefrontIsOpen`/`StorefrontGate` do not hide it (order tracking sits outside the gate for exactly this reason).
- **Tests:** `TenantResolutionTests` covers what each status serves, endpoint by endpoint; `PlatformAdministrationTests` covers the end-to-end effect. Update them deliberately — they are written to go red.
- **API:** a previously-503 endpoint starts answering 200 for closed stores. `StorefrontConfigDto.Status` is what the frontend branches on.
- **Database:** none.
- **Docs and ADR:** [README.md](README.md), [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md), [ADR-0022](../../11-ADR/0022-tenancy-enforcement.md) §6.

## I need to support custom domains properly (DNS verification)

Today: a platform administrator adds a host and it serves traffic immediately; `POST /api/platform/tenants/{id}/domains/{host}/verify` only stamps `VerifiedAt`. Automatic DNS/TLS verification is **PLANNED** (roadmap Phase 23).

- **Inspect:** `Tenant.AddDomain`, `Tenant.VerifyDomain`, `TenantDomain`, `ChangeTenantDomainHandler`, `TenantDirectory.FindByHostAsync`.
- **Rules to respect:** a host is unique platform-wide, and the unique index is the last guard; the host must stay normalized to one spelling; resolution must remain a cached single lookup — no DNS call on the request path.
- **Steps (when the phase arrives):**
  1. Decide what "verified" gates: serving traffic at all, or only issuing TLS. Making resolution require `VerifiedAt` is a breaking change for stores whose domains were added before.
  2. Put the DNS lookup behind a port in Application implemented in Infrastructure (an out-of-process call belongs behind an interface, like every other external system).
  3. Run verification as background work per store, not in the request, and keep the manual override for support.
  4. Store the challenge value (a TXT record token) on `TenantDomain`; that needs a migration.
- **Tests:** Domain tests for the challenge lifecycle; an integration test that an unverified domain behaves as decided.
- **API:** likely a new field on `TenantDomainDto` and a new verification response.
- **Database:** a migration adding the challenge columns; backfill existing domains as verified so nothing goes dark.
- **Security:** without verification, whoever points DNS at the platform first wins a hostname; that is precisely why hosts are unique and why adding one is a platform-only, audited action.
- **Docs and ADR:** an ADR — this changes an operational contract ([ADR-0006](../../11-ADR/0006-tenant-resolution.md) consequences, [ADR-0024](../../11-ADR/0024-platform-administration.md)'s "manual flag" decision).

## I need to change what the storefront config exposes or how it is cached

- **Inspect:** `StoreSettingsMapper.ToStorefront`, `StorefrontConfigDto`, `StoreConfiguration` and `TenantDirectoryCache` in `src/Souq.Infrastructure/Tenancy/TenantDirectory.cs`, `StorefrontController`.
- **Rules to respect:** the endpoint is anonymous — everything in it is public; the ETag is a hash of the serialized body, so any new field changes it; the cache key must stay per store and must be dropped by the same `Invalidate` every write path already calls.
- **Steps:** map the field, confirm the JSON options used for hashing are the ones MVC serializes with, then check the frontend consumers (`TenantProvider`, `storeTheme.js`, `tenantModel.js`).
- **Tests:** `PlatformAdministrationTests` asserts the config body and the 304 revalidation; `StoreAdministrationTests` asserts a settings edit is visible immediately; `frontend/src/app/tenantModel.test.js` covers the frontend mapping.
- **API:** public contract change; the frontend must tolerate the field being absent (older API, cached response).
- **Database:** none.
- **Security:** never add an administrative email, an internal id, a key hint, or anything a competitor could not already see.
- **Docs and ADR:** [README.md](README.md), [WhiteLabel.md](../../08-FRONTEND/WhiteLabel.md).

## I need to audit a new action (or stop auditing one)

- **Inspect:** `src/Souq.Application/Common/Auditing/Auditing.cs`, `src/Souq.Application/Common/Behaviors/AuditBehavior.cs`, `src/Souq.Infrastructure/Auditing/AuditTrail.cs`.
- **Rules to respect:** the request describes its own audit record; the behaviour adds actor, role, area, store, client IP and correlation id; a failed result or an exception discards the line, so the log never claims something that did not happen; the action name must match the required pattern; rows can never be updated or deleted.
- **Steps:** implement `IAuditable` on the request and return an `AuditRecord`. For a store-side command the store comes from the context automatically; for a platform command pass `TenantId` explicitly. Nothing else is wired — the behaviour is registered for every request in `AddApplication`.
- **Tests:** `AuditBehaviorTests` for staging and discarding; assert the new action in the integration test that exercises the command (`PlatformAdministrationTests` or `StoreAdministrationTests` read `/api/platform/audit`).
- **API:** `GET /api/platform/audit?action=` filters by prefix, so keep the `area.subject.verb` shape to preserve prefix filtering.
- **Database:** none; `AuditEntries` is generic.
- **Security:** metadata is chosen field by field — never serialize the whole request. Secrets and tokens must not appear even indirectly (`StorePaymentAudit` records only *which* fields changed).
- **Docs and ADR:** [README.md](README.md); [ADR-0024](../../11-ADR/0024-platform-administration.md) if you change when the row is written.

## I need to change tenant resolution (hosts, development conveniences)

- **Inspect:** `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs`, `src/Souq.API/Tenancy/TenancyOptions.cs`, the `TenancyOptions` `PostConfigure` block in `src/Souq.API/Program.cs`.
- **Rules to respect:** development conveniences (`X-Tenant`, `localhost` → default store, `{slug}.localhost`, the `admin.localhost` platform host) are computed from the environment and **override configuration** — a production deployment must not be able to switch them on; resolution happens before authentication and static files; nothing but the host may select a store in production; an unknown host is a 404, never a fallback store.
- **Steps:** change the resolution order in one place (`ResolveAsync`), keep the platform-host check first, and keep every lookup going through `ITenantDirectory` so it stays cached and bounded.
- **Tests:** `TenantResolutionMiddlewareTests` runs the middleware in both a Production-like and a Development-like configuration and is the right home for a new rule; `TenantResolutionTests` covers the real pipeline.
- **API:** none directly, but every route's reachability depends on it.
- **Database:** none.
- **Security:** the `Host` header is attacker-controlled. Any new lookup key must be bounded (the cache has a size limit for exactly this reason) and must not let a client choose a store it is not being served on.
- **Docs and ADR:** [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md) §3, [ADR-0006](../../11-ADR/0006-tenant-resolution.md), [ADR-0022](../../11-ADR/0022-tenancy-enforcement.md) §5, and [Configuration.md](../../09-OPERATIONS/Configuration.md) for the `Tenancy:*` keys.
