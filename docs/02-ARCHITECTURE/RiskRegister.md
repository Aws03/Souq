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

## 1. Money and payments

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-01 | **JOD minor units at Stripe (open decision P-05).** `StripeAmountConverter` multiplies by 100 for every currency that Stripe does not list as zero-decimal. JOD has three minor digits. If the connected account treats JOD as three-decimal, every charge collects **one tenth** of the order | Unverified (depends on the real Stripe account) | Catastrophic and silent: revenue loss per transaction | Before enabling live JOD: create a test intent for a three-decimal amount on the real account and compare what the dashboard shows. The same applies to the other three-decimal currencies in `CurrencyInfo` |
| R-03 | **Cancelling a paid order refunds money on `orders.manage` alone.** `UpdateOrderStatusCommand` issues a full refund; a staff account with order management but without `store.payments.manage` can move money | Known | Insider or mistaken refunds; audit gap (order-status changes are not audited) | Decide the permission model: require the payments permission for a refunding transition, or audit it explicitly |
| R-04 | **Payment-account use cases live in the Platform feature folder.** The most sensitive code in the system (store gateway secrets) sits where the architecture test attributes it to Platform | Known | Wrong reviewers, wrong tests, boundary erosion | Move to Payments ([TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)) |
| R-05 | **Refunds do not return a coupon use** (recorded in ADR-0031) | Known | A customer can consume a single-use coupon on an order that was refunded | Product decision: decide whether a refund restores the use |

## 2. Customer-visible correctness

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-09 | **Currency changes are not fully guarded.** A store's currency locks only after a product or an order exists; coupons and shipping methods created before that keep their old currency, and a fixed-amount coupon is a bare decimal applied in the basket's currency | Observed | "5 JOD off" silently becomes "5 USD off" | Compare `Money` with its currency in the coupon rules; extend the currency lock to any priced row |

## 3. Tenant isolation and security

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-11 | **Rate limits are per process and partition by a forwarded client address.** With `TRUSTED_PROXY_NETWORKS` covering the Docker bridge and the API port published, a direct client may be able to forge `X-Forwarded-For` and evade limits | Unverified (depends on the deployment's networking) | Brute-force protection weaker than it looks | Only trust the proxy's own address; do not publish the API port when a proxy fronts it; move limits to the edge when scaling out |
| R-12 | **The application connects to SQL Server as `sa`** | Known | No blast-radius limit if the app is compromised | Create a least-privilege login before any real deployment |
| R-13 | **Uploads are served from local disk**, so the files live on one instance | Known | Several API instances need the same mount, or images 404 depending on which instance answers | Move to blob storage behind `IFileStorage` before scaling out |
| R-14 | **Cross-module domain access is not prevented, only counted** (79 crossings today) | Known | Boundaries erode silently; extraction gets harder | The ratchet in [ModuleDomainDependencies.md](ModuleDomainDependencies.md) plus the contracts listed in [ModuleBoundaries.md](ModuleBoundaries.md); every crossing is now classified with a reason in [ModuleBoundaryAudit.md](ModuleBoundaryAudit.md) |
| R-15 | **Identity and Customers depend on each other**, a cycle the target graph forbids | Known | Neither module can be reasoned about, tested or extracted alone | Decide which side owns account provisioning and erasure; expose one contract |
| R-16 | **No TLS, HSTS or security headers in the repository's own deployment**, and the proxy overwrites `X-Forwarded-Proto` with its own scheme, so generated links can come out `http://` | Known | Cookies and tokens over plaintext if deployed as-is; wrong links in emails | Terminate TLS in front, pass the scheme through, and add the headers before any public launch |

## 4. Data and operations

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-18 | **Migrations run automatically at startup, in every environment** | Known | A bad migration is applied by the act of deploying; two instances starting together race | Back up first; consider a deliberate migration step before rollout |
| R-19 | **No automated backups, and no restore procedure in the repository** | Known | Data loss is unbounded | Define and rehearse backup and restore before the first paying store |
| R-20 | **No health checks and no CI pipeline** | Known | A broken deploy is detected by a customer; a red test suite can be committed | Add a health endpoint and a pipeline running the existing suites |
| R-21 | **Several tables grow without any purge:** refresh tokens, stock reservations, in-app notifications, audit entries, dead outbox rows | Known | Slow, unbounded growth; personal data kept longer than necessary | Define retention per table in [OwnershipMap.md](../06-DATABASE/OwnershipMap.md) and implement the sweeps |
| R-23 | **In-process caches (tenant directory, storefront config) with no cross-instance invalidation** | Future (on the second instance) | A store sees stale settings or a stale status for up to a minute | Decide the cache strategy before scaling out ([ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md)) |
| R-24 | **Per-store background sweeps only run for Active stores**, so a suspended store's expired checkouts are never settled and its baskets are never purged | Known | Stock stays reserved after suspension; storage grows | Decide the intended behaviour and test it |

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
