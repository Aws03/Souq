# Architectural risk register

> **What this page is:** the risks a new team should know about before they are surprised by one. Each entry says what it is, how confident we are, what it would cost, and what to do about it.
> **Not the same as [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md):** debt is work we chose to postpone; a risk is a way this system can hurt someone — a customer charged wrongly, a store seeing another store's data, a deployment that cannot be recovered.

**Confidence labels**

| Label | Meaning |
|---|---|
| **Known** | Verified in the code, and its consequence is certain |
| **Observed** | Found by reading the code; the failure has not been reproduced |
| **Unverified** | Depends on something outside this repository (a provider, a runtime, a network) |
| **Future** | Not a problem today; becomes one under a named condition |

## Classification — what kind of thing each open risk is

Reconciled in full during the operational readiness mission: every remaining row was re-read against the code,
not carried forward. The class matters more than the number, because it says **who can close it** — and four of
these cannot be closed by engineering at all.

| Class | Meaning | Risks |
|---|---|---|
| **Operational action** | Engineering is done; a deployment must apply it | R-11, R-12, R-16, R-19, R-20 |
| **Owner decision** | A commercial, legal or policy choice ([OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md)) | R-03, R-25, R-26, R-27 |
| **External verification** | Needs an account or system outside this repository | R-01 |
| **Accepted** | Understood, bounded, and deliberately not fixed | R-05, R-09, R-14, R-18 |
| **Deferred — a named trigger** | Not a problem yet; becomes one under a stated condition | R-13, R-23 |
| **Product / policy work** | Needs a feature or a written policy, not a patch | R-21 |

### Does it block a release?

| Blocks | Risks | Why |
|---|---|---|
| **The first paying customer** | R-01, R-25, R-27 | money collected wrongly and silently; selling where tax is required; selling under a licence that permits resale |
| **A public deployment** | R-12, R-16, R-19, R-20 | connecting as `sa`; no TLS; no scheduled backup; nothing watching, and a red pipeline that cannot block a merge |
| **The second paying store** | R-26 | who is merchant of record decides liability, and it is hard to reverse once stores are onboarded |
| **Nothing today** | R-03, R-05, R-09, R-11, R-13, R-14, R-18, R-21, R-23 | each is accepted, deferred with a trigger, or a control question — see its row |

**What changed in this mission.** R-12, R-16, R-19 and R-20 were all engineering problems and are now all
*operational* ones: the mechanisms exist, are tested, and in three cases were rehearsed against real
infrastructure. None of them is closed, because a mechanism nobody has applied is not protection — and saying
otherwise is how a register stops being true. R-02, R-06, R-07, R-08, R-10, R-17, R-22 and R-28 are absent
because they were closed; their history is in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

**What changed in the M1 architecture audit.** R-04 is closed: the payment-account use cases moved to Payments
and now reach the platform admin path through a published contract, `IStorePaymentAccountEditor`, rather than a
raw class reference from Platform's folder ([ModuleBoundaryAudit.md](ModuleBoundaryAudit.md) — TD-04 in
[TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)). R-14's crossing count dropped from 79 to 74 as a direct
result. R-15 is now fixed (see its row): the cycle it named no longer exists.

## 1. Money and payments

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-01 | **JOD minor units at Stripe (open decision P-05).** `StripeAmountConverter` multiplies by 100 for every currency that Stripe does not list as zero-decimal. JOD has three minor digits. If the connected account treats JOD as three-decimal, every charge collects **one tenth** of the order | Unverified (depends on the real Stripe account) | Catastrophic and silent: revenue loss per transaction | Before enabling live JOD: create a test intent for a three-decimal amount on the real account and compare what the dashboard shows. The same applies to the other three-decimal currencies in `CurrencyInfo` |
| R-03 | **Cancelling a paid order refunds money on `orders.manage` alone.** `UpdateOrderStatusCommand` issues a full refund; a staff account with order management but without `store.payments.manage` can move money | Known | Insider or mistaken refunds; audit gap (order-status changes are not audited) | Decide the permission model: require the payments permission for a refunding transition, or audit it explicitly |
| R-05 | **Refunds do not return a coupon use** (recorded in ADR-0031) | Known | A customer can consume a single-use coupon on an order that was refunded | Product decision: decide whether a refund restores the use |

## 2. Customer-visible correctness

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-09 | **A coupon's fixed discount still carries no currency.** `Coupon.Value` is a bare `decimal` and `CalculateDiscount` stamps the basket's currency onto it, while `MinOrderAmount` in the same row *is* a `Money` with its own column. The way in is closed — the currency lock counts coupons and shipping methods, and `Coupon.EnsureUsable` compares currencies — so a store can no longer change currency after creating a coupon. The asymmetry in the row itself remains | Observed | **Re-confirmed unreachable on 2026-09-18 (M8), by tracing all three legs rather than re-reading this row.** A basket's currency is always the store's (`PricingService` seeds from `Money.Zero(store.Currency)` and `Money.Add` throws on a mismatch); `Tenant.Currency` has exactly one post-construction writer, `Tenant.ChangeCurrency`, which throws when there is commercial activity; its only caller passes `PlatformQueries.HasCommercialActivityAsync`, which counts `Coupons`. There is no store-admin currency path, no soft-delete that hides a coupon row from that count, and no delete-then-change escape (an unused coupon hard-deletes, a used one is refused `409 CouponInUse`). Pinned end to end by `كوبون_وحده_يقفل_عملة_المتجر_بلا_منتج_ولا_طلب`. **Two honest limits on the word "unreachable":** (a) `EnsureUsable`'s currency guard can only fire when a coupon *has* a minimum, so it says nothing about a fixed-amount coupon without one — exactly this risk's case; (b) the lock is forward-looking and was never backfilled, so it protects no store that changed currency before Phase 17. Neither has a victim: nothing is in production, and the lock prevents every new case | Give `Value` its own currency column, or model the fixed discount as `Money`. Both are schema changes, so they wait for a migration that has another reason to exist — M8 deliberately did **not** add a column to guard a path that cannot be walked |

## 3. Tenant isolation and security

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-11 | **Rate limits are per process and partition by a forwarded client address.** With `TRUSTED_PROXY_NETWORKS` covering the Docker bridge and the API port published, a direct client may be able to forge `X-Forwarded-For` and evade limits | Unverified (depends on the deployment's networking) | Brute-force protection weaker than it looks | Only trust the proxy's own address; do not publish the API port when a proxy fronts it; move limits to the edge when scaling out |
| R-12 | **Deployments still connect to SQL Server as `sa`** — but no longer silently. The startup check reads the identity **from the database** and warns, naming the roles it holds; a nominal split (a migration connection carrying the runtime login) is also caught | Known | No blast-radius limit if the app is compromised | Apply `scripts/sql/least-privilege-logins.sql` and point the deployment at the two identities. Skipping it is now audible at every boot |
| R-13 | **Uploads are served from local disk**, so the files live on one instance | Known | Several API instances need the same mount, or images 404 depending on which instance answers | Move to blob storage behind `IFileStorage` before scaling out |
| R-14 | **Cross-module domain access is not prevented, only counted** (74 crossings today) | Known | Boundaries erode silently; extraction gets harder | The ratchet in [ModuleDomainDependencies.md](ModuleDomainDependencies.md) plus the contracts listed in [ModuleBoundaries.md](ModuleBoundaries.md); every crossing is now classified with a reason in [ModuleBoundaryAudit.md](ModuleBoundaryAudit.md) |
| R-15 | ~~**Identity and Customers depend on each other**~~ **FIXED (M9).** The direction decided in M1 was built: `IAccountProfiles` (declared by Identity, implemented by Customers) and `IAccountLifecycle` (declared and implemented by Identity), both declared in Identity so that every reference runs Customers → Identity and no arrow returns ([ModuleBoundaryAudit.md](ModuleBoundaryAudit.md) "The cycle, exactly") | Verified by the generated ratchet, not by reading: **74 crossings across 15 pairs → 62 across 13**, with all twelve Identity↔Customers crossings gone in both directions | ~~Neither module can be reasoned about, tested or extracted alone~~ — and the test suites moved with the responsibility: `AccountLifecycleTests` and `CustomerAccountProfilesTests` now assert what the other module's tests used to | **FIXED (M9) — the one cycle the target graph forbids no longer exists** |
| R-16 | **No TLS in the repository's own deployment.** Headers, HSTS and the forwarded-scheme fix exist; the SPA's CSP is report-only but can no longer drift (a new external origin fails the build) | Known | Cookies and tokens over plaintext if deployed as-is | Terminate TLS in front. The commonest way to get this wrong — an untrusted proxy, so the scheme is ignored and HSTS never sent — is now detected at runtime and warned once per process |

## 4. Data and operations

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-18 | **Migrations run automatically at startup, in every environment**, so deploying *is* migrating | Known | A bad migration is applied by the act of deploying; two instances starting together race. **Measured consequence:** the data-moving migrations expand, migrate and contract in one step, so two application versions cannot share the database across one — rolling and blue-green deployments are unsafe, and a `Down()` loses whatever the old schema cannot represent ([Migrations.md](../06-DATABASE/Migrations.md) §7) | **M17 built the better end state rather than leaving it described.** `Database:MigrateOnStartup` must be chosen explicitly outside Development/Testing — the API refuses to start otherwise — and `false` gives the deliberate step: a self-contained migration bundle run as the migration identity before the new version serves traffic, with the API logging an error naming any pending migration instead of booting against a schema it does not match. Built deliberately *before* a second instance exists, because the moment one is added is the moment nobody is thinking about this. `dotnet ef` now also works from a clean checkout with no secrets (TD-18 closed), which the step needs. **Still true and unchanged:** deploy stop-then-start with the default; back up before any of the five destructive migrations and rehearse the restore. The expand-migrate-contract-in-one-step shape of those five is a property of the migrations themselves, not of when they run |
| R-19 | **No scheduled backups and no off-site copy** — though their absence is now detectable: `scripts/backup-verify.sh` fails on a missing, stale, incomplete, corrupted or never-rehearsed backup set, and is safe to run from cron with an alert. The procedure now exists and is rehearsed ([BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md)); nothing runs it automatically | Known | Data loss is bounded only by how recently someone ran the script by hand | Wire up the schedule, the off-site copy and failure alerting — the deployment-specific step (§7 there) |
| R-20 | **CI does not block merges, and nothing watches the health endpoints** | Known | A red run can still be merged; a broken deploy is detected by a customer | The pipeline's checks were verified step by step and one caught a real tracked credential. Still needed, and neither can live in this repository: **branch protection** requiring the four named jobs, and a monitor on `/health/ready` — whose contract is now documented and drilled |
| R-21 | **Several tables grow without any purge:** refresh tokens, stock reservations, in-app notifications, audit entries, dead outbox rows | Known | Slow, unbounded growth; personal data kept longer than necessary | Define retention per table in [OwnershipMap.md](../06-DATABASE/OwnershipMap.md) and implement the sweeps |
| R-23 | **In-process caches (tenant directory, storefront config) with no cross-instance invalidation** | Future (on the second instance) | A store sees stale settings or a stale status for up to a minute | Decide the cache strategy before scaling out ([ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md)) |

## 5. Product and commercial decisions

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-25 | **No tax model exists (P-06)**, and the pricing pipeline's tax stage is a fixed zero | Known | Cannot sell legally where tax must be shown or collected | Decide inclusive/exclusive pricing, per-store rates, and invoice requirements |
| R-26 | **The payment account model is undecided (D-13)**: every store connects its own account, or the platform adopts Stripe Connect | Known | Blocks the second store taking live payments; affects liability and fees | Decide before onboarding a second paying store |
| R-27 | **The repository is MIT-licensed with a public remote (P-03)** | Known | Anyone receiving the code may resell it | Decide the license and repository visibility before the first sale |

## How to use this register

- Before a release: read §1 and §3. Nothing there should be open when money or real customer data is involved.
- When planning: pair each risk with its entry in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) — the debt item is usually the fix.
- When a risk is closed: delete the row and say so in the commit. A register full of stale entries stops being read. Closed risks keep their history in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md), which tracks each one to its fix.
- Numbers are stable and never reused: gaps in the sequence are risks that were closed, not risks that were forgotten.
