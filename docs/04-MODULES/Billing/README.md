# Billing module

> **Code:** `src/Souq.Application/Features/Billing`, `src/Souq.Application/Features/Subscriptions`, `src/Souq.Domain/Platform/Plan.cs`, `src/Souq.Domain/Platform/PlatformInvoice.cs`, `src/Souq.Domain/Platform/CreditNote.cs`, `src/Souq.Domain/Platform/BillingPeriod.cs`, `src/Souq.Domain/Platform/PlatformBillingSettings.cs`, `src/Souq.Domain/Platform/PlatformDocumentSequence.cs`, `src/Souq.Infrastructure/Persistence/PlatformDocumentNumbers.cs`, `src/Souq.Infrastructure/Persistence/Queries/PlatformBillingQueries.cs`, `src/Souq.Domain/Platform/Subscription.cs`, `src/Souq.Domain/Platform/EntitlementOverride.cs`, `src/Souq.Domain/Platform/Entitlements.cs`, `src/Souq.Domain/Platform/LimitNames.cs`, `src/Souq.Domain/Entities/TenantUsageCounter.cs`, `src/Souq.Infrastructure/Persistence/TenantQuotaGuard.cs`, `src/Souq.Infrastructure/Persistence/QuotaResources.cs`, `src/Souq.Infrastructure/Persistence/Queries/BillingQueries.cs` · **Decisions:** [ADR-0047](../../11-ADR/0047-commercial-control-plane.md), [ADR-0049](../../11-ADR/0049-tenant-quota-enforcement.md), [ADR-0053](../../11-ADR/0053-entitlement-resolution.md), [ADR-0054](../../11-ADR/0054-limit-semantics-and-catalogue.md), [ADR-0056](../../11-ADR/0056-platform-invoices-and-manual-collection.md) · **Change guide:** see **Adding a limit** below

## Purpose

Billing is the platform's **commercial control plane**: it answers *what may this store use, on what terms, and what does it owe*. It is the fourteenth module, and since `C5` it holds both halves — plans, subscriptions and entitlements, **and Souq's own invoices and their collection**.

That second half is not optional. Owner decision `D-13` = A makes every store its own merchant of record: Souq never touches shopper funds and takes no commission, so **a merchant subscription Souq invoices is the only way the company is paid**. Dunning, automated card collection and commission ledgers remain later phases, each waiting on a named decision.

The module exists because commercial concepts do not belong to `Platform`, which owns a store's identity and presentation. But it deliberately owns **no enforcement**: `TenantInfo.HasModule` stays the single enforced answer to "may this store use X?", asked where it was always asked. Billing *feeds* that answer; it does not sit beside it. A second feature-flag system is the failure this module is shaped to avoid.

## Responsibilities

- The plan catalogue: versioned plans, each naming the entitlements it grants and the numeric limits it carries.
- Which plan version is in force for each store, and since when.
- Expiring, attributed, audited entitlement overrides, so support can make an exception without creating a second source of truth.
- **Enforcing a numeric limit** — refusing the (N+1)th product or staff seat, correctly under concurrency, through the one port `ITenantQuotaGuard` (C2).
- **Souq's own invoices** (C5): the platform's billing configuration, the number series, drafting, issuing, the frozen tax snapshot, credit notes, and **collection recorded by hand** — a bank transfer a platform operator sees in a statement and enters. No payment provider is involved anywhere.
- **The ledger of billable units**: `BillingPeriod` (`Open → Closing → Closed`) and an append-only, idempotent `BillableEvent`. It records; it prices nothing by itself.
- **What the merchant sees of its own subscription** — its plan, what it owes, its invoices, and how to pay (`Features/Subscriptions`).
- Nothing else. In particular, no shopper's money ever.

## Not this module's job

| Not this module | Owner |
|---|---|
| Enforcing an entitlement | [Platform](../Platform/README.md) — `TenantInfo.HasModule`, read by `TenantAvailabilityMiddleware` and by `PricingService` |
| Resolving the effective set | Infrastructure — `TenantDirectory` composes the inputs once per cached snapshot |
| A store's identity, domains, status and settings | [Platform](../Platform/README.md) |
| Taking a shopper's money | [Payments](../Payments/README.md). The two money paths never meet — see **Tenant behaviour** |
| Deciding *what* the tiers and their numbers are | The owner — decision `C-12`. This module builds the mechanism and names no tier and no value |
| Deciding a tax rate, or marking one trustworthy | [Tax](../Tax/README.md). Billing asks `ITaxCalculator` and freezes what it answers; it never interprets a rule ([ADR-0055](../../11-ADR/0055-tax-as-a-configurable-capability.md)) |
| Chasing an unpaid invoice, or suspending for non-payment | Nobody yet — `C6`, which needs this module's invoice state plus `C4`'s cross-instance locking |
| Charging a card automatically | Nobody yet — it needs a payment provider, and `C-01` names none |

## Business concepts

- **Entitlement** — a boolean capability key: *may they?* Its key space is deliberately the same as the optional store modules (`StoreModules.All`), because one catalogue and one enforcement point is the whole point.
- **Limit** — a name and a non-negative number: *how much?* A different concept from an entitlement and never conflated with one. Since C2 the **names** come from a closed catalogue (`LimitNames`) while the **values** remain entirely the owner's, so `C-12` is still open: the catalogue lists what the platform knows how to count, not what is sold ([ADR-0054](../../11-ADR/0054-limit-semantics-and-catalogue.md)).
- **An absent limit means uncapped, not zero** — deliberately the opposite of an absent entitlement, which is a refusal. An entitlement grants a capability; a limit only narrows one already granted, so a missing number is a missing restriction, not a missing grant. Reading it as zero would have stopped every existing store the day C2 shipped, since the foundation plan carries no limits at all.
- **Usage counter** — one row per (store, limit name) holding what is consumed. It is a *picture* of the truth, not the truth: `QuotaResources` holds the real count, and a reconciling sweep corrects drift.
- **Plan** — a versioned set of entitlements and limits, identified by `(Code, Version)`. A published plan is **frozen**: a subscriber keeps the terms they subscribed to, so a change is a new version, never an edit.
- **Plan status** — `Draft` (editable, not subscribable) → `Published` (subscribable, frozen) → `Retired` (no new subscriptions; existing subscribers keep it).
- **Subscription** — which plan version is in force for one store. One row per store.
- **Entitlement override** — a support exception that **expires**, is **attributed** to the account that granted it, and is **audited**. It grants; it never denies.
- **The foundation plan** — not a commercial tier. It carries what a store had before plans existed: the three optional modules that are free today, no price, no limits. Every pre-existing store was subscribed to it by the C1 migration, and a newly provisioned store is subscribed to it too. It exists so that introducing plans changed no store's behaviour.
- **Effective modules** — the one answer: what the plan grants (plus active overrides), intersected with what the platform has switched on for that store. Both inputs must say yes.
- **Plan price** (C5) — an amount and an interval on a plan **version**, frozen on publish like the rest of its terms, and **absent by default**. `null` means *nobody has decided yet* and refuses to produce a subscription line; a price of zero is a deliberate free tier that issues a zero invoice and settles it. The currency is never sent by a caller: it is the platform's billing currency, or there is no price.
- **Platform billing settings** — one global row: the invoicing currency, the issuer's identity, the number prefixes, the payment terms and grace period, the payment instructions printed on every invoice, and Souq's **own** jurisdiction-profile selection. **Until the currency and issuer are set, nothing can be invoiced**, and the refusal names its reason.
- **Platform invoice** — `Draft → Issued → Settled`, or `Draft → Cancelled`. A draft is editable and has no number. **Issuing allocates the number, freezes the tax snapshot and copies the issuer, the recipient and the payment instructions onto the document**, then closes editing forever. `Settled` means nothing is outstanding, by payment, by credit, or because the total was zero.
- **Credit note** — the only way to correct an issued invoice: a separate aggregate with its own number series, its own lines and its own tax snapshot, carrying a mandatory reason. Issuing one and reducing the invoice's outstanding amount are a single indivisible act.
- **Invoice payment** — a bank transfer, cash or cheque **that already happened**, recorded by a named operator with the date and the bank reference. Append-only. Partial payments are accepted because they occur; overpayment is refused, because a credit balance is a concept nobody has decided.
- **Overdue** — *computed*, never stored: `Issued` already means something is outstanding, so overdue is `Issued && DueAt < now`. A stored flag would be a row that lies until a job runs.
- **Billable event and billing period** — the metering ledger. A period accepts events only while `Open`; a late event is refused rather than moved, because that period's invoice may have been issued. Each event carries an idempotency key Souq mints, so a retried write bills nothing twice.

## Domain model

Every type lives in the `Souq.Domain.Platform` **namespace** — a tenancy rule compares that namespace by equality, so a tidier sub-namespace would fail the build — while ownership is recorded in `ModuleMap.DomainOwners`.

| Type | Kind | Path | Notes |
|---|---|---|---|
| `Plan` | entity (aggregate root) | `src/Souq.Domain/Platform/Plan.cs` | `Code`, `Version`, `Name`, `Status`; frozen after `Publish()` |
| `PlanEntitlement` | entity (child) | same file | One row per granted key; a related table, not a longer string column |
| `PlanLimit` | entity (child) | same file | Name and value; carried, not enforced |
| `PlanStatus` | enum | same file | `Draft`, `Published`, `Retired` |
| `Limit` | value object | `src/Souq.Domain/Platform/Limit.cs` | Name shape **and** membership validated since C2 |
| `LimitNames` | catalogue (static) | `src/Souq.Domain/Platform/LimitNames.cs` | The closed set of countable limits: `catalog.products`, `staff.seats` |
| `TenantUsageCounter` | entity (store-owned) | `src/Souq.Domain/Entities/TenantUsageCounter.cs` | One row per (store, limit); `ITenantOwned`, so the tenant filter guards the bulk update |
| `ITenantQuotaGuard` | port | `src/Souq.Application/Features/Billing/Contracts/ITenantQuotaGuard.cs` | Reserve / release / peek / reconcile — one port, one implementation |
| `Entitlements` | domain service (static) | `src/Souq.Domain/Platform/Entitlements.cs` | `Granted` and `Effective` — the resolution rule, testable without a database |
| `Subscription` | entity | `src/Souq.Domain/Platform/Subscription.cs` | `TenantId` as a plain column; one per store; only a published plan may be assigned |
| `SubscriptionStatus` | enum | same file | `Active`, `Cancelled` — Souq's own vocabulary, never a provider's |
| `EntitlementOverride` | entity | `src/Souq.Domain/Platform/EntitlementOverride.cs` | Bounded expiry, granting account, written reason, revocable |
| `IPlanRepository`, `ISubscriptionRepository`, `IEntitlementOverrideRepository` | ports | `src/Souq.Domain/Interfaces/IBillingRepositories.cs` | Write side |
| `InvalidPlanException`, `InvalidSubscriptionException`, `InvalidEntitlementOverrideException` | exceptions | `src/Souq.Domain/Exceptions/BillingExceptions.cs` | Stable codes, translated to 422 |
| `PlatformBillingSettings` | entity (global, one row) | `src/Souq.Domain/Platform/PlatformBillingSettings.cs` | Currency, issuer, prefixes, terms, payment instructions, tax selection. `CanIssue` is the single gate |
| `PlatformDocumentSequence` | entity (global) | `src/Souq.Domain/Platform/PlatformDocumentSequence.cs` | One row per series; platform-wide, unlike the per-store `OrderNumberSequence` |
| `PlatformInvoice` | entity (aggregate root) | `src/Souq.Domain/Platform/PlatformInvoice.cs` | `TenantId` as a plain column; all amounts computed from lines and payments, none stored beside them |
| `PlatformInvoiceLine`, `PlatformInvoicePayment` | entities (children) | same file | The line carries text, not a reference, so it reads after the plan retires; the payment carries who recorded it |
| `PlatformInvoiceStatus`, `PlatformPaymentMethod` | enums | same file | Souq's own vocabulary — no provider status is ever persisted as domain state |
| `CreditNote`, `CreditNoteLine`, `CreditNoteStatus` | entities / enum | `src/Souq.Domain/Platform/CreditNote.cs` | Separate aggregate, separate series; `ApplyCredit` on the invoice is `internal` so the two acts cannot be separated |
| `BillingPeriod`, `BillingPeriodStatus`, `BillableEvent` | entities / enum | `src/Souq.Domain/Platform/BillingPeriod.cs` | Three period states so a failed close is resumable without reopening |
| `IPlatformBillingSettingsRepository`, `IPlatformInvoiceRepository`, `ICreditNoteRepository`, `IBillingPeriodRepository`, `IBillableEventRepository` | ports | `src/Souq.Domain/Interfaces/IBillingRepositories.cs` | Write side; every store-scoped read takes `tenantId` as an explicit argument |
| `IPlatformDocumentNumbers` | port | `src/Souq.Application/Features/Billing/IPlatformDocumentNumbers.cs` | The next number in a series; callable only inside the issuing transaction |
| `InvalidPlatformBillingSettingsException`, `InvalidPlatformInvoiceException`, `InvalidCreditNoteException`, `InvalidBillingPeriodException` | exceptions | `src/Souq.Domain/Exceptions/BillingExceptions.cs` | Stable codes, translated to 422 |

## Table shapes

Three of the shapes that [ADR-0047](../../11-ADR/0047-commercial-control-plane.md) enforces appear here, and the difference matters for how each is read:

| Table | Shape | Tenant filter | How it is read safely |
|---|---|---|---|
| `Plans`, `PlanEntitlements`, `PlanLimits` | C — platform-global | none needed | ordinary queries |
| `PlatformBillingSettings`, `PlatformDocumentSequences` | C — platform-global | none needed | ordinary queries |
| `Subscriptions`, `EntitlementOverrides` | **B — platform-owned, tenant-keyed** | **none** | an explicit `TenantId` predicate, every time |
| `PlatformInvoices`, `CreditNotes`, `BillingPeriods`, `BillableEvents` | **B — platform-owned, tenant-keyed** | **none** | an explicit `TenantId` predicate, every time |

Shape B for the invoice is a deliberate choice, not an inherited one: **the invoice is not the merchant's data, it is Souq's book about the merchant — and it must stay readable after the store is archived**, which the tenant filter would have prevented. The child tables (lines, payments) carry no `TenantId`: they are read only through their root, and giving them a tenant key would imply they can be queried alone.

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
| `GetPlatformBillingSettingsQuery` / `UpdatePlatformBillingSettingsCommand` | query / command | The invoicing configuration, with `canIssue` and a named `blockingReason`. The currency locks once an invoice exists |
| `ListPlatformInvoicesQuery` / `GetPlatformInvoiceQuery` | query | The ledger across stores, and one document in full with its credit notes |
| `CreatePlatformInvoiceCommand` | command | A draft, optionally with the subscription line taken from the plan's **frozen** price |
| `AddPlatformInvoiceLineCommand` / `RemovePlatformInvoiceLineCommand` / `UpdatePlatformInvoiceNotesCommand` | command | Draft editing; each refused after issue by the aggregate |
| `CancelPlatformInvoiceDraftCommand` | command | Discards a draft. An issued invoice is never cancelled |
| `IssuePlatformInvoiceCommand` | command | Allocates the number, quotes and freezes tax, copies issuer and recipient — all in one transaction |
| `RecordInvoicePaymentCommand` | command | Records a transfer that already happened, attributed to the operator |
| `IssueCreditNoteCommand` | command | Creates and issues the correction, and reduces the invoice, indivisibly |
| `RecordBillableEventCommand` | command | Idempotent by key; opens the calendar month's period if none is open; refuses if that period is closed |
| `ListBillingPeriodsQuery` / `ListBillableEventsQuery` / `CloseBillingPeriodCommand` | query / command | The metering ledger and its two-step close |
| `AddMeteredLinesCommand` | command | Sums a meter's unbilled units from a **closed** period into one line at an operator-supplied unit price, and marks them billed |

### What the merchant sees (`Features/Subscriptions`)

A second feature folder for the same module, and the boundary is **who reads**. `Features/Billing` is a platform area: its requests carry a `TenantId` and an architecture test proves they are served on the platform host only. A merchant reading its own invoices must be served on its *store* host, so those use cases cannot live there — and they carry no tenant id at all, because the store comes from the host like every other store endpoint.

| Use case | Kind | What it does |
|---|---|---|
| `GetMySubscriptionQuery` | query | The plan and its price, what is outstanding, how many invoices are open and overdue, and the current payment instructions |
| `ListMyInvoicesQuery` | query | Issued and settled invoices only — a draft is not a claim, and a cancelled draft never was |
| `GetMyInvoiceQuery` | query | One document, by an allow-list of visible statuses, and only ever within the caller's own store |

## Public contracts

`IBillingQueries` and `IPlatformBillingQueries` are the module's read ports, declared in Application and implemented by the non-public `BillingQueries` and `PlatformBillingQueries` in Infrastructure.

`IPlatformBillingQueries` computes invoice totals **in memory from the aggregate**, not in SQL. That is a decision, not a shortcut: a line total is rounded once through `Money.FromCalculation` to the currency's minor units, and rewriting that arithmetic in SQL would give an invoice two answers — one on the list screen and one on the document — differing by a fils on the first fractional quantity. Filtering, ordering and paging stay in SQL, and only the current page is materialised.

## Dependencies

Billing references `Souq.Domain.Platform` types and the shared `ITenantDirectory` and `ITenantRepository` ports. Since `C5` it also references exactly one other module's contracts: **`Tax`**, through `ITaxCalculator`, declared in the allowed-contracts map.

That arrow runs one way only, and the shape of the port is what keeps it that way. The taxpayer on a subscription invoice is Souq, whose profile selection lives in `PlatformBillingSettings` — which Billing owns. So `QuoteForProfileAsync` takes the selection as an **argument**; had Tax read that row itself, Tax would depend on Billing while Billing consumes Tax, and the graph would have a cycle. The same single arrow already exists as `Shopping → Tax`, for the same reason: whoever computes a total is who asks.

The dependency that runs the other way is the important one: `Platform`'s `CreateTenantHandler` subscribes a new store to the foundation plan, and `TenantDirectory` reads this module's tables when it builds a tenant snapshot.

## Data ownership

`Plans`, `PlanEntitlements`, `PlanLimits`, `Subscriptions`, `EntitlementOverrides` — all created by the `CommercialControlPlane` migration, which also seeds the foundation plan and subscribes every store that existed before it. Rows are never deleted: an override is revoked, not removed, because *who granted what and why* is half of what an override is for.

`PlatformBillingSettings`, `PlatformDocumentSequences`, `PlatformInvoices`, `PlatformInvoiceLines`, `PlatformInvoicePayments`, `CreditNotes`, `CreditNoteLines`, `BillingPeriods`, `BillableEvents` — created by `PlatformInvoicesAndManualCollection` (C5), additive, with `PriceAmount`/`PriceCurrency`/`BillingIntervalMonths` added to `Plans`. **Nothing here is ever deleted**: an issued document and a recorded payment are accounting records, and a mis-entry is corrected by another document rather than by an edit.

**The settings row is not seeded in production.** The demo seeder creates one, taking the currency from the default store rather than from a literal, so development and QA work; a fresh production database starts without it and cannot invoice until the operator enters the currency and issuer. That is the shape owner decision `C-15` takes in a codebase where `WhiteLabelSourceTests` forbids a currency literal in `src/`.

## API

`/api/platform/plans` (the catalogue), `/api/platform/tenants/{tenantId}/…` (entitlements, plan, overrides, metering), `/api/platform/billing/settings` and `/api/platform/invoices/…` — all platform-host-only.

`/api/admin/store/subscription/…` is the merchant's own view, served on the **store** host, carrying no tenant id anywhere. The generated inventory in [Endpoints.md](../../05-API/Endpoints.md) is authoritative.

## Security and permissions

`platform.billing.manage`, granted to the **platform owner only** — not to a platform administrator. Assigning a plan, granting an exception or issuing an invoice is the commercial relationship with a customer, not day-to-day operation of their store; the same reasoning already separates `platform.users.manage` and `platform.settings.manage`. `C5` added endpoints rather than a permission: issuing money against a customer is the same relationship, so inventing a fourth platform permission would have split one concept across two.

The merchant's own view uses `store.settings.manage` — again **no new permission**. It is what `TenantAdmin` holds and `TenantStaff` does not, which is the right line: the subscription and what is owed are the owner's business, not a shop assistant's. A separate finance role is a roles decision, not an endpoint decision, and would be made when a customer asks for one.

Two rules that used to be assertions and are now tested:

- Requests in the platform area may carry a `TenantId`, and the exemption is **earned**: `ModuleAndContractRuleTests.كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده` proves every such request is reachable only through a `[PlatformEndpoint]` route. An endpoint marked `[AvailableOnAllHosts]` is rejected here specifically, because it is also served on a store host.
- Every platform-area request must be `IAuditable`. Both rules now read one shared list (`ModuleMap.PlatformAreaFolders`), so a future folder is covered by registering it once rather than by remembering two tests.

## Tenant behaviour

**The two money paths never meet.** Shopper → store stays in [Payments](../Payments/README.md): tenant-scoped, and every payment row requires an order. Merchant → platform is `PlatformInvoices` and `PlatformInvoicePayments`: platform-scoped, with no order anywhere in the shape and no provider in the loop. Neither reads the other's tables and neither shares a row type. This is not a preference — the existing payment path throws in platform scope and has no row shape for a charge without an order.

**An invoice survives its store.** Shape B and a `Restrict` foreign key mean archiving a store neither hides nor removes what it owes. A merchant who stops trading with a balance outstanding still has a readable document, which is the point of keeping the platform's books outside the tenant filter.

**Entitlement resolution and its staleness.** The effective set is composed once per tenant snapshot in `TenantDirectory` and cached for 60 seconds, the same window as store status. Every write here calls `ITenantDirectory.Invalidate()`, so an assignment, a cancellation, a grant or a revocation takes effect immediately on the serving instance and within a minute elsewhere. The one case with no write behind it is an override reaching its own expiry: nothing invalidates, so it can outlive its expiry by up to the cache window. That is recorded rather than hidden, and cross-instance invalidation is a later phase.

## Events and background work

None. No domain events, no outbox messages, no sweeps. An expiring override needs no job: expiry is evaluated when the snapshot is built, and an overdue invoice needs none either because overdue is computed from the clock rather than stored.

**The first background job this module will need is `C6`'s dunning sweep**, and it is deliberately absent: it takes an irreversible action against a paying customer, so it waits until suspension is real (`C3`, done) and until a lease stops two instances from doing it twice (`C4`).

## External integrations

None, deliberately. Nothing in this module talks to a payment provider, and nothing in it is shaped by one — which is exactly what makes collection work today in a market where the owner's decision (`C-15`) is that bank transfer is the mainstream case. When a provider is chosen (`C-01`), automated card collection becomes a second way to reach `RecordPayment`, not a different invoice.

## Tests

| Level | File | What it proves |
|---|---|---|
| Domain | `tests/Souq.Domain.Tests/PlanAndEntitlementTests.cs` | A published plan is frozen; an unknown entitlement is refused in a plan; the effective set is an intersection and is empty when either input is missing; only a published plan may be subscribed to; an override expires, is attributed and is bounded |
| Application | `tests/Souq.Application.Tests/Billing/BillingHandlerTests.cs` | Assignment and revocation invalidate the directory; a second active override on one key is refused; an override for a switched-off module is refused rather than silently doing nothing |
| Architecture | `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | Only reviewed Infrastructure types handle tenant-keyed platform rows |
| Architecture | `tests/Souq.ArchitectureTests/QuotaRuleTests.cs` | Count-then-insert is confined to a reviewed list; every limit name has a counting rule and vice versa; the port has exactly one implementation. **The first test checks itself before it checks the code** — it asserts the known sites are still *detected*, so the rule cannot go green while measuring nothing, which is the failure ADR-0049 §obligation 4 was written about |
| Integration | `tests/Souq.IntegrationTests/TenantQuotaTests.cs` | A plan with no limit caps nothing; the ceiling refuses with a stable `QuotaExceeded` code and writes nothing; **two concurrent requests for the last remaining seat never both succeed** (five attempts, mutation-checked — reverting the guard to count-then-write produced four products against a limit of three); archiving frees a seat and restoring re-takes it; a counter created for a store that already has a catalogue starts from the real count, not zero; reconciliation returns a drifted counter to the truth; a staff seat is taken by an invitation and freed by disabling |
| Architecture | `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | Platform-area requests are audited, and their `TenantId` exemption is earned |
| Integration | `tests/Souq.IntegrationTests/CommercialControlPlaneTests.cs` | An unresolvable plan grants nothing end to end; plan changes take effect through the real middleware; another store's override answers 404 |
| Domain | `tests/Souq.Domain.Tests/PlatformInvoiceTests.cs` | An issued invoice cannot be edited or cancelled; a number is allocated once; a tax amount its own snapshot does not produce is refused; exclusive adds to the total and inclusive does not; overpayment is refused and partial payment settles progressively; a zero invoice settles at issue; a credit note reduces the invoice indivisibly, takes no number when it is refused, and cannot be applied to a different invoice |
| Domain | `tests/Souq.Domain.Tests/BillingPeriodTests.cs` | A period closes in two steps and cannot jump; a closed period accepts no event; a unit cannot be billed twice; settings fail closed without currency and issuer; a published plan's price is frozen; a zero price is not an absent price |
| Application | `tests/Souq.Application.Tests/Tax/TaxCalculatorTests.cs` | The platform path reads no store settings, passes the same four gates, and returns the same number as the store path — so an invoice and a basket cannot disagree |
| Integration | `tests/Souq.IntegrationTests/PlatformInvoicingTests.cs` | The whole journey on real SQL Server: fail-closed without configuration, draft → issue → merchant reads it → partial payment → settled, an issued invoice refusing every edit, credit notes with their own series, numbers unique and consecutive **across stores**, the currency locking after the first issue, metering that is idempotent and cannot bill twice, and tax that is not charged without professional verification and is frozen against a later rule version |
| Integration | `tests/Souq.IntegrationTests/TenantIsolationTests.cs` | A merchant asking for another store's invoice by its real id gets **404, not 403** — the table has no tenant filter, so this is the test that proves the hand-written predicate is actually there |

## Failure modes

| If this happens | What the system does |
|---|---|
| A store has no subscription | It has no optional modules at all. Fail-closed is the designed behaviour, not a bug |
| A plan names an entitlement the code no longer has | The key is dropped from the effective set and a warning is logged with the store id. The store keeps working |
| A published plan is edited | The aggregate throws; the API answers 422 `InvalidPlan` |
| A draft plan is assigned to a store | Refused with 422 `InvalidSubscription` |
| An override is granted for a module the platform has switched off | Refused with 409 `ModuleSwitchedOff`, because granting it would have done nothing while reporting success |
| Two overrides for one key | The second is refused with 409 `OverrideAlreadyActive`, so "when does this end?" keeps one answer |
| A store reaches a plan limit | The creation is refused with 409 `QuotaExceeded`, naming the limit. The refusal is an abstention, not a rollback: nothing was written |
| A plan does not name a limit | The store is **uncapped** for it. Not zero — see **Business concepts** and [ADR-0054](../../11-ADR/0054-limit-semantics-and-catalogue.md) |
| A plan carries a limit the code no longer knows | It is dropped with a warning when the snapshot is built, so the store is uncapped for it rather than stopped. Writing such a name is refused at publish time |
| A counter drifts from the truth | `QuotaReconciliationService` recounts per store every `Billing:QuotaReconcileIntervalMinutes` and logs a **warning** with what it corrected — drift means a path is not reporting, so it is never corrected silently |
| `ReserveAsync` is called outside a transaction | It throws. A reservation that cannot roll back with the write it reserved for would leak quota against something that never existed |
| Billing settings are missing, or have no currency or issuer | Drafting and issuing are refused with 422 and a named code (`BillingSettingsMissing`, `BillingCurrencyNotSet`, `BillingIssuerNotSet`). The settings screen shows the same reason |
| The currency is changed after an invoice has been issued | Refused with 422 `BillingCurrencyLocked`. Everything else in the settings stays editable |
| A subscription invoice is requested for a plan with no price | Refused with 422 `PlanNotPriced`. No amount is ever invented |
| An issued invoice is edited or cancelled | The aggregate throws; 422 `InvalidPlatformInvoice`. The correction is a credit note |
| A payment exceeds what is outstanding | Refused with 422. No credit balance is created implicitly |
| A credit note exceeds what is outstanding | Refused with 422 `InvalidCreditNote`, **and no number is taken from the series** — a number drawn for a document that never issued would be a gap |
| A billable event is retried with the same key | The existing event is returned as a success. The unique index is the real guard; the lookup is the courteous one |
| An event arrives for a period that is closed | Refused with 422 `BillingPeriodClosed`. It is not silently moved to the next period, because that period's invoice may already be issued |
| Metered units are loaded from a period still open | Refused with 422 `BillingPeriodNotClosed` — an open period produces an invoice missing whatever arrives a minute later |
| The same meter is loaded onto an invoice twice | The second attempt finds nothing unbilled and is refused with 422 `NoUnbilledUnits` |
| A merchant asks for an invoice belonging to another store | 404, like every other cross-store read here. Drafts answer 404 for their own store too |
| Two invoices are issued concurrently | The sequence row's lock serialises them; the second waits and takes the next number. A rolled-back issue returns its number rather than leaving a gap |

## Common change scenarios

- **Add a new optional capability.** Add the key to `StoreModules`, then add it to a new plan **version**. No existing plan grants it, so no store receives it by accident — which is the property the whole design is built around.
- **Give one store an exception.** Grant an override with a reason and a duration. Do not edit its plan.
- **Change what a tier includes.** Create a new version and assign it. Never edit a published one.
- **Adding a limit (the change guide for this module).** A limit name is an engineering undertaking of **three parts**, and shipping one without the others produces a cap that lies:
  1. A name in `LimitNames`, with what it counts and — just as important — **what frees it**.
  2. A counting rule in `QuotaResources`. The architecture test `كل_اسم_حدّ_له_قاعدة_عدّ` fails the build in both directions if this is missing or orphaned.
  3. `ReserveAsync` on every path that creates the thing (inside the caller's transaction, **before** the write) and `ReleaseAsync` on every path that frees it. Only human review enforces this part — which is why the reconciling sweep warns rather than silently correcting.

  Count only what the merchant can actually empty. Neither products nor staff accounts have a hard delete here, so counting archived products or disabled accounts would make the limit a one-way ratchet with upgrade as the only exit.

## Known limitations

- **No automated collection, and no dunning.** Every payment is entered by a person; nothing chases an overdue invoice, sends a reminder, or suspends a store. `GracePeriodDays` and `DaysOverdueAt` exist as inputs with no consumer — that is `C6`, which also needs `C4`'s locking.
- **No commission, payout or ledger.** With `D-13` = A there is no commission to take, so `C13` has been re-scoped rather than deferred.
- **No PDF and no email.** An invoice is read in the dashboard or through the API. Rendering and delivery are a presentation concern nobody has asked for yet.
- **No tax category per line in practice.** Lines carry a category and every caller passes the default, because assigning categories is the deferred half of [ADR-0055](../../11-ADR/0055-tax-as-a-configurable-capability.md).
- **Nothing emits a meter.** `BillableEvent` is written only through the platform API, so the metering ledger is a mechanism awaiting its first producer — the same position `TaxSnapshot` held for one slice. `Meter` is therefore shape-validated, not a closed catalogue like `LimitNames`.
- **No proration.** Changing plans mid-period does not adjust an invoice; the operator drafts what is right.
- **The number series never resets by year.** Some jurisdictions require it. `Series` is a string so that becomes a value change, but nobody has asked an accountant yet.
- **Only two things are countable.** `catalog.products` and `staff.seats`. Anything else a tier might want to cap — orders per month, storage, API calls — needs the three parts above, and the first two are guarded by a build-time test.
- **No usage is shown to the merchant.** `PeekAsync` exists and reads without taking a lock, but no screen calls it yet, so a merchant meets the ceiling by hitting it. That is the next visible-value change in this module.
- **One subscription row per store, unfiltered unique index.** A cancelled subscription keeps the store's only row, and its previous plan survives only as an audit entry. Billing periods will need a filtered index and a migration.
- **The platform console shows the answer but does not yet let the owner build a tier.** Plans are created through the API, including their price.
- **Cross-instance invalidation is not built.** With more than one API instance, a plan change reaches the others within the cache window.

## Future evolution

A dunning state machine driven by Souq's own invoice state (`C6`), automated card collection behind `IPlatformBilling` once a provider exists (`C-01`), usage-priced tiers once metering has run for a real period and `C-12` is answered, and proration. Each is a separate phase in [CommercialPlatformPlan.md](../../12-ROADMAP/CommercialPlatformPlan.md), and several wait on a decision that is the owner's rather than engineering's.

Quota enforcement is no longer on that list: `C2` built it. Neither are invoices, credit notes, manual collection or the metering ledger: `C5` built them.
