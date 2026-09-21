# Tax module

> **Code:** `src/Souq.Application/Features/Tax`, `src/Souq.Domain/Platform/TaxProfile.cs`, `src/Souq.Domain/Entities/StoreTaxSettings.cs`, `src/Souq.Domain/ValueObjects/TaxSnapshot.cs`, `src/Souq.Infrastructure/Persistence/Repositories/TaxRepositories.cs`, `src/Souq.API/Controllers/TaxControllers.cs` · **Decisions:** [ADR-0055](../../11-ADR/0055-tax-as-a-configurable-capability.md), owner decision `P-06` in [OwnerDecisions.md](../../09-OPERATIONS/OwnerDecisions.md#p-06--tax--decided-as-a-capability-not-as-a-rate)

## Purpose

Tax is the platform's **jurisdiction configuration**, and it is the fifteenth module. It answers *what tax rules applied to this sale, on what date, and who said so* — without a single tax rule living in code.

It exists because the owner's answer to `P-06` was not one of the three options put to them. The question was whether prices are tax-inclusive or tax-exclusive and what the rates are. The answer was that **neither is a platform-wide constant**: tax is a configurable, jurisdiction-aware capability, its values are data, and every value is *configuration requiring verification by an accountant, a tax authority or a lawyer before commercial use*.

So this module holds a mechanism and **no tax law**. A jurisdiction's name may appear in a profile; it may never appear in a branch.

## Responsibilities

- A reusable **jurisdiction profile** that Souq maintains once and any number of stores select.
- **Versions** of that profile, each with an effective-from instant, editable while a draft and frozen on publish.
- **Rates** in basis points, with the category each applies to.
- The **verification state** of each version, with the name of whoever verified it and when.
- A **store's selection**: which profile, whether collection is on, and the store's own registration number.
- An **immutable snapshot** shape (`TaxSnapshot`) for freezing onto an order or an invoice.
- Nothing else. In particular, **no rate, no threshold and no invoice requirement is asserted by this module** — it stores what someone entered and reports whether anyone verified it.

## Not this module's job

| Not this module | Owner |
|---|---|
| Deciding any rate, threshold, exemption or invoice requirement | The owner, with an accountant. This module records the answer and its verification state |
| Computing an order's totals | [Shopping](../Shopping/README.md) — `PricingService` owns the pipeline and asks this module for the tax term |
| Freezing the snapshot onto an order | [Ordering](../Ordering/README.md) — `OrderPlacement` calls `Order.ApplyTax` beside the shipping and discount snapshots |
| Taxing Souq's own subscription invoices | [Billing](../Billing/README.md), using this mechanism with the **platform's** own profile — never a merchant's |
| Assigning a tax category to a product | [Catalog](../Catalog/README.md), in a later slice. Today everything is on the default category |

## Business concepts

- **Jurisdiction profile** — a country or region's rules, held once. One profile per jurisdiction: a second profile for the same jurisdiction would make "which one is right?" unanswerable, so corrections are versions, not profiles.
- **Version** — one immutable set of rules with an effective-from instant. A draft is editable; a published version is frozen. **Corrections are new versions**, which is the only way the rules that applied on a date stay knowable from the database alone.
- **Price mode** — `Inclusive` or `Exclusive`, a property of the **version**, not of the platform. This is the substance of the `P-06` answer: one platform serves a market that displays tax-inclusive prices and another that adds tax at checkout, with no branch in code.
- **Shipping taxable** — a jurisdiction rule with **no default value**, because assuming either answer is wrong by the amount of the shipping tax on every order, in a direction that only shows up in a tax filing.
- **Rate** — a named percentage in **basis points** (1600 = 16%), so nothing about tax arithmetic approaches a floating-point number. Several rates may apply to one category: some jurisdictions levy two taxes on the same sale, so `BasisPointsFor` **sums** them, explicitly.
- **Verification** — `Unverified`, `RequiresProfessionalConfirmation` or `Verified`, with the verifier's name and date. **Engineering never sets `Verified`** — not in code, not in a migration, not as a default.
- **Registration threshold** — recorded where a jurisdiction has one, and **nothing computes with it**. It is shown to whoever decides; the day it is applied, it is applied by a rule an accountant wrote.
- **Store tax settings** — the store's *selection*, not its rules. A merchant picks a profile, switches collection on, and records their registration number. **A merchant never enters a rate**: if they could, a central verification would mean nothing and the values would scatter store by store, which is the problem the profile exists to end.
- **Collection reason** — why tax is not being collected, as a stable code: `NoProfileSelected`, `CollectionDisabled`, `NoEffectiveVersion`, `VersionNotVerified`, or `Collecting`. A zero with no reason reads like a defect.

## The one rule that everything else serves

**Tax is collected only under a version that is published *and* verified.** `TaxProfileVersion.AllowsCollection` is that rule, and the store's read surface reports its reason when it is false.

This is the fail-closed answer to the owner's "requiring verification before commercial use": a researched number — something engineering found in a document — sits in the database and **cannot reach a shopper**. The recorded research about Jordan's regime is exactly such a number, and nothing about it is treated as verified.

## Domain model

| Type | Kind | Path | Notes |
|---|---|---|---|
| `TaxProfile` | aggregate, platform-owned (no tenant) | `src/Souq.Domain/Platform/TaxProfile.cs` | One per jurisdiction; owns its versions. `VersionOn(instant)` resolves the published version effective then |
| `TaxProfileVersion` | entity | same file | Draft → Published. Holds the price mode, shipping taxability, rates, threshold, notes and verification |
| `TaxRate` | entity | same file | Code, name, basis points (0–10000), category |
| `TaxVerification` | value object | same file | State + who + when + note |
| `TaxPriceMode` · `TaxVerificationState` · `TaxProfileVersionStatus` | enums | same file | Stored as `int`; never renumber |
| `StoreTaxSettings` | entity, `ITenantOwned` | `src/Souq.Domain/Entities/StoreTaxSettings.cs` | The store's selection. Deselecting a profile switches collection off with it, so the state cannot claim what it does not do |
| `TaxSnapshot` · `TaxSnapshotLine` | value objects | `src/Souq.Domain/ValueObjects/TaxSnapshot.cs` | Carries **values, not references**: an order's tax is re-derivable from the order alone, for as long as the order exists |
| `ITaxProfileRepository` · `IStoreTaxSettingsRepository` | ports | `src/Souq.Domain/Interfaces/ITaxRepositories.cs` | Implemented in Infrastructure |
| `ITaxCalculator` · `TaxBasis` · `TaxQuote` | port and contracts | `Features/Tax/Contracts/ITaxCalculator.cs` | The quote carries the reason, so a zero is never silent. Shaped so an external tax service becomes another implementation rather than a redesign |
| `TaxCalculator` | application service | `Features/Tax/TaxCalculator.cs` | The only implementation. Public like `PricingService`, for the same reason: a unit test builds it from mocked ports |

## Data ownership

Four tables. `TaxProfiles`, `TaxProfileVersions` and `TaxRates` are **platform tables with no tenant id** — like `Plans`, and for the same reason: they are one answer serving every store. `StoreTaxSettings` is store-owned, one row per store, with the tenant filter and foreign key applied by the reflection loop in `AppDbContext`.

`StoreTaxSettings` references `TaxProfiles` with `Restrict`: a profile a store selected is not deleted from under it.

## API

| Method | Route | Authorization | Use case |
|---|---|---|---|
| GET | `/api/platform/tax/profiles` | `platform.settings.manage`, platform host | List profiles with their versions |
| GET | `/api/platform/tax/profiles/{id}` | `platform.settings.manage`, platform host | One profile |
| POST | `/api/platform/tax/profiles` | `platform.settings.manage`, platform host | Create a profile for a jurisdiction |
| POST | `/api/platform/tax/profiles/{id}/versions` | `platform.settings.manage`, platform host | Draft a version with its rates |
| POST | `/api/platform/tax/profiles/{id}/versions/{versionId}/publish` | `platform.settings.manage`, platform host | Freeze it |
| POST | `/api/platform/tax/profiles/{id}/versions/{versionId}/verify` | `platform.settings.manage`, platform host | Record a professional's verification, by name |
| POST | `/api/platform/tax/profiles/{id}/versions/{versionId}/require-confirmation` | `platform.settings.manage`, platform host | Withdraw verification; collection stops at once |
| GET | `/api/admin/store/tax` | `store.settings` | The store's settings, with the reason if it is not collecting |
| PUT | `/api/admin/store/tax` | `store.settings` | Select a profile, switch collection, set the registration number |
| GET | `/api/admin/store/tax/profiles` | `store.settings` | The profiles a store may choose, each with its verification state |

**`platform.settings.manage` gets its first endpoint here.** Open decision `P-07` recorded that the permission was granted to the platform owner and that *no endpoint required it and no platform-wide setting existed*. A jurisdiction profile is a platform-wide setting in the fullest sense — one value serving every store — so it is the right first holder rather than a fourth permission invented for it. The day verification becomes a finance role separate from the platform owner, it earns its own permission; that is a decision about roles, not about an endpoint.

## Security and permissions

Every write here is audited (`IAuditable`), which is one of ADR-0055's six invariants: a tax configuration change has money and legal responsibility behind it, so who did it and when is not a detail. `Tax` is therefore listed in `ModuleMap.AuditedAreaFolders`. It is **not** a platform area: the store-side requests take their tenant from the host like every other store request, and the platform-side requests target no store at all.

## Tenant behaviour

A store with no profile selected behaves exactly as every store did before this module existed. That is the deliberate default: shipping this capability changed no store's totals.

## Tests

| Level | File | What it pins |
|---|---|---|
| Domain | `tests/Souq.Domain.Tests/TaxProfileTests.cs` | Published versions refuse edits; a correction must be a new version with a later effective-from; `Verified` is unreachable without a named verifier; a draft cannot be verified; basis points outside 0–10000 are refused; rates for one category are summed |
| Application | `tests/Souq.Application.Tests/Tax/StoreTaxSettingsTests.cs` | Collection cannot be switched on without a profile; deselecting a profile switches it off |
| Application | `tests/Souq.Application.Tests/Tax/TaxCalculatorTests.cs` | All four zero-with-a-reason paths, inclusive versus exclusive arithmetic, shipping in or out of the basis, a category with no rate, and that the snapshot's lines sum to the amount charged |
| Domain | `tests/Souq.Domain.Tests/TaxProfileTests.cs` (`OrderTaxTests`) | Exclusive tax raises the order total and inclusive tax does not; an amount that disagrees with its snapshot is refused; tax cannot change once the order is being processed |
| Integration | `tests/Souq.IntegrationTests/TaxConfigurationTests.cs` | The whole platform workflow over HTTP; that a published-but-unverified version does not permit collection; **and that a verified one taxes a real order, shows the tax in the basket, freezes its snapshot, and keeps that snapshot when a newer version is published** |

## How a sale is taxed

`ITaxCalculator` is asked once per quote, at the point in the pricing pipeline where the zero used to be — after the discount and the shipping, because both change the basis. It returns a **reason, not just a number**, and four of its five answers are zeros with a name: no profile selected, collection disabled, no version effective at that instant, or a version nobody verified.

- **The instant is an argument, not `now`.** Rules are resolved by effective date, so re-quoting an old order passes that order's instant rather than today's.
- **Exclusive** tax is `basis × basisPoints ÷ 10000` and is **added** to what the shopper pays. **Inclusive** tax is `basis × basisPoints ÷ (10000 + basisPoints)` — the portion *already inside* a displayed price — and is **not** added. Inverting those two denominators overstates the tax by the rate itself, and `Order.TaxAddedToTotal` carries the same rule so the aggregate cannot disagree with the quote.
- **Shipping enters the basis only if the version says so.** No default (see *Business concepts*).
- **Rounding happens once**, through `Money.FromCalculation`, so no fils leaks between rate lines ([ADR-0014](../../11-ADR/0014-money-precision.md)).
- **The snapshot is frozen onto the order at placement**, alongside the shipping and discount snapshots, and is refused afterwards like them — the invoice does not change. `Order.ApplyTax` also refuses an amount that does not equal the sum of its own snapshot lines, so "an amount with no rules behind it" cannot be stored.
- **The storefront shows a tax line only when tax is collected.** A zero line invites a question with no answer; and without the line, an exclusive-mode store's total would exceed subtotal + shipping with nothing on screen explaining why.

## Known limitations
- **No product tax category.** Every line is on the default category. A rate for another category can be configured and will not apply to anything until products can be assigned one.
- **The registration threshold is recorded and unused.** See *Business concepts*.
- **No jurisdiction ships with the product.** There is no seeded profile, verified or otherwise, and there deliberately never will be one from engineering.
- **No platform or merchant screen yet.** Both configuration surfaces are API-only. The platform console screen and the merchant setting are the next frontend slice; the shopper-facing tax line exists.
- **One tax basis for the whole order.** Per-line tax (which a per-product category would require) is not computed: the basis is goods-after-discount plus shipping-if-taxable, and the rates apply to that. A jurisdiction that taxes two categories at two rates in one basket needs the product-category slice first.
- **Souq's own invoices do not use this yet** — that arrives with `C5`, and it must use the platform's own profile, never a merchant's.

## Future evolution

An external tax service is a later implementation of the same port rather than a redesign — ADR-0055 records why it was rejected for now (a paid dependency, a network call inside the pricing path, and a cross-border transfer that `C-08`'s residency question has not answered). Jordan's national e-invoicing **clearance** model, recorded as unverified research, would sit between "invoice issued" and "invoice legally valid" and is an argument that the store must remain the invoice issuer; nothing about it is built.
