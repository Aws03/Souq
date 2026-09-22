# ADR-0056: Souq's own invoices — one number series, frozen documents, and collection with no provider in the loop

- **Status:** Accepted 2026-09-22, and **implemented by `C5`** on the same day. It executes the owner's answers to `C-15` (merchant subscriptions are invoiced in JOD) and `D-13` (each store is its own merchant of record, so Souq earns by invoicing subscriptions). Supersedes nothing.
- **Date:** 2026-09-22
- **Related modules:** Billing (this is its second half), Tax, Platform, Identity
- **Related ADRs:** [ADR-0047](0047-commercial-control-plane.md) (the three legal table shapes, and *Plan*/*Subscription*), [ADR-0053](0053-entitlement-resolution.md), [ADR-0054](0054-limit-semantics-and-catalogue.md), [ADR-0055](0055-tax-as-a-configurable-capability.md) (the jurisdiction profile this invoice snapshots), [ADR-0014](0014-money-precision.md) (`Money`, three-decimal currencies, one rounding site), [ADR-0029](0029-orders-lifecycle.md) (the per-store order number series this one is deliberately *unlike*), [ADR-0021](0021-transaction-boundaries.md), [ADR-0031](0031-payments-and-refunds.md) (the shopper's money path, which this one never touches)

## Context

`D-13` = A settles how Souq is paid: **each store is its own merchant of record, Souq never touches shopper funds, and Souq's revenue is a subscription it invoices to the merchant.** That single answer turns merchant billing from an optional convenience into the only way the company is paid — and it forecloses the alternative, because with no commission taken from a shopper's payment there is nothing to net against.

`C-15` = A adds that those invoices are denominated in JOD, and notes that bank transfer is a mainstream case in this market rather than an edge one.

Three facts about the repository shape what this has to look like:

- **`C1` built the control plane with no money in it.** *Plan* carries entitlements and limits and no price; *Subscription* records which plan applies and nothing about being paid. Both were deliberate — [ADR-0047](0047-commercial-control-plane.md) says in as many words that everything with money in it was left as design only.
- **`P-06`'s answer landed the day before.** Tax is a platform-maintained, versioned, verification-gated jurisdiction profile, and an order already freezes a snapshot of it. An invoice is the second place a snapshot has commercial effect, and it must behave identically.
- **No currency literal may appear in `src/`.** `WhiteLabelSourceTests` enforces it, and the rule exists because one deployable serves stores in different currencies. So `C-15`'s answer cannot be a constant in code even though it is a firm commercial decision.

## Problem

1. Where does the invoicing currency live, given that the answer is known but may not be written in code?
2. How is an invoice number allocated so that the series is unbroken, unique, and cannot fork?
3. What makes an issued invoice a document rather than a row — and how is a mistake corrected?
4. How is a bank transfer collected, with no provider, without letting a human's typing corrupt the ledger?
5. How does the invoice stay readable and correct years later, after the plan retires, the store is renamed or archived, and the tax rules change?

## Options considered

### A — `const string BillingCurrency = "JOD"`, and an invoice row with a computed total

**Rejected, and it would not compile.** `WhiteLabelSourceTests` fails the build on a currency literal in `src/`, which is the correct outcome rather than an obstacle: the same platform sold to an operator in another market must enter its own currency, not wait for a release. The deeper problem is that a constant makes "not configured yet" unrepresentable, so the first invoice in a fresh production database would be issued in a currency nobody chose.

### B — Delegate invoicing to a billing provider (Stripe Billing, Paddle, Chargebee)

**Rejected for the launch market, and the shape does not foreclose it.** Stripe does not operate in Jordan, and every merchant-of-record vendor checked excludes physical goods and forbids a platform reselling for third-party sellers — the research recorded under `D-13`. Beyond availability, a provider's invoice number series is the provider's: adopting one and later leaving it forks the series, which is exactly the thing an unbroken per-issuer sequence exists to prevent. `IPlatformBilling` is still described in [CommercialPlatformArchitecture.md](../12-ROADMAP/CommercialPlatformArchitecture.md) §4.6 as the port for automated card collection; nothing here blocks it, and nothing here needs it.

### C — A platform-owned invoice aggregate with its own number series, frozen at issue, corrected by credit note, and collected by a recorded human act

**Chosen.** It is the shape every accounting system converges on for the same reasons, and — like [ADR-0055](0055-tax-as-a-configurable-capability.md) reusing *Plan*'s lifecycle — it reuses shapes this repository has already proved: a counter row incremented atomically inside the caller's transaction ([ADR-0029](0029-orders-lifecycle.md)), a value snapshot frozen where it has commercial effect ([ADR-0055](0055-tax-as-a-configurable-capability.md)), and a platform-owned tenant-keyed table read only through an explicit predicate ([ADR-0047](0047-commercial-control-plane.md)).

## Decision

### 1. The invoicing currency is configuration that fails closed

A single global `PlatformBillingSettings` row (shape C) carries the currency, the issuer's name, address and tax number, the number prefixes, the payment terms and grace period, the payment instructions, and Souq's own jurisdiction-profile selection. **Until the currency and the issuer name are set, no invoice can be created or issued**, and the refusal names its own reason (`BillingCurrencyNotSet`, `BillingIssuerNotSet`, `BillingSettingsMissing`) so a disabled button is never a mystery.

The row is **not seeded in production**. The demo seeder creates one, taking the currency from the default store rather than from a literal, so development and QA are usable; a fresh production database starts empty and the operator enters `C-15`'s answer once. That asymmetry is the decision, not an oversight: a value nobody chose must not be inherited by a deployment.

**Once an invoice has been issued, the currency is locked** (`BillingCurrencyLocked`). A ledger that says one currency while its documents say another is not repairable.

### 2. One number series per document kind, for the whole platform

`PlatformDocumentSequence` is a global (shape C) counter row per series, incremented by a single atomic `UPDATE` inside the issuing transaction — the mechanism `OrderNumbers` uses, with the row lock held to commit so a concurrent issue waits and takes the next number, and a rolled-back transaction returns its number rather than leaving a gap.

Two things are deliberately **unlike** order numbers:

- **The series is platform-wide, not per tenant.** The issuer is Souq, and an unbroken per-issuer sequence is an accounting requirement in many jurisdictions; splitting it across merchants would put gaps in Souq's own series.
- **It starts at 1, not at a flattering number.** Order numbers start at 1001 so a new store's volume is not legible to shoppers. The reader here is a contractual counterparty, not a visitor.

Invoices and credit notes have **separate series**, and the settings refuse identical prefixes: two different documents must never carry the same identifier.

**There is no annual reset.** Yearly renumbering is a requirement in some jurisdictions and not in others, and inventing one is exactly what [ADR-0055](0055-tax-as-a-configurable-capability.md) forbids. `Series` is a string rather than an enum so that a year-scoped series becomes a value change when an accountant asks for one.

### 3. An issued invoice is frozen, and corrections are separate documents

A draft is editable and has no number. **Issuing allocates the number, freezes the tax snapshot, copies the issuer, the recipient and the payment instructions onto the document, and closes editing permanently** — lines, notes and cancellation all refuse afterwards. The copying is the point: the invoice must still be true after the plan retires, the store is renamed and the platform's address changes.

A mistake is corrected by a *CreditNote* — a separate aggregate with its own series, its own lines and **its own tax snapshot**, taken at its own issue date because a correction may fall under a later rule version. Issuing a credit note and reducing the invoice's outstanding amount are one indivisible act: `ApplyCredit` is `internal`, so no path can reduce a receivable without a document explaining it, and no document can exist without reducing one.

**A draft can be cancelled; an issued invoice cannot.** That distinction is the difference between a ledger and a table.

### 4. Collection is a recorded human act

`PlatformInvoicePayment` records an amount, a method (bank transfer, cash, cheque, other), the date received, a bank reference, a note, and **who recorded it** — an attribution stored on the document itself, not only in the audit log, because the document is read alone years later. Payments are append-only; correcting a mis-entry is not an edit.

**Partial payments are accepted** because they happen, and refusing them would push the operator into rounding by hand. **Overpayment is refused** rather than absorbed: a credit balance on an invoice is a concept nobody has decided, and creating one implicitly would be a commercial decision made by accident. An invoice becomes `Settled` when payments and credits leave nothing outstanding — including a zero-total invoice at the moment it is issued, which would otherwise sit open forever with no action able to close it.

### 5. Overdue is computed, never stored

`Issued` means something is outstanding, because the status becomes `Settled` the instant it is not. So "overdue" is `Issued && DueAt < now` — a function of the clock, not a field a sweep must update. Storing it would mean rows that lie until a job runs, and the lie would surface on a merchant's screen before it surfaced in a log. **Dunning, reminders and escalation to suspension are `C6`**, and this phase deliberately delivers their inputs (`GracePeriodDays`, `DaysOverdueAt`) without acting on them.

### 6. Tax is the store's mechanism, with the platform's own selection

`ITaxCalculator` gains `QuoteForProfileAsync`, which takes the profile selection as an **argument**. The store-scoped method now delegates to it after reading store settings, so there is exactly one computation, one rounding site and one verification gate for both paths.

The argument matters for the module graph: the invoice's taxpayer is Souq, whose selection lives in `PlatformBillingSettings`, which Billing owns. Had Tax read that itself, Tax would depend on Billing while Billing consumes Tax. Passing it keeps the single arrow `Billing → Tax`, matching `Shopping → Tax`.

**The verification gate applies unchanged**: a published-but-unverified profile version charges a merchant nothing, exactly as it charges a shopper nothing. There is no privileged path to an unconfirmed number.

### 7. Metering exists as a ledger, and prices nothing by itself

*BillingPeriod* is `Open → Closing → Closed` per store; *BillableEvent* is append-only, carries an idempotency key Souq mints, and is refused if its period no longer accepts events. The three states are not decoration: between "stop accepting" and "the numbers are final" there is work that can fail, and a middle state makes that resumable without the period retroactively accepting events again.

**Nothing in the product emits a meter yet**, and `Meter` is therefore shape-validated rather than a closed catalogue — unlike `LimitNames`, which is closed because the machine must know how to count each name. Loading metered units onto an invoice requires a **closed** period and a unit price supplied by the operator: the machine knows how much was measured and not what it sells for, and `C-12` is still the owner's.

## Consequences

- **Souq can be paid.** The path from a merchant to money in a bank account exists end to end, with no payment provider anywhere in it and none required.
- **Nine additive tables, nothing altered** except two new nullable price columns and an interval on `Plans`. Existing plans take an interval of one month by a hand-corrected migration default, because the generated zero is outside the range the domain accepts.
- **A fresh production deployment cannot invoice until the operator configures it.** That is intended, it is visible on the settings screen with its reason, and it is the only way `C-15`'s answer enters the system without a literal.
- **Two new architecture-test registrations**, both deliberate: five readers of platform tenant-keyed tables added to `ReviewedPlatformKeyedReads`, and the number allocator added to `ReviewedBulkWrites`. Both carry their reasoning at the registration site.
- **`Features/Subscriptions` is a second folder for the Billing module**, because `Features/Billing` is a platform area and may not be served on a store host — and a merchant must read their own invoices from their own dashboard. The boundary is *who reads*: Billing is the platform's ledger about the merchant, Subscriptions is what the merchant sees of itself.
- **What this does not do:** it takes no card, sends no reminder, suspends nobody, and prices no tier. Automated collection needs a provider (`C-01`); dunning is `C6` and additionally needs `C4`'s locking; tiers and their values are `C-12`. Each is named where it would otherwise look like an omission.

## Migration

`20260921233822_PlatformInvoicesAndManualCollection` — additive: `PlatformBillingSettings`, `PlatformDocumentSequences`, `PlatformInvoices`, `PlatformInvoiceLines`, `PlatformInvoicePayments`, `CreditNotes`, `CreditNoteLines`, `BillingPeriods`, `BillableEvents`, plus `PriceAmount`, `PriceCurrency` and `BillingIntervalMonths` on `Plans`. No column is dropped or narrowed and no row is deleted. The interval default was changed from the generated `0` to `1` by hand, and the reason is written in the migration.
