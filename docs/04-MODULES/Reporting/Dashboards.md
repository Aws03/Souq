# Reporting: the dashboards

> **Code:** `src/Souq.Application/Features/Reporting/StoreDashboard.cs`, `src/Souq.Infrastructure/Persistence/Queries/StoreReportQueries.cs`, `frontend/src/pages/admin/Dashboard.jsx`, `frontend/src/pages/admin/BusinessOverview.jsx`, `frontend/src/app/PlatformLayout.jsx` · **Module:** [Reporting](README.md) · **Design:** [DesignSystem.md](../../08-FRONTEND/DesignSystem.md)

## 1. Three dashboards, three questions

There is one endpoint per audience, and the audience is defined by the question it asks — not by seniority.

| Dashboard | Reader | The question | Route | Source |
|---|---|---|---|---|
| **Store performance** | The person running the store day to day | *What is happening in my store, and what needs me now?* | `/admin` | `GET /api/admin/reports/dashboard` |
| **Business overview** | An owner, a partner, an investor | *Is the business healthy, is it growing, and where is the risk?* | `/admin/business` | The same endpoint |
| **Platform overview** | The platform owner | *How many stores, how much activity, across everything?* | `/platform` | `GET /api/platform/stats` |

**Why the first two are separate pages rather than tabs.** The manager wants pending orders and low stock; the stakeholder does not know what "reserved stock" means and does not need to. A single dashboard serving both questions serves one of them badly. They are separate pages with different vocabulary, different default periods (30 days against 90) and different framing — but the **same permission and the same response**, because two pages showing different numbers for the same thing means one of them is lying.

**Why the platform overview is a different endpoint.** It is the one place that crosses the tenant boundary, and that privilege is confined to `PlatformQueries` on purpose ([README.md](README.md) §Tenant behaviour). A store's dashboard has no bypass at all: inside a store scope the ordinary tenant filter already narrows every table, so `StoreReportQueries` is a normal query service. Even a bug in it cannot reach another store's rows.

## 2. What a number means

The definitions are in the header comment of `src/Souq.Application/Features/Reporting/StoreDashboard.cs`, and they are repeated here because a dashboard whose vocabulary lives only in code will be read wrongly with confidence.

| Term | Definition |
|---|---|
| **A counted order** | An order that was placed **and** is `Paid`, `Shipped` or `Delivered`. `Pending` has not been paid; `Cancelled` did not complete |
| **Revenue** | The total of counted orders in the period — each order's `PlacedTotal` snapshot, so it **includes shipping and is already net of any coupon discount**. Not a recomputation from the lines |
| **Refunds** | Refunds settled against counted orders, attributed to the **order's** period, not the refund's |
| **Net revenue** | Revenue minus those refunds. This is the headline figure on both dashboards |
| **Average order value** | **Revenue** ÷ counted orders — gross, **before** refunds. This table said *net* until M12; the code, the Arabic formula header, the on-screen hint and a test all say gross, so the table was the outlier. The consequence is worth stating: with a refund in the period, `AOV × orders` does not equal the net revenue shown beside it |
| **New customers** | Customer accounts created in the period, **excluding any since erased**. Not "customers who bought" — an account is what the system actually records. The erasure filter has a visible consequence, undocumented until M12: a **closed past period can show fewer new customers than it once did**, because the right to erasure means leaving no row to count |
| **Repeat customer** | A customer with two or more counted orders over the store's whole life, not within the period. Note the inconsistency with **New customers** above: this one does **not** exclude erased customers, so an erased buyer still counts as repeat while no longer counting as new. Loyalty is cumulative; measuring it against a week is meaningless |
| **Orders by status** | Every order *placed* in the period, in every status including cancelled. This is deliberately a different denominator from revenue, and the panel says so |
| **Best sellers / category performance** | Ranked by revenue from counted orders, top eight |
| **Stock health** | A point-in-time snapshot over **inventory rows, which are variants** — one product with three out-of-stock variants contributes three, not one (this is what V3 changed and what V4 will relabel; see [ProductVariants.md](../Catalog/ProductVariants.md)). `available = on hand − reserved`, the same definition the Inventory module uses — but **`low` is not the same**: here it is `0 < available ≤ threshold`, while the Inventory module's low-stock list is `available ≤ threshold` and therefore *includes* out-of-stock, so the two screens report different numbers under the same word. This query also has **no product-status filter**, so archived products' variant rows are counted here while the Inventory page filters them out. All three divergences were undocumented until M12 and are now pinned by tests. It is not a period figure and is not compared to a previous one |

Two choices inside those definitions are genuine judgement calls rather than derivations, and are written down so they can be argued with:

1. **Refunds are attributed to the refund's period** (C11). Money that left the till in March reduces March, whatever month the sale was in, so "net revenue" is a cash figure a merchant can put beside a bank statement. **This reverses the earlier choice, and the reversal settles an argument this page was having with itself.** The original entry claimed attributing to the *order's* period "keeps a closed period closed"; M12 corrected that as backwards — re-reading January after a March refund returned a *lower* January — and then kept the behaviour anyway on the ground that a refund belongs to the sale it reverses. That ground is real but it is a cohort question, and it was being used to answer a cash question the dashboard's own label asks. Now a past period is genuinely immutable, which is what the page wanted all along. The cohort view (how much of what I sold in March came back) is a separate report and is not built. Two details make the switch correct rather than merely different: the sum comes from `Refund` rows, not `Payment.RefundedAmount`, which is a running total carrying no date at all; and it is filtered to `RefundStatus.Succeeded`, because `Refund.Fail` stamps `CompletedAt` exactly as `Succeed` does, so filtering on the date alone would deduct refunds the gateway rejected.
2. **The product name in "best sellers" is the order-line snapshot**, not the product's current name. A product that was renamed or archived stays readable in yesterday's report. The **category** name is the opposite: it comes from the live category, translated, because a category is a classification rather than a record of what was sold.

## 3. What is not measured, and why

This is a feature of the dashboards, not an omission from them. Both pages say it on screen — folded into a `<details>` for the manager, who knows the vocabulary, and stated plainly for the stakeholder, who may otherwise assume "revenue" means "profit".

| Not shown | Why it cannot be computed honestly |
|---|---|
| ~~**Profit, margin, cost of goods**~~ **Built in C11, and bounded.** `ProductVariant.Cost` is optional and `OrderItem.UnitCost` freezes it at sale time, so the dashboard reports the margin of the lines whose cost is known plus the share of revenue that covers. What is still missing is *allocated* cost — shipping, fees, returns handling — so this is gross margin on goods, not profit |
| **Conversion rate** | Nothing tracks visits or sessions. There is no denominator. Adding one means analytics collection, which is a privacy decision |
| **Forecasts** | Every figure describes a period that has already happened. Nothing on these pages predicts |
| **Customer lifetime value** | Now unblocked in principle (C11 gives per-line cost) and still not built: it needs a per-customer cohort view, which is its own report |

The frontend mirrors this discipline. `businessHealth.js` returns `null` — not `0` — for the repeat rate when a store has no customers at all, because "we do not know yet" and "zero per cent" are different facts.

## 4. The reading of the business overview

`frontend/src/features/reporting/businessHealth.js` turns the same response into a plain-language verdict. Three rules govern it:

1. **No new number.** Everything is derived from the dashboard response the manager also reads.
2. **No profit beyond gross margin on goods.** §3.
3. **No verdict without its reason.** The page never says "healthy" on its own; it says "healthy" and, on the same line, the fact that produced it. A reader who cannot see the reason cannot disagree with the judgement.

The thresholds are collected in one `THRESHOLDS` object so they read as a decision rather than as numbers scattered through the code: a ±5% change is the floor for calling a trend (below it is noise), refunds above 10% of revenue, cancellations above 20% of orders placed, unpaid above 25%, and one product above 50% of period revenue.

**The concentration share is measured against period revenue — and until M12 it was not.** `topProductShare`
divided the largest product's revenue by the **sum of the returned top eight**, so a store selling twenty
products had its leader measured against eight: the share was systematically overstated and the risk fired
early, while this page and the on-screen sentence both said *period revenue* and *sales*. It now divides by
`current.revenue`, with one deliberate asymmetry recorded here rather than hidden: best-seller revenue is
computed from order **lines**, so it excludes shipping and the order-level coupon discount, while `revenue` is
the order total and includes both. The share is therefore slightly **understated** — the safe direction for a
risk signal, which should be late rather than false. (It also now takes the largest product rather than the
first in the list, which happened to agree only because the server sorts.)

**Best-seller and category revenue do not reconcile with the headline revenue, by construction.** Both are
`Σ(unit price × quantity)` over order lines (`StoreReportQueries`), so `Σ topProducts.revenue ≠ Revenue` for any
store that charges shipping or honours a coupon. This page used one word, "revenue", for both figures and
defined it once, which read as a promise that they add up. They do not, and that is not a defect — ranking
products by shipping is meaningless — but it must be said.

`trendOf` compares against the **immediately preceding window of the same length**, and growth from zero is reported as "first sales" rather than as a percentage — a percentage increase from zero is not a number.

## 5. Zero data is a different screen

A store with no orders at all gets guidance, not a report of zeroes: a short line about what to do next and a link to the catalogue. `isBrandNewStore` and `hasNoActivity` in `frontend/src/features/reporting/dashboardView.js` distinguish two cases that look identical in the data and are not: a store that has never sold anything, and an established store that had a quiet week.

## 6. Why the charts are hand-written

The candidate libraries cost roughly 90 kB gzip against a first load of about 135 kB, to draw three chart types. Beyond size, each would have had to be taught the three things that are not optional here: right-to-left, the derived dark palette, and a text alternative for screen readers. `frontend/src/features/reporting/chartScales.js` is pure arithmetic with its own tests; the SVG components on top of it are small. Revisit if a real need appears for zooming, brushing, or a dozen chart types.

## 7. Requests and payloads

The manager's dashboard is **one** request for the whole page. That is not only a performance property: figures taken from several requests are figures from several moments, and a page whose panels disagree by a few seconds is a page nobody trusts.

| Page | Reporting requests | Payload | Rendered |
|---|---|---|---|
| Store performance | 1 | 2.4 kB | ~460 ms |
| Business overview | 1 | 1.0 kB | ~290 ms |

Measured against SQL Server through the development stack. All aggregation happens in SQL: a store with a hundred thousand orders costs the dashboard what a store with a hundred costs. The period is a **closed key** (`Today`, `Last7Days`, `Last30Days`, `Last90Days`, `ThisYear`) and the server computes its boundaries from the injected clock — the browser never sends two dates, so it cannot ask for an arbitrary or an expensive window.

**Three things about that window this page did not say, added in M12.**

- **The window is the store's own day, not the UTC day** (C11). `Tenant.TimeZone` now decides where "today" starts: the boundaries are computed in the store's local time with full `TimeZoneInfo` handling (including the midnight that does not exist on a spring-forward night) and converted to UTC once, for the query. The trend buckets follow the same local day, so the chart's labels — which the browser has always rendered in the store zone — finally describe the data they sit above. A time zone id that the host cannot resolve falls back to UTC and says so in `StoreTimeZone.Resolved` rather than failing the dashboard, because `Tenant.SetLocale` validates the *shape* of an IANA id and deliberately does not consult the OS zone database. **One limit remains, and it is narrow:** the SQL-side bucketing shifts by a single offset taken at the window's start, because EF Core 10 does not translate `AT TIME ZONE` (tried two ways) and raw SQL is barred here. Boundaries and totals are always exact; only in a zone that observes DST, and only on the two transition days, does one hour of orders land in the neighbouring bucket.
  midnight. `Tenant.TimeZone` exists and is surfaced to the platform, and reporting does not consult it — so a
  merchant in UTC+3 sees "Today" begin at 03:00 local, and the previous local evening's orders counted as
  today's. That is a real limitation, not a rounding detail, and it is stated here so nobody has to discover it
  from a number that looks wrong.
- **The range is half-open, `[from, to)`, with `to` set to tomorrow's midnight** — so the current day's bucket
  and the period's last day are partial by design.
- **`Last90Days` and `ThisYear` are bucketed by month, not by day**, and their first and last buckets are
  partial; the first is stamped with the 1st of `from`'s month, which is earlier than the window itself. The
  chart's hint and its screen-reader summary said "by day" for every range until M12; they now follow the
  bucketing, derived from the data rather than from a list of range names.

**Four figures the response returns that this page did not define, and their scope, added in M12.** All four sit
beside period KPIs and are **not** period-scoped: `pendingOrders` (all placed orders still `Pending`, all time),
`pendingRefunds` (all time — and since C11 it counts **refund requests** awaiting settlement, not payments: one
payment with three pending refunds is three items of work, which is what the alert is for), `totalCustomers` (all non-erased accounts, all time, buyers or not — and it is
the denominator of the browser-computed repeat rate, which therefore is not "the share of buyers who returned"),
and `trend` (above).

**One gap worth knowing rather than discovering.** A payment can be captured on an order that was already
cancelled (the reconciliation case `Payment.MarkCapturedAfterClose` exists for). That order is `Cancelled`, so
it is not a counted order: the money is excluded from revenue, and its payment is excluded from the refunds sum
because that sum is scoped to counted orders. Until someone requests a refund, **money genuinely taken appears
in no figure on either dashboard.** The operator's signal for it is the payment record, not the dashboard.

## 8. Authorization

| Rule | Where |
|---|---|
| `store.reports.view` is required; it is granted to TenantAdmin and TenantStaff | `StoreReportsController` with `[HasPermission]` |
| The store comes from the resolved host, never from a parameter | `ITenantContext` in `StoreReportQueries`. There is no tenant id in the route, the query string or the body, so there is nothing for a caller to change |
| A token issued for one store is rejected on another store's host | `AccessTokenValidation`; asserted in `tests/Souq.IntegrationTests/StoreDashboardTests.cs` |
| A customer gets 403, an anonymous caller 401 | Permission policy; asserted in the same file |
| Every dashboard read is audited (`store.dashboard.viewed`) | `ModuleAndContractRuleTests` requires it of every Reporting request |

The frontend route guard is a convenience. The backend is authoritative, and the integration tests assert the boundary rather than the route.

## 9. Tests

| Level | Class | What it covers |
|---|---|---|
| Application | `tests/Souq.Application.Tests/Reporting/StoreDashboardTests.cs` | Window arithmetic for every range, and the previous-period boundary |
| Integration | `tests/Souq.IntegrationTests/StoreDashboardTests.cs` | An unpaid order is not revenue; revenue and average order value; cross-store isolation; a foreign token; customer and anonymous refusal; staff access; an empty store returning zeros with a full trend; an unknown range rejected; the audit row; and that category performance shows a translated name rather than a slug |
| Frontend | `frontend/src/features/reporting/*.test.js` | Scales, the view model, and the business reading — including that a verdict always carries a reason |
| Browser | `frontend/e2e/experience.spec.js` | Both dashboards on a real stack: real figures, the stated limitations, the period key in the request, dark mode, and a text alternative for every chart drawn |

## 10. Relationship to the roadmap

The store dashboard is the work the roadmap describes under **Phase 17**, and the platform overview is the reporting slice of **Phase 18**. They were built here because the experience work needed them, not as a new phase. [ProductRoadmap.md](../../12-ROADMAP/ProductRoadmap.md) records Phase 17 as complete, and for Phase 18 what is done and the two items that wait on owner decisions (storefront preview, D-22, and platform-wide settings, P-07).
