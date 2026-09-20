# ADR-0047: The commercial layer is a control plane inside the monolith, and every commercial table takes one of three enforced shapes

- **Status:** Accepted as the design, 2026-09-20, and **implemented in part by `C1`** on the same day: the *Billing* module, the three table shapes, `Plan`/`Subscription`/`EntitlementOverride`, and the entitlement seam of §4 all exist. What remains design-only is everything with money in it — invoices, ledger, commissions, payouts — and the quota counter of [ADR-0049](0049-tenant-quota-enforcement.md). How §4 was resolved in practice (the intersection rule, and closing the fail-open default) is [ADR-0053](0053-entitlement-resolution.md). Supersedes nothing.
- **Date:** 2026-09-20
- **Related modules:** Platform, Identity, Reporting, and a proposed fourteenth module (*Billing*)
- **Related ADRs:** [ADR-0005](0005-multi-tenancy-model.md) and [ADR-0022](0022-tenancy-enforcement.md) for the isolation this must not weaken; [ADR-0024](0024-platform-administration.md) for the platform area's existing conventions; [ADR-0002](0002-modular-monolith-structure.md) and [ADR-0004](0004-module-boundaries.md) for how a module is added; [ADR-0012](0012-service-extraction-strategy.md) for why this is not a service

## Context

Souq is a working white-label multi-tenant storefront with no commercial layer: no plan, no quota, no
subscription, no invoice, and no way for the platform to charge anyone. The audit in
[CommercialReadiness.md](../12-ROADMAP/CommercialReadiness.md) established that the hard half — isolation, host
resolution, one build per store, server-enforced feature gates — is built and built well, and that what is
missing is the part that turns it into a business.

The temptation at this point is a new service, a second database, or a per-customer branch. All three are named
non-goals with recorded reasons ([ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md)), and nothing in
a subscription model is the measured evidence that would reverse one.

What already exists and is directly relevant: two request scopes enforced at the door (`TenantScope.Tenant` and
`TenantScope.Platform`), a cached per-request store snapshot (`TenantInfo`) that already carries the enabled
module set, one reviewed cross-tenant read class (`PlatformQueries`), a sanctioned platform→store write path
(`ITenantScopeRunner`), and an architecture suite that decides where a Domain entity may live.

## Problem

1. Where do commercial concepts — plan, subscription, invoice, ledger entry, payout, usage counter — live, given
   that a tenant-owned read **throws** in platform scope and a platform-owned table has no tenant filter at all?
2. Do they belong to the `Platform` module, or to a new one?
3. What stops a new commercial table from becoming the first tenant-isolation hole?

## Options considered

| Option | Verdict |
|---|---|
| **A separate billing service** | Rejected. There is one team and one deployment, and splitting would replace one database transaction with sagas for no business gain. It is not a *named* non-goal — [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) lists fifteen and this is not among them — but it falls under §1 by argument, and would need its own ADR. |
| **A second (billing) database** | Rejected. A per-module database is a named non-goal, and cross-referencing a store's own orders for commission would become a distributed read. |
| **Everything on the `Tenant` aggregate** | Rejected. `Tenant` is read on effectively every request through a cached projection; a write-hot usage counter or an invoice history on that row is the wrong shape, and the settings document it already carries cannot be queried inside. |
| **A new `Souq.Domain.Platform.Billing` sub-namespace** | **Rejected on evidence.** `TenancyRuleTests` compares the namespace by **equality** to `Souq.Domain.Platform`, so a sub-namespace is *outside* it and every type in it would be required to implement `ITenantOwned`. Tidier, and it fails the build. |
| **A nullable-tenant ("store or platform") row** | Rejected. `ITenantOrPlatformOwned` is restricted by test to `Souq.Domain.Identity`, with the reason written in the test: an optional tenant on commercial data is a leak waiting to happen. |
| **A control plane in the platform scope of the same deployable, with three table shapes** | **Chosen.** |

## Decision

**The commercial layer is a control plane inside the existing monolith, in the platform scope that already
exists.** One deployable, one database, no broker, no new technology.

### 1. Three table shapes, and every commercial table picks one

`TenancyRuleTests` already enforces four rules over every concrete `Entity` subclass in `Souq.Domain`. Read
together they permit exactly three shapes:

| Shape | Namespace | Tenant filter | Read safely by |
|---|---|---|---|
| **A — store-owned** | anywhere but `Souq.Domain.Platform` | automatic; throws with no tenant | ordinary queries |
| **B — platform-owned, tenant-keyed** | `Souq.Domain.Platform` | **none** | an explicit `TenantId` predicate, every time |
| **C — platform-global** | `Souq.Domain.Platform` | none | ordinary queries |

The assignment: *Plan* is C. *Subscription*, *PlatformInvoice*, *CreditNote*, *LedgerEntry*, *Commission*,
*Payout* and the custom-domain challenge and certificate records are B. *BehaviouralEvent* and the quota
*UsageCounter* are **A** — the first because it is the merchant's own data read by the merchant's own
dashboards, the second because it must sit inside the tenant's transaction (see
[ADR-0049](0049-tenant-quota-enforcement.md)).

The line generalises: **anything the merchant owns is A; anything the platform owns *about* the merchant is B.**
That is also the answer to "who owns the data" when a merchant leaves.

### 2. Shape B gets the `PlatformQueries` discipline, enforced

Shape B has no query filter and no write-guard stamping, so isolation rests entirely on the predicate the caller
writes. `PlatformQueries` already carries the rule in its header — an explicit `TenantId` when the read concerns
one store, aggregates only when it spans stores, called only from audited platform use cases behind platform
permissions on the platform host — and an IL-scanning test permits it alone to bypass the filter.

**That test is extended to cover shape-B repositories.** This is the single change that makes the design safe,
and it is small.

**Done in `C1`, and not by extending that test.** The filter-bypass rule can never fire for shape B, because
shape B has no filter to bypass — so a separate rule was added
(`TenancyRuleTests.قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع`): it discovers tenant-keyed platform
entities by reflection, so a future table of this shape is guarded the day it is added, and it permits only
reviewed Infrastructure types to handle their rows. It found a pre-existing reader (`StoreOrigins`) that no
inventory had listed. The navigation hole this record's own design implies is real and is avoided deliberately:
no navigation runs from `Tenant` to any of these tables. Two known limits are recorded rather than glossed: the scan reads only the `Souq.Infrastructure`
assembly (so `Souq.API` is a gap, while `Souq.Application` is safe by construction because it has no EF
reference), and a nested type inherits its enclosing type's allowance.

### 3. A fourteenth module, registered properly

Commercial concepts do not belong to `Platform`, which owns a store's identity and presentation. They form a
*Billing* module, registered like every other: `ModuleMap.FeatureFolders`, a `ModuleMap.DomainOwners` entry for
**every** new Domain type, an entry in the allowed-contracts map that introduces no cycle, and a module
document — because four inventories are regenerated from reflection and compared byte for byte.

**One rule must be extended in the same change:** the "every platform-area request is audited" test names
`Features.Platform` and `Features.Reporting` **literally**. A new `Features.Billing` folder is not covered until
that list grows, and nothing will fail to remind you.

### 4. Entitlements feed the existing seam and do not create a second one

`TenantInfo.HasModule` is the single enforced answer to "may this store use X?", asked by
`TenantAvailabilityMiddleware` and re-asked inside the pricing pipeline. A plan **derives** the set resolved in
`TenantDirectory.Project`; it does not add a parallel check.

**The seam fails open and that must be fixed as part of adopting it.** The implementation is
`Modules is null || Modules.Contains(module)`, the database default is every module, and the reader silently
drops unknown keys. For three optional features that is a defensible convenience; for a paid entitlement it
grants the product away. A test must prove that an unresolvable plan grants nothing.

**Done in `C1`.** All three links are closed and the test exists; what this record did not decide — how the plan
and the existing per-store switch combine — is settled in [ADR-0053](0053-entitlement-resolution.md) as an
intersection in which every missing input resolves to the empty set.

### 5. A platform row and a tenant row cannot be written in one transaction

`ITenantScopeRunner` opens a **new DI scope and therefore a new `AppDbContext`**, which is the cause of TD-55
and which means "write the platform's commission ledger row atomically with the store's order" is not available
through that path. Commercial designs must either accrue through a domain event on the outbox (at-least-once,
idempotent on the order id) or keep the row shape A beside the order. This is recorded as a constraint, not
solved here.

## Consequences

**Good.** No new deployable, no new datastore, no new dependency. Isolation stays exactly as strong as it is
today, and the one genuinely new risk — an unfiltered platform table — is covered by extending a test that
already exists. Entitlements have one enforcement point rather than two. A fail-open default that would have
given away paid features is closed on the way in rather than discovered later.

**Costs.** Shape B must carry its predicate by hand, forever; the discipline is a test and a review convention,
not a mechanism. Adding a module is more mechanical work than it looks — three registrations, a document, and
four regenerated inventories. Commercial accrual is eventually consistent with the order that caused it, and
that must be visible in the design rather than assumed away.

**Revisit when** a single store's commercial workload distorts the shared database (the trigger ADR-0012 already
names), or a contractual requirement for physical separation arrives — in which case the hybrid path in
[MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) applies and this record does not need superseding.
