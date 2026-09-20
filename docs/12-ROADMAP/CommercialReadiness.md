# Commercial readiness: what it takes to sell this

> **What this page is.** An evidence-based audit of Souq as a **commercial white-label SaaS**, answering four
> questions a buyer and an owner both ask: what works today, what a customer can actually change, what is
> missing before a store can be *sold* rather than *handed over*, and what the smallest honest architecture for
> the missing commercial layer looks like.
>
> **What this page is not.** It is not a roadmap and it does not schedule anything —
> [ProductRoadmap.md](ProductRoadmap.md) owns product capability and [SouqMasterPlan.md](SouqMasterPlan.md) owns
> execution sequence. It is also not a second copy of the registers: what *stops a release* is
> [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md), what *only the owner can answer* is
> [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), and what *we chose to postpone* is
> [TechnicalDebt.md](TechnicalDebt.md). This page links to them rather than restating them.
>
> **Nothing here was built *when this page was written*.** §5 recorded a design so it would not be re-derived
> from scratch later, and §5.0 explains why building it then would have been guessing.
>
> **Its first slice has since been built.** `C1` implemented the plan-and-entitlement half of §5 — the *Billing*
> module, `Plan`, `Subscription` and the entitlement seam — and closed the fail-open default that §5 assumed
> would be fixed on the way in. Nothing with money in it was built: no price, no invoice, no commission, no
> quota enforcement. The audit findings below still stand; treat §5 as the sketch that
> [ADR-0053](../11-ADR/0053-entitlement-resolution.md) finished.
>
> **§5 has since been elaborated into a full architecture.**
> [CommercialPlatformArchitecture.md](CommercialPlatformArchitecture.md) takes the sketch below and works it out
> subsystem by subsystem — including the pieces this page does not cover at all (custom-domain automation, the
> behavioural event foundation, recommendations, the extension model, scale-out) — and
> [CommercialPlatformPlan.md](CommercialPlatformPlan.md) sequences it. **This page remains the audit**: what is
> true today, measured. Where the two disagree about today's code, this page wins; where they disagree about the
> shape of what should be built, the architecture wins, and two of this page's own claims were corrected on
> 2026-09-20 by that work (§5.2 and §5.4, each marked in place).
>
> **Last verified against the code:** 2026-09-20, branch `phase/17-production-hardening`.

---

## 1. The short answer

**The hard half is done. The commercial half does not exist.**

What is genuinely built, and built well, is the part that is expensive to retrofit: tenant isolation, host-based
resolution, one React build serving every store from that store's row, server-enforced feature gates, and a
settings document a merchant edits themselves. A second store with a different domain, currency, language,
palette and catalogue works today and is proven by tests at four layers.

What is missing is the part that turns that into a business: **there is no plan, no quota, no subscription, no
invoice and no way for the platform to charge anyone.** There is also no automated TLS or DNS verification for
merchant domains, which is what makes onboarding customer #2 a manual operation rather than a product.

Neither gap is a design flaw. Both are recorded honestly in the repository already — the
[Platform module document](../04-MODULES/Platform/README.md) says "No plans, quotas or subscriptions. Module
flags are the only commercial lever", and [ADR-0045](../11-ADR/0045-production-edge-and-observability-stack.md)
says automated TLS "needs a real edge … infrastructure this repository has no access to".

---

## 2. What is confirmed working

Each row was verified by reading the code during this audit, and the live rows were exercised against a running
stack with two stores.

| Capability | Where it lives | Confidence |
|---|---|---|
| **Tenant resolution from the host, and nothing else** | `TenantResolutionMiddleware`; no request type may carry a tenant id (`ModuleAndContractRuleTests`) | Verified in code and live: an unknown host answers `404 StoreNotFound`, and the Development-only fallbacks are recomputed from the environment in `Program.cs`, so configuration cannot re-enable them in Production |
| **Read isolation that fails closed** | `AppDbContext` applies the `Tenant` query filter by reflection to every `ITenantOwned` type; a missing tenant context **throws** rather than returning every row | Verified: the filter cannot be forgotten on a new entity, only mis-declared, and `TenancyRuleTests` asserts it against the *built* EF model |
| **Write isolation that fails closed** | `TenantWriteGuardInterceptor` stamps the owner on insert and rejects a foreign or changed owner on update; rejection raises `CrossTenantWriteException` | Verified |
| **The schema cannot express a cross-tenant reference** | Every foreign key between tenant-owned entities carries `TenantId` as part of a composite key | Verified by `TenancyRuleTests` |
| **Token bound to host** | `AccessTokenValidation` requires the token's tenant claim to equal the resolved store's, and requires its absence on a platform host | Verified live: a store token replayed on another store's host answers `401`, disclosing nothing |
| **Another store's id answers 404, not 403** | Ownership checks inside the use cases | Verified live, and `TenantIsolationTests` enumerates every id-bearing endpoint from the live route table and **fails the build if one is missing from its table** |
| **One build renders any store** | No build-time store configuration exists in the frontend; `TenantProvider` boots from `GET /api/storefront/config` before first paint | Verified: `second-tenant.spec.js` runs the same build on a second host and asserts a different name and currency and the absence of the first store's |
| **No per-tenant forks** | A repository-wide sweep for a tenant-keyed conditional found exactly one: a boot-time "the default store still carries the seed name" warning in `DbSeeder` | Verified — customization is genuinely data-driven (§4) |
| **Module gates enforced on the server** | `RequiresModuleAttribute` answers `404 ModuleDisabled` in middleware; the pricing pipeline re-checks | Verified — the client is UX-only |
| **A palette that cannot be made unreadable** | `StoreSettings` rejects a palette below WCAG AA contrast in the Domain, before it is stored | Verified — unusual, and worth selling |
| **Store lifecycle** | Provisioning → Active → Suspended → Archived, guarded in the `Tenant` aggregate and enforced in one middleware | Verified; a closed store still serves its branded configuration and lets its administrator sign in, which was deliberate |
| **Every platform action audited** | `AuditBehavior` stages the row *before* the handler so it commits with the change; `AuditEntries` is append-only | Verified, with the two gaps in §4.3 |

---

## 3. The exact process for selling and provisioning a store today

This is what it takes, end to end, right now. Steps marked **manual** have no endpoint at all.

**Before the first store ever:** one-time platform bootstrap — see
[SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md) §4. The first platform owner is created from
environment variables at startup; there is no endpoint for it.

1. **Agree commercial terms — manual, entirely outside the product.** There is no plan to select, no price to
   quote from the system, no contract, no signup form and no payment. A merchant cannot sign up: registration
   is deliberately refused on the platform scope.
2. **Create the store.** `POST /api/platform/tenants` with name, slug, currency, default culture and time zone.
   The store starts in `Provisioning` with all three optional modules on and default branding. Nothing else is
   created — no domain, no administrator, no catalogue.
3. **Attach a domain.** `POST /api/platform/tenants/{id}/domains`. The host is unique across the platform and a
   platform host is refused. The first domain becomes primary automatically.
4. **Point DNS at the platform — manual**, by the merchant.
5. **Obtain and install a TLS certificate for that domain — manual**, at an edge this repository does not ship
   (§4.4). This is the step that does not scale.
6. **Optionally mark the domain verified.** `POST …/domains/{host}/verify` stamps a timestamp. **It gates
   nothing** — resolution never reads it, so step 3 alone is what makes the domain serve (§4.4).
7. **Invite the first administrator.** `POST /api/platform/tenants/{id}/admins`. Requires a primary domain,
   because the acceptance link is built on the store's own host. The invitation is an account with an empty
   password hash, so it cannot be signed into; the link carries a single-use token that expires in 72 hours.
8. **Activate the store.** `POST /api/platform/tenants/{id}/status` with `Activate`. **A store can be activated
   with no domain and no administrator** — the screens warn, nothing blocks.
9. **The merchant takes over:** accepts the invitation, then edits branding, languages, contact, SEO and
   announcement at `/admin/settings`, and adds catalogue, shipping methods and their own payment keys.
10. **Bill the merchant — manual and outside the product**, every period, forever. Nothing meters, invoices,
    reminds, or suspends for non-payment.
11. **Suspend or archive** when the relationship ends. `Suspended` is reversible; `Archived` is terminal, and
    **empty** — no export, no anonymization, no purge (§4.5).

There is a real platform UI for steps 2, 3, 7, 8 and 11 (a provisioning wizard with a resumable five-step
flow), so the operator is not using Swagger. The manual steps are 1, 4, 5 and 10 — and they are the commercial
ones.

---

## 4. What a customer can customize today

### 4.1 What the merchant controls themselves

| Knob | Reaches the browser how | Verdict |
|---|---|---|
| Display name, per language | storefront configuration | **Merchant** |
| Five brand colours, contrast-checked in the Domain | CSS custom properties written onto the document before first paint | **Merchant** |
| Typography — one of five presets | the preset maps to a font stylesheet | **Merchant** |
| Light / dark / follow-the-system, with every token re-derived per mode | same | **Merchant** |
| Logo, favicon, social image | uploaded; the server generates the path and the Domain refuses any URL outside this store's prefix | **Merchant** |
| Enabled languages and the default | the visitor's language is kept only if the store enabled it | **Merchant** |
| Contact details, social links, SEO title and description, announcement | storefront configuration | **Merchant** |
| Review moderation on/off | server-enforced | **Merchant** |
| Catalogue, product text per language, shipping methods, coupons, their own payment keys | the respective admin screens | **Merchant** |

### 4.2 What only the platform owner controls

Currency (and only before the store has any commercial activity — an aggregate rule), the legal store name,
domains, module flags, and store status.

### 4.3 What nobody can change — the white-label gaps

| Gap | What it means for a customer |
|---|---|
| **Theme presets change nothing.** Three presets are offered, validated, stored and written onto the document — and no stylesheet reads the attribute | A merchant picks one of three themes and sees no difference. The plumbing already exists, so this is the cheapest differentiation available and currently the demo promises three looks and ships one |
| **Layout is fixed in React.** No section order, no navigation shape, no merchant-editable banner or campaign slot | The first request after colours is "move this / add a banner". The answer is no |
| **No store-authored content pages** — no privacy policy, terms, returns, shipping or FAQ (TD-42, blocking `M2`) | In many jurisdictions a store cannot legally sell without a reachable privacy policy and terms. The owner decision is written up with two costed options and a recommendation in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) |
| **Email wording is one global set of templates**, hard-coded per language; per-store are only the sender *display* name, the header colours, the logo and reply-to | Order confirmations reach the merchant's own customers from the platform's mailbox. There is no per-store sending domain and no SPF/DKIM path |
| **Only two languages**, and no per-store override of a single interface string | Blocks any merchant outside Arabic and English |
| **Three module flags, platform-set** (`promotions`, `reviews`, `wishlist`) | Every feature request routes through the platform owner, and everything else — search insights, variants, synonyms — cannot be turned off at all |
| **Crawlers see the platform's title for every store** | The served HTML shell is identical on every host; the store's identity is applied client-side after configuration resolves. Link previews and non-JS crawlers show the platform's name, and an English-only store gets an Arabic right-to-left first paint |

### 4.4 Domains and TLS

- **`VerifiedAt` is decorative.** It is stamped by a manual platform action and read by nothing that makes a
  trust decision — the host→store lookup never consults it, so a domain serves the storefront the moment it is
  added. No DNS TXT, file or CNAME challenge exists. Recorded as F-6 in
  [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md); [ADR-0045](../11-ADR/0045-production-edge-and-observability-stack.md)
  overstated this, and the correction is in §4 of the [ADR index](../11-ADR/README.md).
- **No TLS of any kind ships here.** The bundled proxy listens on port 80; there is no certificate, no ACME
  client and no terminator in the compose file. This is a recorded decision, not an oversight — but for a
  product where every merchant brings a domain, per-domain certificate issuance *is* the product, and today
  each one is a hands-on operation.
- **No automatic subdomain.** There is no base-domain or wildcard concept; a `{slug}` subdomain exists only as
  a Development convenience. Every store needs a hand-added domain row, DNS and a certificate.

### 4.5 Lifecycle gaps that matter commercially

- **Suspension and archive do not revoke sessions or refresh tokens**, unlike disabling an account, which does.
  Not exploitable today, because every permissioned endpoint answers `503` for a closed store — but it is a
  latent hole the moment an endpoint available to a closed store does real work.
- **A suspended store still sends email**: the outbox dispatcher has no status filter.
- **`Archive` is terminal but empty** — no export, anonymization, retention clock or purge, and the status's own
  comment promises an export that does not exist. A departing merchant cannot be handed their data.
- **Provisioning is four or more calls and is not atomic.** A half-provisioned store is the normal resting
  state, nothing expires it, and it is excluded from every background sweep.
- **Two fixed store roles**, no custom roles, no per-permission grants, no way to revoke a pending invitation.
- **Authentication events are not audited at all** — who accepted an administrator invitation, and from where,
  is unanswerable.
- **Caches are per-process.** With a second API instance a suspension takes up to a minute to reach the others
  (R-23). For a platform that suspends for non-payment, that is a commercial exposure, and it is the same
  constraint that makes uploads-on-local-disk block a second instance (TD-20).

---

## 5. The smallest production-quality commercial architecture

### 5.0 Why this is recorded and not built

**The shape of the plan depends on an unanswered question.** D-13 in
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) asks whether each store connects its own payment
account or the platform settles on their behalf. Those two answers produce **different data models**:

- **Own account** (today's mechanism) — money never touches the platform, so the platform must charge the
  merchant directly: a subscription, with its own gateway credentials and its own dunning.
- **Platform settles** — the platform can take a share of the store's revenue instead, and "the plan" becomes a
  take-rate plus limits, metered from order data the platform already has.

Building either now would be guessing a commercial model, which `AGENTS.md` §0 rule 4 reserves for the owner.
What follows is therefore the design that is *common* to both, plus what each answer adds.

### 5.1 The seam that already exists — use it, do not replace it

The repository already answers "may this store use X?" in one enforced place: a module flag, checked by
`RequiresModuleAttribute` in middleware and re-checked in the pricing pipeline. **A plan should feed that seam
rather than introduce a second one.** Concretely: the set of modules a store has becomes *derived from its
plan*, with an explicit platform override retained for support, so there stays exactly one answer to the
question and one place to enforce it.

This is the single most important constraint in this section. A plan system that adds its own parallel check
would give two answers to one question, and the two would drift.

### 5.2 The pieces, and where each belongs

Owned by the Platform module (or a new sibling module if it grows) — one deployable, one database, no broker,
no new technology, consistent with [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md):

| Piece | What it is | Layer |
|---|---|---|
| A *plan* | Platform-owned aggregate: code, display name, price, currency, interval, the module set it grants, and its quota values. Versioned — an existing subscriber keeps the terms they signed | Domain |
| A *subscription* | One per store: which plan, status, current period, trial end. The only thing that may change a store's entitlements | Domain |
| *Entitlements* | The derived answer "which modules and which limits does this store have", resolved once per request alongside the existing store snapshot | Application, read through the existing tenant context |
| A *quota guard* | Consulted by the commands that create countable things (products, staff, storage). **Server-side only** | Application |
| A *platform billing port* | An interface the core owns, implemented in Infrastructure — the same shape as `IPaymentService`, which is the Application-owned port today (`IPaymentGateway` is **not** core-owned: it is the Infrastructure-internal single-account adapter contract that `PaymentGatewayRouter` selects between; an earlier revision of this row named the wrong one) | Application port, Infrastructure adapter |
| *Dunning* | A background job that moves an unpaid subscription through a grace period and then suspends the store, reusing `TenantStatus.Suspended`, whose own definition already names payment | Application + a job beside the existing five. **Corrected 2026-09-20:** this row said "the existing seven". `AddHostedService` is called exactly five times, and only three of those derive from `StoreSweepService` — which is per-store and single-instance by its own declaration, so a dunning run belongs on the outbox's lease pattern rather than that base class |

### 5.3 Three things this design must get right

1. **Platform-to-merchant billing is a different money path from store-to-shopper — verified against the code
   on 2026-09-20, not asserted.** It is not a matter of taste: the existing path *cannot* express a platform
   charge, and would fail in four separate places rather than merely fit badly.
   - `PaymentGatewayRouter` — the only implementation of `IPaymentService` — reaches
     `ITenantContext.RequireTenant()`, which **throws** in platform scope. So does every read or write of a
     `Payment` row: `Payment` is `ITenantOwned`, and `AppDbContext`'s filter says so in its own comment —
     *no store ⇒ throws, even in platform scope*. The interface that would have worked,
     `ITenantOrPlatformOwned`, exists and is deliberately not used here.
   - **A `Payment` cannot exist without an order:** a required composite foreign key to `Orders`, a constructor
     that rejects a missing order id, and a unique index of one payment per order. A subscription charge has no
     order, so there is no row shape for it.
   - **Both idempotency key formats hard-code a store tenant id** (`souq-intent-{tenantId}-…`,
     `souq-refund-{tenantId}-…`).
   - **It would charge the wrong party:** account selection prefers the *store's* connected account, so billing
     a merchant through this port would deposit the platform's own fee into that merchant's Stripe account.

   Reusable unchanged, and worth reusing: the `Money` value object, the AES-GCM secret protection with its
   purpose binding, the reserve → call → record discipline with an idempotency key, and `TenantStatus.Suspended`
   as the state dunning drives. `ITenantScopeRunner` would technically satisfy the filters — and is the wrong
   answer, for the reason in the last bullet.
2. **A quota check is a count-then-write race, and the repository's safe pattern is not the one it looks like.**
   Verified on 2026-09-20, and the earlier wording here was imprecise in a way that would have produced a racy
   guard if followed literally.
   - **The order is load-bearing and it is the reverse of "count then write".** The last-administrator guard
     *writes first*, saves, and only then re-counts inside the same transaction, throwing a private exception to
     roll the whole thing back. That works because the write takes the exclusive lock first, so the other
     racer's count must wait for it and therefore sees the final answer. A guard that counts before it writes —
     the natural reading of "count-then-write" — has no lock and no protection.
   - **It rests on an invariant nothing checks:** SQL Server's default READ COMMITTED being *locking*, not
     snapshot. RCSI is off everywhere in this repository, but it is asserted by no test, no migration and no
     startup check — recorded as TD-68, because a managed database that enables it silently disables this guard.
   - **Do not copy the caps that already exist**: `SearchSynonym.MaxPerStore` and
     `WishlistItem.MaxItemsPerCustomer` both count and then insert with **no transaction and no re-count**, so
     two concurrent creates at the limit both pass. They are soft caps where that does not matter; a commercial
     quota is not. The in-aggregate limits (`Basket.MaxLines`, `Product.MaxVariants`) are not analogues either —
     they are protected by the aggregate root's `rowversion`, and a per-tenant quota has no such root.
   - **There is a second existing mechanism that may fit better:** `OrderNumbers` increments a single per-tenant
     counter row inside the caller's transaction. Every contender queues on one exclusive lock, so there is no
     range-scan deadlock class at all — unlike the re-count, which deadlocks by design and relies on translating
     that deadlock into a clean refusal. Its cost is that the counter must be kept truthful when rows are
     deleted. Which of the two fits is a per-quota decision, not a blanket one.
   - **A quota guard adds an eighth explicit-transaction site**, which matters to F-14: enabling
     `EnableRetryOnFailure` later requires every such site to be wrapped in the execution strategy.
3. **Suspension must actually stop the store.** Today it is a status column plus a per-process cache with a
   60-second lifetime and local-only invalidation. For manual suspension that is fine; for automated
   suspension across replicas it is not, so this design depends on R-23 being closed before the platform runs
   more than one instance — not before the feature ships.

### 5.4 What is explicitly *not* in this design

No new database, no per-tenant database, no message broker, no separate billing service, and no per-tenant
fork. **Corrected 2026-09-20:** this paragraph said all four were named non-goals.
[ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) names fifteen, and *a separate billing service* is
not among them — it falls under §1 (microservices) by argument, and would need its own ADR rather than a
superseding one. The other three are named. Nothing in a subscription model constitutes the measured evidence
that would reverse any of them.

---

## 6. Recommended order

Ordered by *what unblocks the most for the least*, not by size. Each line says what it is blocked on.

| # | Item | Why here | Blocked on |
|---|---|---|---|
| 1 | **Answer TD-42** (policy links or authored pages) | A store cannot legally sell in many jurisdictions without terms and a privacy policy. Option (b) is five optional URL fields on settings — near-zero risk — and it unblocks `M2` | Owner (recommendation already written) |
| 2 | **Answer D-13** (merchant of record) | Every line below it in the commercial stack changes shape depending on the answer (§5.0), and it gets harder to reverse with every store onboarded | Owner |
| 3 | **Make theme presets do something** | The choice is already offered, stored and delivered; only the stylesheet is missing. Cheapest credibility fix on the page | Nothing |
| 4 | **Per-store sending domain for email** | The merchant's own customers see the platform's mailbox today. Needs a DNS/DKIM story that overlaps with item 5 | Nothing in the code; needs a provider decision |
| 5 | **Automated TLS and DNS verification for merchant domains** | This is what makes onboarding a product rather than an operation, and it closes the decorative-`VerifiedAt` gap | An edge that can answer an ACME challenge — infrastructure, per ADR-0045 |
| 6 | **Plans, quotas and subscriptions** (§5) | The actual business model. Deliberately after D-13, because D-13 decides its shape | Item 2 |
| 7 | **Merchant self-service signup** | Only worth building once there is a plan to sign up *to* and a way to charge for it | Item 6 |
| 8 | **Archive: export and retention** | Needed the first time a merchant leaves, not the first time one arrives | Nothing |
| 9 | **Revoke sessions on suspend/archive; stop email for a closed store** | Defence in depth today; load-bearing the moment suspension is automated by dunning | Nothing |

**Not on this list, deliberately:** the P0 items in
[ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) — TLS termination, least-privilege database logins,
scheduled off-host backups, tax (P-06) and the Stripe minor-unit verification (P-05). Those block *any* release,
commercial or not, and that page already tracks them.

---

## 7. Related

[ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) ·
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) ·
[TechnicalDebt.md](TechnicalDebt.md) ·
[RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) ·
[Platform module](../04-MODULES/Platform/README.md) ·
[MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) ·
[WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) ·
[ADR-0011](../11-ADR/0011-white-label-architecture.md) ·
[ADR-0035](../11-ADR/0035-white-label-runtime.md) ·
[ADR-0045](../11-ADR/0045-production-edge-and-observability-stack.md)
