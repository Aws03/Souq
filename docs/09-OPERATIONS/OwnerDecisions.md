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

## How to read the blocking columns

| Column | Means |
|---|---|
| **Engineering** | Is further engineering work blocked until this is answered? |
| **Deployment** | Can the system be deployed at all without an answer? |
| **First paying customer** | Can the first real customer be served without an answer? |

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

**Evidence that exists.** The code and its test show exactly what is sent today (×100). They say nothing about
what the real account expects, and the code isn't prepared for the other answer: moving to ×1000 is a code
change, not a setting. **Evidence still required:** one test charge on the **real** account in the target
currency, and the amount read back from the Stripe dashboard. Nothing in this repository can produce that
evidence — it needs the account.

**Who decides.** Nobody *decides* this one; it is discovered. The owner (or whoever holds the Stripe account)
runs the test charge and reports what Stripe actually did.

> **Do not record P-05 as resolved on the basis of documentation, a support article, or a sandbox result that
> was not run on the account that will take real money.**

---

## P-06 — Tax

**The question.** Are prices tax-inclusive or tax-exclusive, what rates apply to whom, and what must an invoice
show?

**Why it matters.** The pricing pipeline has an explicit zero where tax belongs. In most jurisdictions selling
without handling tax correctly is a legal bar, not a missing feature — and the liability accrues from the first
sale, not from the first audit.

**Affected code.** The pricing pipeline (`PricingService`), the frozen order totals, the invoice/receipt
content, and the storefront's displayed prices.

| Engineering | Deployment | First paying customer |
|---|---|---|
| **Yes** — the rules must exist before they can be built | No | **Yes**, wherever tax must be shown or collected |

**Evidence that exists.** The zero is deliberate and documented rather than an oversight. **Evidence still
required:** the jurisdictions to be sold into, the rates, the inclusive/exclusive convention and the invoice
requirements. **Engineering must not invent any of these.**

**Who decides.** The owner, with an accountant.

---

## D-13 — Who is the merchant of record

**The question.** Does every store connect its own Stripe account, or does the platform adopt Stripe Connect
and settle on their behalf?

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
| No | No | No — **but yes for the second paying store** |

**Evidence that exists.** `PaymentsAndRefundsTests` exercises both routing paths, so either answer is
implementable. **Evidence still required:** the commercial model and Stripe's own requirements for the chosen
one.

**Who decides.** The owner. This is a business-model decision with legal consequences.

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

## TD-42 — Store-authored legal/informational pages: how much of a capability?

**The question.** A store today has no way to publish a privacy policy, terms, a returns policy, a shipping
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
| **Yes** — the M2 phase of [SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md) is blocked on this one deliverable; nothing else in that phase is | No | **Yes, in any jurisdiction that requires a published privacy policy or terms** — see [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) §11's first-sellable-release list |

**Evidence that exists.** The gap itself: verified in code — no content-page capability of either shape exists
today, and the footer's dead links were removed rather than kept as a placeholder. **Evidence still required:**
none technical; this is a scope call between two valid, fully-specified shapes, not a missing fact.

**Recommendation, not a decision.** (b) first: it closes the legal gap at near-zero engineering risk and does
not foreclose building (a) later as a richer, separately-scoped capability if the owner specifically wants
authored pages (a small CMS) rather than just reachable policies.

**Who decides.** The owner, as a product-scope call — guessing between a small feature and a much larger one
is exactly the kind of business decision `AGENTS.md` §0 rule 4 reserves for the owner.

---

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

## What engineering will not do

To be explicit, because these are the ways this page could quietly stop being true:

- **Not invent a tax rule**, not even a "reasonable default" — a wrong rate is worse than an obvious gap.
- **Not pick a licence**, and not leave MIT in place by inertia and call it a decision.
- **Not mark P-05 verified** from documentation, a support article, or a sandbox that is not the real account.
- **Not choose the payment ownership model** by shipping whichever path is easier to code.
- **Not choose F-8's behaviour** by implementing the one that is simpler to write.
