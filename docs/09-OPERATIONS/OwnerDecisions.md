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

**Evidence that exists.** `ProvisioningBoundaryTests` and `frontend/e2e/platform-provisioning.spec.js` prove a
closed store answers `503` to visitors and refuses the platform owner's token.
**Evidence still required:** none. Only the choice.

**Who decides.** The owner, as a security and product call.

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

## What engineering will not do

To be explicit, because these are the ways this page could quietly stop being true:

- **Not invent a tax rule**, not even a "reasonable default" — a wrong rate is worse than an obvious gap.
- **Not pick a licence**, and not leave MIT in place by inertia and call it a decision.
- **Not mark P-05 verified** from documentation, a support article, or a sandbox that is not the real account.
- **Not choose the payment ownership model** by shipping whichever path is easier to code.
- **Not choose F-8's behaviour** by implementing the one that is simpler to write.
