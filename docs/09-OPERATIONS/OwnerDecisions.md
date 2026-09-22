# Decisions only the owner can make

> **What this page is for.** Engineering has taken every one of these as far as it can and then stopped on
> purpose. Each entry states the exact question, what it costs to answer it wrongly, what evidence already
> exists, what evidence is still missing, and precisely what is blocked until it is answered.
>
> **Nothing here is a task engineering forgot.** Four of them are commercial or legal, one needs an external
> account nobody in this repository can reach, and the rest are product choices where two answers are both
> defensible and the difference is felt by customers.
>
> Read with [ReleaseReadiness.md](ReleaseReadiness.md) (what stops a release, triaged) and
> [ProductionReleaseChecklist.md](ProductionReleaseChecklist.md) §18 (the sign-off).
>
> **If you are the owner and not an engineer, start with
> [OwnerDecisionBrief.md](../12-ROADMAP/OwnerDecisionBrief.md)**, which takes the seven decisions that were
> blocking on 2026-09-21 — `C-08`, `C-17`, `TD-42`, `C-15`, `P-06`, `D-13`, `C-01` and `C-19` — and writes each
> one out in plain language with its options, its costs, what is already built, and one precise question. **This
> page stays canonical**: the brief summarises it and links back, and where the two differ this page wins.
> **All eight of those questions were answered on 2026-09-21** — see the next section.

## How to read the blocking columns

| Column | Means |
|---|---|
| **Engineering** | Is further engineering work blocked until this is answered? |
| **Deployment** | Can the system be deployed at all without an answer? |
| **First paying customer** | Can the first real customer be served without an answer? |

---

## Answered by the owner on 2026-09-21

The eight questions of [OwnerDecisionBrief.md](../12-ROADMAP/OwnerDecisionBrief.md) were answered on
2026-09-21. Each answer is recorded **in the decision's own entry below**, in the owner's words, with what it
unblocks and what it costs. This table is an index, not the record.

| id | Answer | What it unblocks |
|---|---|---|
| [`C-08`](#c-08--is-a-visitor-identifier-stored-for-signed-out-shoppers--decided) | **A** — store an opaque visitor identifier for signed-out shoppers | `C9`, then `C10` |
| [`C-17`](#c-17--what-a-suspended-storefront-does--decided) | **B** — admin-only suspension | `C3`, then `C6` — **both built 2026-09-22** |
| [`TD-42`](#td-42--store-authored-legalinformational-pages-how-much-of-a-capability--decided) | **C** — links now; revisit authored pages when a real merchant requires them | `M2`, part of `C8` |
| [`C-19`](#c-19--what-the-merchant-agreement-says-the-platform-operator-can-see--decided) | **A** — disclose the platform's permitted visibility in the merchant agreement | nothing in engineering; the capability already existed |
| [`C-15`](#commercial-platform-decisions--c-01--c-18) | **A** — Souq merchant subscriptions are invoiced in JOD | `C5` |
| [`D-13`](#d-13--who-is-the-merchant-of-record--decided) | **A** — each store is its own merchant of record; Souq does not take shopper funds and earns through merchant subscription billing | `C12`, and it reshapes `C13` |
| [`C-01`](#commercial-platform-decisions--c-01--c-18) | **B** — a redirect-first payment model without transaction-time splitting. **No provider is chosen**; provider onboarding stays an external dependency | the payment port's shape (`C12`) |
| [`P-06`](#p-06--tax--decided-as-a-capability-not-as-a-rate) | **Neither A, B nor C** — tax is a configurable, jurisdiction-aware platform capability, and no tax value is treated as verified until a professional confirms it | the tax *architecture*; no jurisdiction's rules |

**Three answers decide neighbouring questions as a consequence, and are flagged rather than absorbed**, because
engineering did not ask them and the owner may not have meant to answer them:

- **`C-02` (does Souq ever hold shopper funds?) is answered "never" by `D-13` = A's own wording** — "Souq does
  not take shopper funds". It is recorded under `D-13` below and left listed here so it is not mistaken for
  still-open.
- **`C-03`, `C-04` and `C-05` lose their subject at launch.** They ask the commission basis, whether the
  platform refunds its commission, and who absorbs a percentage's rounding remainder. With `D-13` = A and
  `C-01` = B there is **no commission taken from a shopper's payment at all** — Souq's revenue is a
  subscription it invoices. The questions are not answered; they have nothing to apply to until Souq is in a
  funds flow, and they are re-marked accordingly.
- **`C-08` = A does not answer its own three sub-questions.** The lawful basis, the retention period and
  whether rows may leave Jordan are named in option A as the owner's to supply *before the first row is
  written*. They are still required, and the code is built to refuse to write without them.

---

## P-03 — Licence and repository visibility

**The question.** `LICENSE` is MIT and the repository has a public remote. Does it stay that way?

**Why it matters.** MIT permits anyone who receives the code to use, modify and **resell** it. If the plan is to
sell Souq — as a product, a licence, or a hosted service — MIT gives that away. Relicensing is not retroactive:
any copy already taken keeps its MIT rights forever, so the cost of deciding late is unbounded and unrecoverable.

**Affected code.** `LICENSE`, the repository's visibility setting, and the notice in `README.md` (which already
says "under review before the first sale").

| Engineering | Deployment | First paying customer |
|---|---|---|
| No | No | **Yes** — selling under the wrong licence cannot be undone |

**Evidence that exists.** The file, the remote, and the README note. **Evidence still required:** none —
this is a commercial and legal choice, not a finding.

**Who decides.** The owner, with legal advice if the product is to be sold.

---

## P-05 — JOD minor units at Stripe

**The question.** Does the real Stripe account treat JOD as a three-decimal currency (1 JOD = 1000 fils) or
two-decimal?

**Why it matters.** This is the single most expensive thing on the page to get wrong, and it fails silently.
Today the code sends JOD in hundredths. If the real account treats JOD as three-decimal, every charge collects
**a tenth of the price**, with no error anywhere. It is only visible in the settlement report.

**Affected code.**
- `StripeAmountConverter` multiplies every currency Stripe doesn't list as zero-decimal by **100**, JOD included, rounding to the nearest 0.01.
- `Money` accepts three decimals for BHD, IQD, JOD, KWD, LYD, OMR and TND (`CurrencyInfo`), so a price of 59.955 JOD is sent to Stripe as 5996, i.e. 59.96.
- `StripeAmountConverterTests` encodes that two-decimal assumption and nothing else.
- Affected stores: every store whose currency is one of those seven.

| Engineering | Deployment | First paying customer |
|---|---|---|
| **Only if the answer is three-decimal:** a small, deliberate change to the multiplier, its rounding and its test | No | **Yes, if that customer's store prices in a three-decimal currency** |

**Evidence that exists.** The code and its test show exactly what is sent today (×100). **Corrected
2026-09-20:** the sentence that used to follow — that the code "isn't prepared for the other answer" and that
moving to ×1000 "is a code change, not a setting" — is no longer true, and two rows in
[ReleaseReadiness.md](ReleaseReadiness.md) still say it. `StripeAmountConverter` carries an explicit
`HonoursIsoDecimals` switch and a second overload, and `StripeAmountConverterTests` pins **both** hypotheses:
today's ×100, the fact that the flag is false so it cannot be flipped by accident, the ×1000 answer with the
zero-decimal currencies held at ×100, and a test proving the two differ only on three-decimal currencies.
Answering P-05 is a one-value change in front of a green test.

Two further facts for whoever runs the charge. ISO 4217 lists exactly **seven** three-decimal currencies — BHD,
IQD, JOD, KWD, LYD, OMR and TND — which is what `CurrencyInfo` already carries. And the answer may not be
Stripe's to give: Stripe's current public currency documentation no longer contains a three-decimal section at
all, while other providers document the rule explicitly and at least one additionally requires such amounts to
end in a zero — a constraint that reaches back into pricing, tracked as **C-07**.

**Evidence still required:** one test charge on the **real** account in the target currency, and the amount
read back from the provider's dashboard. Nothing in this repository can produce that evidence — it needs the
account.

**Who decides.** Nobody *decides* this one; it is discovered. The owner (or whoever holds the Stripe account)
runs the test charge and reports what Stripe actually did.

> **Do not record P-05 as resolved on the basis of documentation, a support article, or a sandbox result that
> was not run on the account that will take real money.**

---

## P-06 — Tax — DECIDED as a capability, not as a rate

**Decided by the owner on 2026-09-21, and the decision reframes the question.** The question below asked which
of two conventions the platform adopts and which rates apply. The owner's answer is that **neither is a
platform-wide constant**: tax is a configurable, jurisdiction-aware capability, and the values are data that a
professional must verify before they are relied on. Recorded in
[ADR-0055](../11-ADR/0055-tax-as-a-configurable-capability.md).

**The owner's direction, as given:**

> Design tax as a configurable, jurisdiction-aware platform capability. Do **not** hard-code tax law into
> business logic. The architecture should support jurisdiction/tax profiles, configurable tax rates,
> thresholds, inclusive/exclusive taxation, registration/collection rules, effective dates, applicable
> product/order rules where required, store-level tax configuration, immutable tax snapshots on
> orders/invoices, future jurisdiction-specific extensions, and auditability of tax configuration changes.
>
> A jurisdiction profile may contain researched/default values, but those values must be treated as
> configuration requiring verification by the appropriate accountant/tax authority/legal professional before
> commercial use. Do **not** invent or assert tax rates, thresholds, invoice requirements, or legal
> obligations.
>
> The platform should allow: (1) Souq to maintain a reusable jurisdiction profile for a country/region; (2) a
> merchant to select/apply that profile during store setup; (3) a merchant to override permitted store-specific
> settings; (4) a newly onboarded store in the same jurisdiction to reuse the existing verified profile instead
> of re-entering everything; (5) versioning/effective dates so historical orders retain the exact tax rules
> used at the time; (6) explicit indication of whether a profile is verified, unverified, or requires
> professional confirmation.
>
> Keep legal/accounting decisions separate from engineering.

**What this unblocks and what it does not.** It unblocks the tax *architecture* — the profile, its versions,
its effective dates, the store's selection of one, the snapshot frozen onto an order or an invoice, and the
audit of every configuration change. It unblocks **no jurisdiction's rules**: no rate, threshold, invoice
requirement or registration rule ships as a verified value. A profile carries a verification state, and an
unverified profile is visibly unverified everywhere it is used.

**Inclusive versus exclusive is now per profile**, not a platform constant — which is a wider capability than
either option B or option C of the brief would have built, and it is the reason the answer is recorded as a
reframing rather than as a letter.

**What is still the owner's, with an accountant.** Every value, for every jurisdiction Souq sells into: the
rates, who they apply to, the registration thresholds, the invoice wording and content, and whether the store
or the platform is the invoice issuer for the shopper's purchase. Jordan's national e-invoicing **clearance**
model — recorded as research on 2026-09-20, unverified — would make the store the issuer; that remains
unverified and unbuilt.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked for the capability; still blocked for any jurisdiction's values | No | **Yes** — a store selling where tax must be collected needs a *verified* profile, and engineering cannot verify one |

**The original question, kept for the record.** Are prices tax-inclusive or tax-exclusive, what rates apply to
whom, and what must an invoice show?

**Why it matters.** The pricing pipeline has an explicit zero where tax belongs. In most jurisdictions selling
without handling tax correctly is a legal bar, not a missing feature — and the liability accrues from the first
sale, not from the first audit.

**Affected code.** The pricing pipeline (`PricingService`), the frozen order totals, the invoice/receipt
content, and the storefront's displayed prices.

**Evidence that exists.** The zero is deliberate and documented rather than an oversight. **Evidence still
required, and it is not engineering's to produce:** the jurisdictions to be sold into, the rates, the
registration thresholds and the invoice requirements. **Engineering must not invent any of these**, which is
why a profile carries a verification state rather than a default nobody signed.

**Who decides.** The capability was decided by the owner on 2026-09-21 (above). Every value in it is the
owner's, with an accountant.

---

## D-13 — Who is the merchant of record — DECIDED

**Decided by the owner on 2026-09-21: option (a).** In the owner's words:

> **Each store is its own merchant of record; Souq does not take shopper funds and earns through merchant
> subscription billing.**

**What that settles.**

- **The seller is the store.** Every store holds its own payment relationship, its own chargeback liability
  and its own fees. Souq is software.
- **`C-02` is answered "never" by this wording** — Souq is not in the funds flow. Listed separately above so
  it is not mistaken for still-open; if the owner meant to leave it open, this line is the one to correct.
- **Souq's revenue is a subscription it invoices to the merchant**, which makes the billing and dunning
  subsystem (`C5`, `C6`) **mandatory rather than optional** — there is no other way for the platform to be
  paid.
- **Commission netting is foreclosed**, and with it `C-03`, `C-04` and `C-05`, which have no subject until
  Souq is in a funds flow.
- **No licensing conversation with the Central Bank of Jordan follows from this answer**, which was option
  (b)'s cost and is now not incurred. That is a legal conclusion the owner drew, not one engineering verified.

**What it makes mandatory engineering work, and each of these is now a requirement rather than a latent
defect:**

1. **TD-50 must be fixed** — `Payment.Gateway` records the account's *kind* (`stripe:store`), never its
   identity, so a store that replaces its payment account can never refund what the old one took. BR-PAY-04
   requires the account that took the money. Mandatory store accounts make account replacement an ordinary
   event.
2. **Store readiness needs a payment item.** Provisioning reports four (domain, domain verified,
   administrator, status) and checks for no payment account, so a store can be activated and sell with none —
   which under this answer means selling with no merchant of record at all. Whether activation is blocked or
   warned is engineering's to design; that it is reported is not optional.
3. **The deployment-account fallback must stop being reachable in production.** Option (c) — today's live
   behaviour, where a store with no account is paid into the deployment account — is now explicitly *not* the
   model, and it is the position the platform still takes when `SECRETS_KEY` is absent.
4. **`StorePaymentAccounts` needs a concurrency token**; two administrators editing keys silently
   last-write-wins today.

**The original question.** Does every store connect its own Stripe account, or does the platform adopt Stripe
Connect and settle on their behalf?

**The question as written omits the state the product is actually in, and that was corrected on 2026-09-20.**
There are **three** positions, not two, because the live default is neither of the named options:

- **(a) Every store connects its own account.** The merchant is the merchant of record and holds the Stripe
  relationship, the chargebacks and the fees. The platform then has no transaction to take a share of, so it
  must charge the merchant directly — which makes a subscription the only revenue mechanism.
- **(b) Stripe Connect.** The platform onboards merchants as connected accounts and can take
  `application_fee_amount` per transaction. Who is merchant of record then depends on the Connect flavour and
  the charge type, so choosing (b) does not end the question — it replaces it with a narrower one.
- **(c) What is running today, which nobody chose.** A store *may* connect its own account; a store that has
  not is paid into the **deployment account**, making the platform the merchant of record for that store. So
  the merchant of record currently **varies per store**. Verified live on 2026-09-20: neither store on the QA
  stack has a payment account, so the platform is merchant of record for both. `.env.example` states the
  consequence plainly — without `SECRETS_KEY` no store can link an account at all and every store is paid into
  the deployment account.

**(c) is not a neutral "do nothing".** It is an unchosen commercial and legal position that differs per
customer, and the evidence this record already says is missing — *Stripe's own requirements for the chosen
model* — has never been checked against it either.

**D-13 also conflates two questions that are correlated but not the same**, and both need an answer:
1. **Who is legally the seller** (merchant of record, chargeback liability, fees)?
2. **How does the platform collect its own revenue** — a subscription billed to the merchant, or a share of each
   transaction? Option (a) forecloses the second; option (b) permits either.

**What the repository already constrains, verified 2026-09-20.** These do not decide anything; they are the
costs attached to each answer.

- **Option (a) needs TD-50 fixed first.** BR-PAY-04 states that confirming, cancelling or refunding always uses
  the account that took the money. The code does not keep that rule: `Payment.Gateway` records the account's
  *kind* (`stripe:store`), never its identity, so a store that replaces its Stripe account can never refund the
  payments the old one took. Mandatory store accounts make account replacement an ordinary event, which turns a
  latent defect into a routine one.
- **Store readiness has no payment item.** Provisioning reports four: domain, domain verified, administrator,
  status. Nothing checks for a payment account, so a store can be activated and sell with none — taking money
  into the deployment account. Option (a) therefore needs a fifth readiness item, a checkout failure path, and
  a decision about whether activation is blocked or merely warned.
- **BR-PAY-05 is untested** (TD-52): no test names `PaymentGatewayRouter`, because it builds its gateway inline.
  Whichever answer is chosen changes routing, and the routing has no regression net today.
- **`StorePaymentAccounts` has no concurrency token:** two administrators editing keys at once silently
  last-write-wins.
- **Stripe Connect is not a named non-goal.** [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md)
  lists D-13 as undecided rather than rejected, so (b) needs no ADR superseded — only a new one.

**Why it matters.** It decides who holds the customer relationship with Stripe, who carries chargeback
liability, who pays the fees, and who is legally the seller. It is very hard to reverse once stores have been
onboarded under one model.

**Affected code.** `PaymentGatewayRouter` and the per-store payment account (`StorePaymentAccountEditor`,
`Secrets:*`). Both mechanisms already work; the *choice* is what is missing.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked | No | Decided — and the four items above are now required before the **first** paying store, not the second |

**Both named options are narrower than they look — established 2026-09-20 by research into current provider
documentation, and it does not answer the question, only sharpens it.**

- **Option (b) is not available in the home market.** Stripe does not operate in Jordan — the UAE is its only
  MENA country — and a Jordan-registered connected account is restricted to a *recipient* service agreement with
  the `transfers` capability alone: it **cannot process payments** and cannot request the card capability at
  all. "Adopt Stripe Connect" is therefore an expansion option for other markets, not a Jordanian one.
- **Handing the problem to a merchant-of-record vendor is not available either.** Every mainstream one checked
  — Paddle, Polar, Stripe Managed Payments, FastSpring — excludes **physical goods**, and each independently
  forbids a platform reselling on behalf of third-party sellers. Souq is physical-goods e-commerce for many
  stores, so it fails both tests.
- **A third question hides inside option (a).** If Souq never touches shopper funds it cannot net its
  commission from a sale, so platform revenue must be invoiced to the merchant — which makes a complete billing
  and dunning subsystem mandatory rather than optional. If Souq *is* in the funds flow, netting becomes nearly
  free and Souq acquires chargeback liability, KYC obligations and a probable licensing conversation with the
  Central Bank of Jordan. That fork is tracked as **C-02**.
- **Of the providers that do serve Jordan and JOD, only one was verified to support a transaction-time split
  with a native fixed-plus-percentage commission.** Most are redirect-first and several cannot split at all. The
  choice of launch provider is tracked separately as **C-01**, because the first adapter sets the payment port's
  vocabulary.

**Evidence that exists.** `PaymentsAndRefundsTests` exercises both routing paths, so either answer is
implementable, and the provider research above is written up in
[CommercialPlatformArchitecture.md](../12-ROADMAP/CommercialPlatformArchitecture.md) §3 and §6. **Evidence still
required:** the commercial model, the chosen provider's own requirements, and a legal answer on whether
receiving and remitting shopper funds is a licensable activity in Jordan. **The commercial model was supplied
on 2026-09-21 (above).** The licensing question is not reached under that answer, because Souq never receives
shopper funds. The chosen provider's own requirements remain outstanding and are `C-01`'s.

**Who decides.** Decided by the owner on 2026-09-21 (above). It was a business-model decision with legal
consequences.

---

## R-03 — May a refund be issued with `orders.manage` alone?

**The question.** Cancelling a paid order refunds it in full. `TenantStaff` holds `orders.manage` but not
`store.payments.manage`, so a daily operator can move money out. Should refunding require the payments
permission?

**Why it matters.** Tightening it is safer but changes who can do the job: if operators cannot refund, every
refund waits for someone who can, which may be unacceptable for a small store where one person does everything.

**Affected code.** `UpdateOrderStatusHandler` (the refund runs after the cancellation commits) and the
permission matrix.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No | No | No — it is a control question, not a correctness one |

**Evidence that exists.** `UpdateOrderStatusHandlerTests` and `AuthorizationMatrixTests` cover the current
behaviour. **Evidence still required:** how the owner wants the shop floor to work.

**Who decides.** The owner. Engineering can implement either in a small change.

---

## F-8 — What should a duplicate checkout return?

**The question.** When a shopper submits checkout twice, should the second submission **replay** the original
order (`200`) or be **rejected** (`409`)?

**Why it matters.** Replay is invisible to the shopper and makes a retrying mobile client harmless. Rejection
is explicit and surfaces client bugs instead of hiding them, but shows an error for something that actually
worked. Both are defensible; the difference is felt by customers.

**Affected code.** `CreateOrderHandler` and the checkout page. The full implementation design — key ownership,
scope, persistence, expiry, replay, concurrency, failure handling, payment and order interaction, tenant
isolation — is written and ready in [the Ordering module's document](../04-MODULES/Ordering/README.md).

| Engineering | Deployment | First paying customer |
|---|---|---|
| **Yes** — the design is complete but the behaviour is not chosen | No | No — measured: no double charge, and the duplicate order expires |

**Evidence that exists.** `CheckoutIdempotencyTests` measures exactly what happens today: two orders, stock
reserved twice, **but** separate client secrets so no double charge, and both orders expire.
**Evidence still required:** none. Only the choice.

**Who decides.** The owner, as a customer-experience call.

---

## D-22 — Who may preview a closed store, and how?

**The question.** Phase 18 asks for a storefront preview using a preview token. Five choices decide what that
token is:
- who may start a preview;
- which store states it opens;
- whether it is read-only;
- how long it lives and whether it can be revoked;
- how the credential travels from the platform host to the store's host.

**Why it matters.** A preview token is a key that lets requests past the store-status gate. Answered loosely, it
can do three kinds of harm:
- show a store suspended for billing or abuse to people it was closed to;
- let a closed store collect customers or orders;
- leave a credential in URLs, logs or browser storage.

Answered too tightly, it gives the owner nothing better than the settings editor's in-frame preview.

**Affected code.** `TenantAvailabilityMiddleware` (the gate), `AccessTokenValidation` (tokens are bound to
their host, so the owner's session cannot simply be reused), and the platform store page. The options, a
recommendation for each, and the tests the feature must ship with are in
[StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md).

| Engineering | Deployment | First paying customer |
|---|---|---|
| **Yes** — the preview is not built until this is answered | No | No — provisioning, handover and the readiness checklist work without it |

**Evidence that exists** (citations corrected in M11 — the two halves live in different places, and this line
credited `ProvisioningBoundaryTests` with a `503` assertion it does not contain):
- *a closed store answers `503` to visitors* — `TenantResolutionTests`, `PlatformAdministrationTests`, and
  `frontend/e2e/platform-provisioning.spec.js`;
- *it refuses the platform owner's token* — `ProvisioningBoundaryTests` (which activates the store first,
  precisely so the refusal is observable rather than masked by the `503`) and the same e2e spec.

M11 also re-verified that the two dependencies named above are unchanged since this question was written, so
the implementation sketched in [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md) is still
current — with three qualifications about "one change to `IsOpen`" now written into that document.
**Evidence still required:** none. Only the choice.

**Who decides.** The owner, as a security and product call.

---

## P-08 — Product variants: how they differ, how they are priced on screen, how they are chosen — DECIDED

**Decided by the owner on 2026-09-17** and recorded in [ADR-0039](../11-ADR/0039-product-variants-order-identity.md):
- **(a) How variants differ:** structured named options, with at most 3 options per product, 20 values per option and 100 variants per product.
- **(b) The price a multi-variant product shows in lists:** "From" the lowest price among variants that can currently be purchased.
- **(c) Choosing a variant:** an explicit choice is required, and sold-out options stay visible but disabled.

**What happened next.** The groundwork (V1) is built: order lines record the variant, basket and checkout carry it, and stock is administered per variant. The option model and merchant admin (V2) are built ([ADR-0040](../11-ADR/0040-product-option-model.md)): merchants define options and manage variants, and until V3 a product with more than one active variant is not shown in the storefront. The storefront selection (V3) is unblocked apart from the two confirmations below. The status and remaining plan are in [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md).

**The two presentation questions — answered in V3** (2026-09-17, [ADR-0041](../11-ADR/0041-storefront-variant-selection.md)). They were resolved from the repository's own lifecycle rules, not by a new product call, and either can be reversed by the owner with one condition in the read model:
- **deactivated variants are hidden from shoppers** (a deactivated variant is a withdrawal from sale, like unpublishing a product, which disappears from the storefront), while **sold-out active variants stay visible and disabled** as P-08c requires;
- **a product whose variants are all sold out stays listed as unavailable**, exactly as an out-of-stock simple product does today.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked; V1, V2 and V3 are built | No | No — a catalogue with sizes and colours now works end to end |

---

## P-07 — What is a platform-wide setting?

**The question.** `platform.settings.manage` is granted to the platform owner, but no endpoint requires it and
no platform-wide setting exists. Which settings should exist? Candidates include the platform's name as shown in
the console and in platform emails (a constant today), defaults for a new store, and a support contact.

**Why it matters.** Engineering could build a settings screen, but without a decided setting it would either
edit nothing or invent product behaviour.

| Engineering | Deployment | First paying customer |
|---|---|---|
| **Yes** — for a platform settings screen only | No | No |

**Who decides.** The owner, as a product call.

---

## TD-42 — Store-authored legal/informational pages: how much of a capability? — DECIDED

**Decided by the owner on 2026-09-21: option C.** In the owner's words:

> **Links now; revisit authored pages when a real merchant requires them.**

**What that means in build terms.** Option (b) ships now — optional policy-address fields on the store's
existing settings, with the storefront footer showing a link only where a URL is set. Option (a) — a
*ContentPage* aggregate, an authored per-language body, a public page route — is **not cancelled**; it is
deferred until a named merchant asks for it, and the trigger is that request rather than a date. Recorded that
way in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) so the deferral keeps its reason.

**What it unblocks.** `M2` of [SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md), whose only blocked
deliverable this was, and the policy-page half of `C8`. It matches the recommendation this page already
carried, which is recorded below unchanged so the answer is not read as having been led.

**The original question.** A store today has no way to publish a privacy policy, terms, a returns policy, a shipping
policy or an FAQ — the storefront footer used to link to all five with `href="#"`, and Phase 16 removed the
dead links rather than keep the appearance. Two shapes would close that gap, and they differ by an order of
magnitude in what gets built:

- **(a) Authored pages.** A new *ContentPage* aggregate owned by Catalog (or wherever the eventual design
  places it), a migration, admin CRUD with a per-language rich(-ish) body, and a public `/pages/:slug` route —
  a small CMS built into Souq.
- **(b) Policy links.** A handful of URL fields on the store's existing settings (`StoreSettings`) — privacy,
  terms, returns, shipping, FAQ — each optional, each linking out to a page the merchant hosts elsewhere
  (their own site, a document host, a generic policy generator). The footer shows a link only when a URL is
  set. No new aggregate, no migration beyond a few nullable columns, no admin editor beyond a few text fields
  already-existing settings screens can hold.

**Why it matters.** Both answers close the real gap — a store selling in most jurisdictions needs a reachable
privacy policy and terms, and today has neither. They do not close it equally: (a) gives every store rich,
versioned, per-language content it authors inside Souq, at the cost of a genuine new capability (an aggregate,
a table, an editor, a public route, an ADR) that then needs its own lifecycle decisions (can a page be
unpublished? does it need approval? is there a length limit?) most of which nothing in the repository has
opinions about yet. (b) closes the legal gap immediately, with almost no new surface area, but gives a store
nothing more than a link — the actual policy text lives and is maintained outside Souq entirely, which is a
real product-capability difference a merchant would notice.

**Affected code.** `docs/04-MODULES/Catalog/README.md`, `frontend/src/components/layout/Footer.jsx` (the
component that used to render the five dead links), `StoreSettings` and its admin editor if (b) is chosen; a
new aggregate (*ContentPage*, not built), a migration and an admin screen if (a) is chosen.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked — `M2` of [SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md) may close once the links ship | No | **Yes, in any jurisdiction that requires a published privacy policy or terms** — see [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) §11's first-sellable-release list. A link satisfies it; the page it points at is the merchant's |

**Evidence that exists.** The gap itself: verified in code — no content-page capability of either shape exists
today, and the footer's dead links were removed rather than kept as a placeholder. **Evidence still required:**
none technical; this is a scope call between two valid, fully-specified shapes, not a missing fact.

**Recommendation, not a decision.** (b) first: it closes the legal gap at near-zero engineering risk and does
not foreclose building (a) later as a richer, separately-scoped capability if the owner specifically wants
authored pages (a small CMS) rather than just reachable policies.

**Who decides.** Decided by the owner on 2026-09-21 (above), as a product-scope call — guessing between a
small feature and a much larger one is exactly the kind of business decision `AGENTS.md` §0 rule 4 reserves for
the owner.

---

## C-08 — Is a visitor identifier stored for signed-out shoppers? — DECIDED

**Decided by the owner on 2026-09-21: option A.** In the owner's words:

> **Store an opaque visitor identifier for signed-out shoppers.**

**What that authorises, and exactly what it does not.** It authorises the design written out below and in
[ADR-0050](../11-ADR/0050-behavioural-event-foundation.md): an opaque, server-minted, per-browser identifier
with no meaning outside Souq, written on behavioural event rows, with the identifier→customer map kept in a
separate access-controlled table. It authorises `C9` to be built.

**It does not answer the three sub-questions that option A explicitly reserved to the owner**, and option A's
own wording is the reason they are still listed here:

| Still required | Who answers it | What the code does until it is answered |
|---|---|---|
| The **lawful basis** Souq relies on, and whether consent is collected per store or once per platform | the owner, with legal advice | capture is **off unless configured**; there is no default basis in the code |
| The **retention period**, stated publicly | the owner | no default window ships; an unset window refuses to start the capture, rather than inventing 13 or 14 months from the research |
| Whether rows may **leave Jordan** | the owner, with legal advice | no row is sent to any external processor, and adding one stays a separate explicit change |

**Why it is built fail-closed rather than shipped on.** Option A says the owner supplies those three "before
the first row is written". The only way engineering can honour that literally is to make the absence of an
answer stop the write, so `C9` ships the whole foundation with capture disabled by configuration and a startup
check that says so. Turning it on is a deliberate act with the three answers in hand, not a deployment
accident.

**The original question.** When a shopper who is not signed in browses a store, does Souq store an identifier that links
their actions together — the search, the product they opened, the item they added, the order they eventually
placed? Three sub-questions come with a "yes" and are part of the same decision: on what legal basis, for how
long, and may those rows leave Jordan.

**Why it matters.** This is the one item on the commercial plan whose cost rises every day it is unanswered, and
it is the only one that is not reversible by engineering. Every other capability can be built later at the same
price. This one cannot: a purchase that happened before the identifier existed can never afterwards be
attributed to the search that produced it. Saying "not yet" is therefore not a neutral hold — it is a decision
to permanently lose the data for the period of the delay.

It is equally not a free "yes". Today `SearchQueryLog` holds no personal data **by construction** — no customer
id, no IP, no session, no visitor token — which is exactly why its 90-day retention could be an engineering
decision rather than a legal one. A visitor identifier ends that: the event table becomes personal data under
GDPR and under Jordan's data-protection law, and its retention, its lawful basis and its cross-border movement
all become questions with legal answers rather than engineering ones.

**What is proposed, precisely.** An opaque, server-minted, per-browser identifier with no meaning outside Souq —
not an email, not a device fingerprint, not a third-party advertising id, and not derived from an IP address. It
would be written on the event rows that already need to exist, and **the map from that identifier to a customer
account would live in a separate, access-controlled table from day one**, so that an erasure request has a cheap
path and the event store itself never embeds a customer id. Signed-in and signed-out attribution would be joined
through that one table and nowhere else.

**Affected code.** None yet — this decision gates work that has not started. What exists today and is relevant:
`SearchQueryLog` and its `ISearchLog` write path (`void`, never throws, bounded channel that drops rather than
waits) is the pattern the event store would generalise; `SearchLogRetention` is the existing purge; and an
integration test pins `SearchQueryLog`'s exact property set precisely so that a personal-data column cannot be
added to it quietly. That guard is working as designed and should not be weakened — a behavioural event is a new
table, not a wider version of that one.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked — `C9` may be built. The three sub-answers above gate **switching capture on**, not building it | No | No — a store can be sold and served with no behavioural data at all |

**What a "yes" enables**, and nothing else does: search-to-purchase attribution (which query produced which
sale), funnel analysis (where shoppers abandon), click-through rate on any list or recommendation slot, and every
behavioural recommendation — "bought together", "viewed together", and any ranking evaluated against real
outcomes rather than opinion.

**What a "no" permanently forecloses**, and this is the half that must be written down rather than left open:
those four capabilities, for the whole period the answer is "no", with no way to backfill. Attribute and content
similarity still works — it needs no behavioural data and is the only recommender that is correct on day one for
a new store — so "related products" remains possible. Aggregate counts that need no identifier (how many
searches, how many views of a product) also remain possible. What is lost is every measurement that requires
connecting two actions by the same person.

**The smallest reversible design, if the answer is "yes".** Each of these exists so that a later "stop" costs
little: the identifier is opaque and Souq-minted, so it has no value to anyone else and can be rotated or dropped
without touching the rest of the schema; the identity link is a separate table, so erasure is a delete in one
place rather than a rewrite of an append-only store; identifier-free rollups are computed **before** any purge
and kept indefinitely, so a shortened retention costs history rather than erasing it; the retention window is
configuration, not schema; and no row is sent to any external processor, so no cross-border transfer happens by
default and adding one later is a separate, explicit decision.

**Evidence that exists.** The design is written up in full in
[ADR-0050](../11-ADR/0050-behavioural-event-foundation.md), including why the outbox is the wrong carrier, which
fields cannot be reconstructed later, and the identity-link separation. The plan's own §5 states the retention
norms it found (13 months matches one regulator's tracker lifetime; 14 is the industry norm).

**Evidence still required, and it is not engineering's to produce:** the lawful basis Souq intends to rely on and
whether consent is collected per store or once per platform; whether the data may be processed outside Jordan;
what the merchant agreement says about who owns a store's behavioural data; and the retention period the owner
is willing to state publicly.

**Who decides.** Decided by the owner on 2026-09-21 (above), with the three sub-questions still reserved to the
owner and legal advice. Engineering had taken this as far as it could: the design was complete, the mitigations
chosen, and the cost of each answer written above.

---

## C-17 — what a suspended storefront does — DECIDED

**Decided by the owner on 2026-09-21: option B.** In the owner's words:

> **Admin-only suspension.**

**What that means, precisely**, taken from option B as the brief stated it: the storefront shows the branded
unavailable page; the merchant can still sign in and act; customers can still track orders they already paid
for.

**What it unblocks.** `C3` — suspension that actually suspends — and after that `C6`, the dunning state machine
that is the first place the platform acts irreversibly against a paying customer on its own.

**The three consequences that are engineering work and not further decisions:**

1. **The browser app must stop refusing to mount for a suspended store.** Verified in code on 2026-09-21: the
   frontend refuses to mount for *any* non-`Active` store, so a merchant whose store is suspended cannot reach
   their own admin today. Option B requires that they can, so the mount gate has to distinguish the storefront
   from the admin rather than gate the whole application.
2. **Public order tracking must survive suspension.** It carries no closed-store exemption today, so suspending
   a store currently blinds a customer to an order they have already paid for — which option B forbids.
3. **Purchasing must be refused at the server, not only hidden.** A storefront that is dark in the browser is
   not suspended; the write paths are what must refuse.

**What option B does *not* decide, and stays engineering's:** whether sessions are revoked (they are not, under
B — the merchant keeps working), and the wording of the unavailable page, which is copy.

**The defects `C3` fixes under every answer**, so they are not read as part of this decision: suspended and
archived being indistinguishable to a caller, the outbox ignoring store status (TD-67), the background sweeps
not seeing `Provisioning` and `Archived` stores, `Suspend()` refusing to work from `Provisioning`, and one
message shown for three different states.

| Engineering | Deployment | First paying customer |
|---|---|---|
| No longer blocked | No | **Yes** — a platform that bills cannot enforce non-payment without it |

**Who decides.** Decided by the owner on 2026-09-21 (above). It was a product call about what a customer of a
customer sees.

---

## Commercial platform decisions — `C-01` … `C-18`

Designing the commercial SaaS layer surfaced eighteen further questions that are the owner's. They are written
out in full — each with its concrete options and what each one costs — in
[CommercialPlatformPlan.md](../12-ROADMAP/CommercialPlatformPlan.md) §5, which is their canonical home; this
table exists so the register above is not silently incomplete. **None of them blocks `C1`, the first phase,**
which is why the commercial track can start before any of them is answered.

| id | The question | Blocks |
|---|---|---|
| **C-01** | Which payment provider for the launch market? The first adapter sets the port's vocabulary, and Stripe is not available in Jordan | **DECIDED 2026-09-21 as a *model*, not a provider: B — a redirect-first payment model without transaction-time splitting.** No provider is chosen; provider onboarding and contract approval stay an external dependency, and the port must remain provider-agnostic |
| **C-02** | Does Souq ever hold shopper funds? The money-transmission fork, and the thing that decides whether commission can be netted at all | **Answered "never" as a consequence of `D-13` = A**, in that answer's own words. Flagged rather than absorbed — see the index at the top of this page |
| **C-03** | What is the commission basis — goods only, goods plus shipping, after discounts, or gross including tax? | **No subject at launch** under `D-13` = A + `C-01` = B: no commission is taken from a shopper's payment. Not answered; nothing to apply it to |
| **C-04** | Does the platform refund its commission when a store refunds a shopper? Both provider defaults are traps | **No subject at launch**, as `C-03` |
| **C-05** | Who absorbs the rounding remainder on a percentage in a three-decimal currency? | **No subject for commission** at launch, as `C-03`. It still applies to any percentage Souq computes on its **own** invoices, and is answered there by `C5`'s single rounding site |
| **C-06** | Who bears a chargeback, and how is it recovered here, where bank auto-debit is not available? | the ledger |
| **C-07** | Pricing granularity in JOD — accept a 10-fils minimum increment platform-wide, or make the rule conditional per provider and card scheme? | the payment port |
| **C-08** | [Is a visitor identifier stored for signed-out shoppers, on what basis, for how long, and may events leave the country?](#c-08--is-a-visitor-identifier-stored-for-signed-out-shoppers--decided) — **written out in full above** | **DECIDED 2026-09-21: A — yes, an opaque identifier.** The basis, the retention period and the residency answer are still the owner's, and capture stays off until they exist |
| **C-09** | May behavioural data ever be pooled across tenants? | recommendations |
| **C-11** | Custom domains: a managed edge or a self-run certificate client; apex support; the activation SLA; the policy for a domain that stops pointing at us | domain automation |
| **C-12** | What are the plan tiers, and per limit: hard, soft, or overage? | plans and quotas |
| **C-13** | Trials: none, time-limited, or freemium? | plans |
| **C-14** | May support grant a capability outside a plan, and how is it recorded? | entitlements |
| **C-15** | What currency does Souq invoice merchants in, and must it serve merchants with no card on file? | **DECIDED 2026-09-21: A — Souq merchant subscriptions are invoiced in JOD.** Manual/bank-transfer collection is built either way and was never part of the question. Unblocks `C5` |
| **C-16** | Data residency | scale-out and analytics |
| **C-17** | [What does a suspended storefront actually do, and what happens to orders already placed?](#c-17--what-a-suspended-storefront-does--decided) | **DECIDED 2026-09-21: B — admin-only** — written out in full above, because it drives `C3` |
| **C-18** | Customer code: never, webhooks only, or eventually a sandbox? | the extension model |

**What engineering decided without asking**, so it is not mistaken for an open question: where commercial tables
live, the module boundary, the quota mechanism, the payment port's shape, the event envelope, the domain
lifecycle, the ledger's invariants, and which caches need cross-instance invalidation. Those are recorded in
ADRs [0047](../11-ADR/0047-commercial-control-plane.md)–[0052](../11-ADR/0052-bounded-extension-model.md).

## Smaller choices that are also not engineering's

| Decision | The question | Consequence of leaving it |
|---|---|---|
| **Backup schedule and retention** | How often, how many copies, kept how long? ([BackupAndRestore.md](BackupAndRestore.md) §4) | Your exposure equals the interval. Retention is also a legal question where customer data is involved. The job and its health check both exist — `scripts/backup.sh` and `scripts/backup-verify.sh` — so what is missing is the schedule and the alert, not the tooling |
| **The default store** | Adopt, rename or archive the store the Phase 2 migration writes into every database? ([SeedAndBootstrap.md](SeedAndBootstrap.md) §3) | Production serves an Active store named after the demo. The startup log warns on every boot until it is resolved |
| **HSTS scope** | Raise `Security:HstsMaxAgeDays` past 30, add `includeSubDomains` or `preload`? | Longer is safer for you and a longer commitment to a domain you may hand back. Deliberately conservative by default |
| **react-router 7** | Upgrade the router to clear two advisories with no reachable path? | A runtime major upgrade against a measured-unreachable risk — see R-27's neighbour F-23 in [ReleaseReadiness.md](ReleaseReadiness.md) |

---

## Branch protection — the exact setting

Not a judgement call, but it can only be done by someone with repository admin rights, so it is recorded here
rather than left in a commit message.

**GitHub → Settings → Branches → add a ruleset for `main`:** require a pull request before merging, require
status checks to pass, and select all four by the names the workflow declares —
`Build + fast suites`, `Frontend tests + build`, `Integration suite (real SQL Server)`,
`Dependency audit + secret scan`.

Until this is on, CI reports and does not block: a red run can still be merged. The checks themselves were
verified step by step, and the secret scan has already earned its place by catching a literal password in a
tracked script.

**M18 adds a fifth check to select once the release pipeline has run at least once:** `Release gate (build + all
suites)` from [`release.yml`](../../.github/workflows/release.yml). Do not select it before then — a required check
that has never reported blocks every merge.

## GitHub Actions is billing-blocked — nothing has run since before M13

**This is the most consequential open item M18 found, and it is not an engineering one.**

Every workflow run since `35313506879` (M10's closing commit) has failed in about three seconds with the same
annotation on all four jobs:

> The job was not started because recent account payments have failed or your spending limit needs to be
> increased. Please check the 'Billing & plans' section in your settings

The repository is **private**, so Actions minutes are billed. Nothing in the repository can change this.

**What it means in practice:**

- The CI pipeline described everywhere in these docs has not executed for the whole of M11–M18. Every statement
  that "CI runs X" describes a pipeline that is configured to run X, not one observed doing so recently.
- The release pipeline added in M18 has never run at all.
- Branch protection (above) cannot be switched on usefully until runs happen: required checks that never report
  block every merge.

**The owner's options**, in the order they cost:

1. **Resolve the billing failure or raise the spending limit** in GitHub → Settings → Billing & plans. This is the
   direct fix and keeps the repository private.
2. **Make the repository public**, which makes Actions minutes free on standard runners. This is a disclosure
   decision, not a technical one — it publishes all history — and it is therefore the owner's alone. Note the
   repository has been scanned for secrets continuously (`gitleaks`, plus a live-payment-key scan) and that
   history is not rewritten here, so a leak found later could not be erased.
3. **Leave it as is and rely on the local gate.** `scripts/ci-local.sh` runs CI's fast job on Linux in a container
   against exactly what CI would check out. It is a real gate and it found four defects in M18 alone, but it is
   run by whoever remembers to run it, which is precisely the property that makes a control not a control.

**Do not read the last option as equivalent to the first two.** It covers the fast suites only: the integration
suite (Testcontainers inside a container), the frontend job and the supply-chain scans still need the pipeline.


## C-19 — what the merchant agreement says the platform operator can see — DECIDED

**Decided by the owner on 2026-09-21: option A.** In the owner's words:

> **Explicitly disclose the platform's permitted visibility in the merchant agreement.**

**What that covers.** Option A as stated: the operator can see each store's revenue totals and order counts,
its customer list, and the audit log of actions taken in the store, for billing, support and fraud prevention.

**What it changes in the repository: nothing, deliberately.** This was the one item that blocked no engineering
at all — the capability exists, is behind `platform.reports.view`, and is audited with its own action and its
period. The decision means the disclosure is now owed **in the merchant agreement**, which is a contract
drafted outside this repository by whoever writes it. Engineering's part is to keep the capability no wider
than the disclosure: revenue totals and order counts per store, the customer list, and the audit log — and to
treat any widening of platform visibility as a change that revisits this decision.

**Status:** decided. Recorded by C11 (2026-09-21) because the code moved, not because engineering had a view on
the answer.

**What changed.** Until C11 the platform screens showed cross-store *counts* only, and the Reporting module document
said no store's commercial figures reached the platform "by design". C11 reversed that: `GET /api/platform/revenue`
returns each store's revenue for a period, behind `platform.reports.view` and audited. The reversal is not optional —
a platform that bills its merchants cannot compute a commission without knowing what was sold, and `C-03` (the
commission basis) already presumes the figure exists.

**What is the owner's.** Not whether the capability exists, but **what the merchant is told about it**. A merchant
signing up is entitled to know that the platform operator can see their sales totals, and in most jurisdictions that
belongs in the agreement rather than in a support answer later. The same question already applies, and is already
unanswered, to two things that predate C11: the platform can enumerate a store's customer list, and its audit log
records actions taken inside a store.

**Options, as they were put.** (a) State it plainly in the merchant agreement — what the operator can see, and
for what purpose (billing, support, fraud). (b) Narrow the capability to aggregates only, which weakens
per-store billing. (c) Leave it unstated, which was the position then and the one that ages worst. **(a) was
chosen.**

**What engineering did in the meantime.** Kept the read in the single reviewed type allowed to cross the tenant
filter, put it behind a platform-only permission, and audited it with its own action (`platform.revenue.viewed`) and
its period in the record — so whatever the agreement ends up saying, who looked at what is already answerable.


## What engineering will not do

To be explicit, because these are the ways this page could quietly stop being true:

- **Not invent a tax rule**, not even a "reasonable default" — a wrong rate is worse than an obvious gap.
- **Not pick a licence**, and not leave MIT in place by inertia and call it a decision.
- **Not mark P-05 verified** from documentation, a support article, or a sandbox that is not the real account.
- **Not choose the payment ownership model** by shipping whichever path is easier to code. *(Answered by the
  owner on 2026-09-21 — `D-13` = A. The rule stands for every future re-opening of it.)*
- **Not choose F-8's behaviour** by implementing the one that is simpler to write.
- **Not mark a tax profile verified.** `P-06`'s answer is a capability, and a profile's verification state is
  set by the professional who verified it — never by engineering, never by a migration, and never as a default.
- **Not pick a payment provider to close a phase.** `C-01` = B decided the *model*; the provider is a
  commercial and contractual choice, and the adapter stays provider-agnostic until one is contracted.
