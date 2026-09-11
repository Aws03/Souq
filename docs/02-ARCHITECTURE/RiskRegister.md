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
| R-02 | **A declined-then-retried card can capture money against a cancelled order.** `OrderPaymentConfirmation.ConfirmAsync` treats any non-succeeded intent as final: it cancels the order but does not cancel the intent at the gateway. A later successful capture leaves a Cancelled order with a Failed `Payment`, which no refund path accepts | Observed | Customer charged with no order; manual refund through the Stripe dashboard | Cancel the intent when cancelling the order, and add an integration test for "intent succeeds after the order was cancelled" |
| R-03 | **Cancelling a paid order refunds money on `orders.manage` alone.** `UpdateOrderStatusCommand` issues a full refund; a staff account with order management but without `store.payments.manage` can move money | Known | Insider or mistaken refunds; audit gap (order-status changes are not audited) | Decide the permission model: require the payments permission for a refunding transition, or audit it explicitly |
| R-04 | **Payment-account use cases live in the Platform feature folder.** The most sensitive code in the system (store gateway secrets) sits where the architecture test attributes it to Platform | Known | Wrong reviewers, wrong tests, boundary erosion | Move to Payments ([TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md)) |
| R-05 | **Refunds do not return a coupon use** (recorded in ADR-0031) | Known | A customer can consume a single-use coupon on an order that was refunded | Product decision: decide whether a refund restores the use |

## 2. Customer-visible correctness

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-06 | **Order emails can show the wrong total or never arrive.** `OrderEmailHandler` loads the order without its lines, so the computed total reflects shipping only; a discounted order can throw and be retried until the outbox marks it dead | Observed | Customers receive a wrong or missing order confirmation | Use the frozen `PlacedTotal`; add a handler test with lines and a discount |
| R-07 | **A product in a disabled category is invisible but still purchasable.** The storefront hides it, but the basket and checkout check only the product's own status | Known | A store "removes" a category and still sells from it | Decide where sellability is defined, enforce it in the pricing pipeline and at checkout, and test it |
| R-08 | **A closed store answers 503 to everything.** `AvailableWhenStoreClosedAttribute` exists and is honoured, but is applied to no endpoint — so a suspended store cannot serve its own branded "closed" page, and its administrators cannot sign in to fix anything | Known | A suspended store is a blank error for its owner and its customers | Apply it to the storefront configuration endpoint and to sign-in, or change the product decision deliberately |
| R-09 | **Currency changes are not fully guarded.** A store's currency locks only after a product or an order exists; coupons and shipping methods created before that keep their old currency, and a fixed-amount coupon is a bare decimal applied in the basket's currency | Observed | "5 JOD off" silently becomes "5 USD off" | Compare `Money` with its currency in the coupon rules; extend the currency lock to any priced row |

## 3. Tenant isolation and security

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-10 | **The public tracking token is written to the request log** as part of the path, and nginx's default access log records it too | Known | Anyone with log access can open a customer's order tracking page | Redact the segment in `RequestLoggingMiddleware`; decide the log policy at the proxy |
| R-11 | **Rate limits are per process and partition by a forwarded client address.** With `TRUSTED_PROXY_NETWORKS` covering the Docker bridge and the API port published, a direct client may be able to forge `X-Forwarded-For` and evade limits | Unverified (depends on the deployment's networking) | Brute-force protection weaker than it looks | Only trust the proxy's own address; do not publish the API port when a proxy fronts it; move limits to the edge when scaling out |
| R-12 | **The application connects to SQL Server as `sa`** | Known | No blast-radius limit if the app is compromised | Create a least-privilege login before any real deployment |
| R-13 | **Uploads are served from local disk and the proxy does not forward the original `Host` for `/uploads/`**, so the API sees an internal host with no store | Observed | Store images and branding likely 404 through the compose stack; also blocks running more than one instance | Forward the host as the `/api/` location does; move to blob storage before scaling out |
| R-14 | **Cross-module domain access is not prevented, only counted** (78 crossings today) | Known | Boundaries erode silently; extraction gets harder | The ratchet in [ModuleDomainDependencies.md](ModuleDomainDependencies.md) plus the contracts listed in [ModuleBoundaries.md](ModuleBoundaries.md) |
| R-15 | **Identity and Customers depend on each other**, a cycle the target graph forbids | Known | Neither module can be reasoned about, tested or extracted alone | Decide which side owns account provisioning and erasure; expose one contract |
| R-16 | **No TLS, HSTS or security headers in the repository's own deployment**, and the proxy overwrites `X-Forwarded-Proto` with its own scheme, so generated links can come out `http://` | Known | Cookies and tokens over plaintext if deployed as-is; wrong links in emails | Terminate TLS in front, pass the scheme through, and add the headers before any public launch |

## 4. Data and operations

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-17 | **Demo data is seeded into any fresh database, including production.** `DbSeeder` creates the demo store and its catalog unconditionally | Known | A production deployment starts with a demo store and demo products | Gate the catalog seed on Development, or make it explicit configuration |
| R-18 | **Migrations run automatically at startup, in every environment** | Known | A bad migration is applied by the act of deploying; two instances starting together race | Back up first; consider a deliberate migration step before rollout |
| R-19 | **No automated backups, and no restore procedure in the repository** | Known | Data loss is unbounded | Define and rehearse backup and restore before the first paying store |
| R-20 | **No health checks and no CI pipeline** | Known | A broken deploy is detected by a customer; a red test suite can be committed | Add a health endpoint and a pipeline running the existing suites |
| R-21 | **Several tables grow without any purge:** refresh tokens, stock reservations, in-app notifications, audit entries, dead outbox rows | Known | Slow, unbounded growth; personal data kept longer than necessary | Define retention per table in [OwnershipMap.md](../06-DATABASE/OwnershipMap.md) and implement the sweeps |
| R-22 | **`.env.example` ships a non-empty Gmail placeholder**, which defeats the "no email provider" fail-fast: the app starts, picks Gmail, and every message dies after eight attempts | Known | Silent email outage on a fresh deployment | Ship empty placeholders |
| R-23 | **In-process caches (tenant directory, storefront config) with no cross-instance invalidation** | Future (on the second instance) | A store sees stale settings or a stale status for up to a minute | Decide the cache strategy before scaling out ([ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md)) |
| R-24 | **Per-store background sweeps only run for Active stores**, so a suspended store's expired checkouts are never settled and its baskets are never purged | Known | Stock stays reserved after suspension; storage grows | Decide the intended behaviour and test it |

## 5. Product and commercial decisions

| # | Risk | Confidence | Impact | What to do |
|---|---|---|---|---|
| R-25 | **No tax model exists (P-06)**, and the pricing pipeline's tax stage is a fixed zero | Known | Cannot sell legally where tax must be shown or collected | Decide inclusive/exclusive pricing, per-store rates, and invoice requirements |
| R-26 | **The payment account model is undecided (D-13)**: every store connects its own account, or the platform adopts Stripe Connect | Known | Blocks the second store taking live payments; affects liability and fees | Decide before onboarding a second paying store |
| R-27 | **The repository is MIT-licensed with a public remote (P-03)** | Known | Anyone receiving the code may resell it | Decide the license and repository visibility before the first sale |
| R-28 | **The frontend stack decision (D-19) is deferred**, and its trigger has arrived | Known | Each further screen built without the decision is a screen to migrate twice | Decide TypeScript and a query library now, or reword the trigger |

## How to use this register

- Before a release: read §1 and §3. Nothing there should be open when money or real customer data is involved.
- When planning: pair each risk with its entry in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) — the debt item is usually the fix.
- When a risk is closed: delete the row and say so in the commit. A register full of stale entries stops being read.
