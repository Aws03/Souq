# ADR-0054: An absent limit means uncapped, not zero — and a plan may only carry a limit the platform knows how to count

- **Status:** Accepted and **implemented** in C2, 2026-09-21. It answers the question [ADR-0049](0049-tenant-quota-enforcement.md) left to the enforcing phase and `Plan.LimitFor`'s own comment deferred by name ("what absence means is the enforcement decision, C2's, not the catalogue's"). It **reverses** a C1 decision on purpose — see §Options, option C. Supersedes nothing.
- **Date:** 2026-09-21
- **Related modules:** Billing, Catalog, Identity, Platform
- **Related ADRs:** [ADR-0049](0049-tenant-quota-enforcement.md) (the counter-row mechanism this gives vocabulary to), [ADR-0053](0053-entitlement-resolution.md) (the entitlement rule this is deliberately asymmetric with), [ADR-0047](0047-commercial-control-plane.md) §4 (entitlement and limit are different concepts and must not be conflated)

## Context

C1 built *Plan*, *Limit* and *PlanLimit* and enforced none of them. It made two deliberate abstentions, both recorded in the code:

- `Limit` validated the **shape** of a name and not its **membership**, because sentencing a list of names would have answered owner decision `C-12` (what the tiers are) implicitly.
- `Plan.LimitFor` returned `int?` and its comment said an absent limit "is not zero and is not infinity: it is unspecified. What absence means is the enforcement decision (C2), not the catalogue's."

C2 builds the enforcement. Both abstentions now have to end, because enforcement cannot proceed without an answer to either.

## Problem

1. When a store's plan does not name a limit, may the store create the thing?
2. May a plan carry a limit name the platform has no way to count?

## Options considered

### A — An absent limit is zero (fail closed, symmetric with the entitlement)

**Rejected.** It has the right instinct and the wrong target.

The repository's security posture is fail-closed, and [ADR-0053](0053-entitlement-resolution.md) had just closed a fail-open entitlement default. Reading absence as zero would look consistent.

It is not, because an entitlement and a limit are different things, and [ADR-0047](0047-commercial-control-plane.md) §4 says so in as many words. **An entitlement is a gate that grants a capability; a limit is a restriction on a capability already granted.** The absence of a grant is "not granted" — closed. The absence of a restriction is "unrestricted" — there is nothing there to fail either way. Reading a missing number as a prohibition invents a rule rather than enforcing one.

And the consequences are not theoretical:

- The foundation plan carries **no limits at all**, by construction, because C1 created it specifically so that introducing plans changed no store's behaviour. Under this option, every existing store would stop being able to create a product or invite a colleague the moment C2 shipped.
- A store with **no contract** is a state C1 deliberately tolerates: `IStoreEntitlements.AssignFoundationPlanAsync` returns `false` and leaves the store contract-less rather than failing provisioning half-way, and the platform screen shows that state explicitly. Under this option that tolerated state becomes a total stop.
- Publishing a plan and forgetting one number would brick a paying merchant's catalogue, with no error at publish time to warn anyone.

The fail-closed posture is not weakened by this choice, because **the gate is still the entitlement**, and it still fails closed. Nothing about option A protects revenue that ADR-0053 does not already protect.

### B — An absent limit is uncapped, and any shape-valid name may be carried

**Rejected on the second half only.**

Once enforcement exists, a limit name with no counting rule behind it is a **promise that is never kept**: it is written into the contract, displayed on the platform screen, and stops nothing — with no exception, no log line and no red test. Worse than a limit that is not enforced is a *cap a merchant is told they have* and does not.

This is the same failure `Plan.SetEntitlements` already refuses for an unknown entitlement key, with the reason written beside it: *"a plan is a contract, and silently ignoring what is in it means selling a capability that is not granted."* The exact counterpart here is selling a **restriction that does not restrict**.

### C — An absent limit is uncapped, and the limit names are a closed catalogue

**Chosen.**

This reverses C1's first abstention, and the reversal is narrow: the catalogue lists **what the machine knows how to count**, not what is sold. It carries no value, no price and no tier, so `C-12` remains entirely open and entirely the owner's. What changed is not the question; it is that enforcement now exists and an unenforceable name has become a defect rather than a placeholder.

## Decision

**An absent limit means uncapped.** `TenantInfo.LimitFor` returns `int?`; `null` is not a restriction. This is deliberately asymmetric with `TenantInfo.HasModule`, and both methods carry the reason in a comment beside them so the asymmetry cannot be read as an oversight.

**`LimitNames` is a closed catalogue,** and `Limit`'s constructor rejects a name outside it. Two names exist today:

| Name | Counts | Freed by |
|---|---|---|
| `catalog.products` | products that are not archived | archiving (which is this repository's delete) |
| `staff.seats` | store staff accounts that are not disabled; a pending invitation holds a seat | disabling |

**What is counted is what the merchant can empty.** Neither a product nor a staff account has a hard delete in this repository. Had the archived and the disabled been counted, the limit would have been a **one-way ratchet**: it fills and never empties, and a merchant at their ceiling has no option but to upgrade. That is a commercial policy nobody decided, arrived at by an implementation detail, so it was rejected.

Reading stays tolerant where writing is strict, exactly as `StoreModules.Parse` already does: a `PlanLimit` row carrying a name the product no longer knows is **dropped with a warning** when the tenant snapshot is built, never thrown. Dropping resolves to "uncapped", which is the only direction that does not stop a merchant because of drift in platform data.

**Adding a name is an engineering undertaking of three parts, and none may ship without the others:** a counting rule in `QuotaResources`, a reservation on the creation path, and a release on the freeing path. An architecture test (`كل_اسم_حدّ_له_قاعدة_عدّ`) enforces the first in both directions; the other two are named human review in the module's change guide.

## Consequences

**Good.** No existing store's behaviour changed when C2 shipped — the same property C1 was built to preserve. A plan cannot be published carrying a cap that does not cap. The asymmetry with entitlements is written down where both are read, rather than being rediscovered as a suspected bug.

**Costs.** Two rules now differ in their treatment of absence, which is genuinely harder to hold in the head than one rule; the mitigation is that both sites carry the reason inline. A new commercial limit is no longer a data change — it needs code, which is the price of the guarantee that a carried limit is an enforced one. And the platform screen must show "uncapped" explicitly, because a merchant or an operator who sees no number needs to know it means no ceiling rather than an unloaded value.

**Revisit when** a limit is needed that cannot be counted from the database at all — a rate, say, or something measured over a window — at which point `QuotaResources`' "count the rows" shape is the assumption that breaks, not this record's answer about absence.
