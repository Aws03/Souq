# ADR-0053: An entitlement is the intersection of what the contract grants and what the platform has switched on, and every missing input resolves to nothing

- **Status:** Accepted and **implemented** in C1, 2026-09-20. It decides how a plan reaches the enforcement point that already exists, and closes the fail-open default that point carried. Refines [ADR-0047](0047-commercial-control-plane.md) §4, which decided *that* entitlements feed one seam without deciding *how*.
- **Date:** 2026-09-20
- **Related modules:** Billing, Platform
- **Related ADRs:** [ADR-0047](0047-commercial-control-plane.md) (the control plane and the three table shapes), [ADR-0005](0005-multi-tenancy-model.md) and [ADR-0022](0022-tenancy-enforcement.md) (the isolation this must not weaken), [ADR-0024](0024-platform-administration.md) (the platform area's conventions), [ADR-0049](0049-tenant-quota-enforcement.md) (limits, which this record carries but does not enforce — C2 enforces them), [ADR-0054](0054-limit-semantics-and-catalogue.md) (why an absent **limit** is uncapped while an absent **entitlement** here is a refusal)

## Context

Before C1, "may this store use X?" had exactly one enforced answer — `TenantInfo.HasModule`, read by `TenantAvailabilityMiddleware` and again inside the pricing pipeline. The set behind it came from one column, `Tenants.EnabledModules`, which the platform operator edits.

That seam is good and C1 kept it. What C1 had to decide was what happens when a *plan* also has an opinion, and what happens when nobody does.

The second question turned out to be the sharper one. Three independent links in the chain all failed **open**:

1. `HasModule` was `Modules is null || Modules.Contains(module)`, with the parameter defaulting to `null` — a snapshot built without modules granted **everything**.
2. The database default for `EnabledModules` was the literal `promotions,reviews,wishlist` — a row inserted without naming the column granted everything.
3. `StoreModules.Parse` dropped unrecognised keys silently — fail-closed in direction, but invisible, so data drifting from the product was undetectable.

For three free optional features that was a defensible convenience. For a paid entitlement it gives the product away, and it does so without an error, a log line or a red test.

## Problem

1. How does a plan feed the existing seam without becoming a second authorization system?
2. What is the answer when a store has a plan, and separately when it has none?
3. Where is the composition computed, given that the answer is read on effectively every request and is cached?
4. A support exception must be possible without a second source of truth — what shape makes that safe?

## Options considered

| Option | Verdict |
|---|---|
| **The plan replaces the column: effective = plan grants** | Rejected. It deletes the operator's per-store off-switch, which is a real operational need ("this merchant does not want reviews"). Recreating it would mean a per-store plan, i.e. the tenant fork this architecture exists to avoid. |
| **Union: effective = plan grants ∪ enabled column** | Rejected, and it is the dangerous one. The column's own default was "everything", so a union would have made the plan decorative: every store would keep every capability whatever its plan said. It fails open by construction. |
| **A second check beside `HasModule` (a `[RequiresEntitlement]` attribute with its own lookup)** | Rejected by [ADR-0047](0047-commercial-control-plane.md) §4 before C1 began: two checks means two answers, and the one that is forgotten is the one that matters. |
| **A per-request entitlement query** | Rejected. The module gate runs *before* authentication and on every request; it must read a cached snapshot, not the database. |
| **Intersection, composed once in the tenant snapshot, with every missing input resolving to the empty set** | **Chosen.** |

## Decision

### 1. Two inputs, one answer, and it is an intersection

A module is effective for a store when **both** are true:

- the **contract** allows it — the store's active subscription's plan version grants it, or a live override does;
- the **platform** has it switched on — the existing `EnabledModules` column.

The two are not redundant, and naming them separately is the point: the plan is a *commercial ceiling*, the column is an *operational switch*. A merchant who does not want reviews turns them off without changing what they pay for; a merchant whose plan does not include reviews cannot turn them on.

The rule lives in the Domain (`Entitlements.Effective`), not in a query, so it is tested without a database and cannot be restated differently in a second place.

### 2. Every missing input resolves to nothing

`Effective(granted, enabled)` returns the empty set when either input is null or empty, and drops any key the product does not recognise. A store with no subscription, a subscription to a plan that cannot be resolved, or a snapshot built without module information, has **no optional capability at all**.

Three changes make that real rather than aspirational:

- `TenantInfo.Modules` became **non-nullable with no default value**. That is the load-bearing part: leaving a default — even an empty one — would have let every existing construction site keep compiling while silently changing meaning. Removing it made the compiler name all eight sites, and each was then decided deliberately.
- The database default became `''`.
- `StoreModules.Parse` stays tolerant, deliberately — a key removed from the product must not take a store down — but it is no longer *silent*: `StoreModules.Unknown` exposes what was dropped and `TenantDirectory` logs it with the store id.

### 3. Composed once, in the tenant snapshot

`TenantDirectory.Project` reads the three inputs in one round trip — the column, the active subscription's plan entitlements, and the live overrides — and composes them in memory after materialisation. Nothing downstream changes: callers still ask `TenantInfo.HasModule`.

Two consequences are accepted and written down rather than discovered later. The composition must stay **out** of the SQL projection (the existing client-evaluated `StoreModules.Parse` is the precedent; moving the intersection into the query would fail at runtime, not at build). And the answer inherits the directory's 60-second cache: every write here invalidates, so a plan change is immediate on the serving instance and within a minute elsewhere — but an override reaching its **own** expiry has no write behind it and can therefore outlive its expiry by up to that window. For a support exception capped at ninety days that is acceptable; cross-instance invalidation is a later phase.

### 4. An override grants, never denies, and never silently does nothing

An `EntitlementOverride` is bounded (ninety days maximum), attributed to the account that granted it, carries a written reason, and is audited like every platform-area command. It widens the *contract* side only.

Because the answer is an intersection, an override for a module the platform has switched off would change nothing. Rather than write a row that does nothing and report success, that grant is **refused** with a distinct code. A command that succeeds and has no effect is worse than one that fails.

### 5. The platform area's privileges became earned rather than declared

Two architecture rules named `Features.Platform` as a string literal: the one exempting platform requests from "no request carries a `TenantId`", and the one requiring platform requests to be audited. A commercial control plane necessarily carries a `TenantId`, so the first had to grow.

It grew into one shared list (`ModuleMap.PlatformAreaFolders`) read by both rules — and, more importantly, the exemption is now **tested rather than asserted**. A new rule proves that every request type in a platform-area folder is reachable only through a `[PlatformEndpoint]` route, rejecting `[AvailableOnAllHosts]` specifically, because that attribute also serves the endpoint on a store host — where a body-supplied `TenantId` is exactly the cross-tenant hole the original rule exists to prevent.

### 6. Introducing all of this changed no store's behaviour

A *foundation plan* — explicitly not a commercial tier, with no price and no limits — carries the three optional modules that are free today. The C1 migration creates it and subscribes every existing store; provisioning subscribes each new one; the seeder ensures it for the demo store.

This is what makes the fail-closed default safe to ship, and it grants nothing paid: a capability added later is in no existing plan version, so no store receives it by default. That property — *new entitlements are granted to nobody until a plan names them* — is the one this record most wants to preserve.

## Consequences

**Good.** One answer, one enforcement point, one place the rule is written, and it is Domain code with unit tests. Three fail-open links are closed, and the most valuable one is closed by the compiler rather than by vigilance. A new paid capability cannot leak by default. Shape-B tables gained a real architecture guard — which immediately found a pre-existing reader nobody had inventoried.

**Costs.** Two inputs is more to explain than one, and an operator looking at a ticked checkbox for a module the plan does not grant would be misled — so the platform console must show both the switch and the effective answer, and that is part of the work rather than a follow-up. The intersection makes a plan assignment insufficient on its own if the operator has switched a module off. The composition adds two correlated subqueries to a cached, per-host read. And the cache window means entitlement changes are not instantaneous across instances.

**Revisit when** limits need enforcing (the counter design in [ADR-0049](0049-tenant-quota-enforcement.md) introduces a second kind of entitlement question), when there is more than one API instance (cross-instance invalidation), or when real commercial tiers exist and the foundation plan can be retired.
