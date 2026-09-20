# Billing module

> **Code:** `src/Souq.Application/Features/Billing`, `src/Souq.Domain/Platform/Plan.cs`, `src/Souq.Domain/Platform/Subscription.cs`, `src/Souq.Domain/Platform/EntitlementOverride.cs`, `src/Souq.Domain/Platform/Entitlements.cs`, `src/Souq.Infrastructure/Persistence/Queries/BillingQueries.cs` · **Decisions:** [ADR-0047](../../11-ADR/0047-commercial-control-plane.md), [ADR-0053](../../11-ADR/0053-entitlement-resolution.md) · **Change guide:** none yet

## Purpose

Billing is the platform's **commercial control plane**: it answers *what may this store use, and on what terms*. It is the fourteenth module and the newest, and today it holds only the half of that question that needs no money — plans, subscriptions and entitlements. Invoices, dunning, commissions and payouts are later phases with owner decisions in front of them.

The module exists because commercial concepts do not belong to `Platform`, which owns a store's identity and presentation. But it deliberately owns **no enforcement**: `TenantInfo.HasModule` stays the single enforced answer to "may this store use X?", asked where it was always asked. Billing *feeds* that answer; it does not sit beside it. A second feature-flag system is the failure this module is shaped to avoid.

## Responsibilities

- The plan catalogue: versioned plans, each naming the entitlements it grants and the numeric limits it carries.
- Which plan version is in force for each store, and since when.
- Expiring, attributed, audited entitlement overrides, so support can make an exception without creating a second source of truth.
- Nothing else. In particular, no money.

## Not this module's job

| Not this module | Owner |
|---|---|
| Enforcing an entitlement | [Platform](../Platform/README.md) — `TenantInfo.HasModule`, read by `TenantAvailabilityMiddleware` and by `PricingService` |
| Resolving the effective set | Infrastructure — `TenantDirectory` composes the inputs once per cached snapshot |
| A store's identity, domains, status and settings | [Platform](../Platform/README.md) |
| Taking a shopper's money | [Payments](../Payments/README.md). The two money paths never meet — see **Tenant behaviour** |
| Counting a limit, or refusing the (N+1)th thing | Nobody yet. Limits are carried here and enforced in a later phase ([ADR-0049](../../11-ADR/0049-tenant-quota-enforcement.md)) |
| Charging a merchant | Nobody yet — a later phase, blocked on owner decisions |

## Business concepts

- **Entitlement** — a boolean capability key: *may they?* Its key space is deliberately the same as the optional store modules (`StoreModules.All`), because one catalogue and one enforcement point is the whole point.
- **Limit** — a name and a non-negative number: *how much?* A different concept from an entitlement and never conflated with one. The limit **names** are not validated against a catalogue, because deciding what the limits are is the owner's call, not engineering's.
- **Plan** — a versioned set of entitlements and limits, identified by `(Code, Version)`. A published plan is **frozen**: a subscriber keeps the terms they subscribed to, so a change is a new version, never an edit.
- **Plan status** — `Draft` (editable, not subscribable) → `Published` (subscribable, frozen) → `Retired` (no new subscriptions; existing subscribers keep it).
- **Subscription** — which plan version is in force for one store. One row per store.
- **Entitlement override** — a support exception that **expires**, is **attributed** to the account that granted it, and is **audited**. It grants; it never denies.
- **The foundation plan** — not a commercial tier. It carries what a store had before plans existed: the three optional modules that are free today, no price, no limits. Every pre-existing store was subscribed to it by the C1 migration, and a newly provisioned store is subscribed to it too. It exists so that introducing plans changed no store's behaviour.
- **Effective modules** — the one answer: what the plan grants (plus active overrides), intersected with what the platform has switched on for that store. Both inputs must say yes.

## Domain model

Every type lives in the `Souq.Domain.Platform` **namespace** — a tenancy rule compares that namespace by equality, so a tidier sub-namespace would fail the build — while ownership is recorded in `ModuleMap.DomainOwners`.

| Type | Kind | Path | Notes |
|---|---|---|---|
| `Plan` | entity (aggregate root) | `src/Souq.Domain/Platform/Plan.cs` | `Code`, `Version`, `Name`, `Status`; frozen after `Publish()` |
| `PlanEntitlement` | entity (child) | same file | One row per granted key; a related table, not a longer string column |
| `PlanLimit` | entity (child) | same file | Name and value; carried, not enforced |
| `PlanStatus` | enum | same file | `Draft`, `Published`, `Retired` |
| `Limit` | value object | `src/Souq.Domain/Platform/Limit.cs` | Name shape validated; name membership deliberately not |
| `Entitlements` | domain service (static) | `src/Souq.Domain/Platform/Entitlements.cs` | `Granted` and `Effective` — the resolution rule, testable without a database |
| `Subscription` | entity | `src/Souq.Domain/Platform/Subscription.cs` | `TenantId` as a plain column; one per store; only a published plan may be assigned |
| `SubscriptionStatus` | enum | same file | `Active`, `Cancelled` — Souq's own vocabulary, never a provider's |
| `EntitlementOverride` | entity | `src/Souq.Domain/Platform/EntitlementOverride.cs` | Bounded expiry, granting account, written reason, revocable |
| `IPlanRepository`, `ISubscriptionRepository`, `IEntitlementOverrideRepository` | ports | `src/Souq.Domain/Interfaces/IBillingRepositories.cs` | Write side |
| `InvalidPlanException`, `InvalidSubscriptionException`, `InvalidEntitlementOverrideException` | exceptions | `src/Souq.Domain/Exceptions/BillingExceptions.cs` | Stable codes, translated to 422 |

## Table shapes

Three of the shapes that [ADR-0047](../../11-ADR/0047-commercial-control-plane.md) enforces appear here, and the difference matters for how each is read:

| Table | Shape | Tenant filter | How it is read safely |
|---|---|---|---|
| `Plans`, `PlanEntitlements`, `PlanLimits` | C — platform-global | none needed | ordinary queries |
| `Subscriptions`, `EntitlementOverrides` | **B — platform-owned, tenant-keyed** | **none** | an explicit `TenantId` predicate, every time |

Shape B has no query filter and no write-guard stamping, so its isolation rests entirely on the predicate the caller writes. That is not left to review alone: `TenancyRuleTests.قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع` finds every Infrastructure type that handles rows of a tenant-keyed platform table and fails unless it is in a reviewed list with a written reason. The shape is discovered by reflection, so a future table of the same shape is guarded the day it is added.

## Use cases

Every request here carries a `TenantId` or targets a platform-global plan, every one is `IAuditable`, and every one is served only on the platform host. See **Security and permissions**.

| Use case | Kind | What it does |
|---|---|---|
| `ListPlansQuery` / `GetPlanQuery` | query | The catalogue, with a live subscriber count per version |
| `CreatePlanVersionCommand` | command | A new draft version of a plan code; version numbers are allocated, never supplied |
| `UpdatePlanDraftCommand` | command | Edits a draft. Refused for a published version by the aggregate |
| `PublishPlanCommand` / `RetirePlanCommand` | command | The two status transitions |
| `GetTenantEntitlementsQuery` | query | The one answer **with its inputs**: plan grants, active overrides, the platform switch, and the effective result |
| `AssignTenantPlanCommand` | command | Creates or moves the store's subscription. Only a published plan is accepted |
| `CancelTenantPlanCommand` | command | Ends what the contract grants. Does **not** close the store |
| `ListEntitlementOverridesQuery` | query | Overrides for one store, active and historical |
| `GrantEntitlementOverrideCommand` | command | A bounded, attributed, reasoned exception |
| `RevokeEntitlementOverrideCommand` | command | Ends one immediately |

## Public contracts

`IBillingQueries` is the module's read port, declared in Application and implemented by the non-public `BillingQueries` in Infrastructure. The module publishes no `Contracts` namespace: nothing else calls it, and its output reaches the rest of the system through the tenant snapshot rather than through a contract.

## Dependencies

Billing references `Souq.Domain.Platform` types and the shared `ITenantDirectory` and `ITenantRepository` ports. It references **no other module's** Application namespace, so it needs no entry in the allowed-contracts map and introduces no cycle.

The dependency that runs the other way is the important one: `Platform`'s `CreateTenantHandler` subscribes a new store to the foundation plan, and `TenantDirectory` reads this module's tables when it builds a tenant snapshot.

## Data ownership

`Plans`, `PlanEntitlements`, `PlanLimits`, `Subscriptions`, `EntitlementOverrides` — all created by the `CommercialControlPlane` migration, which also seeds the foundation plan and subscribes every store that existed before it. Rows are never deleted: an override is revoked, not removed, because *who granted what and why* is half of what an override is for.

## API

`/api/platform/plans` (the catalogue) and `/api/platform/tenants/{tenantId}/…` (entitlements, plan, overrides). Both are platform-host-only. The generated inventory in [Endpoints.md](../../05-API/Endpoints.md) is authoritative.

## Security and permissions

`platform.billing.manage`, granted to the **platform owner only** — not to a platform administrator. Assigning a plan or granting an exception is the commercial relationship with a customer, not day-to-day operation of their store; the same reasoning already separates `platform.users.manage` and `platform.settings.manage`.

Two rules that used to be assertions and are now tested:

- Requests in the platform area may carry a `TenantId`, and the exemption is **earned**: `ModuleAndContractRuleTests.كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده` proves every such request is reachable only through a `[PlatformEndpoint]` route. An endpoint marked `[AvailableOnAllHosts]` is rejected here specifically, because it is also served on a store host.
- Every platform-area request must be `IAuditable`. Both rules now read one shared list (`ModuleMap.PlatformAreaFolders`), so a future folder is covered by registering it once rather than by remembering two tests.

## Tenant behaviour

**The two money paths never meet.** Shopper → store stays in [Payments](../Payments/README.md): tenant-scoped, and every payment row requires an order. Platform → merchant, when it exists, will be separate records in a separate scope. This is not a preference — the existing payment path throws in platform scope and has no row shape for a charge without an order.

**Entitlement resolution and its staleness.** The effective set is composed once per tenant snapshot in `TenantDirectory` and cached for 60 seconds, the same window as store status. Every write here calls `ITenantDirectory.Invalidate()`, so an assignment, a cancellation, a grant or a revocation takes effect immediately on the serving instance and within a minute elsewhere. The one case with no write behind it is an override reaching its own expiry: nothing invalidates, so it can outlive its expiry by up to the cache window. That is recorded rather than hidden, and cross-instance invalidation is a later phase.

## Events and background work

None. No domain events, no outbox messages, no sweeps. An expiring override needs no job: expiry is evaluated when the snapshot is built.

## External integrations

None, deliberately. Nothing in this module talks to a payment provider, and nothing in it is shaped by one.

## Tests

| Level | File | What it proves |
|---|---|---|
| Domain | `tests/Souq.Domain.Tests/PlanAndEntitlementTests.cs` | A published plan is frozen; an unknown entitlement is refused in a plan; the effective set is an intersection and is empty when either input is missing; only a published plan may be subscribed to; an override expires, is attributed and is bounded |
| Application | `tests/Souq.Application.Tests/Billing/BillingHandlerTests.cs` | Assignment and revocation invalidate the directory; a second active override on one key is refused; an override for a switched-off module is refused rather than silently doing nothing |
| Architecture | `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | Only reviewed Infrastructure types handle tenant-keyed platform rows |
| Architecture | `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | Platform-area requests are audited, and their `TenantId` exemption is earned |
| Integration | `tests/Souq.IntegrationTests/CommercialControlPlaneTests.cs` | An unresolvable plan grants nothing end to end; plan changes take effect through the real middleware; another store's override answers 404 |

## Failure modes

| If this happens | What the system does |
|---|---|
| A store has no subscription | It has no optional modules at all. Fail-closed is the designed behaviour, not a bug |
| A plan names an entitlement the code no longer has | The key is dropped from the effective set and a warning is logged with the store id. The store keeps working |
| A published plan is edited | The aggregate throws; the API answers 422 `InvalidPlan` |
| A draft plan is assigned to a store | Refused with 422 `InvalidSubscription` |
| An override is granted for a module the platform has switched off | Refused with 409 `ModuleSwitchedOff`, because granting it would have done nothing while reporting success |
| Two overrides for one key | The second is refused with 409 `OverrideAlreadyActive`, so "when does this end?" keeps one answer |

## Common change scenarios

- **Add a new optional capability.** Add the key to `StoreModules`, then add it to a new plan **version**. No existing plan grants it, so no store receives it by accident — which is the property the whole design is built around.
- **Give one store an exception.** Grant an override with a reason and a duration. Do not edit its plan.
- **Change what a tier includes.** Create a new version and assign it. Never edit a published one.

## Known limitations

- **No money.** No price, no invoice, no collection, no commission. Deliberate: those need owner decisions on the merchant-of-record model and the invoicing currency.
- **Limits are carried, not enforced.** The plan can say "500 products" and nothing counts products. Enforcement needs a counter design that does not race, which is its own decision record.
- **One subscription row per store, unfiltered unique index.** A cancelled subscription keeps the store's only row, and its previous plan survives only as an audit entry. Billing periods will need a filtered index and a migration.
- **The platform console shows the answer but does not yet let the owner build a tier.** Plans are created through the API.
- **Cross-instance invalidation is not built.** With more than one API instance, a plan change reaches the others within the cache window.

## Future evolution

Metering and billable events, platform invoices with their own number series, a dunning state machine driven by Souq's own invoice state, and quota enforcement over the limits this module already carries. Each is a separate phase in [CommercialPlatformPlan.md](../../12-ROADMAP/CommercialPlatformPlan.md), and several wait on a decision that is the owner's rather than engineering's.
