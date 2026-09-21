# ADR-0055: Tax is a configurable, jurisdiction-aware capability — never a rule in code, and never a value engineering signed

- **Status:** Accepted 2026-09-21. It records the owner's answer to product decision `P-06`, which **reframed the question rather than choosing one of its three options**: neither "no tax", "tax-inclusive" nor "tax-exclusive" is a platform-wide constant. **Not yet implemented** — the phase that implements it is named in §Migration. Supersedes nothing.
- **Date:** 2026-09-21
- **Related modules:** Tax (new, see §Decision), Shopping, Ordering, Billing, Platform, Catalog
- **Related ADRs:** [ADR-0028](0028-basket-and-pricing-pipeline.md) (the pricing pipeline whose tax term is an explicit zero), [ADR-0014](0014-money-precision.md) (`Money`, three-decimal currencies and the single rounding site), [ADR-0047](0047-commercial-control-plane.md) (the platform-maintained, versioned, frozen-on-publish *Plan* this profile is modelled on), [ADR-0054](0054-limit-semantics-and-catalogue.md) (a configured name the machine cannot honour is a promise that is never kept — the reasoning reused here), [ADR-0004](0004-module-boundaries.md) (adding a module is a boundary decision), [ADR-0024](0024-platform-administration.md) (platform-area requests carry a tenant id and are audited)

## Context

The pricing pipeline has had an explicit zero where tax belongs since Phase 8, with a comment naming `P-06` as the reason. That zero was correct: in most jurisdictions handling tax wrongly is a legal bar rather than a missing feature, and liability accrues from the first sale rather than from the first audit. Engineering's standing rule was to invent nothing — [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) says in as many words that it will "not invent a tax rule, not even a reasonable default".

`P-06` was put to the owner as three options: collect no tax, prices are tax-inclusive, prices are tax-exclusive. **The owner answered none of them**, and the answer is the more useful one: tax is to be a configurable, jurisdiction-aware platform capability, with jurisdiction profiles, configurable rates, thresholds, inclusive/exclusive taxation, registration and collection rules, effective dates, product/order applicability where required, store-level configuration, immutable snapshots on orders and invoices, room for jurisdiction-specific extensions, and auditability of every configuration change. A profile may carry researched default values, but they "must be treated as configuration requiring verification by the appropriate accountant/tax authority/legal professional before commercial use", and engineering must not "invent or assert tax rates, thresholds, invoice requirements, or legal obligations".

Two facts about the repository shape what that has to look like:

- **The order has no tax term at all.** `PriceQuote` carries `Tax` and the order does not, so a tax that is charged but not frozen onto the order would be recomputed — differently — the first time a rate changed.
- **Souq now bills its own merchants** (`D-13` = A, answered the same day): Souq's revenue is a subscription it invoices, so tax applies in two unrelated places — the shopper's purchase from a store, and Souq's invoice to a merchant. They are different taxpayers in different jurisdictions and they must not share a code path by accident, only a mechanism on purpose.

## Problem

1. Where does tax law live, so that it is never in a `if (country == …)`?
2. How does a second store in the same jurisdiction reuse the first store's verified configuration instead of re-entering it?
3. How does an order placed last year keep the exact rules that applied when it was placed, after the rate changes?
4. What stops an unverified researched value — a number engineering found in a document — from silently charging a real shopper?
5. Who owns the concept, given that both the storefront's pricing and the platform's own invoicing need it?

## Options considered

### A — A tax rate on the store, and an inclusive/exclusive switch beside it

**Rejected.** It is the cheapest thing that produces a non-zero number, and it fails every one of the five questions. A rate on the store is re-entered per store, cannot be verified once for a jurisdiction, has no effective date, so changing it silently rewrites the past, and it carries no verification state, so a researched number and an accountant-confirmed one are indistinguishable in the database. It also has nowhere to put a threshold, a registration number or a category rule, so the first jurisdiction that needs one puts it in code — which is precisely what the owner's direction forbids.

### B — A tax provider behind a port, with rates fetched from an external tax service

**Rejected for now, and the port does not foreclose it.** A hosted tax service (the kind that answers "what is the rate for this address and this product") solves correctness by outsourcing it, and it is the right answer at a scale Souq is nowhere near. Today it would add a paid external dependency, a per-request network call inside the pricing path, a cross-border transfer of order data that `C-08`'s residency question has not answered, and a failure mode — the service is down — in the one path that must never be wrong. *ITaxCalculator* is shaped so that an adapter calling such a service is a later implementation of the same port rather than a redesign.

### C — A platform-maintained, versioned jurisdiction profile that stores select, with an immutable snapshot on every order and invoice

**Chosen.** It is the shape the owner described, and it is the shape [ADR-0047](0047-commercial-control-plane.md) already proved in this repository for *Plan*: a platform-owned catalogue entity, versioned, frozen on publish, selected by a store, and snapshotted where it has commercial effect. Reusing that shape is most of the argument for it — the lifecycle questions (who edits, what freezing means, how a store's selection is audited) are answered the same way twice rather than invented again.

## Decision

### 1. A fifteenth module, *Tax*

Tax becomes a module — a `Features/Tax` folder, an entry in `ModuleMap`, and a document in `docs/04-MODULES/Tax/` — rather than living inside an existing one. The alternative placements were considered and are worse: inside *Shopping* it would make *Billing* depend on *Shopping* to tax a subscription invoice; inside *Platform* it would give *Platform* a calculation service and a store-level settings aggregate that are nothing to do with administering stores; inside *Billing* it would tax a shopper's basket through the module that bills merchants. Tax is genuinely a shared capability with its own data and its own lifecycle, consumed by *Shopping*, *Ordering* and *Billing* through `Features/Tax/Contracts`, and that is what a module is for here.

Part of the platform area, for the same two reasons *Billing* is: its platform requests target a named store and are all audited.

### 2. The model

| Concept | Owner | What it is |
|---|---|---|
| *TaxProfile* | platform | A reusable jurisdiction — a country or region — that Souq maintains once and any number of stores select. Carries no rate itself. |
| *TaxProfileVersion* | platform | One immutable version of that jurisdiction's rules, with an **effective-from** instant. Editable only while a draft; frozen on publish, exactly as a *Plan* is. Carries the inclusive/exclusive convention, the rounding rule, the rates, the applicability rules, the registration threshold, and the collection rules. |
| *TaxRate* | platform | A named rate inside a version, expressed in **basis points**, with the category it applies to. |
| *TaxCategory* | platform | A code inside a version that a rate applies to, plus the version's default category. What a store's product is assigned is the next slice (§Migration). |
| *TaxVerification* | platform | The version's verification state, and who set it: `Unverified`, *RequiresProfessionalConfirmation*, or `Verified` with the verifier's identity, the date and a note. |
| *StoreTaxSettings* | store (tenant-owned) | Which profile this store selected, its own registration identity where it has one, and the store-level overrides the profile permits — nothing else. |
| *TaxSnapshot* | frozen | What was actually applied: the profile, the version, the convention, and every rate line with its basis points and its computed amount — written onto the order and onto a platform invoice, and never recomputed. |

### 3. The six invariants, and each one exists because of a way this goes wrong

1. **No tax law in code.** Every rate, threshold, convention and applicability rule is a value in a profile version. A jurisdiction's name may appear in a profile; it may never appear in a branch.
2. **A published version is immutable.** A correction is a new version with its own effective-from, never an edit — so the rules that applied on a date stay knowable from the database alone.
3. **The applicable version is the one effective at the moment of the commercial event**, and it is resolved once and snapshotted. An order placed last year keeps last year's rules because the snapshot is what it carries, not a foreign key to something that moves.
4. **Tax is collected only under a `Verified` version.** Under any other state the pipeline's explicit zero stays and the reason is reported to the merchant and on the platform screen. This is the fail-closed answer to the owner's "requiring verification before commercial use": a researched number cannot reach a shopper by being present in the database. **Engineering never sets `Verified`** — not in code, not in a migration, not as a default. It is set by a platform action, attributed to the person who took it, and audited.
5. **Rates are basis points and there is one rounding site**, which is `Money`'s ([ADR-0014](0014-money-precision.md)). A three-decimal currency is the normal case here, not the exotic one.
6. **Every configuration change is audited** — creating a profile, publishing a version, changing verification, and a store selecting or overriding one.

### 4. Inclusive and exclusive are both real, and the server decides which

The convention is a property of the profile version, so one platform serves both. Under an exclusive convention the shopper's total is goods + shipping + tax; under an inclusive one the displayed price already contains the tax and the tax component is derived from it for the receipt. In both cases the number the shopper is shown and the number they are charged come from the same server-side quote — the storefront displays a total, it never computes one.

### 5. Jurisdiction-specific extensions arrive as code behind the port, not as an uninterpreted setting bag

A named setting the platform does not interpret is a promise that is never kept — the same failure [ADR-0054](0054-limit-semantics-and-catalogue.md) refused for a limit name nothing counts. So a version carries the dimensions the calculator actually honours, and a jurisdiction needing something outside them (Jordan's e-invoicing **clearance** step is the recorded candidate, and it is unverified research) gets a modelled concept and an implementation, with its own ADR if it changes a boundary.

### 6. The two taxpayers stay separate

The shopper's tax on a store's order and Souq's tax on its own subscription invoice use the same mechanism and never the same configuration: the store's order resolves the *store's* profile, and Souq's invoice resolves the *platform's* own. They are different taxpayers in possibly different jurisdictions, and nothing in the model lets one default to the other.

## Consequences

**Good.**

- No tax rule is in business logic, so a new jurisdiction is configuration and a verification, not a release.
- The second store in a jurisdiction selects the profile the first store's accountant verified, which is the reuse the owner asked for by name.
- History is exact: an order's tax is reproducible from the order alone, for as long as the order exists.
- An unverified researched value is harmless by construction — it cannot charge anyone.
- Souq's own invoices get tax from the same mechanism without sharing a single value with a merchant's.

**Costs, accepted.**

- It is a genuinely larger build than a rate on a store: an aggregate with versions, a platform editor, a store selector, a snapshot on two different documents, and a calculator. Most of this ADR's length is the argument that the smaller thing does not work.
- **The capability ships with no jurisdiction able to collect anything**, because no version can be `Verified` by engineering. That is the intended state and it must not read as an unfinished feature: the platform screen says which profiles are unverified, and a store whose profile is unverified is told that tax is not being collected and why.
- A fifteenth module is a boundary change, with a document, a `ModuleMap` entry and contract edges to three modules.
- Adding tax to the pricing pipeline changes a number shoppers see, in every store that switches it on. It is behind a per-store selection precisely so that it changes nothing until a merchant, with a verified profile, turns it on.

**What is still not engineering's, and is listed here so this ADR is not mistaken for closing it:** every value in every profile — the rates, who they apply to, the registration thresholds, the invoice content and wording, and whether the store or the platform issues the shopper's invoice in a given jurisdiction. Those are the owner's, with an accountant, and the verification state is where their answer lands.

## Revisit when

- A jurisdiction needs a rule the modelled dimensions cannot express — then a modelled concept, not a setting bag (§Decision 5).
- An external tax service becomes worth its dependency — option B, as an adapter behind the same port.
- A store needs more than one jurisdiction at once (selling into a second country with a different regime). The model deliberately gives a store **one** profile today, matching the one-currency-per-store rule.

## Migration

- **The configuration half shipped on 2026-09-22**: the profile, its versions and rates, publish-and-freeze, the verification workflow with its named verifier, the store's selection, ten endpoints and a module document. Five tables, additive, nothing existing altered.
- **The calculation half is deliberately the next slice, and the two must not be split further.** Consuming a calculator means freezing a snapshot onto the order **in the same change** — otherwise a store could charge tax that its own order does not record, which is worse than charging none. So `PricingService`'s tax term is still an explicit zero, and `TaxSnapshot` exists as a shape with no writer yet.
- **Why the configuration half went first:** it is the part with external lead time. A profile is worthless until an accountant verifies it, and that wait can start now; the arithmetic can be written any week.
- Still ahead of `C5` in [CommercialPlatformPlan.md](../12-ROADMAP/CommercialPlatformPlan.md), because a platform invoice freezes a tax snapshot at issue.
- Additive only: new tables, a nullable snapshot on orders, and no change to any existing total. A store with no profile selected behaves exactly as today — the explicit zero — which is what every existing store does on the day this ships.
- The product-level tax category and the storefront's display of a tax component are the slice after the foundation, named so they are not assumed built.

## Verification

- Domain tests: a published version refuses edits; a correction is a new version; an effective-from in the past of an existing version is refused; `Verified` cannot be reached without an attributed verifier; a rate outside 0–10000 basis points is refused.
- Application tests: the calculator returns zero with a stated reason for every non-`Verified` state; inclusive and exclusive produce the same charged total for the same configured rate; the snapshot is complete enough to re-derive the tax without reading any profile row.
- Architecture tests: *StoreTaxSettings* is tenant-owned with the filter and the composite foreign key; the *Tax* platform folder is audited and may carry a tenant id; no currency or jurisdiction literal enters product code (`WhiteLabelSourceTests` already forbids the currency half).
- Integration: an order's snapshot survives a later version being published, byte for byte.
