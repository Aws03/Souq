# The commercial platform architecture

> **What this page is.** The durable architecture for turning Souq from a working white-label storefront into a
> production-grade, multi-tenant commercial SaaS that can be sold to many independent stores. It covers
> provisioning, custom domains, plans and quotas, merchant billing, platform revenue, marketplace payments,
> customization, the analytics and discovery foundation, privacy and scale-out — as **one coherent design**,
> subsystem by subsystem, with the boundary and the port for each.
>
> **What this page is not.** It is not a schedule — [CommercialPlatformPlan.md](CommercialPlatformPlan.md) owns
> the phase order, the dependencies and what is deliberately deferred. It is not a second copy of the audit —
> [CommercialReadiness.md](CommercialReadiness.md) records what exists today and what is missing. It is not a
> decision on the owner's behalf: the plan's **NEXT OWNER DECISIONS** section lists every question that is
> the owner's, with options and consequences.
>
> **Nothing here is built.** No code changed in the work that produced this document. Where a name is written in
> `backticks` it exists in the repository today; where it is written in *italics* it is proposed and does not
> exist.
>
> **Last verified against the code:** 2026-09-20, branch `phase/17-production-hardening`, at `50a8f01`.

---

## 1. The short version

**One deployable, one database, two planes, two money paths, and no forks.**

1. The commercial layer is a **control plane inside the existing monolith**, living in the platform scope that
   already exists. No new service, no second database, no broker, no per-tenant branch. Nothing in this design
   requires an entry in [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) to be reversed except §14
   (per-tenant custom code), which is addressed by *not* reversing it — see §9.
2. **Two money paths stay apart.** Shopper → store keeps the Payments module. Platform → merchant is a separate
   port, separate records, separate idempotency namespace. §5.1 shows this is not a preference: the existing path
   throws in platform scope and has no row shape for a charge without an order.
3. **Entitlements feed the seam that already exists.** `TenantInfo.HasModule` is the single enforced answer to
   "may this store use X?". A plan derives that set; it does not add a second check.
4. **Behavioural events are captured now, analysed later** — because the fields that make attribution possible
   cannot be reconstructed afterwards.
5. **Quotas are never a count-then-write race.** §5.3 replaces the repository's existing pattern rather than
   copying it, because that pattern depends on an isolation-level invariant nothing asserts (TD-68).
6. **Provider-agnostic means flow-agnostic**, not just account-agnostic. The current port assumes a
   Stripe-shaped intent; the market Souq actually sells into is redirect-first. §4.8 is the correction.

---

## 2. The two planes, and where each concept lives

Souq already has exactly two request scopes, decided at the door by `TenantResolutionMiddleware` and carried in
`ITenantContext`:

| Scope | Set by | Tenant-owned reads | What lives here today |
|---|---|---|---|
| `TenantScope.Tenant` | a store host | filtered automatically; **throws** with no tenant | the whole storefront and store admin |
| `TenantScope.Platform` | a platform host | still throws — the filter is not relaxed | `Tenants`, `TenantDomains`, `AuditEntries`, platform accounts |

That is already a control-plane/application-plane split in the sense the industry means; it simply has no
commercial content. **The commercial layer is the platform scope growing a commercial half.**

### 2.1 The three legal table shapes — this is enforced, not advisory

`TenancyRuleTests` (the test named
*كل_كيان_تجاري_ملك_لمتجر_وجداول_المنصّة_وحدها_بلا_متجر*) inspects every concrete `Entity` subclass in
`Souq.Domain` and enforces four rules. Read together they leave exactly three shapes, and every commercial table
must pick one:

| Shape | Namespace | Tenant filter | FK to `Tenants` | How it is read safely | Existing example |
|---|---|---|---|---|---|
| **A — store-owned** | anywhere except `Souq.Domain.Platform` | automatic, throws with no tenant | required, and composite on any FK to another store-owned row | ordinary queries | `Order`, `SearchQueryLog` |
| **B — platform-owned, tenant-keyed** | `Souq.Domain.Platform` | **none** | a plain column by convention | **an explicit `TenantId` predicate, every time** | `TenantDomain` |
| **C — platform-global** | `Souq.Domain.Platform` | none | none | ordinary queries | `Tenant` |

Three consequences that a design written without reading the test would get wrong:

- **The namespace check is by equality, not prefix.** A tidy `Souq.Domain.Platform.Billing` sub-namespace is
  *outside* `Souq.Domain.Platform` as far as the test is concerned, and every type in it would then be required
  to be tenant-owned. Commercial entities go directly in `Souq.Domain.Platform`, or the rule changes by ADR.
- **The nullable-tenant shape is reserved.** `ITenantOrPlatformOwned` is restricted to `Souq.Domain.Identity`.
  A "belongs to a store or to the platform" billing row fails the build today.
- **Shape B has no safety net.** It gets no query filter and no write-guard stamping, so isolation rests entirely
  on the predicate the caller writes. The repository already has the discipline that makes this reviewable —
  `PlatformQueries` is the one class permitted to bypass the filter, and its header states the rule: an explicit
  `TenantId` when the read concerns one store, aggregates only when it spans stores, called only from audited
  platform use cases. **Shape-B repositories must be held to the same rule, and the architecture test that
  guards `IgnoreQueryFilters` should be extended to guard them.** That extension is the single change that makes
  this design safe, and it is small.

**Which shape each new concept takes:**

| Concept | Shape | Why |
|---|---|---|
| *Plan* (the catalogue of tiers) | C | platform-global; not owned by any store |
| *Subscription* | B | belongs to one store, read by the platform across stores for dunning |
| *PlatformInvoice*, *CreditNote* | B | same; and they must be readable after a store is archived |
| *LedgerEntry*, *Commission*, *Payout* | B | the platform's books, per store |
| *DomainChallenge*, *DomainCertificate* | B | attached to `TenantDomain`, which is already shape B |
| *BehaviouralEvent* | **A** | it is the store's own data, read by the store's own dashboards |
| *UsageCounter* (quota) | **A** | see §5.3 — it must sit inside the tenant transaction |

Note the split: **anything the merchant owns is shape A; anything the platform owns about the merchant is shape
B.** That line is also the answer to "who owns the data" in the contract.

### 2.2 The module boundary

The commercial concepts do not belong in `Platform`, which owns the store's identity and presentation. They form
a fourteenth module — *Billing* (working name) — registered the way every other module is:

- an entry in `ModuleMap.FeatureFolders`, and one in `ModuleMap.DomainOwners` **for every new Domain type**;
- an entry in `ModuleAndContractRuleTests.AllowedContracts`, which must not create a cycle;
- a `docs/04-MODULES/Billing/README.md`, because `GeneratedDocsTests` regenerates four inventories from
  reflection and fails on a byte difference.

Two rules that bite immediately:

- `ModuleAndContractRuleTests` requires **every request in the platform area to be `IAuditable`**, and it names
  `Features.Platform` and `Features.Reporting` literally. A new `Features.Billing` folder is **not covered until
  that test is extended** — and nothing will remind you. Extend it in the same change that creates the folder.
- Every type under `Souq.Infrastructure.Persistence.Queries` must be non-public, so a *BillingQueries* is
  `internal` behind an Application-declared port.

---

## 3. What must NOT be assumed

These are the assumptions that look safe and are not. Each was checked.

**About the repository**

1. **Not** that a tenant-scope row and a platform-scope row can be written in one transaction.
   `ITenantScopeRunner` opens a *new DI scope*, therefore a new `AppDbContext`. This kills "write the commission
   ledger row atomically with the order" through that path, and it is the cause of TD-55.
2. **Not** that `TenantInfo.HasModule` fails closed. It is `Modules is null || Modules.Contains(module)` — a
   snapshot built without modules grants **everything**. The database default is also all modules, and
   `StoreModules.Parse` silently drops unknown keys. Entitlements built on this shape would fail open on a paid
   feature. Fixing the default is part of the plan work, not optional.
3. **Not** that the entitlement column has room. `EnabledModules` is `HasMaxLength(200)`.
4. **Not** that architecture tests cover the whole solution. The IL scans (raw SQL, `IgnoreQueryFilters`, bulk
   writes) read **only the `Souq.Infrastructure` assembly**. `Souq.Application` is safe by construction (it has
   no EF reference); `Souq.API` is a genuine gap.
5. **Not** that a platform endpoint requires a `platform.*` permission. The rule only covers
   `[PlatformEndpoint]`; an endpoint marked `[AvailableOnAllHosts]` is served on the platform host and is exempt.
   A billing surface marked that way would be reachable with no platform permission and the build would stay
   green.
6. **Not** that `Money` can carry a negative or a rate. The constructor throws below zero and there is no
   `Multiply(decimal)` and no `Divide`. A ledger needs an explicit direction (as `StockMovement` already does),
   and a commission must go through `Money.FromCalculation`, which is the single sanctioned rounding point.
7. **Not** that there is one minor-unit table. `CurrencyInfo` and `StripeAmountConverter` already diverge —
   the converter lists MGA and omits ISK/UGX, so a valid `Money` of 12.34 MGA is sent as 12. A second money path
   inherits both tables unless they are reconciled.
8. **Not** that `VerifiedAt` gates anything. Nothing in resolution, gating or authorization reads it — but it
   *is* read for display, and the platform console already shows a `domainVerified` readiness step. The work is
   to make an existing displayed flag load-bearing, not to invent a UI.
9. **Not** that `MarkVerified` can be undone. It is `VerifiedAt ??= utcNow` — one-way, never re-stamped. A
   domain whose DNS is later removed stays "verified" forever.
10. **Not** that archiving a store releases its domain. `TenantDomains.Host` is unique platform-wide and
    `Archive()` tears nothing down, so an archived merchant reserves its domain forever.
11. **Not** that a background sweep sees every store. `ListForBackgroundSweepsAsync` returns Active **and
    Suspended** only — it excludes Provisioning and Archived. An invoicing or trial-expiry run built on
    `StoreSweepService` would silently skip exactly the tenants a trial concerns.
12. **Not** that the last-administrator test proves the guard. `LastAdministratorConcurrencyTests` seeds three
    administrators and disables two, so the in-transaction re-count (`== 0`) can never fire; the assertion passes
    by arithmetic and would stay green with the whole guarded block deleted. **Do not copy its shape for a quota
    test.**
13. **Not** that a deadlock at commit is translated. `InTransactionAsync` wraps only `work()`; the
    `CommitAsync` call sits outside the try, so a commit-time 1205 crosses the layer boundary as a 500.
14. **Not** that a refused, rolled-back action leaves an audit trace. The staged `AuditEntry` commits with the
    handler's first save and is removed by the rollback, and `Discard()` only detaches rows still in `Added`.
    A billing refusal that rolls back is invisible — which is precisely the record a dispute needs.
15. **Not** that one store can hold two provider accounts. `StorePaymentAccounts` has a **unique index on
    `TenantId`**, and its column list is pinned by a test.
16. **Not** that theme presets do anything. `data-preset` is written on every boot and read by no stylesheet.

**About the outside world** (each of these forces a decision listed in the plan's **NEXT OWNER DECISIONS**)

17. **Not** that Stripe is available. Stripe does not operate in Jordan; the UAE is its only MENA country, and
    a Jordan-registered connected account is restricted to a *recipient* agreement with the `transfers`
    capability alone — it **cannot process payments** and cannot request the card capability at all. A
    Jordan-first product cannot use Stripe Connect as its reference model.
18. **Not** that a provider can split a payment. Of the providers checked, only MyFatoorah was verified to
    combine Jordan, JOD, transaction-time splitting and a native fixed+percentage commission. Most MENA PSPs
    cannot split at all, and several "split" products are really later payout batches.
19. **Not** that the payment flow is intent/confirm/capture. Redirect-first hosted pages are the regional norm.
20. **Not** that an MoR-as-a-service vendor can take this over. Paddle, Polar, Stripe Managed Payments and
    FastSpring each exclude **physical goods**, and each explicitly forbids a platform reselling for third-party
    sellers. That door is closed for Souq.
21. **Not** that three-decimal money is freely chargeable. Amazon Payment Services documents that VISA requires
    three-decimal amounts to end in zero — a **10-fils minimum increment** that reaches back into pricing, not a
    serialization detail.
22. **Not** that webhook verification is always a signature check. HyperPay *decrypts* the body (AES-256-GCM,
    IV and tag in headers). A `VerifySignature(payload, signature)` port cannot express it.
23. **Not** that a store's own invoice can be issued abroad. Jordan's e-invoicing is a **clearance** model — the
    invoice is validated by the tax authority before it is legally issued — so a foreign MoR cannot plausibly
    issue a Jordanian domestic invoice on a store's behalf.
24. **Not** that a return redirect means success. One provider states outright that its return URL only means
    the flow was entered and exited; another distinguishes a callback sent regardless from a return that depends
    on the shopper staying on the page. The browser return is the least trustworthy of the three ingress paths.
25. **Not** that a currency's exponent is the same for charges and for payouts, or that "processable" implies
    "settleable" — one provider documents JOD as a three-decimal *processing* currency while listing no MENA
    payout currency but the UAE dirham.
26. **Not** that supported currencies are a property of the adapter. Two regional providers state that a
    merchant's available currencies follow that merchant's own profile and acquiring relationships, so it is
    **per-tenant configuration**.
27. **Not** that the provider guarantees idempotency. One regional provider's own documentation shows a repeated
    key returning the *previous* response for a different amount, because it does not fingerprint the payload.
    Most document no mechanism at all.
28. **Not** that webhooks are signed, retried or ordered. One provider sends exactly one unsigned delivery per
    event with no retries — which is why an authoritative status query and a scheduled reconciler are part of
    the design from day one rather than a later optimisation.
29. **Not** that onboarding, once complete, stays complete. Consent is revocable, OAuth tokens expire and can be
    revoked by the merchant, and capabilities flip on periodic review. *Revoked* is a distinct state from
    *Rejected*.
30. **Not** that a refund fails only when it exceeds the original. Refunds fail for reasons unrelated to the
    original payment — an insufficient platform refund balance, or a reversal against an emptied merchant
    balance — and money movement is not symmetrically reversible: after winning a dispute, cross-border rules
    may leave no way to repay the merchant.

---

## 4. The subsystem catalogue

Each entry states: **responsibility · boundary · domain concepts · ports · persistence · tenant/security ·
concurrency · now · later**.

### 4.1 Merchant provisioning and lifecycle

- **Responsibility.** Take a store from "sold" to "serving", resumably, and back out again cleanly.
- **Boundary.** `Platform` (the aggregate) + *Billing* (the commercial half).
- **Domain concepts.** *ProvisioningWorkflow* with a recorded step outcome; the lifecycle states. Souq has
  `Provisioning`, `Active`, `Suspended`, `Archived`; the design adds *Trial* and separates *Offboarding* from
  *Archived*, because "temporarily off" and "leaving" are different decisions with different data consequences.
- **Ports.** *IProvisioningSteps* — each step an idempotent command keyed by a stable id, with an explicit
  failure policy (retry / continue / abandon-and-alert).
- **Persistence.** Shape B, one row per workflow with a step log.
- **Tenant/security.** Platform scope; every step audited. `Provisioning` already opens the entire permissioned
  admin surface while keeping the storefront closed — that is the onboarding seam, already built.
- **Concurrency.** Atomicity is not available across steps that call DNS, a certificate authority or a payment
  provider; **resumability** is. Record progress, commit the dedup marker with the side effect, and use an
  explicit in-progress state for steps that cannot join a transaction.
- **Now.** The state machine, the step log, the *Trial* state, and fixing the sweep list so provisioning and
  archived stores are reachable by the jobs that must see them.
- **Later.** Self-service signup. It is only worth building once there is a plan to sign up *to* and a way to
  charge for it.

### 4.2 Custom domains, DNS verification and TLS

- **Responsibility.** Turn "the merchant owns this host" and "we can serve TLS for it" into two facts the system
  checks, keeps checking, and can explain to the merchant.
- **Boundary.** `Platform`.
- **Domain concepts.** Two state machines on `TenantDomain`, deliberately separate, because all four
  combinations occur in production: verified with no certificate (CAA blocks issuance); verified and certified
  but no longer pointed at us; certificate valid while ownership lapsed; neither.
  *Ownership*: Pending → Verifying → Verified → Failed / Moved / Revoked.
  *Certificate*: None → Pending → Active → Renewing → Failed / Expired.
- **Ports.** Two, and the naming matters. ***IDomainAttachment*** — `Attach(host)` returns a provider reference
  plus **DNS instructions** (record type, name, value, and *why*: routing, ownership, or challenge delegation),
  then a status poll. A port shaped like an ACME client cannot absorb a managed edge where the platform never
  sees a CSR or a private key. ***IDnsProbe*** — resolve TXT/CNAME/A/CAA against authoritative nameservers; this
  one is genuinely provider-agnostic and serves verification, the CAA pre-flight, and merchant-facing
  diagnostics.
- **Persistence.** Shape B beside `TenantDomain`: token and its expiry, the expected DNS target, last-checked,
  next-check, attempt count, last failure code, certificate not-before/not-after, issuer, renewal state.
- **Tenant/security.** The authorization gate — the equivalent of an on-demand-TLS "ask" endpoint — must answer
  yes **only** for a host in a verified state, never merely present. Without that, anyone pointing a CNAME at the
  edge triggers issuance in Souq's name. Host normalization already exists in two deliberate variants:
  `TenantDomain.TryNormalizeHost` validates (use on write) and `RequestHost.Canonical` only unifies (use for any
  host-keyed cache or limit). `IPlatformHosts` already refuses a platform host as a store domain.
- **Concurrency.** Certificate storage needs atomic operations so it can provide locking; with more than one
  instance, local disk is the same failure TD-20 already describes for uploads. Verification and renewal are
  backoff-driven scheduled work with a `NextCheckAt`, **not** outbox messages — the outbox delivers things that
  happened.
- **Now.** The two state machines, the token, the probe, the gate, and making `VerifiedAt` load-bearing.
- **Later.** The edge itself, and wildcards (which require DNS-01 and therefore zone control or delegation).
- **Watch.** Certificate lifetimes are falling and authorization reuse with them, so validation now re-runs on
  essentially every renewal: the merchant-facing promise is "keep this record in place permanently", not "point
  it here once". Every certified merchant domain is published to public Certificate Transparency logs, so the
  customer list is enumerable — a disclosure question for the merchant agreement.

### 4.3 Plans and entitlements

- **Responsibility.** One answer to "what may this store use, and how much of it?".
- **Boundary.** *Billing* owns the plan; `Platform` keeps enforcement, because that is where it already is.
- **Domain concepts.** *Plan* (code, display name, price, interval, the entitlement set it grants, its numeric
  limits) — **versioned**, so an existing subscriber keeps the terms they signed. *Entitlement* (boolean:
  may they?) and *Limit* (numeric: how much?) are different concepts and must not be conflated. An
  *EntitlementOverride* is expiring, attributed and audited, so support can grant an exception without creating a
  second source of truth.
- **Ports.** None new for the check: the effective module set is resolved in `TenantDirectory.Project`, where
  the set is already built, and continues to be asked through `TenantInfo.HasModule`. A plan **feeds** that
  snapshot; it does not sit beside it.
- **Persistence.** *Plan* shape C; *Subscription* shape B. The entitlement set outgrows a 200-character column,
  so it becomes a related table rather than a longer string.
- **Tenant/security.** Server-enforced or it is not an entitlement. `[RequiresModule]` is the template for a
  *[RequiresEntitlement]* attribute, and the 404-not-403 convention comes with it. A tier must never be read from
  a token claim.
- **Concurrency.** `Tenant` carries a `rowversion`, so a plan write inherits optimistic concurrency and can
  surface as a 409 — expected, and the existing retry conventions apply.
- **Now.** The plan, the subscription, the derived module set, **and closing the fail-open default**, which is
  the security half of this item.
- **Later.** Self-service upgrade and downgrade flows.

### 4.4 Quotas — and why the existing pattern must not be copied

This is the single most dangerous piece to get wrong, and the repository's own safe-looking pattern is the wrong
model. §5.3 gives the full analysis and the decision.

- **Responsibility.** Refuse the (N+1)th creation of a countable thing, correctly, under concurrency.
- **Ports.** *ITenantQuotaGuard* — `ReserveAsync(limitName)` — **exactly one** Application-owned port with
  **exactly one** Infrastructure implementation, plus an architecture test in the existing shape forbidding any
  other count-then-insert. The present defect is not that one guard is wrong; it is that nothing asserts the
  invariant.
- **Now.** The guard, the counter, the test, and closing TD-68 with it — TD-68's own text says the same
  invariant would carry any future count-based quota, so building the quota guard *is* how TD-68 gets closed.
- **Later.** Overage billing, which needs metering first.

### 4.5 Metering and billable events

- **Responsibility.** A durable, tenant-keyed record of billable units.
- **Domain concepts.** *BillableEvent* (tenant, meter, quantity, occurred-at, **a deterministic idempotency key
  Souq mints itself**), and *BillingPeriod* with Open → Closing → Closed and the rule that nothing may attach to
  a closed period — a late event rolls into the next one.
- **Persistence.** Shape B, append-only.
- **Why it is not telemetry.** Sampled or dropped telemetry is unfit for billing. This is the one event stream
  in the design that may **not** use the drop-on-full channel of §4.11.
- **Now.** The ledger and the period close. **Later.** Usage-priced tiers.

### 4.6 Platform billing: subscriptions, invoices, dunning

- **Responsibility.** Charge the merchant, issue a compliant document, chase non-payment, and suspend.
- **Domain concepts.** *Subscription* with **Souq's own status enum** — provider status vocabularies disagree,
  and one disagreement is dangerous: in one major provider `canceled` is terminal, in another it means "will
  expire at term end". Never persist a provider status as domain state. *PlatformInvoice* with **Souq's own
  number series**, allocated at issue and immutable afterwards, because an unbroken per-issuer series is a legal
  requirement in many jurisdictions and a provider swap would fork the sequence. *CreditNote* as a separate
  aggregate — corrections never mutate an issued invoice. A **tax snapshot** frozen at issue: country, tax id and
  its validation state, B2B/B2C, reverse-charge applicability, the rate, and the literal legend text.
- **Ports.** *IPlatformBilling* — deliberately small and money-carrying, not a "sync the subscription" port:
  register or replace an instrument on file (returns an opaque handle plus display-safe brand/last4/expiry);
  collect an amount against an issued invoice; report the outcome asynchronously; reverse an amount.
  Everything the providers call product/price/subscription stays ours.
- **Concurrency.** Dunning is a platform-scope sweep; `StoreSweepService` is per-store and single-instance by its
  own declaration, so this job needs the outbox's lease pattern, not that base class.
- **Tenant/security.** Platform scope, audited. **Suspension must be driven by Souq's own invoice state, not by
  a provider webhook** — that keeps the behaviour identical when the provider changes or when a merchant pays by
  bank transfer with no provider in the loop at all, which in this market is a mainstream case, not an edge one.
- **Now.** The invoice, the credit note, the subscription state machine, the dunning policy, and manual/offline
  collection. **Later.** Proration refinements and automated card collection.
- **A dependency the design must not hide.** Automated suspension is only safe once cache invalidation crosses
  instances (§4.14) and once suspension actually revokes sessions and stops email (TD-66, TD-67).

### 4.7 Platform revenue models

All five shapes the brief names — subscription, fixed fee, percentage commission, fixed + percentage, and
per-transaction platform fee — are **pure Domain arithmetic over `Money`**, evaluated against the billable-event
ledger to produce invoice lines. None of them belongs to a provider.

Two rules make them safe:

- **Rates are basis points, amounts are minor units, and rounding happens once** — in
  `Money.FromCalculation`. A percentage applied to a three-decimal currency and then rounded must have a single
  tested rule, and the remainder must have a named owner.
- **A minimum or a cap is platform-computed.** No provider expresses either natively on a percentage, so if the
  pricing page promises one it becomes an extra invoice line type.

The **collection method** — netted from the sale, or invoiced in arrears — is a separate decision from the
pricing shape, and it is the owner's (D-13). The Domain must be able to express the revenue model without
knowing which.

### 4.8 The payment provider abstraction

Today's `IPaymentService` is genuinely **account-agnostic** and genuinely **not flow-agnostic**. It is a
two-step intent flow returning a `ClientSecret` and a `PublishableKey`; `PaymentIntentState` is a rename of one
provider's intent statuses. A redirect-first hosted-page provider has no client secret to return, and a
marketplace split has nowhere to put a destination or a fee. Adding either means changing an Application
interface — the opposite of what the port promises.

The correction, supported by seven independent e-commerce platforms' prior art:

- **The entry verb is not `Confirm`.** It is *StartPayment(attempt)* returning a **discriminated result** the
  core carries without understanding: `Redirect(url)` · `ClientScript(sessionId + widget config)` ·
  `BrowserPost(url + signed fields)` · `CompletedSynchronously(status)` · `DeferredOutOfBand(instructions)` —
  the last covering cash on delivery and bill-payment rails, which in this market are not edge cases.
- **A narrow typed surface plus an explicitly persisted opaque bag.** Every prior-art project carries one;
  none succeeded in typing the provider's payload. Treat the bag as storage, not as a hole in the abstraction.
- ***PaymentAttempt* is a first-class aggregate**, distinct from `Order` and from the provider's reference.
  Redirect-first means the shopper leaves the process, so the attempt must be addressable when they come back,
  when the webhook arrives, and when the reconciler polls — in any order, possibly concurrently.
- **Money totals derive from an append-only event log**, not a mutable status column. That is the only surveyed
  design that survives out-of-order webhooks, partial captures, partial refunds and duplicate deliveries.
- **Webhook ingestion is an inbox, not a handler.** Persist `(provider, eventId)` with a unique index, ack fast,
  process asynchronously. Verification takes **raw bytes and headers** — not `(payload, signature)` — because at
  least one regional provider decrypts rather than verifies.
- **Webhooks cannot be routed by host.** A provider callback carries no store host. The route carries the tenant
  (`/webhooks/payments/{providerKey}/{tenantPublicId}`) or an opaque token, and verification is per-tenant.
- **Capabilities are data, not exceptions.** The core asks the adapter what it supports —
  auth-then-capture, partial refund, stored instruments, transaction-time split, programmatic sub-merchant
  onboarding, supported currencies, **amount granularity** — and refuses to offer what cannot be served.
  Amount granularity is the JOD/VISA 10-fils rule, and it is a pricing constraint, not a formatting one.
- **Refund is its own entity with its own lifecycle and its own provider reference**, because under a split it
  may additionally require a reversal that can fail independently.
- **Idempotency is Souq's**, minted from its own aggregate identity and recorded *before* the outbound call,
  because most regional providers document no idempotency key at all and at least one implements it wrongly.
- **An authoritative status query and a scheduled reconciler are part of the design, not an optimisation.**
  Every provider surveyed exposes "tell me the real state of this reference", and at least one delivers each
  webhook exactly once, unsigned, with no retries — so polling is the only ingestion path with a real guarantee.
  Attempts that never return get an expiry sweep; an authorization that was never captured is a dated liability.

`IPaymentGateway` — the Infrastructure-internal adapter contract — is the right seam and already has two
implementations; TD-52 already specifies the injectable factory that makes it testable. What changes is its
*shape*, and `IPaymentService` above it.

### 4.9 Merchant-owned payment accounts and marketplace payments

- **What exists.** A store may connect its own account; secrets are AES-GCM encrypted with purpose binding; the
  router selects per call; a store without an account is paid into the deployment account. The platform holds
  merchants' raw secret keys — which is itself a custodianship and rotation constraint that no document currently
  states.
- **What blocks marketplace payments, precisely.** The adapter sets no fee, destination or on-behalf-of field;
  `Payment` has no gross/net distinction and its column list is pinned by a test; there is no connected-account
  identifier anywhere; `StorePaymentAccounts` allows one account per store by unique index; refunds cannot
  reverse a fee; and no payout, ledger or settlement entity exists.
- **TD-50 is a prerequisite, not a nicety.** A payment records the *kind* of account, never its identity, so
  replacing a store's keys strands every refund taken by the old one — and a failed refund cannot be retried, so
  the customer's money has no in-app path back. Mandatory store accounts make account replacement routine, which
  turns a latent defect into an operational one.
- **Per-tenant credentials cannot share a schema.** Regional providers need different tuples entirely, and
  several pin the host per country. The adapter declares its configuration schema; the platform stores it
  encrypted.

### 4.10 Commissions, payouts, refunds, chargebacks and reconciliation

- **Responsibility.** Model money movement as explicit financial facts, not as fields on an order.
- **Domain concepts.** *LedgerEntry*, append-only, with an explicit **direction** — `Money` cannot be negative,
  so direction is its own concept exactly as `StockMovement` already does for stock. *Commission* accrued against
  an order. *Payout*. *Chargeback* with a **`LiableParty`**, because liability varies per provider *and* per
  charge type within one provider, and a platform can have both kinds of store at once.
- **Persistence.** Shape B, append-only, with **three provider reference columns per movement** — payment,
  fee/split/transfer, and payout/settlement. Every provider surveyed has all three levels under different names;
  collapsing them into one column makes "why did the bank deposit differ from the sales total?" unanswerable.
- **Concurrency.** A platform-scope ledger row **cannot** be written in the same transaction as the tenant-scope
  order, because `ITenantScopeRunner` opens a new context. So either the commission accrues from an order
  domain event through the outbox (at-least-once, idempotent on the order id), or the accrual row is shape A and
  lives with the order. **This is a real fork and the plan names it as one.**
- **Reconciliation.** A periodic job comparing Souq's ledger against the provider's settlement report. The
  comparison logic is generic; only the fetch and the column mapping are per-provider.
- **Refund policy is a contract term, not a default.** Two booleans must be explicit on every refund: does the
  merchant give the money back, and does the platform give back its commission? Provider defaults for these are
  asymmetric and, if inherited silently, either have the platform funding every refund or keeping commission on
  refunded orders. Partial refunds must be modelled as **proportional**, or rounding drifts — three times faster
  in a three-decimal currency.

### 4.11 The behavioural event foundation

This is the "capture now or lose forever" item, and the existing `ISearchLog` is the right pattern to
generalise — its rationale is written in the code and is correct: `void`, never throws, no `Task` (a signature
returning `Task` invites an `await` on the shopper's path), a bounded channel that **drops** rather than waits,
drops counted and logged, a background writer that batches **inside each store's tenant scope** so the write
guard stamps the right owner, and a singleton channel with a scoped recorder because a scoped service resolved
from the root provider has already broken this codebase once.

What changes, and why it matters:

- **The vocabulary is safe to adopt.** Three independent vendors converged on the same e-commerce event spine
  (search → list impression → list click → item view → cart add/remove → checkout begin/step → purchase →
  refund), so a Souq-native name set maps to any of them.
- **The field that everything depends on is position-in-list plus list identity.** Without the rank a product
  was shown at and the identity of the list it was shown in, no click-through rate, no ranking evaluation and no
  "related products" signal can ever be computed.
- **A search-result-set identifier must be minted at query time and echoed back** on every downstream click,
  add-to-cart and purchase. This cannot be reconstructed by timestamp proximity later. It is the canonical
  example of the whole item.
- **Denormalise at write time.** Price, currency, category, stock status, rank, list id, applied synonyms and
  the ranking version are copied onto the event row, because joining to `Product` at analysis time returns
  today's values, not what the shopper saw.
- **Envelope plus versioned payload**, not one wide table per event type. The envelope is stable forever; the
  payload carries its schema version in the row and consumers read tolerantly.
- **Money on an event follows the Domain**, never a float.
- **The legal position changes, and this is the decision.** `SearchQueryLog` holds no personal data *by
  construction*, which is exactly why its retention could be an engineering decision. A behavioural event needs a
  visitor identifier to be joinable — and the moment that column exists the table becomes personal data and
  retention becomes TD-16's legal question. **Two mitigations belong in the design from day one:** keep the
  identity link in a separate, access-controlled table mapping visitor to customer, so erasure has a cheap path;
  and **roll up before you purge**, keeping identifier-free aggregates indefinitely while raw rows expire.
  A worked warning: the existing `Term` column already stores raw shopper text verbatim for 90 days, and a
  shopper can type an email address into a search box — so "no personal data by construction" is a statement
  about *columns*, not about *content*.
- **Storage.** A single append-only rowstore table keyed `(TenantId, OccurredAtUtc, EventId)` with the minimum
  number of indexes; add a nonclustered columnstore when dashboard scans start to hurt, before considering a
  second datastore.

### 4.12 Search, related products, recommendations

The existing search engine is good and must not be replaced reflexively; ADR-0042 records it was chosen on
measurement. What the design adds is **measurability first**.

- **Related products already has a port, a route and a frontend surface** — `FindRelatedProductsAsync` on
  `ICatalogQueries`. A real recommender replaces its body with zero client change. That is the cheapest honest
  entry point in the whole intelligence track.
- **The baselines, in the order the data allows.** Attribute/content similarity needs **no behavioural data** and
  is therefore the only thing correct on day one for a new tenant. Co-occurrence ("bought together") needs on the
  order of a thousand purchases before it is trustworthy; "viewed together" needs roughly ten thousand product
  views. These thresholds are per tenant, so a **tenant readiness state** should be explicit in the Domain — a
  paid tier promising "personalized recommendations" is unfulfillable for a store with forty orders.
- **Use a rescaled measure, not raw counts.** Raw co-occurrence favours bestsellers; lift favours serendipity;
  Jaccard sits between. That choice *is* the popularity-bias policy, made explicitly rather than learned.
- **The Domain owns a reason, not just a list.** *SameCategory*, *BoughtTogether(support=N)*,
  *ViewedTogether(support=N)*, *Trending*, *MerchandiserPinned*, *PopularFallback* — a closed enum, because each
  reason needs reviewed Arabic copy and an RTL-correct rendering, and because a vendor added later must map into
  it or say Unknown.
- **A slot needs an identity before it needs an algorithm.** Every rendered slot carries a server-generated
  request id with the ordered items actually displayed, or click-through rate is uncomputable.
- **Availability is joined live.** The similarity table is a stale candidate pool by design; publication and
  stock are checked at request time.
- **Never pool behavioural data across tenants** without an explicit owner decision — it is a contract and a
  privacy question before it is an accuracy one.
- **A market-specific trap worth writing down:** if cash on delivery is common and "purchase" is recorded at
  placement rather than at delivery, cancelled COD orders will quietly poison co-occurrence.
- **Search.** Improvements that need no new datastore come first (Arabic light stemming applied identically to
  both sides, the inverted-term table of TD-45 when a measurement demands it). Two real defects to fix while
  here: suggestions match differently from results and ignore merchant synonyms entirely, so a merchant sees
  their synonym work on the results page and not in the dropdown; and typo recovery reads up to ten thousand
  names per no-result search, uncached, on an anonymous endpoint.

### 4.13 Customization without forks

The rule is unchanged and is the product's licence to exist: **customization is configuration; anything else
becomes a product feature for every tenant or is declined.** The design adds capability without adding forks:

1. **Make theme presets real.** The hook, the validation, the delivery and the editor all exist; only the
   stylesheets are missing. This is the cheapest credibility fix available and it is CSS.
2. **Ordered, typed sections.** `Storefront.jsx` composes a fixed JSX list and the section components are
   already prop-driven — they are renderers waiting for a descriptor list. A server-validated registry of
   section types, published through the existing options endpoint, turns layout into data without turning it
   into code.
3. **Safe custom content.** Store-authored pages (TD-42) with a per-language body, bounded and sanitized. The
   owner's scope decision is already written up with two costed options.
4. **Per-store string overrides**, applied as an overlay after boot. Note the deep-merge point that exists today
   runs only on a *language switch*, not on first load, so the overlay needs an explicit call.
5. **What stays refused:** arbitrary CSS, arbitrary scripts, per-tenant branches.

### 4.14 The bounded extension model

For the exceptional customer-specific requirement, the answer is **integration, not execution**:

- **Outbound webhooks** with per-tenant secrets, HMAC signing, retries, replay protection and an idempotency
  key. The outbox already provides the durable half.
- **A scoped API** for the merchant's own systems.
- **No customer code in this process.** Running merchant code costs a sandbox, an egress proxy, fuel metering,
  determinism bans and a separate privilege boundary — that is a platform, not a feature, and it is the argument
  for not building one. [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §14 already names the
  condition that would revisit this ("a paid tier with sandboxed extension points, designed as a product feature
  available to everyone"); this design deliberately does **not** meet it and says so.

### 4.15 Merchant analytics and decision support

- The existing dashboards are the foundation; the gaps are known and specific.
- **Reports are UTC-only** although `TenantInfo.TimeZone` is already projected on every request — so a merchant's
  "today" is wrong by their offset, and fixing it needs no new plumbing.
- **Profit and margin are impossible** because no cost price exists. It belongs on `ProductVariant`, where price
  already lives, and `OrderItem` already carries `VariantId` — so line-level margin is computable the day the
  field exists.
- **Refund-period attribution needs the `Refund` rows**, not `Payment.RefundedAmount`, which is a running total
  with no date. Note `CompletedAt` is stamped on failure too, so a naive switch would count failed refunds as
  revenue reductions.
- **A platform-owner revenue view** reads through `PlatformQueries`. Per-store drill-down with an explicit
  `TenantId` predicate is an existing sanctioned pattern, not a new precedent.
- **The dashboard cannot be parallelised**: fifteen aggregates share one `AppDbContext`, and EF throws on
  concurrent operations. Making it faster means a rollup or a replica, and materialisation is currently an
  *accepted rejection* in ADR-0044 — a rollup table must supersede it with a measurement.

### 4.16 Future ML and AI without vendor coupling

The stable abstraction is **candidate generation → ranking → explanation**, with re-ranking rules as Souq's own
code that is never delegated. A vendor, if one is ever added, is one more candidate source behind
*IRecommendationCandidateSource*. Three rules keep the core clean:

- the raw event store stays the system of record, so a vendor can be removed;
- offline vendor metrics are never the shared yardstick — only Souq's own online measurement is;
- sending events to an external vendor is a **cross-border transfer of personal data**, so the gate belongs in
  the adapter, and some tenants may be configured with no external destination at all.

Note that the existing no-network rule for read ports is a narrow tripwire on six type names, not a boundary —
an external call behind an Application-layer interface passes it. Treat it as documenting intent.

### 4.17 Retention, privacy, isolation and auditability

- **Isolation** is already structural and proven at four layers; nothing in this design weakens it, and §2.1 is
  the rule that keeps new tables inside it.
- **Retention** must be decided per table, and the design's own new tables split cleanly: metering and ledger
  rows are financial records with long statutory lives; behavioural raw rows are bounded and rolled up.
- **Erasure** reaches derived artefacts, not just the event table — the identity-link separation in §4.11 is what
  makes that affordable.
- **Auditability.** Every commercial action is `IAuditable`, with a `billing.`/`plan.`/`payout.` action prefix
  so the existing audit query filters them with no change. Three gaps must close for a commercial platform:
  authentication events are not audited at all; a rolled-back refusal leaves no trace; and `AuditEntries` has no
  retention policy, which a per-page-load audited read will make expensive.
- **Offboarding** is a capability, not a script: a per-store export, then a retention window, then a purge — and
  the domain release described in §3.

### 4.18 Scale-out and multi-instance correctness

The commercial layer is what makes the second instance mandatory, so the inventory must be exact. There are
**three** per-process caches, not the two most documents name — the tenant directory (60 s), the session stamp
cache (30 s), and the rate limiter's in-process windows (so every per-tenant limit multiplies by instance
count). Add local-disk uploads, the single-instance sweeps, and — if ACME is run in-process — certificate
storage.

The ordering that follows from this design: **cross-instance invalidation and a distributed lock must land
before automated dunning**, because a suspension that takes up to a minute to reach another instance is a
commercial exposure, not a latency one.

---

## 5. Three things this design must get right

### 5.1 The two money paths cannot be merged — verified, not asserted

`PaymentGatewayRouter` calls `RequireTenant()`, which throws in platform scope. `Payment` is tenant-owned, so its
filter throws there too. `Payment` cannot exist without an order — a required composite foreign key, a
constructor guard, and a unique index of one payment per order — so a subscription charge has no row shape. Both
idempotency key formats hard-code a store tenant id. And account selection prefers the *store's* account, so
billing a merchant through this port would deposit the platform's own fee into that merchant's account.

Reusable unchanged, and worth reusing: `Money`, the AES-GCM secret protection with purpose binding, the
reserve → call → record discipline with an idempotency key, the operator-driven retry for a call that was never
answered, and `TenantStatus.Suspended` as the state dunning drives.

### 5.2 Entitlements must feed one seam, and that seam currently fails open

`TenantInfo.HasModule` is the single enforced answer, read at two enforcement points. A plan must derive the set
resolved in `TenantDirectory.Project`. But the default is `Modules is null ⇒ everything`, mirrored by a database
default of all modules and a tolerant reader that drops unknown keys. For three optional features that is
defensible; for a paid entitlement it is not. **Closing the fail-open default is part of the entitlement work,
and a test must prove that an unresolvable plan grants nothing.**

### 5.3 A quota is not a count-then-write race — and the repository's safe pattern is not safe enough

The repository's one correct count guard, in `AccountStatusChanger.SetActiveAsync`, **writes first, saves, and
only then re-counts inside the same transaction**, throwing a private exception to roll everything back. The
order is load-bearing: the write takes the exclusive lock first, so the racer's count must wait for it and
therefore sees the final answer. Its own comment records why `SERIALIZABLE` was rejected (range locks then an
exclusive request — a deadlock ending in a server error instead of a clean refusal) and why a named application
lock was rejected (a new concept to solve what the transaction already solves). The deadlock is expected and is
translated into a clean business refusal.

**Three findings change the conclusion for commercial quotas:**

1. **It depends on an invariant nothing asserts.** It is safe only because the default isolation level is
   *locking* read-committed. Under Read Committed Snapshot Isolation the re-count reads a snapshot, never blocks,
   and both racers pass. `READ_COMMITTED_SNAPSHOT` appears exactly once in the whole repository — in that
   comment. Nothing sets it, no migration asserts it, no startup check reads it. And RCSI is **on by default** on
   a managed database this repository's own documentation names as a possible target. The guard fails **silently
   and open**.
2. **Its test does not test it.** `LastAdministratorConcurrencyTests` can never reach the guarded branch.
3. **The existing per-store caps are not models.** `SearchSynonym.MaxPerStore` and
   `WishlistItem.MaxItemsPerCustomer` both count and then insert with no transaction and no re-count. They are
   soft caps where that does not matter; a commercial quota is not. The in-aggregate limits are not analogues
   either — they are protected by an aggregate root's `rowversion`, and a per-tenant quota has no such root.

**The decision: a per-tenant counter row, taken with an explicit lock inside the caller's transaction.**

| | Write-first-then-recount | **Counter row (chosen)** |
|---|---|---|
| What serialises racers | the lock on the row just written | the lock on one per-tenant counter row |
| Depends on isolation level? | **Yes** — RCSI defeats it silently | **No** — an update takes an exclusive lock regardless |
| Deadlock class | by design, translated into a refusal | none; contenders queue on one row in one order |
| Failure mode if wrong | fails **open**, no error, green tests | drift if deletes are not decremented |
| Obligation | none | the counter must be kept truthful |

`OrderNumbers` is the existing proof that the counter shape works here, and also the source of its two costs:
it uses `ExecuteUpdate`, which **bypasses `SaveChanges` and therefore both the write guard and the audit
timestamps**, so a new counter type must be added by name to the reviewed bulk-write allow-list with a written
reason; and it refuses to run outside a transaction. The counter differs from `OrderNumbers` in one way that
matters: order numbers only ever count up and explicitly tolerate gaps, whereas a quota must count **down** on
deletion, archival and erasure — which is the truthfulness obligation, and it needs a reconciling sweep.

A quota guard also adds an explicit-transaction site, which matters to F-14: enabling retry-on-failure later
requires every such site to be wrapped in an execution strategy, and none of the existing ones is.

**And assert the invariant anyway.** TD-68 should close with a startup check that reads the database's own
setting and warns loudly — in the shape `DatabasePrivileges` already uses to report the runtime identity at every
boot — because the *existing* administrator guard still depends on it whether or not quotas do.

---

## 6. Provider-agnostic, and where it stops

**Can be provider-agnostic** (these belong to Souq and survive any swap): `Money` with its exponent table and
one rounding rule; the payment-attempt state machine; the result algebra
(Succeeded / Pending / RequiresAction / Failed / Cancelled); refund as its own entity; idempotency keys minted
from Souq's own aggregates; the webhook inbox and its dedup rule; a coarse onboarding state
(NotStarted / InProgress / RequirementsDue / Active / Restricted / Rejected) with an optional hosted-action URL;
read-only payout visibility; the revenue models and their arithmetic; the financial ledger and the
reconciliation comparison; the subscription, invoice, credit-note and dunning state machines; the domain
lifecycle and its DNS probe; the behavioural event vocabulary; the recommendation reason vocabulary.

**Requires a provider-specific adapter** (and must never reach Domain or Application): charge topology and who
is merchant of record; how a fee is expressed, and in which currency it settles; KYC requirement field names and
document types; capability names and their lifecycles; payout scheduling primitives; negative-balance and
reserve machinery; dispute evidence workflows; reconciliation report formats and the identifier chain; webhook
signing *or decryption*; minor-unit encoding and any divisibility rule; redirect mechanics (GET redirect vs
auto-submitted POST vs hosted iframe vs client SDK); 3-D Secure step-up timing; stored-instrument semantics;
split mechanics where they exist at all; and national e-invoicing clearance.

**The honest boundary:** a port designed to the *union* of these leaks; a port designed to the lowest common
denominator is useless. The line drawn here is: **the core owns the money, the lifecycle and the decision; the
adapter owns the wire.** Anything that changes *who is liable* or *who is the seller* is neither — it is stored
data on the tenant (D-13), because the same platform can legitimately have stores on both sides of it.

---

## 7. Minimum viable commercial foundation

The smallest set that lets Souq sell a store and get paid, with nothing speculative:

1. Plan, subscription and derived entitlements — **with the fail-open default closed**.
2. The quota guard and its counter, closing TD-68 with it.
3. Platform invoices with Souq's own number series, credit notes, and **manual/offline collection** — because a
   merchant paying by bank transfer is a mainstream case here, and because it needs no provider at all.
4. A dunning state machine driven by Souq's own invoice state, ending in `Suspended` — **and the suspension
   fixes it depends on** (revoke sessions, stop email, cross-instance invalidation).
5. Custom-domain ownership verification made load-bearing, with the DNS probe and the two state machines.
6. Real theme presets and store-authored pages — the two cheapest things a merchant actually notices.
7. The behavioural event envelope, captured from day one.

Everything else in §4 is a later enterprise capability: marketplace splits and payouts, reconciliation against
provider reports, usage-priced tiers, recommendations beyond attribute similarity, the extension webhooks, the
platform revenue dashboard, per-store sending domains, and multi-region anything.

---

## 8. Sequenced and deferred

The phase order, the dependency graph and the deferral list live in
[CommercialPlatformPlan.md](CommercialPlatformPlan.md). The one-line summary: **decide the money model, then
build the control plane, then the billing, then the domains, then the intelligence** — and capture behavioural
events before any of it, because that is the only item whose cost rises with every day it is delayed.

---

## 9. Decisions recorded as ADRs

| ADR | What it decides |
|---|---|
| [0047](../11-ADR/0047-commercial-control-plane.md) | Where commercial concepts live: the control plane, the three table shapes, the *Billing* module boundary |
| [0048](../11-ADR/0048-payment-provider-abstraction.md) | The flow-agnostic payment port, the attempt aggregate, the webhook inbox, capability declaration |
| [0049](../11-ADR/0049-tenant-quota-enforcement.md) | The counter-row quota guard, and why the existing pattern is not copied |
| [0050](../11-ADR/0050-behavioural-event-foundation.md) | What is captured now, the envelope, the identity separation, and rolling up before purging |
| [0051](../11-ADR/0051-custom-domain-lifecycle.md) | Two state machines, the attachment port, and the authorization gate |
| [0052](../11-ADR/0052-bounded-extension-model.md) | Integration over execution: webhooks and configuration, no customer code in-process |

---

## 10. Related

[CommercialPlatformPlan.md](CommercialPlatformPlan.md) ·
[CommercialReadiness.md](CommercialReadiness.md) ·
[SouqMasterPlan.md](SouqMasterPlan.md) ·
[ProductRoadmap.md](ProductRoadmap.md) ·
[TechnicalDebt.md](TechnicalDebt.md) ·
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) ·
[ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) ·
[RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) ·
[MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) ·
[ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) ·
[ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) ·
[Platform module](../04-MODULES/Platform/README.md) ·
[Payments module](../04-MODULES/Payments/README.md) ·
[SecurityControls.md](../07-SECURITY/SecurityControls.md) ·
[ADR index](../11-ADR/README.md)
