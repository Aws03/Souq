# Platform module

> **Code:** `src/Souq.Domain/Platform`, `src/Souq.Domain/Auditing`, `src/Souq.Application/Features/Platform`, `src/Souq.Application/Features/Stores`, `src/Souq.Application/Common/Tenancy`, `src/Souq.Application/Common/Auditing`, `src/Souq.Infrastructure/Tenancy`, `src/Souq.API/Tenancy` · **Decisions:** [ADR-0005](../../11-ADR/0005-multi-tenancy-model.md), [ADR-0006](../../11-ADR/0006-tenant-resolution.md), [ADR-0011](../../11-ADR/0011-white-label-architecture.md), [ADR-0022](../../11-ADR/0022-tenancy-enforcement.md), [ADR-0024](../../11-ADR/0024-platform-administration.md) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Souq sells stores. The Platform module owns the store itself: who exists on the platform, on which hosts each store is served, what it looks and reads like, which optional features it has bought, and whether it is open for business. It is its own module because every other module needs an answer to "which store is this request for, and is it allowed to be here?" before it can do anything, and because those answers are commercial decisions (contract, billing, abuse) that belong to the platform owner, not to the store's own staff.

The module also hosts the **audit trail**: the append-only record of who did what in which store. It is a building block rather than a business capability, but its only reader today is the platform area, so it is documented here.

## Responsibilities

- The store (tenant) lifecycle: create, name, currency, locale, activate, suspend, archive.
- Hosts: adding, removing and choosing the primary domain; the platform-wide uniqueness of a host.
- Resolving a request's store from its `Host` header, and deciding whether the requested endpoint is available on that host in that store's state.
- The store settings document: per-language display name and announcement, branding (colours, typography and theme presets, uploaded logo/favicon/social image), contact details, social links, SEO, enabled languages.
- Optional module flags per store, and their server-side enforcement.
- The public storefront configuration one store's frontend boots from, with its cache and ETag.
- The platform administration area: the store list and detail, a store's administrative accounts, inviting a store's first administrator, platform accounts, and the audit log.
- Writing the audit trail for every auditable request, and keeping it append-only.

## Not this module's job

| Not here | Owner |
|---|---|
| Accounts, passwords, sessions, roles and permissions | [Identity](../Identity/README.md) — Platform only *asks* for an invitation or a status change through shared building blocks |
| Cross-store counts and dashboards | [Reporting](../Reporting/README.md) |
| Products, orders, customers, coupons, reviews, shipping | The respective modules; Platform never reads their tables except through the reviewed `PlatformQueries` |
| Payment gateway behaviour and the encrypted store keys | [Payments](../Payments/README.md) — although the store-facing use cases live in this module's `Features/Stores` folder (see Dependencies) |
| Emails and templates | [Notifications](../Notifications/README.md) |
| Reading the review-publishing policy when a review is created | [Reviews](../Reviews/README.md); Platform owns the flag, Reviews owns the decision |

## Business concepts

- **Store (tenant)** — one shop on the platform: name, slug, currency, default culture, time zone, status.
- **Slug** — the store's stable identifier (`marka`), used for subdomains and links; fixed after creation, and never one of the reserved system names.
- **Domain** — a host the store is served on. Unique across the whole platform; exactly one of a store's domains is **primary** (the host used to build invitation links and email links for work started elsewhere).
- **Verified domain** — a host whose ownership the platform confirmed manually. Informational today: verification is not required to serve traffic.
- **Store status** — Provisioning, Active, Suspended, Archived.
- **Settings document** — everything about how the store presents itself, validated as a whole and stored as one JSON document.
- **Branding preset** — a curated typography key and theme key; a new look is a product feature for every store, never a per-client fork.
- **Module flag** — an optional capability a store has (promotions, reviews, wishlist).
- **Platform area** — the platform owner's entry point, served only on platform hosts.
- **Audit entry** — one immutable line: when, which area, which action, which store, which actor, the target and safe metadata.
- **Tenant directory** — the cached host → store map every request consults.
- **Storefront configuration** — the public projection of a store's identity that the frontend boots from.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `Tenant` | aggregate root | `src/Souq.Domain/Platform/Tenant.cs` | Name 2–100 characters with no control characters (it reaches email subjects); slug 2–40 lowercase alphanumerics and dashes, not a reserved name, normalized once at construction; culture within `SupportedCultures`; IANA-shaped time zone; valid currency code, and currency changes only while the store has no commercial activity; guarded status transitions; the default culture is always among the enabled cultures; at most one primary domain and no duplicate host within the store; the primary domain cannot be removed while other domains exist; branding asset URLs must start with this store's upload prefix |
| `TenantDomain` | entity in the `Tenant` aggregate | `src/Souq.Domain/Platform/TenantDomain.cs` | One normalized spelling per host (lowercase, no trailing dot, ≤253 characters, DNS label shape); created only through `Tenant.AddDomain` (internal constructor); `MarkVerified` sets `VerifiedAt` once |
| `TenantStatus` | enum | `src/Souq.Domain/Platform/TenantStatus.cs` | — |
| `StoreSettings` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | Immutable; replaced wholesale by `Tenant`; rebuilt from storage without validation so that a later rule never breaks an existing store |
| `StoreBranding` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | Typography and theme must be keys from `BrandPresets`; uploaded asset paths survive a style change |
| `BrandColors` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | `#RRGGBB` shape; WCAG contrast: text on background ≥ 4.5:1, button text (white or the text colour, whichever reads better) on primary and on accent ≥ 4.5:1, primary on background ≥ 3:1 |
| `StoreContact` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | Email shape and length; phone shape; per-language address through `LocalizedText` |
| `SocialLink` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | Only the eight known networks; absolute `https` URL on that network's own domain; at most `StoreSettings.MaxSocialLinks`, one per network |
| `SeoSettings` | value object | `src/Souq.Domain/Platform/StoreSettings.cs` | Title ≤70, description ≤160 characters per language |
| `LocalizedText` | static helper | `src/Souq.Domain/Platform/StoreSettings.cs` | Only supported culture keys; trims; drops empty values; enforces the caller's maximum length |
| `BrandPresets` | static catalogue | `src/Souq.Domain/Platform/StoreSettings.cs` | The approved typography and theme keys |
| `BrandingAsset` | enum | `src/Souq.Domain/Platform/StoreSettings.cs` | — |
| `StoreModules` | static catalogue | `src/Souq.Domain/Platform/StoreModules.cs` | Known keys only on write (`Format`); tolerant on read (`Parse` drops unknown keys rather than failing a store) |
| `AuditEntry` | append-only record (not an `Entity`) | `src/Souq.Domain/Auditing/AuditEntry.cs` | Area must be one of `AuditAreas`; action must look like `tenant.created`; over-long values are clipped, never rejected — a truncated line beats a missing one |
| `TenantInfo` | Application snapshot record | `src/Souq.Application/Common/Tenancy/TenantInfo.cs` | `HasModule` treats a null module set (test or seed snapshots) as "everything enabled" |

**Aggregate boundaries.** `Tenant` owns its domains and its settings document. Nothing else in the system holds a reference to `TenantDomain`; business rows reference a store only by the `TenantId` foreign key from the shared kernel. `AuditEntry` is deliberately outside every aggregate and has **no** foreign key to `Tenants`: it is a historical witness, not a live relationship.

**Concurrency.** `Tenants` carries a `rowversion` (`HasRowVersion` in `src/Souq.Infrastructure/Persistence/Configurations/PersistenceConventions.cs`), because the platform owner and a store administrator can edit the same store at the same time. A lost race surfaces as `409 ConcurrencyConflict`.

**Lifecycle.**

```mermaid
stateDiagram-v2
    [*] --> Provisioning: new Tenant(...)
    Provisioning --> Active: Activate
    Active --> Suspended: Suspend
    Suspended --> Active: Activate
    Provisioning --> Archived: Archive
    Active --> Archived: Archive
    Suspended --> Archived: Archive
    Archived --> [*]
```

`Suspend` is legal only from `Active`. `Activate` is legal from anything except `Archived`, and is idempotent on an already-active store. `Archive` is terminal and refuses a second call. There is no delete: `TenantRepository.Remove` throws on purpose.

## Use cases

Platform-area requests (`Features/Platform`) all carry an explicit tenant id and are all audited. Store-side requests (`Features/Stores`) never carry one — the store is always the host's store.

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| List stores, with each store's primary domain and administrator state (`ActiveAdmins`, `PendingAdminInvitations`) | `ListTenantsQuery` | `ListTenantsHandler` | `platform.tenants.manage` | `GET /api/platform/tenants` |
| What provisioning accepts: identity limits, reserved slugs, modules, statuses, and the store settings editor's own options | `GetProvisioningOptionsQuery` | `GetProvisioningOptionsHandler` | `platform.tenants.manage` | `GET /api/platform/tenants/options` |
| Store detail | `GetTenantQuery` | `GetTenantHandler` | `platform.tenants.manage` | `GET /api/platform/tenants/{id}` |
| A store's administrative accounts | `ListTenantAccountsQuery` | `ListTenantAccountsHandler` | `platform.tenants.manage` | `GET /api/platform/tenants/{id}/accounts` |
| Create a store | `CreateTenantCommand` | `CreateTenantHandler` | `platform.tenants.manage` | `POST /api/platform/tenants` |
| Rename / change currency | `UpdateTenantCommand` | `UpdateTenantHandler` | `platform.tenants.manage` | `PUT /api/platform/tenants/{id}` |
| Activate / suspend / archive | `ChangeTenantStatusCommand` | `ChangeTenantStatusHandler` | `platform.tenants.manage` | `POST /api/platform/tenants/{id}/status` |
| Add / remove / set primary / verify a domain | `ChangeTenantDomainCommand` | `ChangeTenantDomainHandler` | `platform.tenants.manage` | `POST /api/platform/tenants/{id}/domains`, `DELETE /api/platform/tenants/{id}/domains/{host}`, `POST /api/platform/tenants/{id}/domains/{host}/primary`, `POST /api/platform/tenants/{id}/domains/{host}/verify` |
| Edit a store's settings from the platform | `UpdateTenantSettingsCommand` | `UpdateTenantSettingsHandler` | `platform.tenants.manage` | `PUT /api/platform/tenants/{id}/settings` |
| Set module flags | `SetTenantModulesCommand` | `SetTenantModulesHandler` | `platform.tenants.manage` | `PUT /api/platform/tenants/{id}/modules` |
| Upload a branding file from the platform | `UploadTenantBrandingCommand` | `UploadTenantBrandingHandler` | `platform.tenants.manage` | `POST /api/platform/tenants/{id}/branding/{asset}` |
| Invite a store's administrator | `InviteTenantAdminCommand` | `InviteTenantAdminHandler` | `platform.tenants.manage` | `POST /api/platform/tenants/{id}/admins` |
| Read / set / unlink a store's payment account | `GetTenantPaymentAccountQuery`, `UpdateTenantPaymentAccountCommand`, `RemoveTenantPaymentAccountCommand` | `GetTenantPaymentAccountHandler`, `UpdateTenantPaymentAccountHandler`, `RemoveTenantPaymentAccountHandler` | `platform.tenants.manage` | `GET`/`PUT`/`DELETE /api/platform/tenants/{id}/payments` |
| Read the audit log | `ListAuditEntriesQuery` | `ListAuditEntriesHandler` | `platform.audit.view` | `GET /api/platform/audit` |
| Read a store's own settings | `GetStoreSettingsQuery` | `GetStoreSettingsHandler` | `store.settings.manage` | `GET /api/admin/store/settings` |
| Edit a store's own settings | `UpdateStoreSettingsCommand` | `UpdateStoreSettingsHandler` | `store.settings.manage` | `PUT /api/admin/store/settings` |
| Read what the settings editor may offer (cultures, presets, social networks and domains, limits, contrast thresholds) | `GetStoreSettingsOptionsQuery` | `GetStoreSettingsOptionsHandler` | `store.settings.manage` | `GET /api/admin/store/settings/options` |
| Upload a branding file from the store | `UploadStoreBrandingCommand` | `UploadStoreBrandingHandler` | `store.settings.manage` | `POST /api/admin/store/branding/logo`, `/favicon`, `/social-image` |
| Read / change the review-publishing policy | `GetReviewSettingsQuery`, `UpdateReviewSettingsCommand` | `GetReviewSettingsHandler`, `UpdateReviewSettingsHandler` | `reviews.moderate` to read; **both** `reviews.moderate` and `store.settings.manage` to change | `GET`/`PUT /api/admin/reviews/settings` |
| Read / set / unlink the store's own payment account | `GetStorePaymentAccountQuery`, `UpdateStorePaymentAccountCommand`, `RemoveStorePaymentAccountCommand` | `GetStorePaymentAccountHandler`, `UpdateStorePaymentAccountHandler`, `RemoveStorePaymentAccountHandler` | `store.payments.manage` | `GET`/`PUT`/`DELETE /api/admin/store/payments` |
| The public storefront configuration | `GetStorefrontConfigQuery` | `GetStorefrontConfigHandler` | anonymous | `GET /api/storefront/config` |

Platform account management (`ListPlatformUsersQuery`, `InvitePlatformUserCommand`, `SetPlatformUserStatusCommand`) also lives in `Features/Platform`, but the rules it applies are Identity's; see [Identity](../Identity/README.md).

## Public contracts

| Contract | Path | Who calls it |
|---|---|---|
| `ITenantDirectory` | `src/Souq.Application/Common/Tenancy/ITenantDirectory.cs` | `TenantResolutionMiddleware`; `Features/Platform` handlers; `OutboxProcessor` and `StoreSweepService` (iterate stores); `ProcessPaymentWebhookCommand` (route a webhook to the store that took the payment) |
| `ITenantContext` (+ `RequireTenant`) | `src/Souq.Application/Common/Tenancy/ITenantContext.cs` | Every module: currency, culture, module flags, and the tenant scope |
| `ITenantScopeRunner` | `src/Souq.Application/Common/Tenancy/ITenantScopeRunner.cs` | The platform area when it must *write* inside one store; `ProcessPaymentWebhookCommand` |
| `IStoreConfiguration` | `src/Souq.Application/Features/Stores/StoreSettingsUseCases.cs` | `GetStorefrontConfigHandler` |
| `IPlatformQueries` | `src/Souq.Application/Features/Platform/PlatformModels.cs` | The platform area's read use cases; implemented by `PlatformQueries`, the only reviewed query-filter bypass |
| `IAuditable`, `AuditRecord`, `IAuditTrail`, `IClientInfo` | `src/Souq.Application/Common/Auditing/Auditing.cs` | Every auditable request, and `AuditBehavior` |

`Souq.Domain.Platform` types themselves (`StoreModules` keys, `TenantStatus`) are part of the shared kernel in practice: other modules reference the constants, not this module's use cases.

**Documentation gap:** [Modules.md](../Modules.md) lists a planned *ITenantModules* contract. It does not exist and is not needed — the enabled modules travel inside the cached `TenantInfo` and are asked through `TenantInfo.HasModule`.

## Dependencies

- **Uses:**
  - `IFileStorage` (branding uploads) and `MediaFileInspector` (content sniffing) from the shared kernel.
  - `Common/Accounts` (`AccountInvitations`, `AccountStatusChanger`, `IAccountQueries`) — Identity's rules, deliberately shared building blocks rather than a cross-module call.
  - **Boundary leak:** `src/Souq.Application/Features/Stores/StorePaymentAccounts.cs` owns the store-payment-account use cases but works on **Payments'** domain: `StorePaymentAccount`, `IStorePaymentAccountRepository`, `PaymentKeyRules` and `InvalidPaymentOperationException` (all in `Souq.Domain.Entities`). No architecture test catches this, because the Domain layer is not covered by the module rules and `Features/Stores` belongs to this module.
  - In Infrastructure, `PlatformQueries` reads `Users`, `Customers`, `Products` and `Orders` with `IgnoreQueryFilters`. That is the deliberate, reviewed exception ([ADR-0024](../../11-ADR/0024-platform-administration.md)).
- **Used by:**
  - Everything, through `ITenantContext` (currency, culture) and the request-time resolution.
  - Shopping: `PricingService` refuses a coupon when `StoreModules.Promotions` is off.
  - **Boundary leak:** Reviews' `CreateReviewHandler` loads the whole `Tenant` aggregate through `ITenantRepository` just to read `ReviewsAutoApprove`.
  - **Boundary leak:** Notifications' `NotificationEmails` loads the `Tenant` aggregate through `ITenantRepository` for the email's store name, logo and colours; `StoreOrigins` (Infrastructure) reads `Tenants`/`TenantDomains` directly for a store's primary-domain origin.
- **Enforced vs convention.**
  - Enforced by `tests/Souq.ArchitectureTests/TenancyRuleTests.cs`: platform entities must **not** be tenant-owned; every tenant-owned entity has the named filter and an FK to `Tenants`; `IgnoreQueryFilters` only inside `PlatformQueries`; no raw SQL outside migrations; `ExecuteUpdate`/`ExecuteDelete` only in the reviewed call sites, since they bypass `SaveChanges` and so the write guard; no feature takes the writable `TenantContext`.
  - Enforced by `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`: only requests under `Features.Platform` may carry a `TenantId`; every request under `Features.Platform` and `Features.Reporting` must implement `IAuditable`; no feature folder references another module's namespace outside the allowed contracts.
  - Convention only: auditing store-side commands (`Features/Stores`, `Features/Staff`) — nothing fails the build if a new store command forgets `IAuditable`; keeping other modules off the `Tenant` aggregate; keeping the payment-account editor in `Features/Stores`.

## Data ownership

| Table | Configuration | Tenant-owned? | Notes |
|---|---|---|---|
| `Tenants` | `src/Souq.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` | No — it *defines* the tenant | Unique index on `Slug`; the settings document in the `Settings` JSON column (converted by `StoreSettingsJson`); module keys in the `EnabledModules` column, defaulting to every module so an upgrade never removes a feature; `ReviewsAutoApprove`; `rowversion` |
| `TenantDomains` | `TenantDomainConfiguration` in the same file | No | **Unique index on `Host` across the whole platform** — the last guard against stealing another store's host; cascade delete from `Tenants` |
| `AuditEntries` | `src/Souq.Infrastructure/Persistence/Configurations/AuditEntryConfiguration.cs` | No, and deliberately no FK to `Tenants` | Append-only, enforced by `TenantWriteGuardInterceptor`; indexes on `OccurredAt`, `(TenantId, OccurredAt)`, `(ActorUserId, OccurredAt)` |
| `StorePaymentAccounts` | `StorePaymentAccountConfiguration` | Yes | Owned by Payments; written from this module's `Features/Stores` use cases |

Other modules' data is read only through `PlatformQueries`: a store's administrative accounts (`Users` with an explicit `TenantId` predicate), and "does this store have any commercial activity?" (`Products` or `Orders`), which is what locks a store's currency.

Migrations: `Phase2MultiTenancy` creates `Tenants` and `TenantDomains` and the default store (fixed id 1, named "Marka Demo", slug `marka`); `Phase4PlatformAdministration` adds the settings document, the module column and `AuditEntries`; `Phase13ReviewsWishlist` adds `ReviewsAutoApprove`.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| GET | `/api/platform/tenants` | `platform.tenants.manage`, platform host | — | List stores |
| GET | `/api/platform/tenants/options` | `platform.tenants.manage` | — | Provisioning options (same settings options object as `/api/admin/store/settings/options`) |
| GET | `/api/platform/tenants/{id}` | `platform.tenants.manage` | — | Store detail |
| POST | `/api/platform/tenants` | `platform.tenants.manage` | — | Create a store |
| PUT | `/api/platform/tenants/{id}` | `platform.tenants.manage` | — | Rename / currency |
| POST | `/api/platform/tenants/{id}/status` | `platform.tenants.manage` | — | Activate / suspend / archive |
| POST | `/api/platform/tenants/{id}/domains` | `platform.tenants.manage` | — | Add a domain |
| DELETE | `/api/platform/tenants/{id}/domains/{host}` | `platform.tenants.manage` | — | Remove a domain |
| POST | `/api/platform/tenants/{id}/domains/{host}/primary` | `platform.tenants.manage` | — | Set the primary domain |
| POST | `/api/platform/tenants/{id}/domains/{host}/verify` | `platform.tenants.manage` | — | Mark a domain verified (manual) |
| PUT | `/api/platform/tenants/{id}/settings` | `platform.tenants.manage` | — | Settings |
| PUT | `/api/platform/tenants/{id}/modules` | `platform.tenants.manage` | — | Module flags |
| POST | `/api/platform/tenants/{id}/branding/{asset}` | `platform.tenants.manage` | — | Branding upload |
| GET | `/api/platform/tenants/{id}/accounts` | `platform.tenants.manage` | — | The store's administrative accounts |
| POST | `/api/platform/tenants/{id}/admins` | `platform.tenants.manage` | — | Invite the store's administrator |
| GET/PUT/DELETE | `/api/platform/tenants/{id}/payments` | `platform.tenants.manage` | — | The store's payment account |
| GET | `/api/platform/audit` | `platform.audit.view` | — | Audit log |
| GET/PUT | `/api/admin/store/settings` | `store.settings.manage` | — | The store's own settings |
| GET | `/api/admin/store/settings/options` | `store.settings.manage` | — | The editor's allowlists and limits, read from the Domain. `StoreSettingsOptionsTests` passes every offered option back through the Domain rules |
| POST | `/api/admin/store/branding/logo`, `/favicon`, `/social-image` | `store.settings.manage` | — | Branding uploads (≤2 MB, sniffed content) |
| GET/PUT/DELETE | `/api/admin/store/payments` | `store.payments.manage` | — | The store's payment account |
| GET | `/api/admin/reviews/settings` | `reviews.moderate` | `reviews` | Review-publishing policy |
| PUT | `/api/admin/reviews/settings` | `reviews.moderate` **and** `store.settings.manage` | `reviews` | Change the policy |
| GET | `/api/storefront/config` | anonymous (a reviewed public endpoint) | — | Storefront configuration |

Controllers: `src/Souq.API/Controllers/PlatformControllers.cs`, `src/Souq.API/Controllers/StoreAdministrationControllers.cs`, `src/Souq.API/Controllers/ReviewsController.cs`.

## Security and permissions

- `platform.tenants.manage` (PlatformOwner, PlatformAdmin) runs stores. `platform.users.manage` and `platform.settings.manage` are the owner's alone. `platform.audit.view` and `platform.reports.view` are granted to both platform roles. `platform.settings.manage` is currently **used by no endpoint**, and no platform-wide setting exists (open decision P-07).
- Every controller in the platform area carries `[PlatformEndpoint]`, so it 404s on a store host, and a store token (which carries `tid`) is rejected with 401 on a platform host. `AuthorizationBoundaryTests` proves both, and also that no platform endpoint is anonymous.
- Store-side settings endpoints carry no store id anywhere in the route: the store is the host's store, so editing another store's settings is impossible by construction rather than by a check that could be forgotten.
- Branding files are never URLs from the client: the server generates the path under `/uploads/tenants/{id}/branding/`, the file type comes from sniffed content ([ADR-0016](../../11-ADR/0016-upload-validation.md)), and `Tenant.SetBrandingAsset` refuses any path outside that store's prefix.
- Colour validation is a security-adjacent rule in the sense that it protects readability, not access: an unreadable palette is rejected and nothing is saved.
- Store payment secrets are encrypted with `AesGcmSecretProtector` bound to the store (`SecretPurposes`), never returned in any response, and never written to the audit trail — only "which fields changed" is recorded.

## Tenant behaviour

**Resolution** (`src/Souq.API/Tenancy/TenantResolutionMiddleware.cs`, runs before static files and authentication, only for `/api` and `/uploads`):

1. The host is lowercased and stripped of a trailing dot.
2. A host in `Tenancy:PlatformHosts` puts the request in platform scope (no store).
3. Otherwise, in Development and Testing **only**: an `X-Tenant` header selects a store by slug.
4. The `TenantDomains` map, through `ITenantDirectory`.
5. Otherwise, in Development and Testing **only**: `localhost`/`127.0.0.1`/`::1` resolves to `Tenancy:LocalDefaultTenant` (defaulted in `Program.cs` to `DbSeeder.DefaultTenantSlug`), then `{slug}.localhost`.
6. Otherwise `404 StoreNotFound`. There is no fallback store in production, and the development conveniences are switched on from the environment in `Program.cs`, so configuration cannot enable them.

A request for `/uploads/tenants/{id}/…` on another store's host gets a bare 404.

**Availability** (`src/Souq.API/Tenancy/TenantAvailability.cs`, after routing, so it can see the endpoint's metadata):

| Situation | Result |
|---|---|
| Platform endpoint on a store host, or a store endpoint on a platform host (and not `[AvailableOnAllHosts]`) | `404 NotFound` — the existence of the other area is not revealed |
| Store `Provisioning` and the endpoint is not `[AvailableDuringProvisioning]`, not `AvailableWhenStoreClosedAttribute`, and not behind a `[HasPermission]` policy | `503 StoreUnavailable` |
| Store `Suspended` or `Archived` and the endpoint is not `AvailableWhenStoreClosedAttribute` | `503 StoreUnavailable` |
| The endpoint declares `[RequiresModule(...)]` and the store does not have it | `404 ModuleDisabled` |

Because administration endpoints are recognised by their permission policy, a store's staff can work while it is still being provisioned — which is exactly how a store is prepared before it opens.

**Caches.** `TenantDirectoryCache` (a dedicated `MemoryCache`, 10,000 entries, 60 s for hits, 15 s for misses) holds host, slug, id and storefront-config entries. `ITenantDirectory.Invalidate` bumps a generation counter, dropping every entry at once **on the calling instance only**; other instances follow within 60 s. Every platform command that changes a store calls it.

**Writes.** Platform commands that must write *inside* a store (inviting its administrator, saving its payment keys, storing its branding file) go through `ITenantScopeRunner`, which opens a new DI scope whose `TenantContext` is that store. The write guard and the storage prefix then apply exactly as on a request made on that store's own host — the platform never bypasses them.

## Events and background work

- No domain events are raised by this module.
- `AccountInvited` is enqueued in the outbox inside the target store's scope, so the dispatcher later processes it in that same store's scope. See [Notifications](../Notifications/README.md).
- `AuditBehavior` (`src/Souq.Application/Common/Behaviors/AuditBehavior.cs`) runs after validation and before the handler. It stages the entry into the current unit of work, so the handler's first `SaveChanges` commits the change and its audit line atomically. A failed `Result` or an exception discards the line; a request that saved nothing (a query) has its line flushed after success.
- `StoreSweepService` (Infrastructure) uses `ITenantDirectory.ListActiveAsync` to run other modules' periodic work once per active store.

## External integrations

- File storage through `IFileStorage` (`LocalFileStorage` today) for branding assets, always under the store's prefix.
- AES-256-GCM secret protection (`AesGcmSecretProtector`, key material from `Secrets:Keys` / `Secrets:ActiveKeyId`) for the store payment keys edited through this module's `Features/Stores`.
- No other external system. DNS and TLS for custom domains are **PLANNED** (roadmap Phase 23).

## Tests

| Level | Class | What it covers |
|---|---|---|
| Domain | `tests/Souq.Domain.Tests/TenantTests.cs` | Creation defaults, slug and currency and locale validation, domain rules, guarded status transitions, currency lock |
| Domain | `tests/Souq.Domain.Tests/TenantSettingsTests.cs` | Default settings, the default culture always enabled, preset lists, branding-asset prefix, module replacement, social-link limits, control characters in the name |
| Domain | `tests/Souq.Domain.Tests/StoreSettingsTests.cs` | WCAG contrast maths and rejections, social-link allowlist, contact normalization, SEO limits, module parsing |
| Domain | `tests/Souq.Domain.Tests/InvitationAndAuditTests.cs` | Audit action shape, unknown area, clipping |
| Application | `tests/Souq.Application.Tests/Platform/TenantAdministrationTests.cs` | Unknown store id, directory invalidation, currency lock through the aggregate, invitation without a domain, invitation inside the store's scope |
| Application | `tests/Souq.Application.Tests/Stores/ReviewSettingsHandlersTests.cs` | Policy read/write on the context store, and its audit record |
| Application | `tests/Souq.Application.Tests/Stores/StorePaymentAccountEditorTests.cs` | Key encryption bound to the store, key-mode policy, malformed keys, unlink |
| Application | `tests/Souq.Application.Tests/Common/AuditBehaviorTests.cs` | Actor, store and area on the staged line; discard on failure and on exception; untouched non-auditable requests |
| Application | `tests/Souq.Application.Tests/Common/TenantContextTests.cs` | Set once, and `RequireTenant` throwing in platform scope |
| Integration | `tests/Souq.IntegrationTests/PlatformAdministrationTests.cs` | The full provisioning scenario, the Marka look and ETag/304, module enforcement in the endpoint and in checkout, domain theft, currency lock, platform accounts, append-only audit |
| Integration | `tests/Souq.IntegrationTests/StoreAdministrationTests.cs` | Store-side settings edit and its audit line, unreadable palette rejected, branding formats and host-scoped serving, staff vs settings permissions |
| Integration | `tests/Souq.IntegrationTests/TenantResolutionTests.cs`, `TenantResolutionMiddlewareTests.cs` | Unknown host, suspended and provisioning stores, platform host, development conveniences, upload host binding, log scope |
| Integration | `tests/Souq.IntegrationTests/TenantIsolationTests.cs` | Cross-store reads, writes, uniqueness, tokens, files |
| Architecture | `tests/Souq.ArchitectureTests/TenancyRuleTests.cs`, `ModuleAndContractRuleTests.cs` | The rules listed under Dependencies |
| Frontend | `frontend/src/app/tenantModel.test.js`, `frontend/src/whiteLabel.test.js` | Deriving theme tokens, currency and modules from the config, and mapping the boot response to a screen |

## Failure modes

| Situation | Error code | HTTP | Where it comes from |
|---|---|---|---|
| Host maps to no store | `StoreNotFound` | 404 | `TenantResolutionMiddleware` |
| Wrong area for the host | `NotFound` | 404 | `TenantAvailabilityMiddleware` |
| Store not open for this endpoint | `StoreUnavailable` | 503 | `TenantAvailabilityMiddleware` |
| Endpoint of a disabled module | `ModuleDisabled` | 404 | `TenantAvailabilityMiddleware` |
| Coupon used while `promotions` is off | `ModuleDisabled` | 422 | `PricingService` through checkout |
| Unknown store id in a platform command | `NotFound` | 404 | `PlatformTenants.NotFound` |
| Slug already used | `TenantSlugTaken` | 409 | `CreateTenantHandler` |
| Host already mapped to a store | `DomainTaken` | 409 | `ChangeTenantDomainHandler` |
| Race past either check | `DuplicateValue` | 409 | Unique index → `UniqueConstraintViolationException` |
| Invalid name, slug, culture, time zone, currency, colours, preset, social link, module key, illegal status transition, currency change after activity, branding path outside the store | `InvalidTenantOperation` | 422 | `InvalidTenantOperationException` |
| Inviting an administrator to a store with no domain | `TenantHasNoDomain` | 422 | `InviteTenantAdminHandler` |
| Inviting an administrator to an archived store | `InvalidTenantOperation` | 422 | `InviteTenantAdminHandler` (a `Result` failure, not the aggregate) |
| Branding file too large / wrong type / missing | `FileTooLarge`, `UnsupportedMediaType`, `FileRequired` | 400 | `BrandingFiles.InspectAsync`, the controller |
| Concurrent edits of one store | `ConcurrencyConflict` | 409 | `rowversion` |
| Shape validation | `ValidationFailed` | 400 | `ValidationBehavior` |
| Store data touched with no store in scope (a bug) | `ServerError` | 500 | `TenantContextMissingException` |
| Write aimed at another store (a bug) | `ServerError` | 500 + a Critical log | `CrossTenantWriteException` |
| Update or delete of an audit row (a bug) | `ServerError` | 500 + a Critical log | `TenantWriteGuardInterceptor` |
| Store payment keys without server-side encryption configured | `SecretsNotConfigured` | 503 | `StorePaymentAccountEditor` |
| Test-mode keys where policy forbids them | `TestKeysNotAllowed` | 422 | `StorePaymentAccountEditor` |

## Common change scenarios

Details in [ChangeGuide.md](ChangeGuide.md): adding a store setting; adding a module flag; adding a platform endpoint; changing store-status rules; custom domains and DNS verification; changing what the storefront config exposes; adding an audited action; changing the tenant-resolution rules.

## Known limitations

- **A closed store serves only its configuration.** `AvailableWhenStoreClosedAttribute` is applied to `GET /api/storefront/config`, so a `Provisioning`, `Suspended` or `Archived` store still returns its identity and status (the SPA draws a branded "unavailable" screen) while every other endpoint answers `503 StoreUnavailable` — proven by `PlatformAdministrationTests` and by `frontend/e2e/platform-provisioning.spec.js`, which reads an archived store's config.
- **A store can be activated with no domain and no administrator**, and the only domain of an active store can be removed. Nothing in the aggregate or the handlers prevents an unreachable-but-active store. The platform screens do not invent that rule either: readiness is reported, and activation warns inside its confirmation.
- **`VerifiedAt` is decorative.** Resolution never checks it, so a domain serves traffic the moment it is added. DNS/TLS verification is **PLANNED** (Phase 23).
- **The directory and storefront caches are per-process.** With more than one API instance, a suspension or a branding change takes up to 60 s to reach the others, and each instance computes its own ETag.
- **The settings document cannot be queried inside.** "Find every store whose contact email is X" means scanning JSON.
- **Audit gaps across scopes.** When a platform command writes inside a store (`InviteTenantAdminCommand`, `UpdateTenantPaymentAccountCommand`, `RemoveTenantPaymentAccountCommand`), the work commits in the store's `DbContext` while the audit line commits in the platform's — a crash in between leaves a change without its line. [ADR-0024](../../11-ADR/0024-platform-administration.md) records this for "two platform commands" and suggests the outbox (Phase 14) could close it; Phase 14 shipped and the gap is still open, and the payment commands were added to it in Phase 11.
- **The audit log has no retention or purge**, no export, and no store-facing viewer (an audit viewer for store admins is **PLANNED**, Phase 17). The platform viewer (`/platform/audit`) reads it with the server's filters and paging and says on screen what the log does not contain.
- **An audit line names ids, not people or stores.** `AuditEntryDto` carries the actor's id and role and the store's id; the viewer shows them as such rather than fetching names, because every platform read is itself an audit line and a store's accounts are outside the platform's scope.
- **Store queries are not audited** — only platform-area requests and store-side commands are. Sign-ins, sign-outs and other Identity events are not audited at all.
- **`Archive` is terminal but empty.** No data export, anonymization or deletion happens; the store simply stops serving.
- **No plans, quotas or subscriptions.** Module flags are the only commercial lever, and they are a fixed set of three keys.
- **Readiness reads one page of accounts.** A store's page decides "has an administrator" from the first 100 administrative accounts, newest first; the list uses the server's exact counts.

## Future evolution

- **PLANNED (Phase 17 follow-up):** a store-facing audit view.
- **DECISION REQUIRED (Phase 18, remaining):** storefront preview with a preview token — who may preview, which states, what a preview may do, lifetime and revocation, and how the credential crosses hosts. The brief is [StorefrontPreview.md](StorefrontPreview.md) (D-22).
- **DECISION REQUIRED:** platform-wide settings (`platform.settings.manage`) — no setting is defined (P-07).
- The store list, provisioning wizard, store page, platform accounts (`/platform/accounts`) and the activity log (`/platform/audit`) are delivered — see [FrontendArchitecture.md](../../08-FRONTEND/FrontendArchitecture.md) §5.
- **PLANNED (Phase 23):** automated TLS and DNS verification for custom domains; per-store email-domain authentication.
- **DEFERRED:** plans and subscriptions ("after launch" in the roadmap's Phase 4 notes).
- **FUTURE:** an audited "support mode" that lets the platform act inside a store — described in [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md) and [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) as Phase 18, but absent from the roadmap's Phase 18 scope.
- **FUTURE:** distributed cache invalidation (or shorter lifetimes) when the API runs as several replicas; promoting individual settings fields to columns if they ever need to be queried; a dedicated database per store through the resolver seam described in [MultiTenancy.md](../../02-ARCHITECTURE/MultiTenancy.md).
