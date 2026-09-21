# Souq master plan: the long-term execution contract

> **What this document is.** The durable, repository-resident execution contract for taking Souq from its
> current state (roadmap Phases 1A–18 complete or 🟡, [ProductRoadmap.md](ProductRoadmap.md) §6) through to
> genuine launch readiness. It exists so that **any future Claude session — with no memory of this or any other
> conversation — can read the repository, this file, and the registers it points to, and know exactly what to do
> next without needing a prior chat, a ChatGPT transcript, or a human standing over it.**
>
> **What this document is not.** It is not a second roadmap. [ProductRoadmap.md](ProductRoadmap.md) remains the
> authority on **product capability** (its Phases 1–23, never renumbered). This plan is the authority on **how
> the remaining engineering work — much of it inside roadmap Phases 19–23, some of it new scope this plan
> identifies (the search engine, above all) — gets executed, verified and closed**, phase by phase, to a
> professional standard. Where the two disagree, [ProductRoadmap.md](ProductRoadmap.md) wins on *what "done"
> means for a product capability*, and this document wins on *the sequence and the completion protocol*. §7
> below is the exact crosswalk.
>
> Per [AGENTS.md](../../AGENTS.md) §5, this plan's twenty phases are **engineering-mission phases**, numbered
> `M1`–`M20`, and take no roadmap number — the same convention already used for
> `phase/16-engineering-knowledge-and-handoff` and `phase/17-production-hardening`. They are not the branch
> sequence number either. Never call `M3` "Phase 3" without the `M`: Phase 3 is Authentication and authorization,
> done long ago.
>
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`, at checkpoint `d45bb53`
> (the commit this plan was authored against; see §0 for the live pointer once phases start closing).

---

## 0. Status (machine-readable)

```yaml
plan_version: 1.0.0
current_phase: M20
phase_status: done
next_phase: done        # terminal, and now complete: M2's one blocked deliverable (TD-42 = C, links now) was
                        # released on 2026-09-21 and SHIPPED on 2026-09-22 inside the commercial track's C8.
                        # Nothing in M1-M20 is open. The live pointer is CommercialPlatformPlan.md §0.
blocked_decisions: ["GitHub Actions billing"]   # TD-42 and D-13 were both answered on 2026-09-21 — see
                        # OwnerDecisions.md and CommercialPlatformPlan.md §0. The billing block still stops CI
                        # running at all, and is the only thing left here that is not a commercial-track decision.
last_verified_date: 2026-09-21
last_verified_head: b476a8d         # The M1-M20 plan itself stays terminal. The live pointer for everything
                                    # after it is CommercialPlatformPlan.md §0 — read that block first: it carries
                                    # C1/C2/C11 and the unblocked-debt work that followed, and it lists every
                                    # remaining phase against the owner decision that gates it.
                                    # Was 2288d65, and 50a8f01 before that
baseline_branch: phase/17-production-hardening
```

### After the plan (2026-09-19)

The twenty phases are closed and **this section is not a twenty-first**. It exists because work continued after
the contract ended, on the owner's direct instruction — *make the application actually runnable and usable now* —
and a later session reading only §0 would otherwise not know that the head moved or why.

Nothing here changed the plan's scope or reopened a phase. What it did was **run the thing**, in a browser, on
the stack this repository documents, and fix what that turned up. Six defects, each measured before it was
touched:

1. **`docker compose up` did not work at all** on an Apple Silicon host. The database healthcheck's 25 s start
   window expired while SQL Server — emulated, because Microsoft publishes the image for `linux/amd64` only —
   was still initialising; `sqlcmd` alone took 8.9 s to fail against a 5 s timeout. Both `api` and `web` wait on
   `service_healthy`, so nothing started. Troubleshooting §21.
2. **Every legitimate rejection was logged as a 500 at Error** — 403, 409, 422 alike — because the request log
   sits inside `UseExceptionHandler` and assumed the outer handler would write 500.
3. **A visitor leaving a page logged a server error**, from the authentication layer, for the same underlying
   reason in a different place.
4. **Admin tables could not be scrolled without a mouse** on a phone (WCAG 2.1.1). axe had only ever run at
   desktop width, where the table does not overflow and the rule passes honestly while measuring nothing.
5. **A loading table announced five empty rows as its answer** — no `aria-busy` anywhere on `DataTable`.
6. **Which words fed typo recovery was left to the database**: `Take` without `ORDER BY` over the store
   vocabulary, so the same typo could recover once and not the next time.

And **TD-57's open question was answered.** M20 recorded three journeys failing in isolation with the cause "not
established"; all three causes are now established, reproduced deterministically and fixed — see the row itself.
The class stays open only for its real remainder: the journeys share one seeded store.

**Verified at `e276f54`:** the documented stack starts and serves (web `:8081`, API `:5201`); smoke test 31/31
against it; **109 browser journeys pass with none failing** (96 desktop across 15 files, 13 on a Pixel 7), which
is the first clean full pass this repository has had. Three journeys still need a Development API by design and
were not part of that run. The launch position in `ProductionReleaseChecklist.md` is unchanged: everything that
asserts something about *the repository* is green, everything that asserts something about *a deployment* is
still unmet, because there is still no deployment.

### And after that (2026-09-20, `f5cfece`)

Two further sessions on the owner's instruction, still not a phase and still changing no phase's scope. They
are recorded here for the same reason the section above exists: the head moved twice more.

**Four defects, each measured before it was touched, none of them a flake.**

1. **F-25 — a second sign-in at the same instant lost to the first.** Every successful login writes bookkeeping
   to the rowversioned `User` row, so eight concurrent logins for one account returned 2×200 and 6×409.
   `AccountWriter` retries the *whole decision* on committed state — not just the save, because replaying only
   the save would issue a session against a password hash a concurrent change had just replaced. `PasswordCheck`
   makes a retry cheap (BCrypt once per hash, not once per attempt), which is the only reason a sixteen-attempt
   budget is safe rather than a CPU-exhaustion amplifier.
2. **F-26 — the search-insights pager never disabled *Next***, because one call site of sixteen passed
   `pageSize`/`total` where `Pagination` takes `totalPages`. Found by chasing an intermittent journey failure.
3. **F-27 — five of a store's eight social networks rendered as raw lowercase text** in the footer. A new
   architecture test now compares the footer's icon map with `SocialLink.Networks`, so the gap cannot reopen.
4. **F-28 — a shopper who filled a basket before signing in could get `409` on a `GET`.** The first basket read
   after sign-in merges the guest basket and deletes it, and it creates the customer's basket if there is none;
   two concurrent requests raced on both. Fixing only the first exposed the second, which the two-request
   measurement had hidden.

**The commercial SaaS readiness audit** (§0's own question, answered): [CommercialReadiness.md](CommercialReadiness.md)
records what is confirmed working, the exact provisioning process today, what a merchant can customize, the gaps
ranked, and the smallest architecture for plans and billing — **deliberately not built**.

**D-13 is the gate for all of it, and it is still the owner's.** It was re-examined on 2026-09-20 rather than
restated: the decision record framed it as a binary and omitted the position the product is actually in — a
store *may* connect its own account, and one that has not is paid into the deployment account, so the merchant
of record currently varies per store. Neither store on the QA stack has an account, so the platform is merchant
of record for both. That third state is now written into `OwnerDecisions.md` with the repository constraints
attached to each answer. **No decision was made on the owner's behalf, and no commercial code was written.**

Two of the audit's architectural claims were verified against the code rather than left asserted: the
platform→merchant billing path genuinely cannot reuse the store→shopper one (it throws at platform scope, and
`Payment` cannot exist without an order), and the quota-concurrency claim was **corrected** — the safe pattern
writes first and re-counts, the repository's existing caps are racy, and the invariant the whole thing rests on
is asserted nowhere (TD-68).

**M1 — done.** Closed TD-04 (payment-account use cases moved from Platform's folder to Payments, reaching the
platform admin path through a published contract, `IStorePaymentAccountEditor`, rather than a raw class
reference); corrected TD-04's review-settings half, TD-02's extraction-order claim, and TD-01/TD-03/TD-05 with
today's evidence; crossings 79 → 74 across 15 pairs (was 16); `RiskRegister.md` R-04 closed, R-14/R-15 updated.
Full evidence in M1's own "Completion evidence" field above.

**M2 — closed on 2026-09-22.** Its one blocked deliverable shipped: policy links on the store's settings, with
a footer that renders only the links a merchant actually set. Nothing in `M1`–`M20` is open any more. The
paragraph below is kept as the record of how it was blocked and how the block was lifted.

**M2 — audit complete, and its one blocked deliverable was released on 2026-09-21.** `Catalog/README.md` and
`ProductVariants.md` were re-read in full against the current code: no drift found, nothing to correct. TD-42
(store-authored content pages) needed a scope decision only the owner could make, and the owner answered **C —
links now, revisit authored pages when a real merchant requires them**. The deliverable is therefore *policy
links on the store's settings with a footer that links out*, and it was built inside the commercial track's `C8`
rather than by re-opening this phase — `M1`–`M20` stay terminal per §0. *ContentPage* keeps its deferral with
its trigger named. Variant-image gallery confirmed still genuinely not built (no schema support at all) and
left correctly deferred, not built speculatively. No code changed in M2 itself.

**M3 — done.** The catalog now matches a stored, indexed **normalized** form of its text, so Arabic search works
as people actually type (unvocalized, ه for ة, ي for ى); every query word is a separate condition matched in a
name, a description or a **category** name; `ProductSortBy.Relevance` ranks in SQL; a typo recovers from the
store's own catalogue vocabulary and **says which word it searched**, with the shopper able to refuse
(`exact=true`); suggestions are a real keyboard-operable combobox; and a merchant can teach the vocabulary its
customers use. SQL Server Full-Text Search was **measured unavailable** in the pinned image and rejected on
evidence rather than assumption — see M3's "Completion evidence" above and
[ADR-0042](../11-ADR/0042-local-search-engine.md).

**M19 — done, and the certification found that some of the certification itself was fiction.** The phase's own
question — does every capability have a named test? — was answered by reading the **bodies** of the tests the rule
tables name, across all 229 rules. The reassuring half: every Arabic method name cited exists. The other half: about
two rules in five name a test that asserts only part of what the rule says, and **nothing in the build can tell**,
because the documentation checker's identifier pattern is ASCII-only and cannot see an Arabic method name at all.

`Traceability.md` was rewritten rather than patched. It was wrong in both directions: three rows named *files* where
its own legend promises *classes*; **five gaps it recorded as open had been closed** in M5, M9, M14 and M16; two rows
were contradicted by the rows directly beneath them; and five capability areas that shipped after M8 — catalog
search, search analytics, reporting, observability, deployment — had no row at all. A page whose stated purpose is
that a recorded gap beats a claim of coverage was failing hardest in exactly that direction.

Three defects were fixed rather than filed. **Money:** the merchant dashboard rounded average order value to two
decimals in code, so every three-decimal store — the dinar, this repository's own default — saw an average that
cannot exist in its currency, and a zero-decimal store saw cents; and the storefront derived currency precision from
the *browser's* table while ignoring the value the server sends it. **Accessibility:** `StarRating` announced five
disabled buttons on every product card and never the rating, and its interactive mode was a `radiogroup` whose
children were not radios — axe in jsdom flags neither.

The full browser suite was run for the first time as a suite, and was **not green**: 97/12, then 103/10 serially.
Every remaining failure was taken to its cause and none was a flake — a `settle()` helper that waited for animations
that never end, a locator that matched two live regions, a navigation that cancelled an in-flight add, a journey
asserting a feature that is off by default while its three siblings passed vacuously, and a journey that pressed a
button the product correctly disables. Three journey groups need a Development API, and that too is the product
being correct: in Production a store is reached through a verified domain, and an invitation link must never reach a
production log. See M19's "Completion evidence" above and `docs/10-TESTING/TestingStrategy.md` §6.

**M18 — done, and it began by discovering that CI had never been green.** The first act of the phase was to look
at the actual runs rather than the workflow file, and every run on GitHub had failed: the recent ones in three
seconds on *"recent account payments have failed"*, and the last one that executed on a **test-inventory mismatch
between macOS and Linux**. Five real defects came out of that, none of them findable by running `dotnet test`
again on the development machine:

1. A regex counting frontend tests gave **different answers on macOS and Linux for identical bytes on identical
   .NET 10** — a greedy `.*` inside an *optional* group, resolved differently by the engine's auto-atomicity
   optimization. Reproduced both ways in a container before it was touched; the committed inventory was wrong by
   one, so CI was red on every push.
2. Two duplicate `using` directives: warnings locally, **errors** under CI's `-warnaserror`.
3. `backup-verify.sh` computed backup age with `python3` and, on a host without it, printed "no age check" and
   **exited 0** — the backup alarm failing open on exactly the minimal host a backup job runs on. Now `date`-only
   with GNU/BSD fallbacks, failing *closed*, and a future-dated stamp is a failure too (a wrong clock makes every
   backup look fresh forever).
4. `docker compose up` **crashed on startup**: M17's `Database:MigrateOnStartup` guard was correct and nothing
   set the value, so the only documented deployment path was dead. A test now derives the required settings from
   `Program.cs`'s own guards and asserts the shipped stack supplies each — mutation-tested three ways.
5. A flaky test: `SouqMetricsTests` asserted exact equality over a **process-global** `Meter` that parallel tests
   emit into. 120 consecutive Linux runs clean after the fix, and the fix still catches a counter that stops
   emitting.

Then the CD half itself: a SemVer-tagged release builds versioned images and a migration bundle, and
`scripts/deploy.sh` deploys, waits for `/health/ready`, and **rolls back only when the schema did not move** —
refusing, and printing the restore path, when it did or cannot be read, because an old image on a newer schema is
worse than the outage being escaped. All three branches were exercised on a real stack, including a deliberately
broken version that was automatically rolled back. R-18's deliberate step stopped being a description: the bundle
was built, run against a live database, stepped **back one migration and forward again**, and driven through
`deploy.sh --migrate-bundle` with `MigrateOnStartup=false`.

Browser QA against what the pipeline deployed found a **real keyboard-accessibility defect**: the row actions
menu closed on any scroll, and `scroll-behavior: smooth` means tabbing to a row below the fold scrolls for dozens
of events after the menu opens — so the menu opened and vanished, measured at 32 scroll events and zero menus.
That control was unusable by keyboard on any row below the fold. It now repositions instead of closing; the first
attempted fix (closing when the trigger leaves the viewport) reproduced the same bug from the other side and was
rejected on measurement.

**What M18 did not close, and cannot:** GitHub Actions is billing-blocked, so neither pipeline has run — the CI
fixes above are verified on Linux locally, not observed green on GitHub — and there is no server, so the SSH
deploy step has never executed. Both are owner actions, written out in
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) with the options and what each costs. See M18's
"Completion evidence" above and [ADR-0046](../11-ADR/0046-continuous-delivery-and-rollback.md).

**M17 — done, to the boundary the phase's own text draws.** R-18 stopped being an accepted risk with a described
remedy and became a built one: migrations at startup is now an **explicit choice** outside Development, and
turning it off gives the deliberate bundle step — built *before* a second instance exists, because the moment one
is added is the moment nobody is thinking about it. TD-18 closed along the way and stopped being a convenience:
`dotnet ef` now runs from a clean checkout with no secrets, which the deliberate step requires. R-12 was
re-verified against today's code and was overdue — M13 added two background services since the last measurement,
exactly the shape that fails silently — and passed with zero permission denials and every reverse check still
refusing. Metrics are instrumented with **no new dependency** (ADR-0045 separates what to measure, an engineering
decision, from where to send it, a deployment's). What remains open is named one by one, each because it needs a
real certificate, domain, account or spend. See M17's "Completion evidence" above.

**M16 — done.** Measured first, then changed almost nothing — which is the result the phase's own rule asks for.
There is **no N+1** in any read path (now guarded by a command-count test rather than a timing one), and every
route meets its latency target. The measurement changed the conclusion twice: the plan cache named M13's search
insights as the most expensive statement in the system at 25,375 logical reads a call, and reordering one index
took it to **107** on the running stack; while the icon barrel that looked like an obvious 24 kB win turned out
to hold only ~5 kB of admin-only icons, so it was left alone. Under load the API sits at 0.54% CPU while SQL
Server runs at 47%, so **caching was declined on evidence** (ADR-0044) and the next lever is database capacity.
The load test the strategy named as missing now exists, and the first-load budget the design system promised is
enforced at 170 kB against a measured 156.8. See M16's "Completion evidence" above.

**M15 — done.** The security review found real things. The sharpest was **proven exploitable on a running stack
before it was fixed**: the rate limit keyed on the host as it arrived while tenant resolution lower-cased it, so
changing the capitalisation of `Host` gave every request a fresh bucket — ten logins then 429, then fourteen more
varying only the case, all accepted, straight through nginx. A sibling host under a merchant's own domain could
also plant a session, because `SameSite` governs sending and not setting; `__Host-` closes it. Two findings were in
this programme's own recent work, including M13's retention policy, which was not actually enforced under any load.
Three were claims the control catalogue made that the code did not keep — SEC-LOG-09 is now PARTIAL with TD-55
filed. The detection half was nearly empty: failed sign-ins produced no log line and no log carried a client
address; both now do, verified in a real process. RLS was **decided rather than deferred** (ADR-0043: declined,
with the three conditions that would reverse it). Every finding is fixed with a test or accepted in writing —
fourteen and eight. See M15's "Completion evidence" above.

**M14 — done.** TD-34 closed. The outbox's purge, lease and dispatcher race were asserted only by reading; each
now has a test that was **proven to fail against a deliberately broken processor** before being kept. The race is
real rather than simulated — the first dispatcher is held inside the email provider while holding the lease — and
it asserts the product claim, that the customer receives one email. The lease test covers the half that matters:
an expired lease must *release* a message, or a process that died holding one freezes it forever. The module
document's six message types and three in-app kinds matched the code exactly, and that fact is now a test, because
the existing documentation tests only check that written names exist and never that existing names are written.
ADR-0033's promised review notifications are still not built — re-confirmed, left as the owner's product question,
and the §4 note now test-backed. Verified on the running stack down to the two-hop cascade and a redacted
recipient in the log. See M14's "Completion evidence" above.

**M13 — done.** M3 shipped a vocabulary editor with no way to know what to put in it. The loop is now closed and
was verified end to end in a browser: a shopper searches a word that finds nothing, the merchant sees it, clicks
once, and the search then works — after which the word **leaves the work list on its own**, because it stopped
failing. Logging cannot slow search (a `void` non-throwing port over a bounded drop-on-full channel, drained by a
background writer inside each store's tenant scope) and `GetProductsHandler` holds that guarantee itself rather
than trusting the implementation, because a test with a throwing log proved it was not. Retention exists from the
first commit — 90 days, decidable without the owner because the table has no personal data by construction, with
a test asserting its exact column set so that reasoning cannot be quietly invalidated. Three defects in this
phase's own work were found before it shipped, including the identical scoped-service-from-root mistake M11 found
in the search backfill. See M13's "Completion evidence" above.

**M12 — done.** Of the ten metrics the dashboards document, four were verified, two were incomplete and four
had drifted — and four of the findings were the *product* misleading a merchant, not just a stale document: the
stock KPI counted variants while both dashboards said "products"; the concentration risk divided by the top
eight rather than period revenue, so it fired early; the trend chart said "by day" on ranges bucketed by month,
including in its screen-reader summary; and two raw translation keys were rendering to users, one of them in the
admin sidebar **on every page since M3**. Both key leaks escaped the key-parity test for the same structural
reason, and both shapes now have guards that were verified to fail against the real bugs. The stock snapshot —
which had no test at all, which is why V3's change of counting unit passed unnoticed — is now pinned, unit
included. V4's scope is written out and is smaller than it was, because two of its four items were wording
problems fixed here. See M12's "Completion evidence" above.

**M11 — done.** D-22 and P-07 are still genuinely the owner's — no preview code and no platform setting exist
anywhere, and nothing is built around either. The verification pass earned its keep twice over. It found that
**the API could not boot in Development at all**: the search backfill resolved a scoped service from the root
provider, which only Development's scope validation catches, so the documented `dotnet run` had been broken
since M3 — and the guard now lives in the test factory, so the same mistake fails a test instead of greeting
the next person who clones the repository. It also found the platform boundary was airtight in one direction
and merely *sampled* in two, including no coverage at all that a PlatformAdmin is refused the endpoints that
create and disable platform accounts. Both are now counted, from `RolePermissions` rather than a written list.
And the module README documenting the availability gate turned out less accurate than the brief that merely
depends on it. See M11's "Completion evidence" above.

**M10 — done.** The admin area is entirely on the query layer (TD-25 closed, `set-state-in-effect` 20 → 7,
with the remaining seven all outside it), and the status-colour vocabulary is one module instead of
twenty-two classes in four CSS files — three of which were byte-identical copies. Both items were bigger than
their entries said. The one that mattered most was neither: the only axe sweep that visits dark mode had the
contrast rule **switched off with no recorded reason**, and behind it sat four real defects — the store's own
wordmark at 2.15:1 on every page, secondary text at 4.42:1, the 404 code, and a product badge at **1.06:1 in
dark mode, invisible**. All fixed; the full matrix is now clean with the rule on. See M10's "Completion
evidence" above.

**M9 — done.** A signed-in customer can finally change their password — everything under that screen already
existed and nothing called it — and the phase's demand for *a test that actually attempts the attack* paid for
itself: the attack test found that a refresh token another device had just rotated **survived a password change
for ten seconds** and could mint a fully valid session, which meant an attacker rotating every five seconds
survived every password change. Fixed with no new column, by requiring the token's family to still be alive. The
password policy, which guards every credential in the system, also turned out to have no test at all. And the
register's rank-1 item is closed: the Identity ⇄ Customers cycle is gone, measured by the ratchet at 74 crossings
across 15 pairs → 62 across 13, with the test suites moved to the right side of the boundary too. See M9's
"Completion evidence" above.

**M8 — done.** The platform now has **one** coupon evaluator. `GET /api/coupons/apply` priced a code against a
subtotal the caller supplied, so it skipped the per-customer limit, measured the minimum against a number the
caller chose, and answered a nine-digit subtotal with an eight-digit discount as a `200`; it was deleted rather
than rewritten, because TD-06's other option — reimplementing it over `IPricing` — **is** the basket quote, which
is already anonymous, already rate-limited the same way, and already prices the caller's real basket. Four tests
that used the route were re-pointed rather than dropped, one of them the reviewed public-endpoint list that
stopped the deletion passing unreviewed. R-09's residual was re-confirmed unreachable by tracing the code, dated,
and given two honest limits — and **no currency column was added**, because the path cannot be walked. The coupon
also got its first browser journey ever, asserting that the total the shopper saw is the total the order recorded.
One documentation claim was corrected that P-06 depends on: introducing tax is not a drop-in, because Ordering
has no tax slot. See M8's "Completion evidence" above.

**M7 — done.** Inventory's never-negative invariant now holds against the race that actually happens —
**commit against release on the same reservation**, a payment confirming in the instant the expiry sweep
releases the hold — which no existing test covered (they all raced two *different* reservations). The ledger
invariant Σ movements = on hand was audited across every write path, found sound, and then **ratcheted**: a
rule now fails the build if `StockMovement` is ever constructed inside Infrastructure, which `InternalsVisibleTo`
makes possible and which would break reconciliation silently. TD-25 was closed for the inventory screen after
proving the stale-page defect real. A **32px** row-actions button — the only way to act on any admin table row —
was found at phone width and fixed in the shared component under `@media (pointer: coarse)`. Two of the phase's
own tests turned out to be the problem: §11 overstated what it measured, and the platform phone test could never
run against the container stack. See M7's "Completion evidence" above.

**M4 — done.** Every storefront route now holds its layout at 320/768/1280/2560 in both languages, with no
control outside the viewport, no touch target under 24px, and no axe violation in either language or either
theme — asserted by `frontend/e2e/responsive-storefront.spec.js` against the container stack. Five defects were
found and fixed, each visible at only one width in one language; the most serious was a navbar that **silently
clipped the cart button** between 768px and ~940px because `overflow-x: hidden` hid the overflow instead of
showing it. The one acceptance criterion **not** met — a cyclic focus trap in dialogs — is recorded as TD-48
rather than claimed. See M4's "Completion evidence" above.

**M5 — done.** Checkout's most important use case is no longer one ~120-line method with sixteen dependencies:
`CheckoutQuote` (no effect) → `OrderPlacement` (one transaction) → `CheckoutPayment` (outside it, with
compensation), closing **TD-13** without rewriting a single test case. **R-24** is decided: background sweeps now
cover suspended stores, whose expired stock holds were previously released by nothing at all — a change to
BR-TEN-22, made because that rule was recorded as UNTESTED with this exact consequence as a known defect.
**F-8 remains the owner's call**, and M5 established that the browser is not a source of duplicate checkouts, so
the choice stays a server-side one. The no-oversell criterion was **already met** and was verified rather than
rebuilt; the real gap — concurrency with mixed quantities — is now covered. See M5's "Completion evidence" above.

**M6 — done, and nothing about money changed.** The phase's value is that claims became provable and four
documented ones turned out false. **P-05** is now one switch with *both* hypotheses tested, so the owner's answer
is a one-value change in front of a green test instead of conversion logic written under pressure. **R-03** is
pinned: staff can refund by cancelling a paid order, admins by refunding explicitly — and until now implementing
the "yes" answer would have left every test green. Three places claimed refunds follow the account that took the
money; they follow its *kind*, and replacing a store's keys strands a refund (**TD-50**, P1). A security claim
with no test behind it now has one. **TD-51** and **TD-52** were filed rather than fixed, because both would
change payment behaviour or payments infrastructure inside an audit phase. The fake gateway's Production guard
was verified by booting a container, not by reading the code.

### And after that (2026-09-20, the commercial architecture)

Still not a phase, and it changed no phase's scope: **no code was written and no commercial feature was built.**
What it produced is the durable design for the commercial track, so that the first line of commercial code is
written against a decided architecture rather than under pressure from a signed contract:
[CommercialPlatformArchitecture.md](CommercialPlatformArchitecture.md) (the subsystem catalogue, the two money
paths, the provider-agnostic payment port, quotas, the event foundation, customization, privacy, scale-out),
[CommercialPlatformPlan.md](CommercialPlatformPlan.md) (phases `C1`–`C14`, their dependencies, the deferral list
and the owner decisions), and six ADRs, `0047`–`0052`, each **Accepted as the design and not implemented**.

Three things it corrected rather than asserted, each verified against the code:

1. **§0's `last_verified_head` was stale by two commits** (`f5cfece` against a `50a8f01` tip), which §5's first
   STOP condition names explicitly. Corrected above.
2. **A quota must not copy the last-administrator guard.** That guard is safe only because the default isolation
   level is *locking* read-committed — `READ_COMMITTED_SNAPSHOT` appears exactly once in this repository, in its
   own comment, and is asserted nowhere — and **its test cannot reach the guarded branch**: three administrators
   are seeded and two are disabled, so the in-transaction re-count for zero never fires and the assertion passes
   by arithmetic. [ADR-0049](../11-ADR/0049-tenant-quota-enforcement.md) chooses a counter row instead and closes
   TD-68 with it.
3. **P-05 is smaller than its own record says, and P-06 is larger.** `StripeAmountConverter` already carries an
   explicit `HonoursIsoDecimals` switch with **both** hypotheses pinned by tests, so answering P-05 is a
   one-value change in front of a green test. Meanwhile Jordan's e-invoicing is a *clearance* model, which makes
   tax a separate outbound integration rather than a pricing-pipeline field.

The commercial track is gated on **D-13**, and research for this work sharpened that question rather than
answering it: Stripe does not operate in Jordan (a Jordan-registered connected account is recipient-only and
cannot process payments), and every mainstream merchant-of-record vendor checked excludes physical goods *and*
forbids a platform reselling for third-party sellers. Both named options in the original D-13 framing are
therefore narrower than they look. No decision was made on the owner's behalf.

### And after that (2026-09-20, `C1`)

**The commercial track started, and it is its own plan.** `docs/12-ROADMAP/CommercialPlatformPlan.md` owns the
phases `C1`–`C14`; this section is not their home and will not grow a phase per `C` item. It records only that
the head moved again and why, so a later session reading §0 is not surprised.

**`C1` — the commercial control plane — is done.** A fourteenth module, *Billing*, now owns plans, subscriptions
and entitlements; it owns no money and touches no payment code. The one thing worth carrying forward from it is
the defect it closed rather than the feature it added: `TenantInfo.HasModule` **failed open**. Its default was
`null`, and `null` meant *every module is enabled*. So did the database default on the column behind it. For
three free optional features that was a defensible convenience; for a paid entitlement it gives the product away
silently, with a green suite. Closing it properly meant removing the parameter's default value entirely so the
compiler named all eight construction sites — a behaviour change that is only safe when it cannot be inherited
by accident.

Nothing any store can do changed: a *foundation plan* carrying exactly today's three free modules was created by
the migration and assigned to every existing store, and to every new one at provisioning. The design and its
consequences are [ADR-0053](../11-ADR/0053-entitlement-resolution.md).

Two guards were added that outlive the phase. Platform tables that carry a `TenantId` but have **no** tenant
filter — the shape the new commercial tables use — had no architecture rule at all, because there is no filter
to bypass and so the existing bypass rule can never fire for them; the new rule found a pre-existing reader that
no inventory had listed. And the platform area's privilege of carrying a `TenantId` in a request is now **earned
rather than declared**: a test proves every such request is reachable only through a platform-host endpoint.

### And after that (2026-09-21, `C2`)

**`C2` — quotas, and closing TD-68 — is done**, and its home is still
[CommercialPlatformPlan.md](CommercialPlatformPlan.md), not this section. Recorded here for the same reason as
the block above: the head moved, and a session reading §0 should not be surprised.

Plan limits are now **enforced**. The mechanism is one conditional statement — `SET Used = Used + 1 WHERE Used
< @limit` on a per-store counter row — and the reason it is that and not the obvious thing is the part worth
carrying forward: **the repository's own safe-looking count-then-write pattern fails open**, silently, under
`READ_COMMITTED_SNAPSHOT`, which a managed database this repository names as a possible target enables by
default. An `UPDATE` re-qualifies its predicate against the last committed value and takes an exclusive lock
regardless of isolation level; a `SELECT COUNT` does not. [ADR-0049](../11-ADR/0049-tenant-quota-enforcement.md)
had already decided this; C2 implemented it and **checked the check** — reverting the guard to count-then-write
made the concurrency test produce four products against a limit of three, so the test can genuinely fail, which
is the property ADR-0049 §obligation 4 exists to demand.

**TD-68 closed in two places, not one.** The quota does not inherit the invariant at all. The *existing*
administrator guard still does, and still depends on locking read-committed — so a startup check now reads the
database's own setting and warns loudly. A test could only ever have asserted the test container, which has the
setting off; the risk was always a host that has it on.

**Three decisions C2 made that no plan had named**, all in [ADR-0054](../11-ADR/0054-limit-semantics-and-catalogue.md):
an absent limit means **uncapped, not zero** (deliberately asymmetric with the entitlement gate, and the only
answer that does not stop every existing store); the limit names became a **closed catalogue**, reversing a C1
abstention without answering `C-12`; and archived products and disabled staff **do not count**, because neither
has a hard delete here and counting them would have made every limit a ratchet that only an upgrade can release.

**And a correction to what "green" meant.** `HEAD` at `38daea9` did not build clean from a pristine checkout: a
blocking `.Result` in C1's own test violates `xUnit1031`, and the incremental build was not recompiling that
project — so `dotnet build -warnaserror` reported success without ever compiling the file. Verified in a
throwaway worktree at `38daea9` before fixing. **A gate that runs on cached build state is not the gate**, and
§3 step 8's "actually ran on the exact commit being recorded" now has a concrete way to be false.

---

Keep this block current in the same commit that closes a phase: `current_phase`, `phase_status`
(`not_started` | `in_progress` | `blocked` | `done`), `next_phase`, `blocked_decisions` (the exact ID from
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), e.g. `P-06`), `last_verified_date` and
`last_verified_head` (the commit hash the phase closed at). A session picking this plan up cold reads this block
**first**, then confirms it against `git log -1` and `git status` before trusting it — the repository is still
the source of truth if this block and the commit history ever disagree.

**`last_verified_head` is the phase's last *work* commit, not necessarily the branch tip.** Closing a phase
takes one more commit — the one that writes this block and the phase's completion evidence — and that commit
cannot contain its own hash. So after a clean close the tip is normally one documentation commit *ahead* of
`last_verified_head`, and **that is not a mismatch**: §3 step 1's "stop and report" is for a tip that is
*behind*, *diverged*, or carries work this plan does not account for. Confirm with
`git log --oneline last_verified_head..HEAD` — if all it shows is that phase's own closing `docs(roadmap):`
commit, the checkpoint is intact and work continues.

---

### The commercial SaaS question, audited (2026-09-20)

Asked directly: **can this be sold to a customer as their own store, without a fork per customer?** Audited
against the running stack and the code, not the documents. The short answer is yes for the storefront, and the
gaps are commercial rather than architectural.

**Proven on the Production-mode container stack**, by creating a real second tenant through the platform API:
three calls — create tenant, register its host, activate — produce a second store that resolves on **its own
domain**, with its own name, slug, currency (USD beside the first store's JOD) and default language (`en`
beside `ar`), while the first store is untouched. `cross-tenant-adversarial.spec.js` then passes against the
pair. Nothing needed a code change, a branch or a rebuild, which is the actual test of the white-label claim.

**Isolation is structural, not per-query.** Every commercial row implements `ITenantOwned`; EF Core applies a
global filter that **throws when no tenant is in context rather than returning every row**; the unit of work
stamps and validates `TenantId` on write; a cross-tenant read answers 404, never 403, so the other store's
existence does not leak. `TenancyRuleTests` fails the build if a new entity misses the filter or the foreign
key, and `IgnoreQueryFilters` is confined to an audited platform path.

**What a customer can already have without touching code:** their own domain (unique platform-wide, one
primary), name per language, logo/favicon/social image, a colour palette the Domain refuses unless it clears
WCAG AA, one of five typography presets, light/dark default, the opening animation, contact details, social
links, SEO text per language, announcement, enabled languages, time zone, currency (locked once the store has
commercial activity), their own administrators and staff with permissions, and three toggleable modules
(promotions, reviews, wishlist) that the **server** enforces rather than merely hiding.

**What is missing before a store can actually be sold, measured in the code:**

| Gap | Evidence | Size |
|---|---|---|
| **No plan, quota or billing** on the tenant model — `Tenant` carries Name, Slug, Status, DefaultCulture, Currency, TimeZone, ReviewsAutoApprove and nothing commercial | `src/Souq.Domain/Platform/Tenant.cs` | The real blocker |
| **Custom-domain TLS is manual.** `VerifyDomain` is a platform action that stamps `VerifiedAt`; there is no DNS challenge and no certificate issuance | `TenantAdministration.cs`, R7 | Deployment + code |
| **Theme presets change nothing.** `classic/minimal/bold` validate and reach `<html data-preset>`, but no stylesheet reads the attribute — the only mention in `styles.css` is a comment | verified by search | Frontend only |
| **E-mail wording is shared.** A store personalises sender name, logo, primary colour and reply address (`EmailBranding`) — not the text | `Common/Notifications/Email.cs` | Product feature |
| **No store-authored pages** (terms, privacy, about) | TD-42, blocked on the owner | Owner decision |
| **No per-store product attributes**; the data model is fixed by design | WhiteLabel §5 | Deliberate |

**The extension model this points at — and deliberately does not build yet.** The temptation with a paying
customer is a fork, a per-tenant branch, or an `if (tenant == …)`. All three are refused by
[ADR-0011](../11-ADR/0011-white-label-architecture.md), and nothing found in this audit argues for changing
that. The smallest production-quality extension model, in the order that pays:

1. **A plan on the tenant** — a named tier plus a small set of numeric limits, enforced centrally the way
   modules already are (server-side, not UI-hidden). Modules become a property of the plan rather than a
   free-floating flag list. This is the one piece nothing else can substitute for, and it is small: a value
   object on `Tenant`, a check beside the existing module check, and the platform screen to set it.
2. **Make the preset attribute real** — give `classic/minimal/bold` actual layout switches. The hook, the
   validation and the plumbing already exist; only the stylesheets are missing. This converts "every customer
   looks the same" into three genuinely different storefronts for the cost of CSS.
3. **Store-authored content pages** (TD-42) — the most-requested customisation that is *content*, not code.
4. **Editable e-mail text per store**, bounded to the existing templates' slots.

Anything a customer asks for beyond those becomes a product feature for every tenant or is declined —
unchanged. **None of this is scheduled here**, and no speculative architecture was added: this section records
what an audit found so the next decision is made with the measurements in hand rather than under pressure from
a signed contract.

## 1. How a session continues this plan

A fresh Claude session — no prior conversation, no memory — reads, in order: `AGENTS.md` §0, this file's §0
status block, `git log --oneline -20` and `git status` on the branch named in `baseline_branch`, then the
module documents and registers §7/§8 name for the phase in `current_phase`. That is the entire recovery
procedure. Nothing else is required, and nothing else should be trusted over it.

### Commands this plan recognizes

| Command | Means |
|---|---|
| **`souq continue`** | Read this plan and the repository state, identify the first incomplete phase (`phase_status` ≠ `done`, or the lowest `Mn` with no completion evidence in §8), verify its checkpoint per §3 step 1, execute it completely per the protocol in §3, update §0, then **automatically continue to the next incomplete phase** without stopping to report a milestone. Repeat until a §5 STOP condition is hit or there is no incomplete phase left. |
| **`souq continue phases N-M`** | Execute exactly phases `MN` through `MM` inclusive, sequentially, each with the full §3 protocol, then stop and report — even if further phases are incomplete. |
| **`souq continue until launch`** | Equivalent to `souq continue`, but explicit that the run should not pause at phase boundaries to ask "should I keep going?" — it keeps going through **M20** unless a §5 STOP condition fires. A §5 stop is not a failure of the run; it is the run doing its job. |

**Do not stop merely because a phase, a subtask, or a milestone is complete.** Update §0, commit, and move to
the next incomplete phase in the same session, per the completion protocol in §3. A session only stops for the
reasons in §5, for running out of context (in which case leave the tree clean and §0 accurate so the next
session picks up exactly here), or because the requested range (`phases N-M`) is exhausted.

---

## 2. Non-negotiables carried from `AGENTS.md`

This plan adds sequencing and a completion protocol; it does **not** relax a single rule in `AGENTS.md` §0. In
particular, and because a long autonomous run is exactly where these are easiest to forget under pressure to
show progress:

- Never weaken a test, or the control a test protects, to make a phase look done.
- Never weaken tenant isolation, authorization or payment safety for convenience or speed.
- Never invent a business rule. A gap in [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) is a reason to
  stop and ask (§5), not to guess.
- **No premature distributed architecture.** Microservices, Kafka or any message broker, event sourcing, a
  database per module or per tenant, distributed transactions, Kubernetes, a second ORM, or replacing SQL
  Server or React remain named non-goals ([ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md)). No
  phase in §8 may introduce one. Where a phase's scope could tempt it (search, performance, scale — M3, M16,
  M17), the phase definition says explicitly what to build instead and what measured evidence would be needed
  to revisit that — never "it would scale better" as the reason.
- A new dependency is a decision, not a default. Propose it in the phase's commit/report; do not add it
  quietly. `MediatR` stays pinned to 12.x.
- Never read, print or commit `.env`.
- Never push or merge to `main` unless explicitly authorized. Never force-push, rebase, reset away work, or
  rewrite history. Coherent Conventional Commits; the repository builds at every one.
- Never stop a container this session did not start, and never run a destructive database operation without
  explicit owner approval (§5).

---

## 3. The completion protocol (every code-changing phase)

Every phase in §8, and every sub-step within it, follows this sequence. A phase is not "done" until every step
below has actually been run and its evidence recorded (§8's "Completion evidence" field, plus the phase's own
commit(s)) — not asserted from memory of a similar phase.

1. **Verify the checkpoint.** `git status` clean, `git log -1` matches `last_verified_head` in §0 (or the prior
   phase's recorded checkpoint), no interrupted Git operation (`.git/MERGE_HEAD`, `rebase-merge`, etc.). If it
   differs, **stop and report the mismatch** before touching anything — do not assume which version is right.
2. **Discover.** Read the phase's own definition in §8, the module document(s) it names, the ADRs it names,
   `BusinessRules.md` for the area, and the tests that already cover it. Inspect the actual current code — never
   trust a prior phase's report or this plan's own prose over what is in the repository today.
3. **Implement**, in the correct layer (Domain → Application → Infrastructure → API → frontend), following
   `AGENTS.md` §2–§5 and the module's `ChangeGuide.md` where one exists.
4. **Test**, at the level of the rule: a Domain rule gets a Domain test, a rule needing a lookup gets an
   Application test, tenancy and cross-module behaviour gets an Integration test, a user flow gets a browser
   journey. Testing is part of implementation, not a step after it — write the test with the change, not once
   the change already "works."
5. **Run the real application.** Rebuild the affected Docker image(s) when backend or frontend code changed
   (`docker compose build api frontend` or the touched service), bring the stack up (`docker compose up -d`),
   and confirm `/health/live` and `/health/ready` are green and the container logs show a clean start — a
   passing unit-test suite is not evidence the application runs.
6. **Verify representative flows** against that running stack — API calls with `curl`/`httpie` for backend-only
   phases, and the relevant Playwright journeys (`frontend/e2e`, one file at a time per the rate-limit runbook
   in [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md)) for anything a shopper, merchant or
   platform owner would notice.
7. **Fix any real defect found in steps 5–6** before continuing. Investigate it properly; do not paper over it
   with a special case, and do not weaken the test that caught it.
8. **Run the full applicable release gate**, per the phase's own "Test requirements" field, at minimum:
   ```bash
   dotnet build -warnaserror
   dotnet test tests/Souq.Domain.Tests
   dotnet test tests/Souq.Application.Tests
   dotnet test tests/Souq.ArchitectureTests
   dotnet test tests/Souq.IntegrationTests        # if the phase touched persistence, tenancy, auth, payments, or any request pipeline
   cd frontend && npm run lint && npm run typecheck && npx vitest run && npm run build
   ./scripts/release-gate.sh --suites             # mirrors the above in one command; add --env-file/--backup-dir/--api-url when a real target exists
   ```
   A controller, use case, module boundary or test file changed? Regenerate the inventories:
   `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`.
9. **Update documentation** in the same change: the module document(s), `BusinessRules.md` if a rule is new or
   changed, an ADR if an architectural decision was made (copy a recent ADR's structure — Status, Date, Related
   modules, Related ADRs, Context, Problem, Options considered, Decision, Consequences — and index it in
   [docs/11-ADR/README.md](../11-ADR/README.md); the next number is printed there), `TechnicalDebt.md` for
   anything closed or newly recorded, and this plan's §0 status block.
10. **Commit**, Conventional Commits, one coherent change per commit (a phase is usually several commits, not
    one). **Push the branch to `origin`** (never to `main` without explicit authorization — see §4), then
    **verify the remote HEAD matches** (`git ls-remote origin <branch>` against `git rev-parse HEAD`).
11. **Verify the working tree is clean** (`git status --porcelain` empty) and **record the checkpoint** — update
    §0's `last_verified_head` and `phase_status: done`, in the same commit as step 9's documentation update if
    practical, then push that too.
12. **Continue** to the next incomplete phase per §1, without stopping to ask permission for a phase this
    document already authorizes — unless step 1's fresh verification or the phase's own work surfaces a §5
    condition, in which case stop exactly there and report it.

### Docker policy (explicit)

- Rebuild the images this phase actually touched; do not rebuild the whole stack speculatively.
- Bring the stack up with `docker compose up -d --build <service>` (or the whole stack for a phase that spans
  layers), and verify health and logs before calling step 5 done.
- Exercise real flows against the running containers, not only against `dotnet run`/`npm run dev` — the two are
  different runtime configurations (Production-mode middleware, nginx headers, the container's own health
  probe) and a defect can exist in one and not the other.
- **Preserve volumes and data that were not created by this phase's own work.** Never run `docker compose down
  -v` against a stack holding data this session did not create. Never stop a container this session did not
  start (check `docker ps` before touching anything already running).
- Clean up only what this phase's own verification created (QA stores, QA products, disposable containers spun
  up for a rehearsal) — the same rule `DeveloperQualityGates.md` states for the Playwright journeys.
- **Record a genuine Docker/runtime limitation rather than weakening a test to route around it** — for example,
  insufficient free memory for SQL Server in the container running this session (`docker run --rm alpine free
  -m`, need ~2–3 GiB free per [AGENTS.md](../../AGENTS.md) §7). Write the limitation into the phase's completion
  evidence; do not delete or skip the test that needed it.

### Release-gate policy (explicit)

"Green" for a phase means every suite named in that phase's "Test requirements" actually ran and passed on the
exact commit being recorded, not on an earlier commit, not from memory of a similar run, and not with a suite
silently skipped. `./scripts/release-gate.sh --suites` reports precisely this for everything checkable without a
deployment target (5 sections; 3 more — target deploy config, backup liveness, a live deployment probe — report
**skipped**, not green, until `--env-file`/`--backup-dir`/`--api-url` are given a real target). **A section
reporting "skipped" must be reported as skipped in the phase's completion evidence, never folded into "release
gate green."** `--require-all` is the standard this plan holds a *real production release* to (M20); no earlier
phase needs every section green, but every phase must say honestly which sections it ran.

---

## 4. Git, branch and history policy (explicit)

- **Branch:** this plan's phases execute on the branch named in §0's `baseline_branch` (today
  `phase/17-production-hardening`) — the same branch V1–V3 of product variants, the storefront, both dashboards
  and the production-hardening audits were already committed to. Continue on it unless the owner asks for a new
  branch; do not invent a `phase/master-plan-*` branch on your own initiative, since this plan is a continuation
  of the same engineering line, not a new one.
- **Commits:** Conventional Commits (`feat(module): …`, `fix(module): …`, `test(module): …`, `docs(…): …`), one
  coherent change per commit, the repository builds and its suites pass at every commit — not just at the end of
  a phase.
- **Push:** push completed work to `origin` after each phase closes (§3 step 10), and after any commit a session
  is about to end on, so a fresh session never has to guess what is only local. Verify the remote actually moved
  (`git ls-remote`) — do not report a push that was not confirmed.
- **Never force-push, rebase, squash, reset, or otherwise rewrite history.** If a commit was wrong, fix it
  forward with a new commit.
- **Never merge to `main`.** That step is the owner's, explicitly, when the owner decides a batch of phases is
  ready to become the product's history. This plan produces commits ready for that review; it does not perform
  it.
- If the working tree is ever dirty for a reason this session did not cause (a stray file, an interrupted prior
  operation), stop and report it per §3 step 1 rather than cleaning it silently — it may be evidence of
  something, not noise.

---

## 5. STOP conditions

Do the safe work first, leave the repository clean and the branch pushed, update §0 to say exactly where things
stand, then stop and report. Do not guess past any of these:

| Condition | Examples in this repository today |
|---|---|
| **Checkpoint mismatch** | §3 step 1 finds `git log -1` does not match `last_verified_head`, or the tree is dirty, or a Git operation is mid-flight |
| **Destructive or irreversible production action** | Dropping a column or table, narrowing a type, deleting rows, any migration that loses data, or running one of these against a real deployment — write it, do not run it ([Migrations.md](../06-DATABASE/Migrations.md)) |
| **Real production credentials or payment verification requiring the owner** | **P-05** (JOD/three-decimal-currency Stripe minor units — needs a real test charge on the real account); connecting or rotating any real Stripe, email-provider or `SECRETS_KEY` credential |
| **Production DNS/TLS/environment action requiring owner access** | Terminating TLS for a real domain, DNS delegation, provisioning cloud infrastructure the owner must pay for or own (M17's actual go-live, not its code and scripts) |
| **Unresolved business/tax/licensing/payment-ownership decision** | **P-03** (licence and repository visibility), **P-06** (tax model), **D-13** (merchant of record: per-store Stripe accounts vs. Stripe Connect) |
| **Security-sensitive action requiring explicit approval** | **D-22** (who may preview a closed store, and how — a new credential past the store-status gate); **R-03** (whether a refunding cancellation needs `store.payments.manage`); enabling branch protection (a GitHub admin setting, not a file) |
| **Architectural decision where repository evidence is insufficient** | Anything matching `AGENTS.md` §10's description of a conflict — a new inter-module dependency, a rule that would have to live in two places, a boundary test whose "fix" is to change the test |
| **Other named open owner decisions** | **P-07** (what a platform-wide setting is), **F-8** (replay vs. reject a duplicate checkout) |

Everything else that can be decided from the repository — code, tests, ADRs, `BusinessRules.md`, the module
docs, this plan — is this plan's to decide and execute autonomously. When a phase in §8 depends on one of these
decisions, its own entry says so explicitly and names which sub-scope can still proceed without it.

---

## 6. Self-recovery: what the next session needs, and does not need

The next session needs **only**: this repository, at whatever commit `origin/phase/17-production-hardening`
(or the branch named in §0) currently points to. It does not need, and must never be told it needs, a prior
ChatGPT conversation, a previous Claude session's chat log, or a human's memory of what was decided — everything
that matters was written back into the repository per `AGENTS.md` §8 and this plan's §3 step 9. If a claim in
this document and the code disagree, **the code wins**, exactly as `AGENTS.md` §0 rule 1 states for every other
document here — fix this plan in the same change that notices the drift.

Practically, a session with no memory recovers state by:

1. `git log --oneline -20` and `git status` — what is actually committed and whether the tree is clean.
2. This file's §0 — what the *last session that updated it* believed was true.
3. [ProductRoadmap.md](ProductRoadmap.md) §6 and §7, [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md),
   [TechnicalDebt.md](TechnicalDebt.md), [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) — the
   registers this plan draws its phase scope from; they may have moved since §0 was last updated (another
   session, or the owner, may have closed a decision or fixed something directly).
4. `docs/11-ADR/README.md` for anything decided since.
5. The actual tests (`dotnet test`, `npx vitest run`) — the ground truth for "does this already work," which
   beats every document including this one.

If 1–2 disagree with 3–5 (for example, §0 says `M6` is `in_progress` but `PaymentsAndRefundsTests` and the
module document already show it done), **trust the registers and the tests, fix §0, and say so in the commit
message** — that is the self-correction this plan is designed to survive on.

---

## 7. Crosswalk: this plan's phases vs. `ProductRoadmap.md` Phases 19–23

`ProductRoadmap.md` already names five remaining product-roadmap phases. This plan does not compete with them —
it is the detailed execution of them, plus the additional first-class scope (principally the search engine, M3,
and search analytics, M13) the roadmap's five-line phases do not break out on their own. Closing a roadmap phase
still means updating `ProductRoadmap.md` §6 itself, in the same commit that closes the last master-plan phase
feeding it — this plan does not relieve that document of its own authority.

| `ProductRoadmap.md` phase | Fed primarily by | Also touched by |
|---|---|---|
| **Phase 19 — Testing** ⏳ | M19 (full acceptance and UX certification) | Every phase M2–M18 (each phase's own "Test requirements" *is* the coverage-gap closure for its area; Phase 19's exit criterion — "coverage of critical behaviour is documented" — is met incrementally, not deferred to one late phase) |
| **Phase 20 — Security review** ⏳ | M15 (dedicated security review) | M9 (account lifecycle), M6 (payment correctness), M11 (platform operations) |
| **Phase 21 — Performance review** ⏳ | M16 (performance, scale, resilience) | M3 (search indexing cost), M4 (frontend bundle/response budgets already tracked in `DesignSystem.md`) |
| **Phase 22 — Documentation** ⏳ | M20 (launch readiness) | Every phase (§3 step 9 makes documentation part of each phase, not a late catch-up) |
| **Phase 23 — Production readiness review** ⏳ | M17 (production infrastructure/deployment), M18 (CI/CD and release engineering) | M20 (the go-live checklist itself) |

New scope this plan adds that the roadmap's five phases do not name on their own: **M3 (search engine)** and
**M13 (search analytics)** are a genuine product capability, not a testing/security/performance/docs/readiness
concern — closing them should also add a short entry to `ProductRoadmap.md` §6 (a new numbered product phase, or
folded into Phase 16's "Remaining" note, whichever the owner's next roadmap edit prefers; this plan does not
pre-empt that placement, only flags that it is needed).

---

## 8. The twenty phases

Each phase lists: Objective · Scope · Dependencies · Backend/API · Database · Frontend/UX · Security · Testing ·
Browser QA · Docker/runtime verification · Documentation/ADR · Technical debt touched · Acceptance criteria ·
Completion evidence · Next-phase trigger. "Completion evidence" is filled in by the session that closes the
phase (a commit range, a test count, a checkpoint hash) — it is written as a template here, not pre-filled,
because pre-filling it would be exactly the kind of claim this plan's own §2 forbids elsewhere in the
repository.

### M1 — Architecture and repository final audit

- **Objective.** Re-verify, by reading the current code rather than trusting any prior report (including this
  plan's own account of it), that the architecture is what `AGENTS.md` §2–§3 says it is, and close or
  consciously re-file every open structural finding before nineteen more phases build on top of it.
- **Scope.** A fresh pass over [ArchitectureEvaluation.md](../02-ARCHITECTURE/ArchitectureEvaluation.md),
  [ModuleBoundaryAudit.md](../02-ARCHITECTURE/ModuleBoundaryAudit.md) and
  [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md) (regenerate, don't assume);
  triage TD-01 (module boundaries enforced only inside `Souq.Application.Features`), TD-02 (four undelivered
  contracts — *ICustomerDirectory*, *IUserDirectory*/*IAccountTokens*, *IOrderHistory*, *ISellableItems*), TD-03
  (the Identity↔Customers cycle), TD-04 (store-side payment/review use cases filed under Platform), TD-05
  (no per-module Domain namespaces); confirm every non-goal in
  [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) still holds against the current code and
  volume; refresh [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) against what the last several phases
  actually changed.
- **Dependencies.** None — this is the first phase.
- **Backend/API.** ~~Extract the highest-value contract from TD-02 first (Shopping → Catalog)~~ **What was
  actually done, and why it differs from the plan as written:** re-reading [ModuleBoundaryAudit.md](../02-ARCHITECTURE/ModuleBoundaryAudit.md)'s
  own rank table (not TD-02's older, less careful claim) showed the highest-value *safe* fix was rank 3 — TD-04,
  the payment-account use cases misfiled under Platform — not rank 5 (*ISellableItems*, Shopping → Catalog,
  which is 12 crossings but all reads, no writes). Rank 1 (the Identity⇄Customers cycle) is genuinely the
  highest-value fix but is ~13 files across two modules' account lifecycle — too large to attempt safely inside
  an audit phase, so its *direction* was decided and documented (TD-03) and its extraction deferred to M9.
  TD-04 was attempted as "no logic change" per the audit's original estimate; it was not — moving both files
  together broke two independent architecture rules at once (`TenantId` may only be carried by
  `Features.Platform` requests, and the moved editor would have made `Payments` reach back into `Platform` for
  `PlatformTenants.NotFound`). The actual, correct fix: `Features/Payments/Contracts/StorePaymentAccountContracts.cs`
  publishes `IStorePaymentAccountEditor`; `StorePaymentAccountEditor` (moved to `Features/Payments`) implements
  it; `TenantPaymentAccounts.cs` (staying in `Features/Platform`, because its commands carry `TenantId`) calls
  only the interface through `ITenantScopeRunner`; `AllowedContracts["Platform"] = ["Payments"]` authorizes the
  one legitimate crossing. TD-04's review-settings half was re-examined and found *already correctly placed* —
  it works on `Tenant`, a Platform aggregate, not a Reviews one — so nothing there needed to move; the original
  claim was corrected in the same commit. Result: 79 → 74 crossings, 5 class-D/C rows closed, one new class-A
  contract. TD-01/TD-02's remaining extractions (the cycle, *IAccountTokens*, *ISellableItems*, etc.) are
  deferred to M2/M5/M9 as originally planned, with TD-02's own text corrected to point at the audit's rank
  table as the authority rather than repeat a now-superseded independent claim.
- **Database.** None. TD-03's cycle resolution needs no schema change (both contracts are pure read/write
  operations over existing tables), so no migration was written or deferred here.
- **Frontend/UX.** None — confirmed; no frontend file was touched.
- **Security.** `TenancyRuleTests` and `ModuleAndContractRuleTests` re-run as a baseline (both green before and
  after); no rule in `AGENTS.md` §3 regressed. The TD-04 fix itself is a tenant-isolation-relevant result: the
  platform admin path to a store's payment-account editor is now proven, by a passing architecture test, to be
  reachable only through `ITenantScopeRunner`'s tenant-scoped resolution and a published contract — never a
  raw class a future change could resolve outside that scope by accident.
- **Testing.** `Souq.ArchitectureTests` in full (88/88, including `GeneratedDocsTests` regeneration);
  `Souq.Domain.Tests` (433/433) and `Souq.Application.Tests` (376/376) unaffected; the moved
  `StorePaymentAccountEditorTests.cs` (5 tests) still passes unchanged under its new path and namespace;
  targeted integration tests (`PaymentsAndRefundsTests`, `PlatformAdministrationTests`,
  `ProvisioningBoundaryTests` — 19 tests) green against the real API and SQL Server; the full integration suite
  run for final confirmation (see this phase's completion evidence for the count).
- **Browser QA.** None — this phase is structural, no user-facing behaviour changed.
- **Docker/runtime verification.** `dotnet build -warnaserror` clean (0 warnings, 0 errors) after every edit;
  the full integration suite runs the real API against a real containerized SQL Server, which is the runtime
  verification for a backend-only, DI-registration-touching change like this one — no separate compose boot
  was needed since no container image, environment variable or startup path changed.
- **Documentation/ADR.** Update `ModuleBoundaryAudit.md`, `ModuleDomainDependencies.md` (generated),
  `RiskRegister.md`; an ADR only if a boundary itself moved (not for re-confirming one).
- **Technical debt touched.** **TD-04 closed** (the payment-account half; the review-settings half's original
  claim was corrected instead, since it was already right). **TD-05 re-confirmed** with today's evidence (no
  namespace drift found across the fix; the cheaper choice still holds). **TD-01 re-confirmed** with an updated
  crossing count (74, was 79) and no new crossing added. **TD-02 corrected** to defer to
  `ModuleBoundaryAudit.md`'s rank table rather than repeat its own superseded "Shopping → Catalog first" claim;
  extraction itself deferred to M2/M5/M9 as planned. **TD-03's direction decided** (Customers → Identity kept;
  two Identity-declared contracts close the reverse arrow) and documented in both `TechnicalDebt.md` and
  `RiskRegister.md` (R-15); extraction itself deferred to M9 — ~13 files across two modules' account lifecycle
  is real, valuable work, but too large to attempt safely inside an audit phase alongside everything else M1
  already found. `RiskRegister.md`'s R-04 closed (deleted, per the register's own convention); R-14's count and
  R-15's row updated to match.
- **Acceptance criteria.** Every TD item above is either closed or re-filed with a dated re-confirmation, not
  left stale — **met**. `ModuleDomainDependencies.md`'s crossing count is not higher than the last verified
  figure without a documented reason — **met**, it dropped (79 → 74) with the reason recorded in three places
  (`ModuleBoundaryAudit.md`, `ModuleBoundaries.md`, `TechnicalDebt.md`). Every non-goal is re-confirmed against
  current measured evidence — **met**: `ExplicitNonGoals.md` was read in full against the current code and
  found still accurate (all fourteen items' "not now because" reasoning still holds; items 1–3 already carry a
  dated Phase 17 re-check and needed no further update).
- **Completion evidence.** Commit(s) on `phase/17-production-hardening` implementing the TD-04 fix and the
  documentation reconciliation described above, starting from checkpoint `fc6c1cd`. `dotnet build -warnaserror`:
  0 warnings, 0 errors throughout. `Souq.Domain.Tests` 433/433, `Souq.Application.Tests` 376/376,
  `Souq.ArchitectureTests` 88/88 (including a regenerated `ModuleDomainDependencies.md`, `UseCases.md`,
  `Endpoints.md`, `TestInventory.md`) — all unchanged in count from the phase's start except the generated
  inventories, which now correctly attribute the moved use cases to Payments. Targeted integration tests
  (`PaymentsAndRefundsTests`, `PlatformAdministrationTests`, `ProvisioningBoundaryTests`): 19/19. **Full
  `Souq.IntegrationTests` suite: 330/330, 0 failed (3m 35s).** Frontend lint re-run as a sanity check though no
  frontend file was touched: 0 errors, 20 pre-existing warnings — the known baseline, unchanged.
  `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, 3 skipped (deployment-target sections, expected
  without a real target). Working tree clean and pushed at close.
- **Next-phase trigger.** M2 may start once this phase's commit is on the baseline branch with a clean tree and
  `Souq.ArchitectureTests` green on that exact commit — **satisfied**.

### M2 — Catalog and product completeness

- **Objective.** Bring the Catalog module — categories, products, translations, the variant model V1–V3 — to a
  state with no known gap short of the deliberately deferred V4 (per-variant reporting), and close the
  documented content-capability gap (TD-42) enough that a store can legally represent itself.
- **Scope.** Full audit of `docs/04-MODULES/Catalog/README.md` and
  [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md) against the current code; category tree depth
  and edge cases (empty categories, deeply nested trees, category deletion with live products); image/gallery
  completeness including the still-missing variant-specific imagery noted in
  [ADR-0041](../11-ADR/0041-storefront-variant-selection.md)'s consequences; TD-42's content-page capability
  (store-authored pages — privacy, terms, returns, shipping, FAQ — with a slug and per-language body at
  `/pages/:slug`, linked from the footer only when a page exists).
- **Dependencies.** M1 (if Catalog's contracts were touched there).
- **Backend/API.** A `ProductPage`/`ContentPage` aggregate owned by Catalog (or wherever `ModuleBoundaries.md`
  says store-authored content belongs — check before assuming Catalog), admin CRUD behind a permission, a
  storefront read endpoint; variant gallery association if the product image model doesn't already support
  per-variant images (check before building — it may already, if not wired to the picker).
- **Database.** New table(s) for content pages if TD-42 is built (additive migration, no data movement); no
  destructive change expected.
- **Frontend/UX.** `/pages/:slug` rendering (Arabic/English, RTL/LTR, light/dark); footer links restored only
  when a page exists (per TD-42's own note — never a dead link); admin editor for content pages if built;
  variant-aware gallery on the product page if variant images are added.
- **Security.** Tenant isolation on the new content-page table from day one (query filter, write guard,
  isolation test) — the same pattern every tenant-owned table already follows.
- **Testing.** Domain tests for the new aggregate's invariants (unique slug per store, at least the store's
  default-language body required); Application tests for the use cases; Integration tests for tenant isolation
  and the storefront read; frontend Vitest for the page and the footer's conditional link.
- **Browser QA.** A content page in Arabic and English, both themes, phone and desktop; the footer with zero,
  one and several pages; the variant gallery (if built) following the picker's selection.
- **Docker/runtime verification.** Full stack boot, storefront and admin flows against the live compose stack.
- **Documentation/ADR.** Update `Catalog/README.md`; an ADR for the content-page capability (a new aggregate and
  a new public route are exactly what `AGENTS.md` §8 calls architectural); update `TechnicalDebt.md` to close
  TD-42.
- **Technical debt touched.** TD-42 — **that decision turned out to be needed after all; see below.**
  Variant-image gallery: re-checked against the current code (`ProductImage` has no `VariantId` or any per-
  variant association at all — confirmed by reading `src/Souq.Domain/Entities/ProductParts.cs`) and left
  explicitly deferred, not built: it is a net-new feature (a schema change plus admin and storefront UI), it is
  named *not scheduled* in `Catalog/README.md`'s own "Future evolution" section, and nothing depends on it —
  building it now would be exactly the kind of scope-creep this plan's own protocol (§3 step 2, "discover"
  before "implement") is designed to catch, not a defect to fix.
- **Audit performed, verified accurate — no drift found.** `Catalog/README.md` and `ProductVariants.md` were
  read in full against the current code. Every claim checked (the V3 storefront gate removal, `VisibleProducts`,
  the category `IsActive` filter, `GuardConcurrentEdit`'s scope, the search implementation, the fifteen "Known
  limitations") matched the code exactly — including limitation 9 ("plain substring match, no full-text index"),
  which is confirmed still true and is explicitly M3's scope, not M2's, so it was not touched here.
- **One additional limitation confirmed but deliberately not fixed, with the reasoning recorded.**
  `Catalog/README.md` limitation 3 — hiding a category hides only its *direct* products; an active child of a
  hidden category, and that child's own products, stay visible (`ListAdminCategoriesQuery`'s handler filters
  `IsActive` with no ancestor walk; `CatalogQueries.VisibleProducts()` checks only the product's direct
  category). Whether "hide a category" should mean "hide this node" or "hide this subtree" is a genuine
  semantic choice with no strong existing precedent either way in this codebase (unlike V3's variant-deactivation
  question, which had a direct analogy — "like unpublishing a product" — this one does not: a merchant
  reorganizing a category tree might deliberately want a child to stay visible while its old parent is hidden).
  Guessing here would be inventing a business rule (`AGENTS.md` §0 rule 4), so it stays documented as a
  limitation rather than "fixed" by assumption.
- **Acceptance criteria.** Not met in full — **stopped for a genuine decision**, per §5's own anticipation of
  this exact outcome. `ProductVariants.md` has no stale "not built" claim this phase would have closed (the
  audit found none to close). The content-page capability itself — TD-42 — needs the owner's answer to proceed.
- **STOP — owner decision required (TD-42's own recorded prerequisite, `AGENTS.md` §9 "a business rule where
  guessing changes commercial behaviour" / a product-scope call).** The question: should a store's legal/
  informational pages (privacy policy, terms, returns, shipping, FAQ) be **(a)** authored inside Souq — a new
  `ContentPage` aggregate, a migration, admin CRUD, and a public `/pages/:slug` route, the shape this phase's own
  Backend/API/Database/Frontend fields above already describe — or **(b)** a much smaller capability: a handful
  of URL fields on the store's existing settings (`StoreSettings`), each linking out to a policy the merchant
  hosts elsewhere, with the footer showing a link only when a URL is set? Both close the real gap TD-42 names (a
  store selling in most jurisdictions needs a reachable privacy policy and terms). They differ by an order of
  magnitude in engineering footprint (a new aggregate, table and admin editor, versus four or five nullable
  columns and no new architecture at all) and in product capability (rich per-language authored content with
  its own lifecycle, versus a link the merchant maintains on their own site). Repository evidence does not
  favour one over the other — `TechnicalDebt.md`'s own TD-42 entry names this exact fork as its prerequisite
  ("a product decision on scope") rather than assuming the answer, and this phase's own scoping, written before
  execution, assumed (a) without that decision having been made. **Nothing else in M2 is blocked by this** — the
  audit above is complete, and M3 does not depend on this decision (see its own trigger). Recommendation, not a
  decision: (b) first — it closes the real legal gap immediately, at near-zero engineering risk, and does not
  foreclose building (a) later as a richer, separately-decided capability if the owner wants authored pages
  specifically (a CMS-lite feature) rather than just reachable policies.
- **Completion evidence.** Audit portion: read `Catalog/README.md` (302 lines) and `ProductVariants.md` (271
  lines) in full against `CatalogQueries.cs`, `ProductParts.cs`, `Product.cs` and `GetCategoriesHandler`; zero
  corrections needed. No code changed in M2 — the phase is genuinely blocked on the TD-42 decision above for its
  one concrete deliverable, and building around that decision (guessing) is exactly what this plan's §2 and
  `AGENTS.md` §0 forbid.
- **Next-phase trigger.** M3 does not depend on M2's blocked deliverable (TD-42) — only on `CatalogQueries`
  being stable, which it is. **M3 may start now**; M2 resumes and closes the moment the owner answers TD-42.

### M3 — Professional local multilingual product search engine

- **Objective.** Replace today's substring `Contains`/`CHARINDEX` keyword match
  (`CatalogQueries.SearchProductsAsync`) with a real search architecture: Arabic and English normalization,
  typo/fuzzy tolerance, synonyms and related terms, category-aware recovery, ranking, suggestions, and graceful
  no-result handling — entirely **local and deterministic**, inside the existing SQL Server, with **no runtime
  dependency on an external AI API**. A query like `مكلسة` must be able to recover toward `مكنسة` and surface
  the household-electrical category, precisely because the normalization and correction data make that
  relationship explicit and inspectable, not because a model guessed it.
- **Scope.**
  1. **Normalization**, applied identically to indexed text and to every incoming query: Arabic diacritics
     (tashkīl) and tatweel stripped; alef variants (أ إ آ) folded to ا; ة/ه and ي/ى folding per the standard
     Arabic search-normalization rules; Latin case folding and diacritic stripping for English/transliterated
     terms; whitespace and punctuation normalization. This is a pure function, Domain-testable without a
     database.
  2. **Indexing.** SQL Server Full-Text Search (a database *feature*, not a new external dependency or service —
     it ships with SQL Server, so it does not trigger `AGENTS.md` §5's "propose a new dependency" bar the way an
     external search service would) over the normalized product name, description and category name columns,
     with the Arabic (LCID 1025) and English word breakers. Verify the target SQL Server edition/image actually
     ships Full-Text Search before committing to it (the current `docker-compose.yml` image and its production
     equivalent) — if it does not, the fallback is a normalized-text computed column plus trigram/n-gram
     similarity computed in SQL, still local and deterministic; either way, the decision and its evidence go in
     the ADR this phase writes.
  3. **Fuzzy/typo tolerance and spelling recovery.** A `SearchCorrections` (or similarly named) table, owned by
     Catalog, mapping common misspellings/typo patterns to their corrected form per store/language, seeded from
     nothing invented — either left empty until real no-result search logs (M13) show a genuine pattern, or
     seeded from a small, explicit, reviewed list the phase's own report names. A deterministic edit-distance
     fallback (e.g. Damerau–Levenshtein within a small threshold over normalized tokens) for terms with no
     recorded correction.
  4. **Synonyms and related terms.** A `SearchSynonyms` table, owned by Catalog, per store and language, editable
     from the merchant admin (so a store can teach the engine its own vocabulary, e.g. a regional product name),
     expanding a query into the set of terms actually searched.
  5. **Category relationships.** When a query resolves to few or no product matches but a corrected/expanded
     term matches a category name, surface that category as a suggestion — this is what makes the `مكلسة` →
     `مكنسة` → household-electrical example real: the correction resolves the term, and the term's own category
     association (already modelled — products already belong to categories) does the rest without any new
     "which category does this mean" guesswork.
  6. **Ranking**, computed in SQL alongside the existing purchasable-price computation from V3 so nothing
     disagrees between a card, a sort and a search hit: exact normalized name match ranks highest, then prefix
     match, then full-text/trigram score, then description match; ties broken by the store's existing sort
     rules.
  7. **Suggestions ("did you mean")** and **autocomplete** as the query is typed, both server-computed from the
     same normalization/correction/synonym data — no separate index or service.
  8. **No-result recovery**: when normalized-exact and full-text both return nothing, fall back to the
     correction/fuzzy path and say so on screen ("showing results for … instead of …"), never silently swap the
     shopper's own words without telling them (the same "never silently switch" principle V3 already applies to
     variant selection, ADR-0041).
- **Dependencies.** M2 (a stable `CatalogQueries` read model and, if content pages add categories/tags worth
  indexing, that they exist first).
- **Backend/API.** `ICatalogQueries.SearchProductsAsync` gains the normalized/ranked path behind the same
  contract shape (additive — `ProductSearch` and `ProductDto` need no breaking change); a new
  `GET /api/search/suggestions` (or folded into the existing search endpoint) for autocomplete; admin endpoints
  for `SearchSynonyms` CRUD, tenant-scoped like every other admin resource.
- **Database.** New tables (`SearchCorrections`, `SearchSynonyms`, or a single `SearchVocabulary` table — the
  phase's ADR decides the shape) with the standard tenant query filter and write guard; a Full-Text Search
  catalog and index if that path is chosen (a schema change, additive, reviewed like any migration); a computed
  normalized-text column on `ProductTranslations` if the trigram fallback is chosen instead.
- **Frontend/UX.** The search box gains autocomplete/suggestions; the results page shows a "did you mean"
  banner only when a correction actually fired, never invents one; empty-result state offers the suggested
  category instead of a dead end; Arabic and English, RTL/LTR, both themes, phone and desktop; keyboard
  navigation through suggestions is a real listbox pattern (`role="listbox"`/`role="option"`, arrow-key and
  Escape handling), not a styled `<div>` a screen reader cannot see.
- **Security.** `SearchSynonyms`/`SearchCorrections` are tenant-owned data — the standard isolation test applies;
  the search endpoint stays public (storefront) but the admin vocabulary editor is behind a permission; no query
  parameter reaches raw SQL (the existing `Contains`-to-`CHARINDEX` pattern already avoids injection — the new
  path must too, and a test should say so explicitly since full-text queries have their own syntax that a naive
  pass-through could abuse).
- **Testing.** Domain tests for the normalization function (a table of Arabic/English/mixed inputs and their
  expected normalized form — this is the one piece of this phase safe to fully specify without a database);
  Application/Integration tests for ranking order, synonym expansion, the fuzzy fallback, and that a search for
  an inactive/unavailable product's term does not leak it (reuses the V3 purchasability rules); a contract test
  proving the "did you mean" banner only appears when a correction fired.
- **Browser QA.** `مكلسة` recovering to `مكنسة`-tagged products/category in a store whose catalog actually has
  that relationship (seed it for the test, do not fake the production catalog); autocomplete keyboard
  navigation; empty state; phone-width search UI; axe-clean suggestions listbox.
- **Docker/runtime verification.** Confirm Full-Text Search (if chosen) actually initializes inside the
  `docker-compose.yml` SQL Server image on a fresh container, not only on a developer's pre-existing database —
  this is exactly the kind of thing that works locally and silently fails on a clean deploy.
- **Documentation/ADR.** A new ADR (next number per [docs/11-ADR/README.md](../11-ADR/README.md), currently
  0042) recording the indexing choice, the evidence for it, and — explicitly — why an external/AI-backed search
  service was not chosen and what measured evidence would justify revisiting that; update `Catalog/README.md`
  and `ApiDocumentation.md`/`Endpoints.md` (generated) for the new endpoints.
- **Technical debt touched.** None inherited; if the trigram fallback is chosen over Full-Text Search for a
  reason that might not hold at scale, record that explicitly as a new TD item rather than leaving it implicit.
- **Acceptance criteria.** Arabic and English queries with common typos recover to the intended product/category
  in a seeded test catalog; ranking is server-computed and agrees with what the list/sort/filter already agree
  on from V3; no AI API call exists on the runtime search path; autocomplete and no-result recovery are
  axe-clean and keyboard-operable.
- **Completion evidence.** Four commits on `phase/17-production-hardening` from checkpoint `46a89b9`:
  normalized ranked search, typo recovery and the relevance-first UI, suggestions as a real combobox, the two
  defects the browser pass found, and the merchant vocabulary. Recorded in
  [ADR-0042](../11-ADR/0042-local-search-engine.md) with the measurements behind each choice.
  - **The plan's preferred index was measured unavailable and rejected on evidence.** In the exact image
    `docker-compose.yml` pins, `SERVERPROPERTY('IsFullTextInstalled')` is `0` and `CREATE FULLTEXT INDEX` fails
    with *Msg 7609*, while `CREATE FULLTEXT CATALOG` **succeeds** — so a catalog-only migration would have passed
    CI and failed at the first query. Full-Text Search also could not have delivered per-store merchant-editable
    synonyms (its thesaurus is a server-level file) or any typo tolerance. What replaced it is ~72× faster than
    the substring scan it removed (0.2 ms vs 14.4 ms at 2,500 products/store), so no search service is justified.
  - **Tests.** Domain 524 (normalization table, bounded Damerau–Levenshtein, the derived-projection invariant,
    vocabulary rules), Application 385, Architecture 89 (including a new ratchet that fails the build if any
    catalog read service ever makes a network call — the acceptance criterion "no AI API on the runtime search
    path" as an executable rule, not a promise in prose), Integration 360, frontend 615 across 78 files.
    `dotnet build -warnaserror`: 0 warnings, 0 errors.
  - **Docker/runtime.** An isolated stack (`-p souq-m3-qa`, its own env file and volumes — the owner's `.env` and
    `souq_souq_db_data` untouched) built and started clean: `/health/live`, `/health/ready` and the web proxy all
    200, migrations applied, logs free of errors. Real flows exercised against it: unvocalized Arabic finding
    vocalized names, scattered words, Arabic-Indic digits, `مصباخ` → `مصباح` recovery, `exact=true` honouring the
    shopper's refusal, suggestions, and `limit` validation.
  - **Browser QA.** `frontend/e2e/search.spec.js`, 8/8 on that container stack. It found **two real defects that
    every green suite had missed**: `nested-interactive` (a button inside `role="option"` — jsdom's axe did not
    flag it) and a 129 px horizontal overflow at 320 px that M3 itself introduced with the fourth sort button.
    Both fixed in the same change.
  - **Limitations recorded, not hidden.** TD-45 (mid-word matching is still a narrow-index scan; the inverted-term
    table is the measured next step, *before* any external service), TD-46 (no linguistic stemming — decide from
    M13's real no-result data, not from principle), TD-47 (a residual ~2 px page overflow at 320 px with the
    mobile sheet open, pre-existing and attributed, left to M4 which owns the systematic responsive pass).
    The plan's separate `SearchCorrections` table was **deliberately not built**: a directed synonym pair already
    expresses a forced correction, and the plan allowed a single vocabulary table. Reasoned in ADR-0042.
  - `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped** — target deploy config, backup
    liveness and the live deployment probe, none of which has a target in this environment. Working tree clean
    and pushed at close (e0bed4e).
- **Next-phase trigger.** M4 may start independently of M3's exact completion (different surface area), but
  should not start until M3's search-box UI shape is stable enough that M4's responsive pass covers the final
  markup rather than a version M3 is about to change underneath it.

### M4 — Storefront UX and responsive excellence

- **Objective.** A systematic pass over **every** storefront screen — not only the critical journeys already
  covered by Phase 16's seventeen journeys — at phone, tablet, desktop and unusually narrow/wide viewports, with
  zero clipped text, zero inaccessible controls, zero hidden buttons, zero horizontal overflow, zero broken
  dialogs or tables, in both languages, both directions, both themes.
- **Scope.** Every route under the storefront route tree in `frontend/src/App.jsx`; the variant picker (V3) and
  the new search UI (M3) at the extremes; long product names/descriptions in both languages; long category
  names in navigation; a cart/basket with many lines; a checkout form with validation errors visible at once;
  every empty state (empty cart, empty wishlist, empty search results, empty category) and every loading state.
- **Dependencies.** M2, M3 (their UI surfaces are in scope for this pass).
- **Backend/API.** None expected — this phase is frontend-only unless it surfaces a genuine data gap (e.g. a
  missing truncation-friendly short name) that the backend must supply, in which case treat that as a small,
  additive contract change with its own test, not a redesign.
- **Database.** None expected.
- **Frontend/UX.** Fix every defect M4 finds at the CSS/layout/component level, following
  [DesignSystem.md](../08-FRONTEND/DesignSystem.md) tokens and patterns — no ad-hoc pixel values, no
  breakpoint invented outside the design system's own scale; verify `dir="rtl"`/`dir="ltr"` switching does not
  mirror anything that should not mirror (numbers, prices, the logo) and does mirror what should (icons implying
  direction, layout flow).
- **Security.** None specific — but confirm the pass did not accidentally expose a control (a hidden button
  that should have been a permission-gated action) per `AGENTS.md` §4's "a hidden button is never a permission"
  principle re-stated in `AIHandoff.md` §10.
- **Testing.** Extend `frontend/src/a11y.test.jsx` and per-component Vitest coverage for anything fixed;
  viewport-specific assertions where a component's behaviour genuinely differs by width (not just CSS that a
  screenshot would catch better than a unit test).
- **Browser QA.** The real, systematic part of this phase: Playwright across `frontend/e2e/responsive.spec.js`'s
  existing phone project, plus new coverage at tablet width and at deliberately unusual widths (very narrow,
  e.g. 320px; very wide, e.g. 2560px) for every route in scope; axe on every route in both languages and both
  themes; a manual-style sweep is not a substitute for the automated one, but is a reasonable first discovery
  pass before writing the automated assertion.
- **Docker/runtime verification.** Journeys run against the live compose stack per the runbook, not only
  against `npm run dev`.
- **Documentation/ADR.** Update `DesignSystem.md` only if a genuinely new pattern was needed (e.g. a responsive
  table pattern that didn't exist before); no ADR expected unless a token or breakpoint scale itself changes.
- **Technical debt touched.** None expected to open; if a defect found here is out of scope to fix immediately
  (rare, but possible for something entangled with M3's still-moving search UI), file it in `TechnicalDebt.md`
  rather than leave it undocumented.
- **Acceptance criteria.** Every storefront route holds at 320px and 2560px with no horizontal scroll; every
  dialog is a real dialog (focus trap, Escape, scroll lock — the pattern Phase 17 already established for the
  admin area); axe reports zero violations across the full route list in both languages and both themes.
- **Completion evidence.** One commit on `phase/17-production-hardening` from checkpoint `89a3ee3`. The phase's
  output is `frontend/e2e/responsive-storefront.spec.js` — every storefront route at **320/768/1280/2560**, in
  **both languages**, plus axe across every route in **both languages and both themes** — and the five defects
  that matrix found. **Every one of them appeared at exactly one width in one language**, which is the argument
  for the matrix: a single viewport would have found none.
  - **The navbar was silently clipping the cart, wishlist and notification buttons between 768px and ~940px.**
    A signed-in bar needs 941px in English; the only rule that thinned it was `max-width: 767px`, and the bar's
    `overflow-x: hidden` — a safety net against spilling — swallowed the controls rather than overflowing. No
    scrollbar, no symptom, the buttons simply absent. A new **960px** breakpoint (measured, not chosen) moves
    the text links into the hamburger menu where they already live on phones; documented in `DesignSystem.md` §11.
  - **Checkout pushed "Continue to payment" out of the viewport at 320px** (31px of page overflow): the
    single-column grid used `1fr`, whose floor is min-content, so the summary's nowrap totals forced a 335px
    track onto a 320px screen. `minmax(0, 1fr)` fixed it.
  - **The cart row's five columns did not fit 320px**, putting remove 7px past the edge; the stepper and total
    now drop to a second line below 480px.
  - **Four undersized touch targets**: cart remove 16×19 (a *destructive* action), rating stars 22×22 (the
    review form's primary control), "View all" 56×19, navbar "Log out" 48×16 — all below WCAG 2.5.8's 24px and
    this repo's own 40px phone rule. Read-only stars are `disabled` and therefore excluded, which is what the
    standard says rather than a convenience.
  - **Long content is now part of the matrix**: the spec creates a product with a very long name in both
    languages and visits it, because short seeded names reveal nothing about overflow.
  - **Result:** zero page-level horizontal scroll, zero controls outside the viewport, zero targets under 24px,
    zero axe violations, across 16 routes × 4 widths × 2 languages (× 2 themes for axe).
  - **Verified on the container stack**, not the dev server — production middleware, nginx and the container
    health probe. `playwright.config.js` takes `SOUQ_E2E_BASE_URL` (added in M3) and the spec takes admin
    credentials from the environment, because a Production-mode container requires a 12-character password while
    the development seed uses a shorter one.
  - **Not met, and recorded rather than claimed: the focus trap.** This phase's acceptance criteria say "every
    dialog is a real dialog (focus trap, Escape, scroll lock)". Escape, focus-in, focus-restore and scroll lock
    are all implemented (`useDialog`, and `ProductZoom` by hand); a **cyclic Tab trap is not**, so Tab still
    walks into the page behind an open dialog. Filed as **TD-48** with the fix described, plus **TD-49**
    (`NotificationBell` declares `role="dialog"` for a non-modal popover). `useDialog`'s own comment claimed
    `FrontendGuide.md` documented this gap and it did not — that page now does.
  - Frontend gate: lint 0 errors (21 warnings, the known TD-25 baseline), typecheck clean, 615 tests across 78
    files, build clean. Backend: Domain 524, Application 385, Architecture 89, Integration 361.
    `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped** (no deployment target).
  - **The gate failed once, environmentally, and that is recorded rather than smoothed over.** Its first run
    reported 31 integration failures in 9m08s. The same commit — whose only changes were CSS, JSX and
    documentation, which cannot touch a .NET integration test — passed **361/361 in 4m20s** standalone, and the
    gate itself passed clean on re-run. The cause is the Docker memory ceiling this machine has (~2.9 GB total,
    with an unrelated container stack of the owner's holding ~1 GB): the symptom is dozens of simultaneous
    failures and roughly double the runtime, which `TestingStrategy.md` §94-107 already describes. Nothing was
    weakened to make it pass; the run was simply repeated with the memory free. Working tree clean and pushed at
    close (b0d4b8c).
- **Next-phase trigger.** M5 may start in parallel conceptually but should follow M4 in execution order per §1's
  sequential default, since M5's checkout screens are exactly the ones M4's dialog/table/overflow sweep should
  have already hardened.

### M5 — Cart, checkout and order reliability

- **Objective.** Prove, under real concurrent and adversarial conditions (not just the happy path), that a
  basket becomes an order reliably: stock does not oversell, prices are never client-computed, duplicate
  submissions behave in one documented way, and every failure mode surfaces a clear, correctly-coded error.
- **Scope.** `CreateOrderHandler`'s full breadth (TD-13 flags it as 16 dependencies and ~17 responsibilities —
  this phase is the natural place to attempt the quote → place → pay split TD-13 recommends, if it can be done
  without destabilizing checkout mid-plan; otherwise document why it waits and re-file TD-13 with today's
  evidence); checkout-expiry sweep correctness across store states (R-24: a suspended store's sweep is
  currently skipped — decide and document, or escalate if it needs the owner); F-8's two implemented-but-
  undecided behaviours (replay vs. reject a duplicate checkout) — **this is a named owner decision (§5); this
  phase's job is to make sure both paths stay correctly implemented and tested, not to choose one**.
- **Dependencies.** M1 (if the Shopping→Catalog contract extraction happened there; otherwise this phase inherits
  it).
- **Backend/API.** Any split of `CreateOrderHandler` must preserve its exact external contract and error codes
  (`AGENTS.md` §5's "no client-bindable request carries a TenantId," the RFC 7807 error contract, every code a
  client already branches on); F-8's design is already written in the Ordering module document — do not
  re-design it, just keep both paths (replay and reject) correctly buildable behind a flag or a clear decision
  point so the owner's eventual answer is a small change, not a redesign.
- **Database.** None expected unless F-8's eventual answer needs an idempotency-key table (the design already
  anticipates this — see the Ordering module document) — write it, do not apply it destructively, and only
  build the table if this phase is explicitly asked to prepare for F-8's resolution rather than wait for it.
- **Frontend/UX.** Checkout's error states surfaced clearly for every rejection this phase hardens (stock
  drained mid-checkout, a stale coupon, a stale shipping method); the money-barrier tests TD-32 already
  describes as covered stay covered after any refactor.
- **Security.** Re-run `CheckoutIdempotencyTests` and the payment-adjacent tenancy tests; confirm no split of
  `CreateOrderHandler` introduced a transaction boundary crossing a network call (ADR-0021, `AGENTS.md` §3 rule
  20).
- **Testing.** Integration tests for concurrent checkout against the same stock (two customers, one unit left);
  a load-shaped test (a burst of concurrent checkouts against a small stock pool) proving no oversell — this
  overlaps with M16's performance scope but belongs here first as a *correctness* test, not a *performance*
  measurement.
- **Browser QA.** A real checkout through the UI to a paid order on the fake gateway, including a deliberately
  stale basket (a line goes out of stock between page load and submission) and a duplicate submission (double
  click / double tab) — assert the *current, documented* behaviour precisely, whichever F-8 path is live.
- **Docker/runtime verification.** The full checkout flow against the live compose stack, not only integration
  tests against `WebApplicationFactory`.
- **Documentation/ADR.** Update the Ordering module document with whatever this phase changes structurally; an
  ADR only if `CreateOrderHandler`'s split changes an architectural boundary (it likely does, given TD-13's
  description — plan for one).
- **Technical debt touched.** TD-13 (attempt or re-file with evidence), TD-14 (the three hand-rolled retry loops
  — unify if touched by this phase's own changes, otherwise leave), R-24 (decide or escalate).
- **Acceptance criteria.** No oversell under concurrent load in the test above; `CreateOrderHandler`'s
  responsibilities are either genuinely reduced with full test coverage preserved, or TD-13 is re-filed with
  today's evidence explaining why not yet; F-8 remains a clean two-path implementation, not a de facto choice
  made by omission.
- **Completion evidence.** Commits on `phase/17-production-hardening` from checkpoint `432372d`.
  - **The headline criterion was already met before this phase, and was verified rather than rebuilt.**
    "No oversell under concurrent load" is covered by `InventoryAndOrderTests`: five customers on the last unit
    give exactly one order and four `InsufficientStock`, and eight parallel orders against a stock of three give
    exactly three. What was **missing** is that every such test orders quantity 1, so the winner count always
    equals the stock and only "all or nothing" is exercised. Added a burst of ten concurrent orders with mixed
    quantities (1–5) against a stock of 6, asserting the invariant that actually matters — the **sum** of what
    succeeded never exceeds stock — which catches a partial-fit miscount that quantity 1 cannot.
  - **TD-13 — closed, not re-filed.** `CreateOrderHandler` went from one ~120-line method with **sixteen**
    dependencies to ~15 lines over three collaborators, each owning a different guarantee: `CheckoutQuote` (no
    effect), `OrderPlacement` (one transaction, all or nothing), `CheckoutPayment` (outside the transaction,
    with the compensation that was previously a `catch` block buried mid-method — the piece TD-13 named as
    easiest to break). The external contract is unchanged and `CreateOrderHandlerTests`' fifteen cases were
    **not rewritten**, only the line that constructs the handler, so they still measure behaviour and not the
    new seams. The crossing ratchet confirms the boundaries did not move: the same four Ordering→Customers
    crossings, re-attributed to `CheckoutQuote`.
  - **R-24 — decided and closed.** Sweeps ran for Active stores only, so a suspended store's expired stock
    holds were released by *nothing* — its shoppers cannot complete a payment, so the store would return from
    suspension with stock reserved against orders that can never complete, and the defect would surface at
    reactivation rather than at suspension. Sweeps now cover **active and suspended**; provisioning has nothing
    to expire and archived is a closed record, so both stay excluded. `BackgroundSweepScopeTests` pins all four
    states. **This changes BR-TEN-22 as previously written**, which was marked UNTESTED with exactly this
    consequence recorded as a known defect; the reasoning is now in the rule and it is reversible in one
    predicate if the owner wants suspension to freeze everything instead.
  - **F-8 stays the owner's to decide.** `CheckoutIdempotencyTests` was not touched: two orders, stock reserved
    twice, separate client secrets so no double charge, both expiring. What M5 *did* establish is that the
    browser is not a source of duplicates — the submit button disables while busy, and a real double click
    sends exactly one `POST /api/orders`, asserted on the running stack. So whichever path the owner chooses,
    it is a server-side choice and not a de facto one made by a clicking finger.
  - **Browser QA on the container stack** (`frontend/e2e/checkout-reliability.spec.js`, 3/3): a line that sells
    out between page load and submit is refused **with a reason on screen** rather than a dead button; the
    double click above; and the happy path still reaching a paid order after the split.
  - **Docker/runtime.** The stack was rebuilt and restarted on the refactored checkout: `/health/live`,
    `/health/ready` and the web proxy all 200, **zero** `LogLevel: Error` entries in the API log, and the three
    journeys above run against it rather than against `WebApplicationFactory`.
  - **TD-14 left alone deliberately.** The three hand-rolled retry loops were not touched by this phase's
    changes, and M5's own scope says to unify them only if it did.
  - Tests: Domain 524, Application 385, Architecture 89, Integration 364. Working tree clean and pushed at
    close (a1295d1).
- **Next-phase trigger.** M6 may start once checkout's happy and adversarial paths are both green on the same
  commit.

### M6 — Payments and financial correctness

- **Objective.** Everything money-adjacent that is not already `FIXED` in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md)
  is closed or is explicitly an owner decision, and nothing here is changed silently.
- **Scope.** R-01/P-05 (JOD and every three-decimal currency's Stripe minor-unit multiplier) — **this phase
  prepares the code path for either answer but cannot itself produce the answer**: make the multiplier and
  rounding a single, well-tested, table-driven point (`StripeAmountConverter`) so that *if* the owner's real
  test charge (§5) comes back three-decimal, the fix is the smallest possible diff, already anticipated and
  already tested against both hypotheses; R-03 (refund permission) — same pattern: keep both permission shapes
  implementable behind a clear switch, do not choose; D-13 (merchant of record) — `PaymentGatewayRouter` and the
  per-store account mechanism are already built per both paths (`OwnerDecisions.md`); audit them for
  completeness against *both* possible answers, closing any gap found in either path without picking one.
- **Dependencies.** None beyond the baseline; independent of M2–M5.
- **Backend/API.** No behavioural change to amounts, capture, cancellation, refunds, retries or idempotency
  without an ADR and tests, per `AGENTS.md` §0 rule 3 — this phase's job is largely audit, test-hardening and
  preparing clean decision points, not changing money behaviour.
- **Database.** None expected.
- **Frontend/UX.** None expected beyond surfacing whatever error states the audit finds missing.
- **Security.** Re-verify webhook signature verification, idempotency keys on refunds, and that no card data
  ever reaches this codebase (Stripe Elements scope) — `SecurityControls.md`'s existing control → test → gap
  table for payments is the checklist; confirm each row against the current code, don't just re-read it.
- **Testing.** `StripeAmountConverterTests` extended to assert both the two-decimal and (parameterized,
  currently-inactive) three-decimal hypothesis, so switching the multiplier later is a one-line change with an
  already-green test, not a scramble; `PaymentsAndRefundsTests` extended for any gap the D-13 audit finds;
  `AuthorizationMatrixTests` extended for the R-03 both-shapes preparation.
- **Browser QA.** A full paid order and a full refund through the fake gateway, both currently-live permission
  and routing shapes.
- **Docker/runtime verification.** Confirm the fake gateway is never implicit outside Development/Testing in the
  compose stack's Production-mode configuration (`AGENTS.md` §4's explicit control) — a live check, not a code
  read.
- **Documentation/ADR.** Update `docs/04-MODULES/Payments/README.md`; no ADR unless a code path is added (the
  audit itself, done correctly, should not need one — it is verification, not decision).
- **Technical debt touched.** None expected to open; TD-14's payment retry loop is in scope if this phase's own
  audit touches it.
- **Acceptance criteria.** `StripeAmountConverter` is proven correct under both hypotheses with tests naming
  which is *live* today and why; both D-13 routing paths and both R-03 permission shapes remain fully
  implementable with no half-built path; nothing about actual payment behaviour changed without an ADR.
- **Completion evidence.** Commits on `phase/17-production-hardening` from checkpoint `a0b9ef6`. **No payment
  behaviour changed** — that needs an ADR ([AGENTS.md](../../AGENTS.md) §0 rule 3) — but what is *provable* did,
  and four documented claims turned out to be false.
  - **P-05 prepared (the phase's named criterion).** `StripeAmountConverter` derives its multiplier through one
    switch, `HonoursIsoDecimals`, `false` today so nothing shipped moves. Its tests grew 5 → 14 rows and assert
    **both** hypotheses — the live ×100 and the ×1000 that becomes correct if the owner's real charge says so —
    plus a test that states out loud which one is live, so it cannot be flipped by accident. The answer, when it
    comes, is one value in front of an already-green test. The tests also pin the trap the audit found: ISK and
    UGX are zero-decimal in ISO but ×100 at Stripe, so the obvious `10^decimals` derivation would silently
    charge a hundredth of the price; `Math.Max(100, …)` is why, with a row per currency saying so. The
    three-decimal codes are deliberately not listed in `src/` (the white-label rule forbids currency literals
    there) — the multiplier reads `CurrencyInfo`, the single source for decimals.
  - **R-03 pinned, not decided.** An explicit refund needs `store.payments.manage` (admin only); cancelling a
    *paid* order refunds it in full on `orders.manage`, which staff hold. The audit traced that to sequence —
    the gate was placed when the endpoint only moved statuses, and the refund joined the same handler in Phase
    11. The real danger was that **nothing pinned it**: implementing the "yes" answer as an in-handler guard
    would have left the entire suite green, since no attribute changes and every existing caller is an admin
    holding both permissions. A test now states today's answer and asserts money actually moved.
  - **Three places claimed a refund goes to the account that took the money.** It goes to the account *kind*:
    `stripe:store` resolves the store's **current** account, so replacing a store's keys — including the
    ordinary test→live switch — sends a refund to an account where the intent does not exist, where it fails and
    cannot be retried. `Payment.cs`, `PaymentGatewayRouter.cs` and `Security.md` now say what the code does.
    Filed as **TD-50** (P1, needs an ADR) rather than fixed inside an audit phase.
  - **A security claim with no test now has one.** SEC-CFG-08 said store payment keys are never logged,
    "enforced by construction", naming tests that cover passwords and tokens but never a Stripe secret. A canary
    secret is now connected, read back through two paths, and asserted absent from every log message, property
    and scope.
  - Also recorded: **TD-51** (the refund idempotency key protects a retry only inside Stripe's 24-hour window,
    and `RetryRefundAsync` has no age limit — three places stated it unconditionally) and **TD-52** (nothing
    tests `PaymentGatewayRouter` because it constructs its gateway inline; all four routing rules were verified
    by reading, so this is a coverage gap, not a half-built path). Both fixes change payment behaviour or
    payments infrastructure, so both were filed, not made here.
  - **D-13 audited against both answers.** Live: the deployment account as merchant of record, with per-store
    accounts a complete opt-in. Stripe Connect is a *seam*, not code — and the docs already said so accurately.
    Webhook attribution is sound: a store-signed event naming another store is refused, a deployment-signed one
    is applied inside a fresh scope for the named store, and the event's intent id is never trusted.
  - **Verified live, not read** (the phase asks for exactly this): an API container in Production mode with no
    payment provider configured **refuses to start** — `InvalidOperationException` from
    `PaymentProviderSelector.Select`. The fake gateway can never be implicit outside Development/Testing.
  - **Browser QA** on the container stack: a paid order **and a full refund through the admin UI**, asserting the
    refunded amount rather than a status badge. The refund half had no browser coverage at all before this.
  - Tests: Domain 524, Application 385, Architecture 89, Integration 375. `./scripts/release-gate.sh --suites`:
    5 passed, 0 failed, **3 skipped** (no deployment target). Working tree clean and pushed at close (ad0d338).
- **Next-phase trigger.** M7 may start independently; no hard dependency on M6.

### M7 — Inventory and fulfillment

- **Objective.** Confirm stock reservation, release and the per-variant model (V1–V3) hold under the same kind
  of adversarial conditions M5 applied to checkout, and that fulfillment-adjacent operations (stock movements,
  low-stock alerts, thresholds) are complete and observable by a merchant.
- **Scope.** `InventoryItem` never-negative invariant under concurrent reservation/release; the checkout-expiry
  sweep's interaction with reservations (already covered structurally, re-verify); stock movement history
  completeness for audit purposes; low-stock threshold behaviour per variant (V2/V3 built the per-variant
  administration — confirm the merchant-facing alert (`stock.low`, built per ADR-0034/Phase 14) fires correctly
  per variant, not just per product).
- **Dependencies.** M2 (variant model stability).
- **Backend/API.** Audit-only unless a genuine gap is found (e.g. a per-variant threshold not actually wired to
  the notification path) — fix what's found, don't redesign what isn't broken.
- **Database.** None expected.
- **Frontend/UX.** Confirm the admin inventory screens (already migrated or still on TD-25's hand-rolled fetch
  list — check current state, don't assume) show per-variant stock clearly; TD-25's inventory screen migration
  to the query layer is in scope if this phase touches that screen anyway.
- **Security.** Tenant isolation on inventory movements re-verified; no cross-tenant stock visibility.
- **Testing.** A concurrency test explicitly for reservation/release racing (distinct from M5's checkout-level
  test — this one is Inventory-module-scoped); low-stock notification firing tests per variant.
- **Browser QA.** Admin inventory screens at phone width (folds into M4 if not already covered); a stock
  movement history view for a variant.
- **Docker/runtime verification.** Live stack, a real stock adjustment and its movement record.
- **Documentation/ADR.** Update `docs/04-MODULES/Inventory/README.md` if V3's per-variant model changed anything
  this phase touches beyond what ADR-0041 already recorded.
- **Technical debt touched.** TD-25 (inventory screen — **migrated here**, the screen was in scope).
- **Acceptance criteria.** No negative stock reachable under concurrent load; low-stock alerts fire per variant
  correctly; the admin inventory screen has no stale-data gap TD-25 already named.
- **Completion evidence.** **Done.** All three acceptance criteria met, each with evidence rather than a reading.
  Most of this phase's scope was found **already covered**, and is reported as such rather than rebuilt:
  - **Already covered, verified not assumed.** `InventoryItem` is per-variant with a per-variant
    `LowStockThreshold`; `StockBecameLow(ProductId, VariantId, Available, Threshold)` is raised at
    `InventoryItem.cs:128`; `StockBecameLowHandler` enriches it with the `variantLabel` read from the product
    aggregate (so Inventory knows nothing about options); and
    `tests/Souq.IntegrationTests/ProductOptionAdminTests.cs:295-297` **already** asserted
    `variantId`/`variantLabel`/`available` on the notification for a multi-variant product. Nothing was
    rebuilt here.
  - **The one genuine concurrency gap, now pinned.** Every existing test races two *different* reservations.
    The race that actually happens in production is **commit against release on the same reservation** — a
    payment confirming in the instant the expiry sweep releases the hold. Both touch `Reserved` and commit also
    touches `OnHand`, so if both succeeded the unit would leave twice, as a sale *and* a release, with the
    ledger unable to say which. `rowversion` on the inventory row rejects the loser: exactly one `Sale`
    remains, `onHand: 0, reserved: 0`.
  - **A test deleted rather than kept.** Writing that race surfaced two guards (`Release` on a closed hold and
    `Restock` on an uncommitted one are both no-ops). An integration test was written for them, then **removed**
    on finding `tests/Souq.Domain.Tests/InventoryItemTests.cs:134` and `:151` already assert exactly that at the
    layer the rule belongs to. A slower duplicate of a green test is a cost, not coverage.
  - **The ledger invariant is now enforced, not merely unviolated.** An audit of every path that writes
    `InventoryItem.OnHand` found **no path without a matching `StockMovement`**: four assignment sites, all
    inside the entity, all paired with `Record`, all in the same `SaveChanges` (the concurrency retry detaches
    movements too, so a failed attempt orphans nothing); the only migration that writes `OnHand` directly
    (`20260911141732_Phase6Inventory`) inserts a compensating opening-balance row for every mismatch in the same
    migration; and no `ExecuteUpdate`/raw SQL touches the table. But `Souq.Domain` opens its internals to
    `Souq.Infrastructure` for EF, so a ledger row **could** be built there without the matching stock change —
    breaking Σ ledger = on hand with no exception, discoverable only as a stock count that stops reconciling
    weeks later. `ModuleAndContractRuleTests` now scans Infrastructure's IL for `Newobj` of `StockMovement`, and
    the rule was **verified to fail against a deliberate violation** before being kept.
  - **TD-25 closed for this screen, after proving the defect was real.** `Pagination` fires while a request is in
    flight and `load()` had no generation guard, so a response for a page the merchant had already left could
    paint its stock numbers under the wrong page label — on the one admin screen whose numbers are read and then
    *acted on*. The screen is now on the query layer (`Staff.jsx`'s existing pattern), the page is the cache key,
    and an adjustment invalidates the shared `['inventory']` root so the table and the low-stock banner refresh
    together. Three of the five new tests in `Inventory.test.jsx` were each **run against the pre-migration
    screen and observed to fail** (page 2's rows rendered under "page 1"; the table blanked between pages; the
    low-stock count re-fetched once per page). The lint rule that counts the pattern went 20 → 19 hits. The
    movement drawer was **not** touched: it already discards superseded responses with an `alive` flag.
  - **A real defect found only in a real browser, fixed in the shared component.** `RowActionsMenu`'s trigger was
    a flat `32px` square with no media query — and it is the **only** way to act on a table row anywhere in the
    admin area. Measured at 32px on the inventory screen at phone width, against the 40px phone rule
    DesignSystem.md §11 already stated. Fixed under `@media (pointer: coarse)` (the input device, not the window
    width — a narrow desktop window has a precise mouse; a large tablet has a finger), so every admin table
    gained it at once; **verified live at 40×40 on an emulated Pixel 7**. `responsive.spec.js` now asserts it
    across inventory, products and orders, and refuses to pass if no table had rows to measure.
  - **Two gaps in the tests themselves, fixed.** DesignSystem.md §11 claimed `responsive.spec.js` asserted
    "touch targets at least 40px" when it measured only the bottom tab bar. And the platform-area phone test had
    hard-coded `http://admin.localhost:5173`, so it **could never run against the container stack at all** — it
    now derives the platform origin from `baseURL`, and passes there for the first time.
  - **Tenant isolation re-verified, and the test already existed.**
    `TenantIsolationTests.cs:219` covers both movement routes with a **positive control** (store A's admin does
    see rows for the same ids), `:237` covers stock levels and low-stock, and a meta-test enumerates every
    endpoint with a route parameter so a new inventory route cannot escape the isolation table. `StockMovement`
    is `ITenantOwned`, its FKs are composite and tenant-scoped, and no inventory query uses
    `IgnoreQueryFilters()`.
  - **Docker/runtime verification, live rather than read** (this phase asks for exactly this): a real stock
    adjustment on the container stack took variant 2 from 12 → 8 and wrote `Adjustment -4 → 8` carrying its
    reason; a second adjustment crossed the threshold (available 4, threshold 5) and produced the notification
    `stock.low` with `variantId: "2", available: "4", threshold: "5"` — the per-variant alert, observed firing,
    not inferred from a handler.
  - **Browser QA** on the container stack: `frontend/e2e/admin-inventory.spec.js` (new) covers the row read cell
    by cell, the movement history drawer opened **from the inventory screen** (it had only ever been opened from
    the variants page), and an adjustment that refreshes table and banner together and lands in the ledger with
    its reason. `/admin/inventory` was also missing from the phone-width sweep entirely and was added.
  - **Not claimed:** TD-25's other eight admin screens remain on `useEffect` (P3, no phase schedules them);
    `StockMovementType.Return` is still never written; the ledger still records no actor. All three were already
    documented and none is in this phase's scope.
  - Tests: Domain 524, Application 385, Architecture **90** (+1, the ledger-construction rule), Integration
    **376**, frontend Vitest **615** (+5). `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped** (no deployment target).
    Working tree clean and pushed at close (10132db).
- **Next-phase trigger.** M8 may start independently of M7.

### M8 — Promotions and pricing

- **Objective.** Close TD-06 (the duplicate, weaker coupon-evaluation endpoint) and confirm the full pricing
  pipeline — coupons, currency guards, the still-zero tax stage — is internally consistent and has no path that
  disagrees with checkout's own pricing.
- **Scope.** TD-06: delete `GET /api/coupons/apply`'s client-trusted-subtotal path, or reimplement it over
  `IPricing` so it can never disagree with checkout (the fix TD-06 itself names — pick whichever preserves any
  real frontend usage found, or delete outright if nothing depends on it: check first); R-09's residual
  (`Coupon.Value` has no currency column, currently unreachable but a schema gap — decide whether to close it in
  this phase's migration or explicitly re-file with today's evidence in `RiskRegister.md`).
- **Dependencies.** M6 (pricing correctness assumptions) for context, not a hard build dependency.
- **Backend/API.** Fix or remove the TD-06 endpoint; if `Coupon.Value` gains a currency column, an additive
  migration with backfill from the coupon's store currency (not destructive — write it carefully, this is
  exactly the kind of migration `AGENTS.md` §0 rule 6 wants hand-reviewed).
- **Database.** The `Coupon.Value` currency column, if this phase closes R-09's residual — additive only.
- **Frontend/UX.** None expected beyond confirming nothing used the now-removed/rewired coupon-apply endpoint
  incorrectly.
- **Security.** Confirm the pricing pipeline remains the single source of a total everywhere it's shown
  (`AGENTS.md` §4 — server-side pricing) after TD-06's fix.
- **Testing.** `PricingServiceTests` extended for any TD-06 rewiring; a currency-mismatch test for the fixed
  coupon column if built.
- **Browser QA.** A coupon applied at checkout, in the store's currency and (if a multi-currency test store
  exists) rejected cleanly in another.
- **Docker/runtime verification.** Live stack, a real coupon redemption.
- **Documentation/ADR.** Update `docs/04-MODULES/Promotions/README.md`; no ADR expected (this closes a
  documented defect, it does not change the pricing architecture).
- **Technical debt touched.** TD-06 (**closed**), R-09's residual (**re-filed with today's evidence and dated**).
- **Acceptance criteria.** No coupon-adjacent endpoint can produce a total checkout would refuse; `Coupon.Value`
  either has a currency guard or the unreachability is re-confirmed and dated.
- **Completion evidence.** **Done.** Both acceptance criteria met. No migration was written and no schema
  changed — the phase's two candidate changes were each decided on measured evidence, one to delete and one to
  leave alone.
  - **TD-06 closed by deleting the endpoint, not rewriting it.** `GET /api/coupons/apply` took the subtotal from
    the caller, so it skipped the per-customer limit, measured the coupon's minimum against a number the caller
    chose, and — with no validator and no upper bound beyond the currency's minor units — answered
    `subtotal=999999999` with a discount of about 150,000,000 as a `200`. Of TD-06's two named fixes,
    "reimplement it over `IPricing`" **collapses into the endpoint that already exists**: pricing the caller's
    real basket through `IPricing` is exactly what `GET /api/basket/quote` does, anonymously via the guest
    basket, under the same `coupon-preview` limit, in the store's currency. So deletion cost no capability, and
    it completes [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md), whose context opens on money being
    computed in three places.
  - **One clause of TD-06 was false and is corrected rather than repeated.** It claimed the endpoint skipped the
    module check. `RequiresModule` was on the controller all along and `TenantAvailability` runs ahead of
    authentication — asserted against that very route by an existing test. A debt item that misstates the defect
    invites the wrong fix.
  - **Nothing called it, and the four tests that touched it were re-pointed, not dropped.** `client.js` had no
    entry; the checkout's "Apply" button re-quotes the basket. Three tests used the route only as a convenient
    anonymous specimen for cross-cutting rules, so each moved to another specimen: the problem+json contract to
    `POST /api/orders` with an unknown code (the same `422 CouponNotFound` through the surviving evaluator),
    module gating to `GET /api/coupons` (still anonymous, because the gate precedes authentication), tenant
    isolation to `GET /api/basket/quote` (where the refusal is an in-band outcome inside a `200`). The fourth
    was `AuthorizationBoundaryTests`' reviewed public-endpoint list, **which is what stopped this deletion
    passing unreviewed** — a public route cannot appear or vanish without a deliberate edit there.
  - **Its two unit tests were deleted rather than moved,** because both rules they covered live in Domain and
    are tested there (`CouponTests` for percentage rounding, `MoneyTests` for excess minor units). The second
    test's scenario existed *only* because a client could supply the amount.
  - **R-09's residual: re-confirmed unreachable and dated, with no column added.** The unreachability was
    verified by tracing three legs rather than re-reading the register: a basket's currency is always the
    store's (`PricingService` seeds from `Money.Zero(store.Currency)`, and `Money.Add` throws on a mismatch);
    `Tenant.Currency` has exactly one post-construction writer, `Tenant.ChangeCurrency`, which throws on
    commercial activity; its only caller passes `HasCommercialActivityAsync`, which counts `Coupons`. No
    store-admin currency path, no soft-delete hiding a coupon row from that count, and no delete-then-change
    escape. Pinned end to end by an existing test. A currency column was **deliberately not added**: the
    register's own remedy says it waits for a migration with another reason to exist, and guarding a path that
    cannot be walked is speculative work. **Two honest limits on the word are now written into R-09** rather
    than left implied: `EnsureUsable`'s currency guard can only speak when a coupon *has* a minimum — which is
    not this risk's case — and the lock is forward-looking and was never backfilled, so it protects no store
    that changed currency before Phase 17. Neither has a victim while nothing is in production.
  - **The pricing pipeline is internally consistent, verified rather than asserted.** `PricingService` is the
    only place in the repository where a total is assembled, and the quote-versus-order agreement is already
    pinned across subtotal, discount and shipping by `BasketTests` and `ShippingTests` — so no new test was
    written for it.
  - **But the tax stage's documentation understated what closing P-06 costs, and that is corrected.** The
    Shopping README said the stage "keeps its fixed place in `PriceQuote` and `BasketDto`, so introducing tax
    changes no contract". True of those two, and misleading overall: **Ordering has no tax slot at all.**
    `Order.TotalAmount` is `(subtotal − discount) + shipping` with no tax term, and `Place` snapshots it into
    `PlacedTotal`. The two agree today *only because tax is zero*; a non-zero tax in the pipeline alone would
    quote one figure and charge another. It would be caught rather than shipped (the tests above), but P-06's
    answer needs an `Order` column, not just a line in a table. **No tax rule was invented** — P-06 remains the
    owner's.
  - **Browser QA: the coupon's first browser journey, ever.** `frontend/e2e/checkout-coupon.spec.js` (new) is
    the first browser coverage the coupon has had, despite three admin screens and a checkout step. Three
    journeys on the container stack: a percentage coupon showing its discount in the store's currency where
    **the total the shopper saw is the total the order recorded** (40 less 25% is 30, compared digit-wise so the
    assertion is about the amount, not the formatting); an unknown code refused with a readable reason that
    leaves the total untouched, renders no discount row and does not block checkout; and a coupon below its
    minimum refused with its own `InvalidCoupon` reason — precisely what the deleted endpoint got wrong.
  - **Docker/runtime verification, live:** a real redemption end to end on the container stack — the merchant's
    coupon, the browser showing 10 off a 40 basket, and the redemption recorded against order 1001 as
    `discount 10.0 JOD, status Confirmed` in the merchant's own redemptions view. The refused coupon left no
    redemption. Also confirmed live that the deleted route is gone and leaks nothing: it answers `405`, but so
    do `api/coupons/nonsense` and `api/coupons/9999` — any extra segment under that controller does, because the
    `{id:int}` templates match for method negotiation. It is now indistinguishable from any path that never
    existed.
  - **Not claimed:** no tax model (P-06, owner's); `Coupon.Value` still has no currency column (R-09, waiting
    for a migration with another reason); refunds still do not release a coupon use (R-05, a product decision).
  - Tests: Domain 524, Application **383** (−2, the deleted preview's unit tests), Architecture 90, Integration
    376, frontend Vitest 620, browser journeys **104 in 15 files** (+3). `./scripts/release-gate.sh --suites`:
    5 passed, 0 failed, **3 skipped** (no deployment target). Working tree clean and pushed at close (c579bec).
- **Next-phase trigger.** M9 may start independently of M8.

### M9 — Customer accounts and security lifecycle

- **Objective.** Close the one named, unambiguous gap (TD-29 — no password-change screen despite the endpoint,
  client function and context method all existing) and audit the rest of the account lifecycle — registration,
  sign-in, refresh-token rotation and reuse detection, erasure — against its own documentation rather than
  against assumption.
- **Scope.** TD-29 (build the screen); a fresh read of `AuthenticationAndAuthorization.md` and
  `docs/04-MODULES/Identity/README.md` and `docs/04-MODULES/Customers/README.md` against the current code,
  specifically TD-03's Identity↔Customers cycle (registration creates a customer; erasure and profile updates
  write the user) — if M1 didn't already resolve TD-03's direction, this is the natural phase to finish it,
  since it is squarely account-lifecycle scope.
- **Dependencies.** M1 (TD-03's direction, if decided there).
- **Backend/API.** TD-29's endpoint already exists — audit it for completeness (current-password verification,
  session invalidation on change, rate limiting) rather than assume it's finished because it exists.
- **Database.** None expected unless TD-03's resolution needs one (write it carefully, additive).
- **Frontend/UX.** The password-change screen itself, in `/account`, following the existing `AccountLayout`
  shell; both languages, both themes, phone width, a11y (label association, error announcement).
- **Security.** Re-verify refresh-token rotation and reuse detection under a genuinely adversarial test (reuse
  an old token after rotation, confirm the whole family is revoked); confirm password change invalidates other
  sessions (a control worth having and worth testing explicitly, not assuming).
- **Testing.** A component test for the new screen; an integration test for password-change's session
  invalidation; `AuthSessionTests` extended if a gap is found in the reuse-detection audit.
- **Browser QA.** Sign in, change password, confirm other sessions are signed out (a second browser context in
  the same Playwright run is the natural way to prove this).
- **Docker/runtime verification.** Live stack, the full sign-in → change-password → re-sign-in flow.
- **Documentation/ADR.** Update `Identity/README.md` and `Customers/README.md`; an ADR only if TD-03's direction
  changes an existing contract shape.
- **Technical debt touched.** TD-29 (**closed**), TD-03 (**closed — the cycle is gone**), TD-16's retention scope
  (**re-filed with the split made explicit**), and R-15 closed with it. TD-53 newly filed.
- **Acceptance criteria.** A signed-in customer can change their password from the UI; reuse detection and
  session invalidation are proven by a test that actually attempts the attack, not just documented as existing.
- **Completion evidence.** **Done.** Both acceptance criteria met — and the second one earned its wording: the
  attack test found that the control did not hold.
  - **TD-29 closed: the screen exists, at `/account/password`.** Everything beneath it already did — the
    endpoint, its validator, `api.changePassword`, `AuthContext.changePassword` — and nothing called any of it,
    so a signed-in customer had to sign out and ask for an emailed reset link to change the password they were
    already authenticated with. A route, not a fourth panel on `/account`, because Phase 16 split that page
    precisely for carrying three concerns; going the other way would have undone a recent deliberate decision.
  - **What the screen promises is what the handler does.** Every other session is revoked and a fresh one issued
    for this device, so "signs you out everywhere else, this device stays signed in" is a description, and it is
    printed above the button rather than after it. A wrong current password attaches to its own field, because
    the server returns a `400` with a stable code rather than a `401` — which the client would read as an expired
    session and sign the person out over a typo.
  - **The password rules stopped being copied.** `Register` and `ResetPassword` each carried their own
    length-only check, so a password without a digit passed the browser and was refused by the server: a full
    round trip for a message that could have been immediate. All three screens now share
    `features/account/passwordForm.js`, which mirrors `PasswordRules` including letters-and-digits and uses
    `\p{L}`/`\p{Nd}` so it is exactly as Unicode-aware as `char.IsLetter`/`char.IsDigit`.
  - **The password policy had no test at all.** It guards every credential in the system — customer, store
    staff, platform owner — and nothing asserted 8–128, letters-and-digits, or must-differ-from-current.
    `PasswordRulesTests` now does, and it is also where the frontend's copy is held level: it asserts that an
    Arabic password with Arabic-Indic digits is accepted, which is the claim the browser-side `\p{Nd}` depends
    on. Had that been false, the client would have been *wider* than the server — the direction that sends a
    request certain to be refused.
  - **A real security hole, found by writing the test the phase asked for.** `RevokeAllAsync` sweeps
    `ListActiveForUserAsync`, filtered `RevokedAt == null && UsedAt == null`, so a token another device had
    consumed by rotation seconds earlier was never revoked — and `IsWithinReuseGrace` requires only
    `RevokedAt is null` and is evaluated *before* reuse detection. So whoever held that device's cookie could,
    within ten seconds of the password change, mint a completely valid new session carrying the new security
    stamp. **Ten seconds is not a narrow window to anyone who knows it:** an attacker rotating every five
    seconds survives every password change the victim makes. The HTTP attack test was written first and observed
    to return `200`.
  - **Fixed without a new column, because the data already distinguished the two cases.** An innocent tab race
    means the first tab's rotation *succeeded*, so the family still holds an active token; a password change, a
    logout or reuse detection revokes that token and leaves the family with none. The grace path now requires a
    live family. When the family is dead the answer is a plain "session ended" and deliberately **not** reuse
    detection — that branch rotates the stamp, which would sign out the person who just changed their password,
    from the device they changed it on, because some other device they had just signed out happened to poll.
    Password change is the only exposed path: erasure and disable both set `Disabled`, so `userActive` is false
    and the branch is unreachable.
  - **The existing tab-race unit test began failing, and that was correct.** Its fixture returned an empty
    family, which is not what an innocent race looks like. The successor is now explicit, and the complementary
    case — a dead family inside the grace — is asserted too, including that no stamp rotates.
  - **Reuse detection was already attack-tested, and is reported rather than redone.**
    `التجديد_يدوّر_الرمز_وإعادة_رمز_قديم_تُسقط_الجلسة_كلها` backdates `UsedAt` to escape the grace, replays the
    old cookie over real HTTP, and asserts the whole family is dead — the legitimate browser's newest cookie no
    longer refreshes and its access token is rejected. That half of the criterion was met before this phase.
  - **TD-03 and R-15 closed: the one cycle the target graph forbids no longer exists.** The register's rank-1
    item. `IAccountProfiles` (declared by Identity, implemented by Customers) and `IAccountLifecycle` (declared
    and implemented by Identity) were built — **both declared by Identity on purpose**, because a cycle is two
    arrows and only one may survive, so a contract pointing the other way would have documented the cycle rather
    than broken it. **Measured, not asserted:** the generated crossing ratchet went from **74 crossings across
    15 pairs to 62 across 13**, with all twelve Identity↔Customers crossings gone and both pairs off the table.
    Identity no longer names `Customer` or `ICustomerRepository`; Customers no longer loads `IUserRepository`,
    mutates `User`, reads `PasswordHash`, or revokes `RefreshToken` rows — session invalidation is back inside
    the module that owns sessions.
  - **The test suites carried the same violation as the code, and moved with it.** Customers' tests asserted
    `_user.Status`, `_user.PasswordHash` and `Forget(7)` *through* customer erasure; Identity's tests asserted
    the shape of the `Customer` aggregate through registration. Each module pinned the other's behaviour.
    `AccountLifecycleTests` and `CustomerAccountProfilesTests` now hold those assertions on the right side, so
    coverage moved rather than shrank — and gained the erase's **save-then-forget ordering**, which nothing had
    pinned and which matters: forgetting first would let a concurrent request re-cache the old stamp.
  - **And a note for whoever re-opens that pair:** `AllowedContracts` could not have caught a regression there.
    All twelve crossings travelled through `Souq.Domain.Interfaces`, which that rule does not police. The guard
    that counts is `ModuleDomainDependencies.md`.
  - **Erasure is still atomic.** `CustomerErasure` stages what Customers owns and then calls
    `IAccountLifecycle.EraseAsync`, which saves — so everything staged is committed with it, exactly as when
    this module did the account's half itself. That idiom is not new: `AuthSessionIssuer.IssueAsync` already
    saves what its caller staged.
  - **TD-16 re-filed with the split made explicit, not solved.** Only part of it is the owner's: refresh tokens
    and stock reservations carry no legal question and their own expiry already says when a row stops meaning
    anything, so purging them is technical work and the natural first slice. Audit entries and in-app
    notifications are what actually waits on a retention decision. M9 did not answer it and does not pretend to.
  - **TD-53 newly filed, deliberately undecided.** The five-attempt lockout guards sign-in but not
    change-password, whose only brake is the shared `auth` limit (10/min per host+IP). Reaching it needs a valid
    access token, so what it buys an attacker is persistence rather than entry. It was filed rather than fixed
    because extending the lockout there means a customer who mistypes their current password five times loses
    their whole account for fifteen minutes — a product call as much as a security one.
  - **Browser QA** on the container stack: `frontend/e2e/account-password.spec.js` (new), including the journey
    that no single context can prove — two independent browser contexts, both signed in, one changes the
    password, and the other is checked on **both** paths out: its access token (a protected page is refused) and
    its refresh cookie (a reload does not restore the session). Also that the old password stops working and the
    new one starts, and that a weak new password sends no request at all.
  - **Docker/runtime verification:** that same journey is the phase's live sign-in → change-password →
    re-sign-in flow, run against the container stack.
  - **Not claimed:** the 30-second per-instance security-stamp cache still means a multi-instance deployment
    honours a revoked access token for up to 30 seconds — already documented, and no second instance is
    deployed; TD-53 above; audit and notification retention (TD-16, the owner's).
  - Tests: Domain 524, Application **408** (+25), Architecture 90, Integration **377** (+1), frontend Vitest
    **635** (+15), browser journeys **107 in 16 files** (+3). `./scripts/release-gate.sh --suites`: 5 passed,
    0 failed, **3 skipped** (no deployment target). Working tree clean and pushed at close (fbe8665).
- **Next-phase trigger.** M10 may start independently of M9.

### M10 — Merchant back-office completion

- **Objective.** Close the frontend architectural debt specifically in the admin area (TD-24's half-migrated
  feature folders, TD-25's remaining hand-rolled-fetch screens, TD-28's five divergent status-colour maps) and
  confirm every store endpoint behind a permission genuinely has a working, tested admin screen — not just a
  caller in `frontend/src/api/client.js` (Phase 17's own bar).
- **Scope.** TD-25's named remaining screens (categories, coupons, customers, inventory, orders, payments,
  products, review moderation, shipping methods) migrated to the TanStack Query pattern the storefront and
  several admin screens already use; TD-28's status/tone vocabulary unified into one module; TD-24 addressed
  opportunistically on any screen this phase substantially touches anyway (per its own stated fix — "move each
  screen when it is next substantially changed" — do not do a big-bang move that isn't otherwise warranted).
- **Dependencies.** M7 (if the inventory screen migration overlaps), M9 (Identity/Customers screens if TD-03
  changed their shape).
- **Backend/API.** None expected — this is a frontend data-fetching pattern migration, not a contract change.
- **Database.** None.
- **Frontend/UX.** Each migrated screen keeps its exact current behaviour and tests, gaining only the query
  layer's stale-response discarding TD-25 names as the actual defect being fixed (a superseded response landing
  after a newer one, showing briefly-wrong data) — this is not a visual redesign.
- **Security.** None specific — confirm no permission check was accidentally weakened by the migration (a
  common risk when a screen's data-fetching is rewritten: re-run `AuthorizationMatrixTests`).
- **Testing.** Existing Vitest coverage for each migrated screen must still pass unchanged in intent (assert the
  same behaviour, via the new pattern); add a regression test for the actual stale-response race TD-25
  describes, proving it is fixed.
- **Browser QA.** Each migrated screen under a slow/racing network condition (Playwright's route interception
  to delay one response) proving the stale-response defect is gone.
- **Docker/runtime verification.** Live stack, each migrated screen exercised.
- **Documentation/ADR.** Update `FrontendArchitecture.md`; no ADR (this executes an already-decided pattern,
  ADR-0038, it does not choose a new one).
- **Technical debt touched.** TD-25 (**closed**), TD-28 (**closed**), TD-24 (partial and **not** claimed closed),
  TD-43 (applied to a second spec), TD-54 newly filed.
- **Acceptance criteria.** The lint rule TD-25 cites (`react-hooks/set-state-in-effect`) reports zero hits in the
  screens this phase migrated; one `tone` vocabulary used by all five screens TD-28 named.
- **Completion evidence.** **Done.** Both acceptance criteria met — and both items turned out bigger than their
  entries said, in opposite directions: one was mechanical and one was four hidden accessibility defects.
  - **TD-25 closed: eight screens migrated, `set-state-in-effect` 20 hits → 7.** Categories, Coupons,
    Customers, Orders, Products, ReviewModeration, SearchSynonyms, ShippingMethods (Inventory moved in M7).
    None of the seven remaining hits is an admin screen — they are storefront and checkout components outside
    the item's scope, and they are now named in the register so whoever takes them has the list.
  - **The defect was demonstrated before it was fixed, and pagination was the wrong place to look.** Search is
    where it bites: typing a word fired a request per keystroke, so an earlier reply could paint over a later
    one — a table of results for something other than what is written in the box, on screens where a merchant
    then acts on a row. `Orders.test.jsx` (that screen had **no test at all**) was run against the
    pre-migration screen and observed to fail twice over: the stale row rendered over the newer results, and
    four requests fired for one word.
  - **Two deliberate, named behaviour changes** rather than silent ones: search is now debounced, matching
    `platform/Stores.jsx`, the existing pattern for a searchable table on this layer; and "reset to page 1 when
    a filter changes" moved from an effect into the handler, which is what takes the lint count to zero. The
    single observable difference — typing then fully deleting it while past page 1 now returns you to page 1 —
    is recorded rather than glossed.
  - **Payments was left on the old pattern on purpose.** It is on TD-25's list but has no lint hit (its
    `setState` sits inside `.then`, not the effect body) and no reachable race: it fetches on mount and after
    save, with no criteria to outrun. Migrating it would mean extracting a form component to seed state from
    query data — a structural change to the payments screen with no defect behind it.
  - **TD-28 closed, and it was not five maps.** An inventory found **nine named maps, eight inline ternaries
    and six implicit `status.toLowerCase()` lookups** across four CSS modules — three byte-identical copies of
    the same five rules, and a fourth with the same five names at two different values because a WCAG contrast
    fix had been applied to only one. Twenty-two classes collapsing to five real tones.
  - **The drift was visible, not theoretical.** `Succeeded` was blue for a payment and green for a refund
    **rendered inches apart in the same drawer**; `invited` was amber on the Staff screen and blue on the
    platform Accounts screen **from the same helper function**; "enabled" was blue on two tables and green on
    two others; order `Pending` rendered at two different contrast grades depending on the screen.
  - **Unifying is not redesigning, and the line was drawn explicitly.** Every case where two screens gave
    different answers for the *same value* was fixed — those are defects by definition. Every self-consistent
    status kept its tone. The four that are defensible but unexamined (`Draft`, `Released`, payment
    `Cancelled`, and the Stripe key-mode badge, which is not a domain status at all) are **TD-54**, with the
    argument against each written at its line in `statusTone.js` rather than lost.
  - **`neutral` was the missing member.** No badge tone was neutral before, so every status was forced into a
    colour that meant something — which is part of why `Draft` is a warning and `Released` a danger.
  - **A tone that does not derive per theme is not a tone.** The amber had no token: two hex literals, so it
    never followed dark mode — a near-white pill on a dark surface. `--color-warning`/`--color-warning-soft`
    now join the other three, derived per tenant *and* per mode with the foreground chosen by measured
    contrast. Verified live: `#8A5A0E on #EDE4D6` → `#AD8C56 on #28261C` between modes.
  - **Then the finding that mattered most, and it was a guard rather than a colour.** The only axe sweep that
    visits every route in **dark mode** had `color-contrast` disabled, with no recorded reason — the single
    rule that could catch a colour not following the theme. Enabling it found **four real, pre-existing
    defects**, all fixed:
    - the **store's own wordmark at 2.15:1, on every page**: the accent was readability-checked in dark mode
      and used raw in light, an asymmetry that reads like an oversight;
    - secondary text at 4.42:1 on `--color-surface-alt`, which is *darker* than the background it was measured
      against — **the same mistake already corrected for status colours in that very file**, never corrected
      for muted text;
    - the 404 code, because a colour readable on white is not readable on a slightly darker surface (for dark
      text, white is the *easiest* background, not the hardest) — the reference is now the hardest surface;
    - the product "New" badge at **1.06:1 in dark mode — invisible**, because it took its text from the brand
      colour while sitting on the accent, and the brand colour lightens in dark mode.
  - **The full storefront matrix is now axe-clean with contrast on:** every route × 4 widths × 2 languages ×
    2 themes, 6/6 green. And `--color-accent-on-surface` gives accent-as-text its own readable derivation, so
    `--color-accent` stays exactly the merchant's colour for borders, focus rings and fills.
  - **Two test-harness defects fixed, both of which had been hiding verification.** Five specs hardcoded
    `admin@souq.com`, so they could only run against a stack seeded with exactly that and failed elsewhere
    with a navigation timeout rather than a message — which is why the product-variants journey could not be
    run against the container stack while verifying this phase (it passes 5/5 now, including its Arabic, RTL
    and dark-mode axe test). And `admin-inventory.spec.js` needed TD-43's settle-then-click helper: the row
    menu closes on scroll, so clicking mid-scroll detached the item — "element was detached from the DOM", no
    product defect, and the retries cost 1.1 minutes where the fixed spec takes 17 seconds.
  - **Browser/Docker verification:** every migrated screen exercised on the container stack; the tone tokens
    read out of a live browser in both themes; the storefront matrix and the admin, variants, coupon and
    password journeys all green against it.
  - **Not claimed:** TD-24 is *not* closed — the nine screens M10 touched already had their pure logic in
    `features/`, so there was nothing to move for them, and `api/client.js` (a hundred endpoints in one module)
    is untouched and remains the substantive half. `back-office.spec.js` still needs TD-43's helper. The seven
    remaining `set-state-in-effect` hits are storefront/checkout, outside TD-25.
  - Tests: Domain 524, Application 408, Architecture 90, Integration 377, frontend Vitest **649** (+14),
    browser journeys 107 in 16 files. `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped**
    (no deployment target). Working tree clean and pushed at close (d84e2c4).
- **Next-phase trigger.** M11 may start independently of M10.

### M11 — Platform owner and tenant operations

- **Objective.** Close what Phase 18 left explicitly open that is engineering's to close, and hold the rest as
  named owner decisions rather than let them look forgotten.
- **Scope.** D-22 (storefront preview) and P-07 (platform-wide settings) are **owner decisions, not engineering
  gaps** — this phase's job is to confirm [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md)'s
  design is still accurate against the current tenant-availability gate and token-validation code (so the moment
  the owner answers D-22, implementation is a small, ready change), and to leave P-07 visibly blocked rather
  than build a settings screen with nothing true to edit. Anything else in the platform area findable by a fresh
  read of `docs/04-MODULES/Platform/README.md` against the code.
- **Dependencies.** None specific.
- **Backend/API.** None built for D-22/P-07 themselves (§5 stop). Any other platform-area gap found by the fresh
  read is fixed directly if small and unambiguous, or filed in `TechnicalDebt.md` if not.
- **Database.** None expected.
- **Frontend/UX.** None expected beyond what a found-and-fixed gap needs.
- **Security.** Re-verify `ProvisioningBoundaryTests` and the platform/store token-separation tests still hold;
  this is exactly the boundary D-22 would extend, so it must be airtight before that decision is even made.
- **Testing.** Whatever a found gap needs; otherwise this phase is largely a verification pass.
- **Browser QA.** `frontend/e2e/platform-provisioning.spec.js` re-run clean.
- **Docker/runtime verification.** Live stack, the provisioning flow end to end.
- **Documentation/ADR.** Update `StorefrontPreview.md` only if the underlying gate code changed shape since it
  was written (re-verify, don't assume); no ADR unless a gap fix is itself architectural.
- **Technical debt touched.** None filed — the fresh read found documentation drift and two test gaps, all
  fixed here rather than deferred.
- **Acceptance criteria.** D-22 and P-07 remain correctly and visibly blocked in `OwnerDecisions.md` (not
  silently built around); everything else in the platform area not blocked on those two is complete.
- **Completion evidence.** **Done.** Both acceptance criteria met. This was billed as "largely a verification
  pass" and it earned its keep: the verification found a defect that stopped the application booting at all in
  one of its three environments.
  - **D-22 and P-07 are still genuinely blocked, verified rather than assumed.** No preview code exists
    anywhere — an exhaustive search for `preview` in `src/` returns only the unrelated coupon rate-limit
    policy, and in `frontend/` only the in-frame look preview the brief already names as the acknowledged
    non-substitute. No platform-wide setting is stored anywhere and no `/platform/settings` route exists.
    `platform.settings.manage` is still granted to the owner and required by no endpoint, exactly as P-07
    says. **Nothing is built around either.**
  - **`StorefrontPreview.md`'s design is still current**, because neither dependency has changed: the
    availability gate and access-token validation were both last touched on 2026-09-11, before the document
    was written. So the moment D-22 is answered, the change is still the small one described.
  - **But three claims in it were wrong or understated, and are corrected.** It said "no credential today is
    ever put in a URL" — false: the order-tracking token is a 128-bit value in the *path* of a deliberately
    shareable link, and every invitation, reset and verification link carries `?token=`. The recommendation
    survives on its own reasoning (a fragment is not sent to the server and does not reach logs or `Referer`),
    but it may not rest on a false precedent. And "one change to `IsOpen`" hides that `IsOpen` is a pure static
    with no request access (so a signature change), that **the gate runs before authentication** — so the
    grant must be readable without the auth pipeline, which the recommended cookie satisfies by luck rather
    than analysis — and **before the rate limiter**, so a grant lookup would sit ahead of it on every request
    to a closed store. All three are now written down.
  - **D-22's own evidence line credited a test with an assertion it does not contain.** It said
    `ProvisioningBoundaryTests` proves a closed store answers `503`; that file contains no `503` assertion at
    all — it activates the store first, precisely so the token refusal is observable rather than masked. The
    `503` evidence is real but lives in `TenantResolutionTests`, `PlatformAdministrationTests` and the e2e
    spec. The citation is split correctly now: an owner reading a decision brief should be able to follow its
    evidence to the line.
  - **`Platform/README.md`: one materially wrong claim and a self-contradictory cluster, both fixed.** It said
    a closed store serves **only** `GET /api/storefront/config` and "every other endpoint answers 503" — wrong
    since Phase 12 (R-08): sign-in, refresh, sign-out and the current user carry the same attribute, which is
    the point, because a suspended store's administrator has to be able to get in and fix it. The module
    README documenting the gate was **less accurate than the brief that merely depends on it**. Separately,
    five statements still placed the store-payment use cases in `Features/Stores` while a sixth in the same
    file recorded their move to `Features/Payments` in M1.
  - **Four more corrections of the same kind:** "platform requests all carry an explicit tenant id" (the rule
    is permissive, not mandatory — four platform queries carry none); the `IgnoreQueryFilters` list was missing
    `Coupons` and `ShippingMethods`; the currency-lock question was given as `Products` or `Orders` when it is
    four tables — **the very breadth M8 relied on to close R-09**; and three real omissions were added (the
    three `/api/platform/users` endpoints and `/api/platform/stats`, the `IPlatformHosts` contract, and the
    `DomainReserved` failure mode).
  - **The defect: the API could not boot in Development at all.** `SearchIndexBackfill` resolved the scoped
    `ITenantDirectory` from the **root** provider. Development turns DI scope validation on, so startup threw
    and the API never listened — meaning `dotnet run --project src/Souq.API`, the command in CLAUDE.md, had
    been broken since M3 introduced the backfill. Production and Testing have that validation off, so there the
    same call silently gives a scoped service the lifetime of the process: the same bug without a message.
    **Every verification in this programme ran on the container stack and every integration test runs in
    Testing — two environments that do not check, and a third that does which nothing visited.**
  - **And the guard, which matters more than the fix.** The test factory now sets `ValidateScopes` and
    `ValidateOnBuild`, so the same mistake is a failing test rather than a surprise for whoever clones the
    repository — and `ValidateOnBuild` catches the wider case of a dependency never registered at all. All 379
    integration tests pass with both enabled.
  - **The boundary was airtight in one direction and *sampled* in two.** The enumerating meta-test covers every
    platform endpoint against a store token automatically. But the 403 sweep excludes platform endpoints, so
    role gradation *inside* the platform rested on one hand-written assertion — `POST /api/platform/users` and
    `POST /api/platform/users/{id}/status`, which create and disable platform accounts, had none. The new sweep
    derives its expectation from `RolePermissions`, so moving a permission between the two roles updates the
    expectation and a new owner-only endpoint is covered the moment it is routed. The reverse direction (store
    endpoint, platform token) was seven hand-picked GETs; it is now counted. Both sweeps carry floors so
    neither can pass vacuously. **This is the boundary D-22 would extend, and the phase asked for it to be
    airtight first.**
  - **Browser QA, and its honest limit.** The provisioning journey runs **7 of 8** in the environment it was
    designed for. It could not run against the container stack at all until this phase — it hardcoded the
    platform host, the owner account and the port in four places (now all environment-driven, completing the
    sweep begun in M9 and continued in M10: every e2e spec now takes its stack from the environment). Even so
    it *requires* the development server, and not by preference: it asks a new store's `{slug}.localhost` for
    its storefront config **before** registering that domain, and subdomain-to-slug resolution is gated on
    `AllowDevelopmentResolution` because Production deliberately requires registered domains. The 8th test
    needs a working local email path, which on this machine routes to the owner's real provider from
    user-secrets — an environment condition, not a product defect, and not one to work around.
  - **Docker/runtime verification:** the container stack served the platform host (`admin.localhost` → 200),
    and the platform area was exercised live in M9's phone sweep (stores list, a store page, the new-store
    wizard, accounts, audit). Provisioning end to end is covered by
    `PlatformAdministrationTests.تجهيز_متجر_كامل_من_المنصّة_حتى_دخول_مديره_على_نطاقه_ثم_إيقافه`.
  - **Also clarified while debugging:** `Email:Provider=Log` is an escape hatch for the *absence* of a provider,
    not an override — a configured key wins over it. That is what the code has always done and what the startup
    exception implies, but the setting's name invites the opposite reading, and it cost a detour here.
  - **Not claimed:** D-22 and P-07 are unanswered and remain the owner's; no preview and no platform settings
    screen exist. The 8th provisioning test is unrun here. `back-office.spec.js` still needs TD-43's helper.
  - Tests: Domain 524, Application 408, Architecture 90, Integration **379** (+2), frontend Vitest 649.
    `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped** (no deployment target). Working
    tree clean and pushed at close (82efa7a).
- **Next-phase trigger.** M12 may start independently of M11.

### M12 — Reporting and business intelligence

- **Objective.** Confirm the three existing dashboards (store, business overview, platform) are complete and
  correct against `Dashboards.md`'s own definition of every metric, and scope — without building — the V4
  per-variant reporting `ProductVariants.md` explicitly defers.
- **Scope.** A metric-by-metric audit of `Dashboards.md` against the live queries computing each one (do the
  numbers actually mean what the page says they mean, today, after V3's purchasable-pricing changes to the read
  model?); write, but do not build, the V4 scope (per-variant sales, the stock KPI's relabelling from
  product-counted to variant-counted) as a clearly PLANNED section of `ProductVariants.md` — this phase's brief
  explicitly excludes building V4 (it is real product-roadmap scope, not a gap to close opportunistically).
- **Dependencies.** M2, M3 (search-adjacent metrics if M13 wants a shared reporting substrate — check before
  duplicating one).
- **Backend/API.** Fix only what the metric audit finds genuinely wrong (a KPI that quietly drifted after V3,
  for instance) — this phase does not add new reporting endpoints for V4.
- **Database.** None expected.
- **Frontend/UX.** None expected beyond a found-and-fixed metric display.
- **Security.** Re-confirm no store's commercial figures reach the platform list (Phase 18's own stated
  boundary) still holds.
- **Testing.** A regression test per metric fixed, tied to the exact V3-era read-model shape.
- **Browser QA.** The three dashboards, re-verified in both languages/themes at least at the level Phase 17/18
  already established.
- **Docker/runtime verification.** Live stack, real orders/stock feeding real dashboard numbers.
- **Documentation/ADR.** Update `Dashboards.md`; update `ProductVariants.md`'s V4 section with the written
  (not built) scope.
- **Technical debt touched.** None opened — everything found was fixed or documented here.
- **Acceptance criteria.** Every metric `Dashboards.md` documents is verified correct against the current code;
  V4's scope is written clearly enough that a future phase can build it without re-deriving requirements.
- **Completion evidence.** **Done.** Both acceptance criteria met. Of the ten metrics `Dashboards.md` documents,
  **four were verified as written, two were incomplete and four had drifted** — and the audit found four places
  where the *product itself*, not just the documentation, told a merchant something untrue.
  - **The stock KPI counts variants while both dashboards said "products".** The query counts `InventoryItem`
    rows, and since V3 a row is a variant — so a product with three sold-out variants contributes three. The
    Inventory screen was corrected for exactly this in V2 ("stock items (products or their variants)") and the
    two dashboards were left behind, in both languages. Fixed.
  - **The concentration risk divided by the top eight, not by period revenue.** `topProductShare` summed the
    eight returned products as its denominator while both the documentation and the on-screen sentence said
    "period revenue" and "sales" — so a store selling twenty products had its leader measured against eight,
    the share was systematically overstated, and the risk fired early. Now divided by `current.revenue`, with
    the one asymmetry recorded rather than hidden: line revenue excludes shipping and the coupon discount, so
    the share is now slightly *understated* — the safe direction for a risk signal, late rather than false. It
    also takes the largest product instead of the first, which had agreed only because the server sorts.
  - **Two test fixtures were masking that**, which is why nothing caught it: one summed the top products to
    exactly period revenue so the two denominators were indistinguishable, and the share test asserted the
    *first* element while its own name said "the highest". The default fixture also described a 70%-concentrated
    store that other tests called "healthy". All corrected to mean what they say.
  - **The trend chart said "by day" on ranges bucketed by month.** `Last90Days` and `ThisYear` group monthly on
    the server; the hint was a constant — and it is also the chart's **screen-reader summary**, so a person who
    cannot see the chart was told only the wrong thing. The bucket is now derived from the spacing of the data,
    so a new monthly range describes itself without anyone remembering to update a list.
  - **Two raw translation keys were rendering to users, and for the same structural reason.** The platform
    owner's landing page showed the literal `platform.stores` as its first tile's label (that key is an
    *object* — the stores page's namespace — and i18next returns the key when asked for an object as a string).
    The admin sidebar showed `admin.nav.searchSynonyms` on **every admin page since M3**, which added the nav
    entry and its key but never its translation. The key-parity test scans literal `t('…')` calls; one of these
    keys is built with a template literal and the other lives in a data table, so neither was visible to it.
    **Two guards now cover both shapes, and each was verified to fail against its own bug.**
  - **AOV and new customers now say what they compute.** AOV divides *gross* revenue, so `AOV × orders` will
    not equal the net revenue shown beside it once a refund exists; new customers excludes since-erased
    accounts, so a closed past period can show fewer than it once did. Both were found by reading the KPI
    tooltips — which state each formula to the merchant — against the query, and both are now pinned by tests.
  - **The stock snapshot had no test at all**, the audit's highest-value gap: one line, `Inventory(0,0,0)` on an
    empty store. So nothing distinguished `low` from `outOfStock`, nothing pinned the threshold boundary, and
    nothing recorded the counting unit — which is precisely why V3's change went unnoticed. Three tests now
    cover the buckets, the boundary (available *equal* to the threshold is low, not out), and the unit (one
    product with three variants adds three rows, not one).
  - **Also corrected in `Dashboards.md`:** revenue's composition (the `PlacedTotal` snapshot, including shipping,
    already net of discount); that best-seller revenue is a **different basis** from the headline figure and
    therefore does not reconcile, where the page had used one word for both; that `low` and archived-product
    handling diverge from the Inventory module the page claimed to match; that the window is **UTC and ignores
    `Tenant.TimeZone`**, which exists — a merchant in UTC+3 sees "Today" begin at 03:00 local; that the range is
    half-open so the current day is partial; the four shipped figures the page never defined and that three of
    them are **all-time**, not period-scoped, while sitting beside period KPIs; and one gap worth knowing rather
    than discovering — **a payment captured on an already-cancelled order appears in no dashboard figure** until
    someone requests a refund.
  - **And one rationale that was backwards.** The refund-attribution entry claimed the choice "keeps a closed
    period closed"; it does the opposite — re-reading January after a March refund returns a lower January. The
    choice stands, on the honest ground that a refund belongs to the sale it reverses, but it may not be sold as
    stability it does not provide.
  - **The platform boundary holds, verified live as well as in code.** `GET /api/platform/stats` returns seven
    cross-store scalars and **no money field at all**, takes no tenant parameter, and the stores list carries no
    commercial column — checked against the running stack, not only read. The one per-store commercial fact
    that exists (`HasCommercialActivityAsync`) is a boolean consumed inside a domain guard and never projected.
  - **V4 written out, and narrowed by what M12 fixed.** `ProductVariants.md` §11.1 records the scope so it can
    be built without re-deriving it. Two of its four items turned out to be wording and documentation problems,
    fixed here; what remains is the per-variant best-seller breakdown (with the grouping decision and the
    renamed-product trap named), a product-counted stock figure beside the row-counted one, load tests at the
    100-variant limit, and the migration rehearsal — **and none of it needs a schema change**, because
    `OrderItem.VariantId`/`VariantLabel` and the per-variant inventory rows already exist.
  - **Browser and runtime verification:** both dashboards read out of a live browser in **en/light and ar/dark** —
    the corrected stock wording rendered, the trend hint rendered, axe clean in both, and the raw-key sweep is
    what found `admin.nav.searchSynonyms`. Real orders feed real numbers on the container stack (revenue 60,
    orders 2, AOV 30 — internally consistent).
  - **Not claimed:** the UTC-versus-store-time-zone question is documented, not solved — changing it moves every
    merchant's "Today" and is a product decision, not a defect to patch inside an audit phase. Platform stats
    still have no server test pinning their figures. Several all-time figures still sit beside period KPIs
    without saying so on screen (documented now, not relabelled).
  - Tests: Domain 524, Application 408, Architecture 90, Integration **382** (+3), frontend Vitest **656** (+7).
    `./scripts/release-gate.sh --suites`: 5 passed, 0 failed, **3 skipped** (no deployment target). Working tree
    clean and pushed at close (8e230da).
- **Next-phase trigger.** M13 may start once M3's search engine exists to analyze.

### M13 — Search analytics and discovery intelligence

- **Objective.** Give a merchant visibility into what shoppers actually search for and what search produced
  nothing, so the vocabulary M3 built (`SearchSynonyms`, `SearchCorrections`) can be improved from real evidence
  rather than guesswork — still entirely local, no external analytics service.
- **Scope.** A `SearchQueryLog` (or similarly named), owned by Catalog or Reporting per `ModuleBoundaries.md`'s
  existing convention for where analytics-shaped data lives (check before assuming), recording the normalized
  query, result count, store and timestamp — **no personal data**: no customer id, no IP, nothing TD-16's
  retention gap would make worse; a merchant-facing "top searches" and "searches with no results" view, feeding
  directly into M3's synonym/correction editor (a merchant sees `مكلسة` produced zero results, adds it as a
  correction for `مكنسة`, and the next shopper's search just works — a closed, inspectable loop, not a model
  retraining itself invisibly).
- **Dependencies.** M3 (the vocabulary it improves), M12 (the reporting UI pattern it likely reuses).
- **Backend/API.** A write path from the search endpoint (fire-and-forget or the existing outbox-adjacent
  pattern — do not hold a request open on a logging write); a read endpoint for the merchant view, paged like
  every other admin list.
- **Database.** New table, tenant-owned, standard isolation; a retention/purge policy from day one given TD-16's
  existing warning about unbounded growth — this phase should not repeat TD-16's mistake on a brand-new table.
- **Frontend/UX.** A simple admin screen — top queries, zero-result queries, a direct "add as correction/synonym"
  action linking into M3's editor.
- **Security.** Confirm the log genuinely carries no personal data before it ships, not as an afterthought;
  tenant isolation test as standard.
- **Testing.** Integration test for the write path not blocking the search response; Application test for the
  zero-result aggregation; a retention/purge test from the start (learning TD-16's lesson rather than repeating
  it).
- **Browser QA.** A search with no results, then the merchant admin showing it, then adding a correction, then
  the same search recovering — the whole loop, once, end to end.
- **Docker/runtime verification.** Live stack, the full loop above.
- **Documentation/ADR.** Update `Catalog/README.md` (or `Reporting/README.md`, wherever it's filed); no ADR
  expected (this is additive, low-risk data, not an architectural decision) unless the merchant-editing loop
  changes how `SearchSynonyms` itself works.
- **Technical debt touched.** None expected to open (the retention policy built in from the start is this
  phase's whole point).
- **Acceptance criteria.** A merchant can see what shoppers search for and what fails, and can close that gap
  from the same screen, without ever leaving the admin area or involving engineering.
- **Completion evidence.** **Done.** Both acceptance criteria met, verified as a closed loop in a browser on the
  container stack: a shopper searches a word that finds nothing, the merchant sees it on the next screen refresh,
  clicks "add as synonym" once, and the same search then returns the product — after which **the word leaves the
  work list on its own**, because it no longer fails. Nothing left the admin area.
  - **Owned by Catalog**, checked rather than assumed: `ModuleMap.cs:56` already maps `SearchSynonym` to Catalog,
    and the trace exists to improve that vocabulary, so it is the same module and the same `catalog.manage`
    permission. It is also the same *screen*: the read is a second tab on `/admin/search-synonyms`, not a
    fourteenth sidebar entry, because a question and its answer should not be two navigations apart.
  - **Logging never slows search, and that is enforced not assumed.** `ISearchLog.Record` returns `void`, never
    throws, and only deposits into a bounded channel (`DropWrite`, 1000); `SearchLogWriterService` batches and
    saves each store's rows **inside that store's tenant scope**, which is what makes the write guard stamp the
    right `TenantId`. `GetProductsHandler` wraps the call in try/catch anyway — a test with a throwing log proved
    the handler was relying on the contract instead of holding the guarantee, and that is a product property, not
    an implementation detail.
  - **Retention from the first commit, with the reasoning made checkable.** TD-16 says no table here has a
    retention policy and that personal data needs a legal answer first. This table has no personal data *by
    construction*, so 90 days was an engineering decision this phase could just make — and
    `جدول_السجلّ_لا_يحمل_عموداً_شخصياً` asserts the table's exact column set so a later column cannot quietly
    invalidate that reasoning. The purge is a bulk `ExecuteDelete`, so it was added to `ReviewedBulkWrites`
    deliberately and a test proves one store's purge leaves its neighbour's rows alone.
  - **Three defects found in this phase's own work before it shipped**, each by a test written to prove the
    behaviour rather than to pass: `ITenantDirectory` resolved from the root provider in the background writer
    (the identical M11 defect, which would have broken `dotnet run` in Development again — caught pre-emptively);
    a word that *always* finds results rendering as "failed 0 times" in a warning tone, in an Arabic plural form
    for zero; and a zero-result share of "0%" on a store with no searches at all, which is a reassuring claim
    about a measurement that never happened. The latter two now live in a tested pure module, not in JSX.
  - **`COUNT(DISTINCT col1, col2)` is not SQL** — the first read implementation returned 500 on SQL Server and
    the integration tests caught it. The summary now comes from one aggregate *over the grouped set*, which also
    removed a round trip and makes the totals describe the window rather than the page (the M12 mistake).
  - **`Tabs` is a real component** with the full ARIA contract, because half a tab implementation does not work:
    roving tabindex, `aria-controls`/`aria-labelledby`, arrow keys that follow reading direction (left goes
    forward in RTL — an Arabic-layout defect no screenshot shows), the keydown handler on the tab rather than the
    tablist (`jsx-a11y` was right to reject the container), and a panel that is deliberately not focusable
    because it contains buttons.
  - **Evidence.** Commits `3b25efa` (backend) and `7483d23` (merchant screen) on
    `phase/17-production-hardening`, plus this closure. Tests: 395 integration (12 new in
    `SearchAnalyticsTests`), 412 Application (4 new), 524 Domain, 90 architecture, 670 frontend unit (14 new);
    `npm run lint` back to its 8-warning baseline with 0 errors; `tsc --noEmit` clean. Browser: 6/6 in
    `e2e/admin-search-insights.spec.js` on the container stack (loop, keyboard tabs, dark mode, English/LTR,
    axe clean at wcag2a/wcag2aa), and 10/10 phone in `responsive.spec.js` including new tab touch-target and
    no-horizontal-scroll checks. Runtime: stack rebuilt, migration `20260918072543_SearchQueryLog` applied,
    all three containers healthy, no errors in the API log; the full loop also exercised directly over HTTP.
  - **One environmental finding, not a product defect.** The QA SQL container died with SQL error 596
    ("insufficient system memory in resource pool 'internal'") while sharing a 2.8 GiB Docker host with the
    owner's unrelated `searchsys` stack. Diagnosed and fixed by capping **this** stack's SQL memory
    (`MSSQL_MEMORY_LIMIT_MB=900`) so the two coexist — the owner's workload was not stopped. Whether the repo's
    own `docker-compose.yml` should carry a default cap is left as a question for M16 (performance), not decided
    here.
- **Technical debt.** None opened. TD-16's retention gap is *narrowed*, not closed: this is the first table in
  the system with a retention policy, and it got one only because it has no personal data. The tables TD-16
  actually names still wait on the owner's legal answer.
- **Next-phase trigger.** M14 may start independently of M13.

### M14 — Notifications and communications

- **Objective.** Confirm the outbox-backed notification system (ADR-0034) has no untested recovery path (TD-34)
  and that every notification a merchant or customer should receive per the current feature set actually fires,
  correctly localized, with the correct store identity.
- **Scope.** TD-34's three untested recovery paths (purge, lease expiry, two dispatchers racing); a fresh
  cross-check of `Notifications/README.md`'s notification list against what the code actually sends (the ADR
  index already flags ADR-0033's promised review-pending/review-decision notifications as never built — confirm
  that is still accurate and either build them if now in scope or re-confirm the deferral with today's
  evidence).
- **Dependencies.** None specific.
- **Backend/API.** No new notification types invented without checking `BusinessRules.md`/the module document
  first — if review-pending/decision notifications are genuinely wanted, that's a product-scope question near
  M11/owner territory, not an assumption this phase makes alone; if it's clearly just "finish what Phase 14
  already decided to build," build it.
- **Database.** None expected.
- **Frontend/UX.** None expected unless a new in-app notification type needs a UI treatment.
- **Security.** Confirm no personal data or secret appears in a logged notification body (ADR-0020's rule,
  `StartupAndSecurityTests`).
- **Testing.** The three TD-34 recovery-path integration tests (purge, lease expiry, dispatcher race); any new
  notification's handler test.
- **Browser QA.** A notification-triggering flow (order status change, low stock) observed end to end against
  the live stack's actual configured provider chain (Resend → Brevo → Gmail SMTP → log fallback).
- **Docker/runtime verification.** Live stack, the outbox dispatcher actually running and delivering.
- **Documentation/ADR.** Update `Notifications/README.md`; update the ADR-0034 drift note in
  `docs/11-ADR/README.md` §4 if this phase resolves it either way.
- **Technical debt touched.** TD-34 (close).
- **Acceptance criteria.** TD-34's three paths are proven by tests that actually exercise the failure, not
  inferred from the happy path; the notification list in the module document matches the code exactly.
- **Completion evidence.** **Done.** Both acceptance criteria met.
  - **TD-34 closed, and each test was proven to fail before being kept.** A recovery-path test that cannot fail
    is decoration, so each was run against a deliberately broken processor first: the purge against a `DELETE`
    that forgot `ProcessedAt` (it took unsent and dead rows with it), the lease against a due query ignoring
    `LockedUntil`, and the race against a `ClaimAsync` that sets the lease but never checks who won. All three
    failed as intended, then passed against the real code.
  - **The race is real, not simulated.** The first dispatcher is held inside the email provider
    (`Emails.Block()`) *while holding the lease*, so the second runs against a genuinely claimed row rather than
    hoping to land in a microsecond window. Its assertion is the product claim rather than the mechanism: the
    customer receives **one** email — which needed a new `CountTo` helper, since two identical emails read as
    one through `LastTo`.
  - **The lease test covers both halves, and the second is the one that matters.** A lease that protects is the
    easy half; a lease that *releases* on expiry is what makes at-least-once delivery true, and its failure mode
    is a message that simply never arrives — invisible until a customer complains.
  - **The purge test asserts what survives, not what is deleted.** Its three "kept" cases are the real risk: an
    unsent row deleted is an email that never arrives, and a dead row deleted is the loss of the only record of
    why it failed.
  - **The notification list matches the code — and now stays matched.** All six outbox message types and all
    three in-app kinds were checked against `Notifications/README.md` and matched exactly. That is a fact about
    today, so it was turned into a test: the existing documentation tests verify that every name written down
    *exists*, and nothing verified that everything that exists is *written down* — omission being the direction
    that misleads a reader. `NotificationDocumentationTests` now asserts both lists, and was proven to fail
    against an undocumented seventh type.
  - **ADR-0033's promised review notifications are still not built**, re-confirmed rather than assumed — and
    that §4 note is now test-backed, so it cannot go stale unnoticed. Building them was **not** taken on: the
    phase text itself says that is a product-scope question, and nothing in `BusinessRules.md` or the module
    document asks for them. Left as the owner's.
  - **Security criterion verified on a running process, not inferred.** With no provider key configured the
    chain ends at `ConsoleEmailService`, which logged `PasswordReset to m***@example.test was not sent` — the
    recipient redacted, no token, no body. The failure logs carry message id, type, attempt count and exception
    *type* only.
  - **Evidence.** Commit `c08e410` plus this closure, on `phase/17-production-hardening`. Tests: 398 integration
    (3 new), 412 Application, 524 Domain, 92 architecture (2 new). Runtime: stack rebuilt and healthy; the
    background dispatcher observed handling messages on its real 5-second cadence; the **two-hop cascade**
    verified live (`OrderStatusChanged` → in-app row + `OrderEmailRequested` → `OrderShipped` email); all three
    in-app kinds observed in the database with correct payload shapes (`order.status`, `order.new`,
    `stock.low`); outbox steady state `pending=0 processed=42 dead=0 locked=0` — no stuck leases.
- **Technical debt.** TD-34 closed. None opened.
- **Next-phase trigger.** M15 may start independently of M14.

### M15 — Dedicated security review

- **Objective.** The formal review [ProductRoadmap.md](ProductRoadmap.md) Phase 20 names, applied to the system
  as it exists **after** M1–M14, not as a repeat of the Phase 17 hardening audit (which already closed R-02,
  R-06, R-07, R-08, R-10, R-17, R-22 — this phase does not re-litigate those, it verifies they still hold and
  covers what they did not).
- **Scope.** A STRIDE threat model per module (13 modules, `docs/04-MODULES/`); the OWASP ASVS Level 2
  checklist, item by item, against the current code; a full authorization-matrix review
  (`AuthorizationMatrixTests` as the executable half, a manual review of `Permissions.cs` against
  `AuthenticationAndAuthorization.md` as the other half); cross-tenant attack tests extended for every endpoint
  M1–M14 added (the existing `TenantIsolationTests` pattern — every endpoint with a resource id, tried from
  another store); security headers, CSP and HSTS re-verified against the current middleware (R-16 already
  fixed the mechanism; this phase confirms it still holds and that the SPA CSP's still-open report-only status
  gets its promised one browser pass); rate limits re-verified including R-11's spoofing risk under the
  deployment topology M17 will define; secret and key rotation procedure written (not necessarily executed —
  that needs the real `SECRETS_KEY`, an owner-adjacent action) if it does not already exist; a dependency audit
  (already in CI — confirm it is still clean, not a new build); SQL Server Row-Level Security evaluated as
  defense-in-depth and explicitly accepted or rejected with evidence, not left implicit.
- **Dependencies.** M1–M14 (this phase reviews their cumulative surface).
- **Backend/API.** Fix any finding directly if it is a clear defect (the Phase 17 pattern: verify in code,
  reproduce, fix, test); a finding that is a design trade-off rather than a defect is recorded, not silently
  fixed one way.
- **Database.** RLS only if adopted — additive, carefully reviewed, and only after the evidence for it is
  written down (per this phase's own scope item above).
- **Frontend/UX.** Any XSS/CSRF-adjacent finding fixed directly (the existing CSP/security-header work is the
  precedent for how carefully this is verified end-to-end, not just unit-tested).
- **Security.** This entire phase is the security requirement.
- **Testing.** Every STRIDE finding gets a regression test where one is possible; `AuthorizationMatrixTests` and
  `TenantIsolationTests` extended for anything new since Phase 17.
- **Browser QA.** A manual-style adversarial pass (attempt cross-tenant access from the browser with a real
  session, not just an API test) on at least the highest-value flows (checkout, payments, admin).
- **Docker/runtime verification.** Security headers and CSP verified against the actual running container, not
  a unit test's simulation of it (R-16's own note that the SPA CSP still needs "one browser pass").
- **Documentation/ADR.** Update `SecurityControls.md`'s control → implementation → test → gap table exhaustively;
  update `ReleaseReadiness.md` for every P0/P1/P2 item this phase closes or newly discovers; an ADR for any
  structural security decision (RLS adoption or rejection, for instance).
- **Technical debt touched.** Whatever the audit surfaces; R-11 (rate-limit spoofing, deployment-dependent —
  likely re-filed pending M17), R-20 (branch protection remains a GitHub setting outside this repository's
  reach — re-confirm it's still off and still recorded as the owner's to flip).
- **Acceptance criteria.** Every finding is either fixed (with a test) or explicitly accepted with a written
  rationale in `ReleaseReadiness.md` — the roadmap's own Phase 20 exit criterion, verbatim.
- **Completion evidence.** **Done.** The exit criterion is met: every finding is either fixed with a test that
  fails against the old behaviour, or accepted with a written rationale in `ReleaseReadiness.md` — which now
  carries the full ledger, fourteen fixed and eight accepted.
  - **The review actually found things, and the sharpest was proven exploitable before it was fixed.** The rate
    limit keyed on `Request.Host.Host` as it arrived while tenant resolution lower-cased it, so one store had a
    fresh bucket per capitalisation. Demonstrated on the running stack: ten logins then 429, then fourteen more
    varying only the case of the host, **all accepted**. No proxy to get behind, no header to forge — and it
    worked straight through nginx, which forwards `$http_host` verbatim. That is unlimited credential stuffing,
    reset-mail flooding and coupon guessing: everything `SEC-AUTHN-14` exists to stop.
  - **A sibling host under a merchant's own domain could plant a session.** `HttpOnly`, `Secure` and
    `SameSite=Strict` each stop something real and none of them stops this, because `SameSite` governs *sending*
    and says nothing about a same-site host *setting* a cookie. On a white-label platform where merchants bring
    their domains, that is an account takeover. Closed with `__Host-`, which is the prefix that actually applies
    — `__Secure-` would not have — at the knowingly-paid price of `Path=/`.
  - **Two findings were in this programme's own recent work.** M13's search-log retention deleted one batch per
    six hours against an anonymous, unrate-limited writer, so the ninety days it documented as its achievement
    were not enforced at all; and M13's review of its own module document had left `catalog.manage`'s new reach
    over search unmentioned in the role table. Reviewing recent work as adversarially as old work is the point.
  - **Three findings were things the documentation asserted and the code did not do**, which is the failure mode
    a control catalogue exists to prevent: SEC-LOG-09 claimed audit rows commit with their change (true except
    for the three platform→tenant commands that cross a scope — now PARTIAL, TD-55); SEC-LOG-05 read as coverage
    of security events while authentication failures produced no log line at all; SEC-AUTHZ-05 named one helper
    as the mechanism for six use cases when it has two call sites.
  - **The detection half was nearly empty and is no longer.** Failed sign-ins are logged with an outcome that
    separates stuffing from forgetfulness, and **every** log line now carries the client address — verified on
    the running stack, which is the only place it could be verified, since the in-process test server has no
    connection to have an address.
  - **RLS was decided, not deferred** ([ADR-0043](../11-ADR/0043-row-level-security-evaluation.md)). Declined,
    because every path around the application filter is already a failing build, because its per-connection
    session value is a silent footgun under EF pooling, because the platform area would need a mode switch and
    four exemptions (a second copy of the same rules in another language), and because the application connects
    as `sa` and could disable the policy anyway. Three conditions that void the decision are listed.
  - **The validation pipeline's silence is now a build failure.** 59 of 141 requests had no validator and nothing
    required one — in a repository that build-enforces everything else. The rule is about shape, not presence:
    45 of those 59 carry only ids and enums, where a validator adds a file and no safety. Writing it found a bug
    in itself first (checking only the immediate base type treats every paged validator as absent), which
    surfaced as a single false offender.
  - **Evidence.** Nine commits, `13dd1e0`…`bc0ac6f`, plus this closure. Tests: 418 integration, 425 Application,
    524 Domain, 94 architecture — all green; two new architecture rules (`ValidationRuleTests`) and one new
    integration guard (every outbox type has a registered handler, written because that gap was real and silent).
    Runtime, on the rebuilt container stack: the host-case bypass now returns 429 as it should; `Cache-Control:
    no-store` present and the storefront config keeping its deliberate `no-cache` + ETag; the `__Host-` cookies
    issued and round-tripping; failed logins logged at Warning with outcome, account and **`ClientIp`**.
    Browser: `csp.spec.js` **zero violations across sixteen routes**, `cross-tenant-adversarial.spec.js` 4/4.
    Dependency audit re-confirmed: .NET reports no vulnerable packages; npm's shipped set has two *moderate*
    react-router advisories, below the CI gate and already analysed as F-23.
  - **Browser suite state, stated plainly.** 86 of the desktop journeys pass, up from 77, through three fixes
    that were faults in the tests and never in the product: `networkidle` (which the storefront's own
    boot-time refresh call holds open), a spec that created sold-out products and never cleaned up — breaking a
    different spec — and a hardcoded dev-server URL. Of the 11 that still fail, **six are
    `second-tenant.spec.js`, which is hardcoded to the Vite dev server and its seed data and cannot pass against
    containers by construction** (TD-56); the other five pass when their file runs alone and fail only inside a
    27-minute serial run (TD-57). The one of those five that touches changed code was checked specifically: it
    passes in isolation twice, and the cookie's round-trip is proven directly against the running stack.
- **Technical debt.** Opened TD-55 (audit row across a tenant scope), TD-56 and TD-57 (the two browser-suite
  classes above). None closed — R-11 and R-20 remain deployment- and GitHub-shaped, as this phase predicted.
- **Next-phase trigger.** M16 may start independently of M15, but should follow it in execution order since a
  performance change should not undo a just-closed security finding without re-review.

### M16 — Performance, scale and resilience

- **Objective.** The formal review [ProductRoadmap.md](ProductRoadmap.md) Phase 21 names: measured evidence for
  where the system's real bottlenecks are, before any complexity is added to address one that does not actually
  exist yet.
- **Scope.** Query plans and indexes leading with `TenantId` (the R8 risk-register mitigation) verified against
  real `EXPLAIN`/execution-plan output, not assumed from the schema; N+1 queries found by a real profiling pass
  over the storefront and admin read paths (M3's search and M12's dashboards are the most likely new sources
  given how recently they were built); caching evaluated with evidence (tenant config and catalog reads are the
  two `ProductRoadmap.md` already names as candidates) — **built only if a measurement justifies it**, per this
  plan's §2 non-negotiable against premature complexity; response compression; image optimization and a CDN
  decision (a genuine infrastructure decision, likely feeding into M17); frontend bundle-size budgets (already
  tracked informally in `DesignSystem.md` — make the budget explicit and enforced in CI if not already); load
  tests against targets **agreed before this phase starts producing numbers to justify itself against** — if no
  target exists yet, the first step of this phase is proposing one from the current traffic/scale assumptions
  and getting it confirmed (an owner-adjacent step only if it touches a commercial commitment; otherwise
  engineering can set an internal target from evidence, e.g. "p95 catalog latency under 300 ms," matching the
  example already in the roadmap).
- **Dependencies.** M1–M15 (measuring the system as it will actually ship, not an earlier shape of it).
- **Backend/API.** Fix only what measurement justifies (an index, a batched query, a cache with a measured hit
  ratio) — no speculative optimization, and no new caching layer/service beyond what already exists in-process
  without evidence it is needed (`AGENTS.md` §5's non-goals apply here as much as to the search phase).
- **Database.** Index additions only, backed by a measured query plan; additive, reviewed.
- **Frontend/UX.** Bundle-size budget enforcement; any measured slow render path fixed.
- **Security.** None specific, beyond confirming a caching layer (if added) does not leak one tenant's data into
  another's cache key space — a tenancy risk any cache introduces and must be tested against explicitly.
- **Testing.** A load-test harness (tool decision — document the choice and why) with results attached to this
  phase's evidence; an N+1 regression test for whatever path was fixed (a query-count assertion, not a timing
  assertion, is the stable way to pin this).
- **Browser QA.** Real-user-perceived performance on the slowest measured flow, verified in a browser (not just
  server-side timing) at least once.
- **Docker/runtime verification.** Load tests run against the actual compose stack's resource limits, so the
  numbers mean something close to production, not a developer machine with no constraints.
- **Documentation/ADR.** `ScalingStrategy.md` updated with real measurements; an ADR for any caching or indexing
  strategy adopted, with the evidence attached, per §2's rule against "it would scale better" as a reason.
- **Technical debt touched.** None expected to open unless a fix is deliberately partial and re-filed.
- **Acceptance criteria.** The load-test targets agreed at the start of this phase are met, with the evidence
  — not the claim — recorded (`ProductRoadmap.md` Phase 21's own exit criterion).
- **Completion evidence.** **Done.** The acceptance criterion is met: targets were set at the start of the
  phase, and the evidence — not the claim — is recorded in `ScalingStrategy.md`, `DesignSystem.md` and
  [ADR-0044](../11-ADR/0044-caching-evaluated-not-adopted.md).
  - **The load test the strategy named as missing now exists.** `scripts/load-test.py`, Python standard library
    only, so anyone who clones the repository can run it — a tool that must be installed first is a tool that is
    not run, and a performance number nobody can reproduce is not evidence. Run against the compose stack with
    its own resource limits, not a machine with none.
  - **No N+1 anywhere**, and it is now guarded. `ReadPathQueryBudgetTests` counts SQL commands per request at
    two data sizes; every storefront and admin route holds constant between one product and ten, and basket
    pricing costs the same for eight lines as for one. Counting commands rather than milliseconds is the point:
    a timing assertion goes red on a loaded runner and gets deleted as flaky.
  - **Steady-state latency is inside every target** (catalogue p50 56 ms against a 300 ms p95 target; the
    sixteen-aggregate dashboard 43 ms). In a browser, the slowest measured flow shows its first real figure in
    850 ms cold and 61 ms warm.
  - **The measurement changed the conclusion twice, which is the whole reason for measuring.**
    - The plan cache named the most expensive statement in the system, and it was **M13's own**: 25,375 logical
      reads a call, because the index served the grouping and gave nothing to the "most recently typed form"
      question the same screen asks. Extending the key to `SearchedAt DESC, Id DESC` took it to **107** on the
      running stack — and it *replaced* the old index rather than joining it, so the hottest table's write cost
      is essentially unchanged. `Term` was deliberately left out of the included columns: it buys 148 → 110
      reads and costs storing the term twice on the most-written table.
    - The icon barrel ships 24 kB gzip to every visitor — 15% of first load — which looks like an obvious win
      until it is counted: only 8 of 45 icons are admin-only, so splitting it recovers about 5 kB of 157.
      **Not done**, and the measurement is what says so rather than intuition.
  - **Caching was decided on evidence and not built** (ADR-0044). Under load the API sits at **0.54% CPU while
    SQL Server runs at 47%** — the constraint is database capacity, which is Stage 1's last lever and Stage 3,
    not read latency. And the candidates cache badly: the most-written table is the search log, the dashboard's
    aggregates change with every order, and every cache key here is a tenancy question before it is a
    performance one.
  - **The frontend budget the design system promised is enforced.** That page said "no budget is enforced in CI;
    bundle-size budgets are PLANNED for Phase 21" — this is it, measured the way its own figures were measured
    (a real browser, gzipped transfer, both languages) rather than by summing `dist`. First load is **156.8 kB**
    against 136.5 when last taken: about 20 kB across the phases since, no single culprit, which is exactly the
    growth a budget exists to catch.
  - **Two vacuous measurements were caught before they became findings**, and both are recorded because they are
    how a performance test lies: a 404 route costs zero queries and reads as the most efficient endpoint in the
    system, and a query-string parameter spelled wrong takes a short-circuit that returns an empty list. Every
    measured call must now succeed first.
  - **Evidence.** Commits `4b0d913`, `c52ba59`, `4a62d49` plus this closure. Tests: 421 integration (3 new
    budget tests), 94 architecture, all green. Runtime: all measurements on the rebuilt container stack with its
    memory limits; migration `SearchLogInsightIndex` applied and verified in place with the index count
    unchanged.
- **Technical debt.** None opened. The uncovered `ORDER BY CreatedAt DESC` on `Products` is recorded in
  `ScalingStrategy.md` as the next index to reach for, deliberately not taken now: a real shape, a small
  absolute cost at present volume, on a table that does not grow without bound.
- **Next-phase trigger.** M17 may start independently, though a scaling decision made here likely shapes M17's
  infrastructure choices, so running them in this order is deliberate.

### M17 — Production infrastructure and deployment

- **Objective.** Everything [ProductRoadmap.md](ProductRoadmap.md) Phase 23 names that is genuinely engineering
  work (not the owner-only pieces already carved out in `OwnerDecisions.md`) is built, documented and rehearsed.
- **Scope.** R-12 (least-privilege database logins — the recipe and the measurement already exist per
  `DatabasePrivileges.md`; this phase **applies it to a real target deployment**, which may itself be an owner-
  adjacent action if no deployment target exists yet — build everything up to the point that applying it is a
  runbook step, and stop there if no real target is available); R-16 (TLS termination — the headers and HSTS
  mechanism are done; the actual certificate and terminator are a deployment's, built as infrastructure-as-code
  or a documented runbook this phase can produce without needing the owner's real domain yet); R-19 (backup
  schedule, off-site copy, retention, alerting — the mechanism is rehearsed per `BackupAndRestore.md` §8; this
  phase wires the schedule and the alert); migrations as a deliberate deployment step instead of running at
  startup (R-18 — currently accepted for a single instance; build the deliberate-migration-step alternative so
  scaling to more than one instance does not silently reintroduce the startup-race risk); observability
  (structured logs, metrics, traces, alerts — logging and correlation ids already exist per ADR-0018; metrics
  and traces and an actual alerting destination are new); automated TLS for custom domains (a real, non-trivial
  piece — likely an "on-demand TLS at the edge" pattern per the roadmap's own recommendation, R7); per-tenant
  email-domain authentication (SPF/DKIM, R9's mitigation).
- **Dependencies.** M15 (security review's findings on the deployment surface), M16 (scaling decisions).
- **Backend/API.** A deliberate migration-bundle mechanism (a CLI step or a documented `dotnet ef database
  update` runbook entry, separated from `DbSeeder.SeedAsync`'s current automatic call) if R-18's mitigation is
  built here.
- **Database.** The least-privilege login application itself, following `least-privilege-logins.sql` exactly as
  written and measured.
- **Frontend/UX.** None expected.
- **Security.** This phase closes or substantially advances R-12, R-16 (TLS), R-19 (backup operations); each is
  currently `OPEN — deployment action` in `ReleaseReadiness.md`, and each's *mechanism* already exists — this
  phase's job is applying and rehearsing the operation, not inventing the mechanism a second time.
- **Testing.** `MigrationRehearsalTests` extended if TD-35 (splitting it per phase) is addressed here as a
  natural side effect of touching the migration step; a restore drill re-run against whatever the real backup
  schedule now produces.
- **Browser QA.** A full smoke test against the real (or realistic staging) deployment target once TLS and
  least-privilege logins are applied — the same critical-journey set Phase 16–18 already established, run once
  against the hardened target.
- **Docker/runtime verification.** This entire phase *is* the Docker/runtime verification for production — a
  real (or staging-equivalent) deployment, health checks observed externally, logs reviewed for the least-
  privilege refusals `verify-least-privilege.sh` already proves work.
- **Documentation/ADR.** Update `Deployment.md`, `BackupAndRestore.md` (schedule and alerting sections),
  `DatabasePrivileges.md` (applied, not just measured); an ADR for the TLS/CDN/observability stack choices.
- **Technical debt touched.** TD-18 (no design-time `DbContext` factory — a natural fix alongside deployment
  tooling work), TD-20 (uploads on local disk — this phase's own scope explicitly names moving to blob storage
  "before Phase 23," i.e. here, if scaling to more than one instance is in scope this phase), TD-35 (if split
  here).
- **Acceptance criteria.** The go-live checklist items engineering can close without the owner (per
  `OwnerDecisions.md`'s explicit "smaller choices" and the P0 items marked "deployment action, now with a
  measured recipe") are closed; whatever remains open is because it genuinely needs the owner's real account,
  domain or infrastructure spend, named explicitly.
- **Completion evidence.** **Done, to the boundary this phase's own text draws.** The acceptance criterion is
  that what engineering can close without the owner is closed, and whatever remains open is named explicitly
  because it needs a real account, domain or spend. Both halves are below.
  - **R-18 is no longer accepted-and-described; it is built.** `Database:MigrateOnStartup` must be chosen
    explicitly outside Development/Testing — the API refuses to start otherwise, the same rule `Email:Provider`
    already sets — and `false` gives the deliberate step with a self-contained migration bundle. Built
    deliberately **before** a second instance exists, because the moment one is added is the moment nobody is
    thinking about this. The half that matters is what `false` does when the step is forgotten: it logs an
    **error naming the pending migrations** rather than booting a version against a schema it does not match and
    failing on the first request with a missing-column error one step from the real cause.
  - **TD-18 closed, and it stopped being a convenience on the way.** `dotnet ef migrations list --project
    src/Souq.Infrastructure` now runs from a clean checkout with no API host, no appsettings and no secrets —
    verified. The deliberate migration step needs exactly that, because it runs from a deploy machine or a
    bundle, and neither has (nor should have) the runtime's secrets.
  - **R-12 re-verified against today's code, and it was overdue.** `DatabasePrivileges.md` asks for a re-run
    after any new background job; since the last measurement the codebase gained M13's table and **two**
    background services, which is precisely the shape that warning describes — a job missing a permission fails
    silently, in no response. The harness passed unchanged: ready in 11 s, six real paths at 200, **zero
    permission denials**, and the reverse checks still refusing a table create, a drop, a self-grant, a database
    create and any other login's password hash. So `db_datareader + db_datawriter` covers a bulk `ExecuteDelete`
    and a write path outside any request.
  - **Metrics instrumented with no new dependency** ([ADR-0045](../11-ADR/0045-production-edge-and-observability-stack.md)).
    ADR-0018 deferred OpenTelemetry because it needs a backend — true of the *exporter*, never true of the
    *instrumentation*. Four counters on the runtime's own `System.Diagnostics.Metrics`, chosen against one test
    (*would this wake a person?*), with high-cardinality tags deliberately excluded. Each already had a log line:
    a log says what happened in one request, a counter says how often this hour, and only the second is
    something an alert is built on.
  - **Backup retention got a mechanism and deliberately not a policy.** Retention is recorded as an owner
    decision and a legal one; `--prune-older-than <days>` deletes nothing unless asked, and only sets that
    completed — a directory without `SHA256SUMS` was interrupted or is being written now, and deleting it would
    hide a failure rather than tidy one.
  - **Named explicitly as still open, because each needs the owner's real infrastructure:** a TLS certificate
    and terminator (R-16 — the behaviour, headers, HSTS and both proxy diagnostics exist; the certificate does
    not); automated TLS for merchant domains (R7 — needs an edge that can answer an ACME challenge, though the
    `TenantDomains` data it would read exists); the backup schedule, off-site copy and alert destination (R-19 —
    script, verifier, drill and prune all exist); a metrics exporter destination; applying least-privilege
    logins to a real server (the recipe is re-verified, the server does not exist); per-tenant SPF/DKIM (R9 —
    needs real domains); and branch protection (R-20 — a GitHub setting). TD-20 (uploads on local disk) is
    deferred rather than closed: `IFileStorage` is already the seam, and local disk is correct until a second
    instance exists.
  - **Evidence.** Commits `b017f73`, `c12e227`, `5161bd3` plus this closure. Tests: 422 integration (1 new),
    427 Application (2 new), 524 Domain, 94 architecture — all green. Runtime: the least-privilege harness run
    end to end on throwaway infrastructure against today's image; `dotnet ef` verified working from the
    Infrastructure project alone.
  - **One harness fixed rather than tolerated.** M16's query-budget test measured more than it meant to: M13's
    background writer flushes every 20 ms in the test host and *this test provokes it* — a keyword search
    enqueues a row whose INSERT lands in the next window. It passed alone and failed in the suite, which is the
    worst way to fail because it reads as random.
- **Technical debt.** TD-18 closed. TD-20 and TD-35 untouched and re-confirmed as open. None opened.
- **Next-phase trigger.** M18 may start independently, though it naturally follows since CI/CD needs a real
  deployment target to deploy *to*.

### M18 — CI/CD and release engineering

- **Objective.** Close TD-31 (branch protection — a GitHub setting, escalate per §5, not build around it) and
  build the CD half `ci.yml` does not yet cover: environments, a deployment pipeline, and the disciplined use of
  the release gate this plan's §3 already treats as canonical.
- **Scope.** CI already exists and is comprehensive (`ci.yml`: build with warnings as errors, three fast suites,
  frontend lint/typecheck/test/build, integration suite over real SQL Server, NuGet/npm audits split
  shipped/tooling, secret scan, a live-payment-key scan, a clean-working-tree check for generated docs) — this
  phase does **not** rebuild it, it extends it to actually deploy: defined environments (at minimum staging and
  production, matching whatever M17 established as the real target), a deployment workflow triggered on a
  tag or a protected-branch merge (which itself needs TD-31's branch protection to mean anything — flag the
  dependency explicitly rather than build a deployment pipeline that a red run could still trigger); a
  release-tagging convention.
- **Dependencies.** M17 (a real target to deploy to).
- **Backend/API.** None expected — this is pipeline/infrastructure work, not application code.
- **Database.** None.
- **Frontend/UX.** None.
- **Security.** The deployment pipeline itself must not become a new secret-leak surface — CI secrets scoped
  per environment, no production credential available to a workflow triggered from a fork or an unprotected
  branch.
- **Testing.** A pipeline dry run against staging, at minimum once, before this phase is called done.
- **Browser QA.** The critical journeys run once against whatever the pipeline actually deployed, not only
  against a hand-run local stack — closing the loop that everything in M1–M17 was building toward.
- **Docker/runtime verification.** The pipeline's own deploy step is the runtime verification here.
- **Documentation/ADR.** Update `.github/workflows/`'s own inline documentation and `Deployment.md`; an ADR for
  the CD strategy chosen (this is exactly the kind of "different persistence or transaction strategy... anything
  future engineers would otherwise have to reverse-engineer" `AGENTS.md` §8 asks for).
- **Technical debt touched.** TD-31 (escalate — cannot be closed by engineering, only flagged as ready the
  moment the owner flips the GitHub setting).
- **Acceptance criteria.** A tagged commit can be deployed to the defined environment(s) through the pipeline,
  observed to succeed, and rolled back cleanly if it needs to be — rollback is not optional in this phase's
  acceptance criteria even though it is easy to defer.
- **Completion evidence.**
  - **The phase's first finding was that CI had never been green.** `gh run list` showed every run failed:
    the last fifteen in ~3 s with *"the job was not started because recent account payments have failed or your
    spending limit needs to be increased"*, and the last one that actually executed (`35313506879`, M10's close)
    on `GeneratedDocsTests.جرد_الاختبارات_مطابق_للمصدر`. The workflow file had been described as comprehensive
    in six documents; none of them had looked at a run.
  - **Defect 1 — a platform-dependent regex.** `VitestCase` was `^\s*(it|test)(\.each\(.*\))?\s*\(`.
    Reproduced in isolation: the line `it.each(FORBIDDEN)('…')` matched **0 times on macOS and 1 on Linux**, same
    bytes, same .NET 10.0.12, both arm64. Cause is the greedy `.*` inside an *optional* group and .NET's
    auto-atomicity optimization; proven by bisecting the pattern (making the group mandatory, or the quantifier
    lazy or bounded, made both agree). Fixed to `^[ \t]*(it|test)(\.each)?\s*\(`, which never looks for a
    closing paren; `test.describe`/`beforeAll`/`skip` stay uncounted as before. Inventory regenerated: 665 → 666.
  - **Defect 2 — `-warnaserror`.** Duplicate `using Souq.Application.Common.Interfaces;` in `ChangePassword.cs`
    and `ResetPassword.cs` (CS0105), added in M15. Warnings locally, build errors in CI.
  - **Defect 3 — the backup alarm failed open.** `backup-verify.sh` computed age with `python3` and, when absent,
    took the `note "no age check"` branch and exited 0 — a nine-day-old backup passing on a minimal host. Age is
    now computed by `utc_epoch` in `lib.sh` (`date -u -d`, falling back to BSD `date -u -j -f`, the same
    two-implementation shape as `sha256_of`), the unreadable branch is `problem` not `note`, and a future-dated
    stamp fails too. Three tests added, including a shape check that the verifier must not depend on an
    interpreter that may be absent.
  - **Defect 4 — the shipped stack did not boot.** `docker compose up` died with
    `InvalidOperationException: Database:MigrateOnStartup غير مضبوط` — M17's guard is right and nothing in
    `docker-compose.yml`, `appsettings*` or `.env.example` set it. Observed on a real boot before it was fixed.
    `ConfigurationSourceTests.كل_إعداد_يمنع_الإقلاع_بغيابه_مضبوط_في_حزمة_docker` now derives the required keys
    from `Program.cs`'s own guard messages and asserts compose supplies each as a real environment entry;
    mutation-tested three ways (renamed so the substring still matched, commented out, deleted) — the first
    version of the test passed the renamed mutation and was tightened.
  - **Defect 5 — a flaky test.** `SouqMetricsTests.كل_عدّاد_يُصدر_قيمته` compared measurements from a
    process-global `Meter` for exact equality while `AuthHandlersTests` emits `souq.auth.login_failed` in
    parallel — ~1 failure in 6 Linux runs. Fixed by a unique tag and `Distinct()` on names, which keeps "which
    counters fired" exact; proven still able to catch a counter that stops emitting (temporarily emptied
    `RecordSearchLogDropped` → failed; restored → passed). **120 consecutive Linux runs clean** afterwards.
  - **Defect 6 — `npm run lint` exited non-zero.** 14 `no-undef` errors in `frontend/scripts/bundle-budget.mjs`
    (M16): a Node script linted with browser globals. CI's frontend job had been red since M16 for this alone.
    ESLint now has a `scripts/**/*.mjs` block with Node **and** browser globals (the file's `page.evaluate`
    bodies name browser APIs lexically). Zero errors; the 8 remaining warnings are the known
    `set-state-in-effect` set awaiting TD-23.
  - **CD built.** [`.github/workflows/release.yml`](../../.github/workflows/release.yml): SemVer-tagged trigger
    with a rejecting tag check, a gate job running all four suites (integration included) plus a clean-tree
    check, images pushed to GHCR tagged with **both** version and commit, and a self-contained migration bundle
    kept 90 days. `docker-compose.yml` gained `image:` with a version variable — without it a deployment is
    "build from present source", which cannot be rolled back because the previous state never had a name.
  - **Rollback verified, all three branches, on a real stack** (`souq-qa`, ports 5291/8091):
    | Case | Observed |
    |---|---|
    | Deploy `v0.0.1` | `DEPLOY OK v0.0.1`, readiness 200, schema head read before and after |
    | Deploy `v0.0.2-broken` (entrypoint replaced so it never becomes healthy), schema unchanged | compose reported the container unhealthy, script took the failure path, **rolled back to `v0.0.1`** and re-verified ready; exit 1 |
    | Same failure with the schema state withheld | **refused to roll back**, printed the restore path and the previous version; exit 1 |
    Staging the second case exposed a defect in the script itself — `up` failing called `die` and skipped the
    rollback, leaving the environment broken. Fixed so `up` failure takes the same path as a failed health check.
  - **R-18's deliberate step rehearsed, not described.** Bundle built `--self-contained -r linux-arm64`, run in
    the compose network against the live database (reported up to date), then **reverted
    `20260918125847_SearchLogInsightIndex` and applied it again**, then driven through
    `deploy.sh --migrate-bundle` with `DATABASE_MIGRATE_ON_STARTUP=false`: stop → migrate → start → ready.
  - **Local Linux gate.** `scripts/ci-local.sh` runs CI's `fast` job (and `--frontend`) inside the SDK/node
    containers against `git ls-files` content — exactly what CI checks out. Green: 524 Domain, 427 Application,
    100 architecture, nothing regenerated, frontend lint 0 errors, typecheck clean, 86 test files / 673 tests,
    build. It does **not** cover the integration suite (Testcontainers in a container) or the supply-chain scans,
    and says so rather than implying coverage it lacks.
  - **Browser QA on the pipeline-deployed stack → a real accessibility defect, fixed.** `RowActionsMenu` closed
    on any scroll event; with `html { scroll-behavior: smooth }`, focusing a row's ⋮ below the fold scrolls for
    dozens of events, so a keyboard user pressing Enter got a menu that vanished. Measured before: 32 scroll
    events, menu count **0** at 150 ms and after settling; a visible row opened fine, isolating it to the scroll.
    The menu now **repositions** on scroll (rAF-throttled) instead of closing. The first fix attempt closed when
    the trigger left the viewport and reproduced the same bug from the other side (at open time the smooth scroll
    has only begun) — rejected on measurement, not argument. After: menu stays open in both cases. Three Vitest
    tests added and mutation-tested against the original behaviour.
  - **Journeys, against what `deploy.sh` deployed:** storefront 17/17, back-office 2 passed + 4 skipped (the
    platform-account journeys need `SOUQ_API_LOG`), store-administration / admin-inventory / product-variants /
    search all passing, phone project 11/11. `storefront.spec.js` test 11 was fixed rather than accommodated: it
    took the *first* product and pressed "Increase quantity", which is **correctly disabled** when that product's
    stock is 1 — it now picks a product that can actually hold two, and says so when none can.
  - **Suites at close:** 524 Domain, 427 Application, 100 architecture, 422 integration, 673 frontend unit —
    all green on macOS **and** the fast three re-verified on Linux.
  - **TD-31 escalated, not worked around**, and it grew: branch protection was already owner-only, and the
    billing block means CI does not run at all. Both are in
    [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) with three options and what each costs — including
    that making the repository public is a *disclosure* decision and therefore not an engineering call.
  - **Not verified, and not claimed:** the SSH deploy step (no server) and either pipeline actually running on
    GitHub (billing). `release.yml`'s deploy job is gated on `vars.DEPLOY_HOST` so a green run can never imply a
    deployment that did not happen.
- **Next-phase trigger.** M19 may start independently of M18.

### M19 — Full product acceptance and UX certification

- **Objective.** The single, systematic, whole-product pass this plan's phased approach otherwise risks never
  doing all at once: every critical journey, every screen, every language/theme/viewport combination, verified
  together on one commit, with a coverage-gap analysis closing the loop on `ProductRoadmap.md` Phase 19's own
  exit criterion.
- **Scope.** A coverage-gap analysis across `TestInventory.md` (generated) and `Traceability.md` — every
  capability in `BusinessRules.md` has a test, named explicitly, not assumed; the remaining named test gaps
  (TD-32's forms — address, profile, review — and any admin screen still untested after M10); TD-43 (the
  smooth-scroll/row-menu journey race in `back-office.spec.js`) — apply the same settle-then-click helper
  `product-variants.spec.js` already uses; TD-44 (the stale whole-definition options `PUT`) — if this phase's
  full-product pass touches the merchant options form in a way that makes fixing it natural, close it;
  otherwise re-file with today's evidence exactly as V3 did; a full E2E suite run, every file, across every
  supported language/theme/viewport combination in one coordinated pass (not the incremental per-phase journeys
  M2–M18 each already ran).
- **Dependencies.** M1–M18 (this phase certifies their cumulative result).
- **Backend/API.** TD-44's fix if in scope (send the row version with the options form, refuse a stale
  definition with `409`) — additive, no breaking change to the existing contract's happy path.
- **Database.** None expected.
- **Frontend/UX.** TD-32's remaining form tests; TD-43's journey fix.
- **Security.** A final `TenantIsolationTests`/`AuthorizationMatrixTests` run on the exact commit this phase
  certifies — the last checkpoint before M20 treats the system as launch-ready.
- **Testing.** The coverage-gap analysis itself is the primary test deliverable — a document, backed by the
  generated `TestInventory.md` and `Traceability.md`, naming every capability and its test, with no capability
  left unnamed.
- **Browser QA.** Every journey in `frontend/e2e` (all files, not a sample), across English/light, Arabic/RTL/
  dark, desktop and phone, run to completion on one commit, in one coordinated session respecting the auth
  rate-limit runbook.
- **Docker/runtime verification.** The entire journey set against the live compose stack.
- **Documentation/ADR.** Update `TestingStrategy.md` and `Traceability.md`; close `TechnicalDebt.md` entries
  TD-32 (to whatever extent this phase covers), TD-43, TD-44 (if addressed).
- **Technical debt touched.** TD-32, TD-43, TD-44 (or explicit today-dated re-filing for any not closed).
- **Acceptance criteria.** `ProductRoadmap.md` Phase 19's exit criterion verbatim: "Test suites are ready for CI,
  and coverage of critical behavior is documented" — with the documentation actually produced, not asserted.
- **Completion evidence.**
  - **The coverage audit was done by reading test bodies, not Tests columns.** All **229 rules** in
    `BusinessRules.md` were audited against the suites. Every Arabic test method name the rule tables cite
    **exists** — the documents are not inventing tests. But roughly **two rules in five name a test that asserts
    only part of what the rule states**, about two dozen name no test at all (a class, or nothing), and three
    have no test of any kind. That is the honest headline, and it is invisible to every existing check:
    `DocumentationTests`'s identifier pattern is ASCII-only, so it **cannot see an Arabic method name** — it has
    never verified that a cited test exists in the cited suite, let alone that it asserts the rule (TD-58).
  - **`Traceability.md` was rewritten, not patched, because it was wrong in both directions.** Verified one by
    one: three rows named **files** where the legend promises **classes** (`ProductHandlersTests.cs` holds four
    classes, none of them so named); **five recorded gaps had already been closed** — TD-03 in M9, TD-34 in M14,
    the non-active-store sweeps in M5, the password policy in M9, the query budget in M16 — so the page
    over-reported open risk while under-reporting the tests that closed it; two rows contradicted the rows
    directly beneath them (V2 and V3 shipped); "two permissions unused" was one. It now carries a dated
    **last-verified** line, and new sections for the five capability areas that had shipped since M8 with **no
    row at all**: catalog search, search analytics, reporting, operability/observability, and deployment.
  - **Two real defects were found by the audit and fixed, not filed:**
    - **The merchant dashboard rounded money to the wrong precision.** `AverageOrderValue` was
      `decimal.Round(…, 2)` in code, so every three-decimal store — the Jordanian dinar, which is this
      repository's own default — was shown an average that cannot exist in its currency, and a zero-decimal
      store was shown cents. It now rounds by `CurrencyInfo.MinorUnits`, the same rule `Money` applies to every
      other amount. A theory over JOD and JPY proves it: against the old code both returned `33.33`; they now
      return `33.333` and `33`.
    - **The storefront ignored the currency precision the server sends it.** `locale.currencyDecimals` is
      produced from `CurrencyInfo.MinorUnits`, asserted in tests, and was read by nobody: the SPA derived
      decimals from the *browser's* ISO table. They agree today, which is exactly what makes a divergence
      dangerous — an old browser or a changed currency would show a shopper a precision the server did not
      compute, with nothing failing anywhere.
  - **TD-32 closed.** The three forms it named — address, profile, review — now have screen tests asserting what
    only a screen can: what reaches the server versus what is in form state, which field an error attaches to,
    and whether a rejection loses what the customer typed.
  - **Writing those tests surfaced a third defect, in a component both forms share.** `StarRating`'s display mode
    rendered five *disabled buttons* inside a `span` carrying an `aria-label` — a label on a roleless element is
    ignored, so a screen reader announced five disabled buttons on every product card and every review row and
    never said the rating. Its interactive mode declared `role="radiogroup"` over plain buttons with no
    `role="radio"` and no `aria-checked`: a group where nothing announces which option is chosen, and which does
    not answer the arrow keys its own role promises. Both fixed; **axe in jsdom flags neither**, which is worth
    recording about rule-based sweeps.
  - **TD-43 closed, and its diagnosis corrected.** It read "without a product defect" and prescribed a test-side
    fix; M18 measured a real keyboard-accessibility defect instead. **TD-44 re-filed** with today's evidence
    rather than closed: `GuardConcurrentEdit` only marks the root `Modified`, no product DTO exposes a version
    at all, so the remedy is a contract addition across read model, form and handler — and this phase does not
    touch the merchant options form, which is the plan's own condition.
  - **The full browser suite was run, and it was not green the first time.** First pass: **97 passed, 12 failed**.
    Serial re-run: **103 passed, 10 failed**. The difference is the answer to five of them — with two workers,
    parallel specs mutate the same catalogue and compete for a memory-capped database. That is now written into
    `DeveloperQualityGates.md` rather than rediscovered.
  - **Each remaining failure was taken to its cause. None was a flake:**
    - `settle()` — shared by six specs — awaited **every** animation including `iterations: Infinity`. The
      announcement bar scrolls forever on every storefront page, so it could never resolve there: a 60-second
      timeout presented as a test failure. Fixed in all six; the Arabic/RTL/dark journey it blocked now runs in
      7 seconds.
    - `getByRole('status')` matched **two** elements — the variant hint and the search announcer that has been
      in the DOM since M3. The hint got a stable hook; the assertion now targets what it means.
    - A journey navigated away while an add-to-basket was still in flight, cancelling it, so one of two basket
      lines silently never arrived. It now waits for the server to confirm each add.
    - The opening-reveal journey asserted a feature that is **store-opt-in and off by default**, so it could
      never pass — and its three siblings, which assert *absence*, were passing vacuously. The spec now enables
      the setting and restores it afterwards. **Four tests became meaningful; one stopped being a false failure.**
    - `storefront.spec.js` took the *first* product and pressed "Increase quantity", which is **correctly
      disabled** when that product's stock is 1. It now picks a product that can hold two, and says so when none
      can.
  - **Three journey groups need a Development API, and that is the product being correct.** `{slug}.localhost`
    resolution is Development-only because in Production a store is reached through a registered, verified
    domain; and invitation links are logged only in Development, because a production log must not carry a secret
    link. Run separately against a Development stack, **all three groups pass**: platform provisioning 9/9,
    back-office including the platform-accounts section, and second-tenant 6/6.
  - **The certification runs, reported as they came out.** Against the container stack, three full serial runs:
    **103 → 106 → 111 passed**, each after fixing what the previous one exposed. The final run's four failures
    are two `second-tenant` journeys — which cannot pass there **by design**, because `second.localhost` resolves
    only where Development host resolution is allowed — and two that **pass when their own file is run**
    (`account-password`'s device sign-out and `admin-inventory` test 3). Against a Development stack, the three
    groups that require one pass **21/21**: platform provisioning 9, second-tenant 6, back-office 6, including
    the invitation-link journeys that had never run here before.
  - **TD-56 closed by fixing it; TD-57 corrected rather than closed.** An earlier draft of this phase claimed
    TD-57's class was closed. It is not, and the row now says so: five of its named journeys had findable causes
    and were fixed, and the suite is still not reliably green as a single serial run. Claiming otherwise would
    have been precisely the failure this phase exists to catch.
  - **TD-56 and TD-57 background.** `second-tenant.spec.js` no longer hardcodes a port, and the
    fixture it needs — which `DeveloperQualityGates.md` described and then said "nothing in the repository
    creates it" — is now `scripts/qa-second-store.py`: idempotent, through the real API, invitation flow
    included. The consequence of that sentence was measurable: all six of those journeys failed on this machine
    because nobody had done the manual step.
  - **Six findings filed with today's evidence** rather than hand-waved: TD-58 (nothing validates a rule's named
    test), TD-59 (four shipped capability areas have no rule ids), TD-60 (no rate limit is asserted anywhere, and
    `SouqApiFactory` disables all four for the whole suite), TD-61 (an archived store's endpoints are asserted
    nowhere — only suspension is), TD-62 (an undecryptable store payment secret failing loudly is untested;
    `PaymentGatewayUnavailableException` has **zero** test references), TD-63 (numeric security policies are
    asserted through their own constants, so `MaxFailedLogins = 500` is a green build).
  - **Two more flaky tests were found by running the suite rather than a filter, and both had real causes.**
    A review-page query-count test measured once, so a background service's query inside the window inflated it —
    fixed the way M16 already fixed its sibling (`ReadPathQueryBudgetTests`): measure twice, take the minimum,
    which can only remove foreign queries and never masks a real N+1. Verified still live: it reads exactly 3.
    And a search-analytics test built its "second written form" with `ToUpperInvariant()` — **which does nothing
    to Arabic**. It depended on the six random hex characters in the term containing a Latin letter, so about
    one run in sixteen failed on the test's own guard with nothing wrong in the product. The second form is now
    a **tatweel inserted into the stem**: a written difference that normalization removes, which is what the
    test's name claims in the first place.
  - **Suites at close:** 524 Domain, 427 Application, 100 architecture, 424 integration, 698 frontend
    unit across 90 files; lint clean, type-check clean, build clean.
  - **Acceptance criterion, honestly split.** "Test suites are ready for CI" is true of the code — the build is
    warning-free and 1,400+ tests discover and run — and **unproven of the pipeline**, which has not executed
    since before M13 for billing reasons (TD-31). "Coverage of critical behavior is documented" is now actually
    produced: the audit above, `TestingStrategy.md` §6, and a rewritten `Traceability.md`.
- **Next-phase trigger.** M20 may start once this phase's full journey run is green on one commit with a clean
  tree.

### M20 — Launch readiness and post-launch foundation

- **Objective.** The final gate: every P0 in `ReleaseReadiness.md` is closed or explicitly accepted in writing
  by the owner (`ProductRoadmap.md` §11's own bar for "first sellable release"), documentation is finalized per
  Phase 22's exit criterion, and a concrete, honest list of what still requires the owner is the only thing
  standing between this repository and a real customer.
- **Scope.** Run `./scripts/release-gate.sh --require-all` against the real deployment M17/M18 established, and
  resolve every section it reports rather than one that "would pass if run" — the standing rule this plan has
  followed throughout §3, applied here at maximum strictness; finalize every document `ProductRoadmap.md` §10
  names; an API reference generated from OpenAPI (if not already produced as part of `ApiDocumentation.md`'s
  own evolution); a tenant onboarding guide (distinct from `DevelopmentGuide.md`, which is for engineers, not
  a new client store's operator); operational runbooks (incident response already exists per
  `IncidentResponse.md` — confirm it is current against everything M15–M18 built); the go-live checklist itself
  signed off.
- **Dependencies.** M1–M19 (this is the terminal phase).
- **Backend/API.** None expected beyond closing whatever `--require-all` still reports failing that is
  genuinely engineering's (not P-03/P-05/P-06/D-13's owner-only items, which this phase reports as still open,
  by name, rather than blocks itself on).
- **Database.** None expected.
- **Frontend/UX.** None expected.
- **Security.** The final security posture check — everything M15 found either fixed or accepted in writing,
  re-confirmed on the exact launch commit.
- **Testing.** The full gate 3/gate 4 suite from `DeveloperQualityGates.md`, on the exact commit intended for
  the first real customer.
- **Browser QA.** M19's full journey set, re-run once more on the exact launch commit (not assumed still valid
  from M19's own commit if anything changed since).
- **Docker/runtime verification.** The real deployment, live, health-checked, logs reviewed, for the exact
  commit being called launch-ready.
- **Documentation/ADR.** `ProductionReleaseChecklist.md` fully completed and signed off (§18's sign-off
  section); `ProductRoadmap.md` updated to show Phases 19–23 closed against this plan's evidence; this plan's
  §0 updated to `current_phase: done` — the plan's own terminal state.
- **Technical debt touched.** A final honest pass over `TechnicalDebt.md` — nothing closed dishonestly to make
  the register look emptier than it is; anything still open at launch is explicitly accepted as post-launch
  work, not silently dropped.
- **Acceptance criteria.** Every P0 in `ReleaseReadiness.md` is `FIXED` or carries a dated, written owner
  acceptance; `ProductRoadmap.md` §11's "first sellable release" checklist is fully accounted for, item by item,
  with each remaining gap named as either "engineering, not yet done" (a plan failure to admit) or "owner's,
  named in `OwnerDecisions.md`" (not engineering's to close).
- **Completion evidence.**
  - **The gate was run, not read.** `./scripts/release-gate.sh --require-all --suites` against the container
    stack — the only deployment that exists — with a real backup directory and a real drill record. Result:
**backups ✔, dependency audit ✔, smoke test ✔ (31 checks against the live
    deployment), build warning-free with warnings as errors ✔, frontend lint/types/tests/build ✔.** Two sections
    do not pass, and both are honest:
    - **§1, the target configuration, fails — correctly.** The env file it was pointed at runs the *fake*
      payment gateway, the *log* email adapter and demo seed data. Those are exactly what a QA stack should
      use and exactly what a production deployment must not, and `audit-config.sh` says so in three FAILs and
      four WARNs. A pass here would have meant the gate was not looking.
    - **§3, the suites, had to be run separately.** Running them inside the gate starts a second SQL Server
      through Testcontainers while the stack's own is up; on a 2.84 GiB Docker host shared with the owner's
      unrelated containers, that starves both. The first attempt proved it: the suite section failed and the
      smoke test saw a **500 on the catalogue read** — which turned out to be
      `SqlException … Connection reset by peer` after 33 seconds, the database dropping the connection, not a
      product defect. The same endpoint answers 200 consistently with memory restored, and the full gate then
      reports the smoke test green. The suites are green on this commit, run on their own: **524 Domain, 427
      Application, 100 architecture, 424 integration, 698 frontend**.
    That episode is itself worth recording: **the gate's own resource cost can manufacture a failure that reads
    like a defect**, and the only way to tell was to chase the 500 to its exception rather than accept it.
  - **Backups became a working chain rather than a script that exists.** A set was taken from the running stack
    (database + uploads + a manifest naming the migration head and row counts), checksum-verified, and then
    **restored onto clean throwaway infrastructure**: row counts and migration head matched the manifest,
    composite tenant foreign keys were intact, no order row was orphaned, no constraint was left untrusted, and
    the database came back `ONLINE` and writable. `backup-verify.sh` then read that drill record back and
    passed — the same verifier that, before M18, would have reported success on a stale backup.
  - **The first rehearsal attempt failed** — the throwaway SQL Server did not start inside the script's 60-second
    wait, because an emulated amd64 image was competing for memory with the QA database on a 2.84 GiB Docker
    host. It tore its own infrastructure down cleanly and said so. Re-run with memory freed, it passed. Recorded
    because it is the script behaving correctly under a constraint of this machine, not of the product.
  - **`ProductRoadmap.md` §11 is accounted for item by item**, each line marked *built and verified*, *built but
    unverifiable here*, or *the owner's*. The two that matter most are honest about their state: a live payment
    gateway and real email delivery are **built and have never run against a real account**.
  - **Roadmap phases updated against this plan's evidence:** Phase 20 ✅ (M15), Phase 21 ✅ (M16), Phase 22 ✅,
    Phase 19 🟡 (exit criterion met in substance; the pipeline that would enforce it cannot run), Phase 23 🟡
    (mechanisms built and rehearsed; the target is the owner's).
  - **The release checklist is not signed off, and says why.** Several REQUIRED boxes are assertions about a
    deployment that does not exist. Every box that is an assertion about *the repository* is green; every box
    that is an assertion about *a deployment* is unmet for the same single reason.
  - **The debt register was not tidied to look better.** 52 rows, 7 struck through, 45 open — one P0 (TD-50,
    which account a refund resolves) and six P1. M19's own over-claim on TD-57 was **corrected rather than left
    standing**, which is the behaviour this register is for.
  - **Browser QA on the launch commit: 103 passed, 5 failed** — two `second-tenant` journeys that cannot pass
    against a Production-mode stack by design, and three of the rotating set TD-57 describes. Two of those three
    now fail **in isolation as well**, which they did not earlier in the session: the shared QA store has grown
    to ~70 inventory rows, 196 products and 205 customers across M19 and M20's runs. For `admin-inventory` the
    symptom is precise and the **cause was not established** — the row the journey just created is not found on
    either page of the screen it must appear on, with the interface language pinned and the paging verified.
    TD-57 says exactly that, because an unfinished investigation recorded is worth more than a guess written as
    a fix.
  - **Dates were not bumped.** Twenty-one documents carry "Last verified: 2026-09-17". They were not re-read in
    this phase, so their dates were left alone: stamping a date this phase did not earn is the precise failure
    mode the plan's §2 forbids.
- **Next-phase trigger.** None — this is the plan's terminal phase. A session reaching this point with every
  acceptance criterion met reports exactly that, and exactly what remains for the owner, and stops per §5's
  "unresolved business/tax/licensing/payment-ownership decision" condition, because at that point everything
  else genuinely is the owner's.

---

## 9. Architect's review of this plan

Written immediately after drafting §8, from the same evidence, as the senior-review pass the task asked for —
not a rubber stamp of the phase list above.

- **Dependency correctness.** The chain M1 → M2 → M3 → M4 is a real, necessary sequence (structure, then the
  catalog the search engine reads, then search, then the UI sweep that must cover search's own markup). M5–M14
  are largely independent of each other and could, with a larger team or a different session-per-track model,
  run in parallel; this plan still lists them sequentially because `souq continue`'s single-session model
  benefits from a stable, unambiguous order more than from a theoretical parallelism it cannot actually exploit
  alone. M15 (security) deliberately sits *after* M1–M14 rather than earlier, because reviewing security on a
  system that is about to change under M2–M14 would mean reviewing a moving target; M16 (performance) follows
  M15 so a performance fix cannot silently reopen a just-closed finding; M17–M18 (infrastructure, CI/CD) follow
  the review phases rather than precede them, because deploying a system before its security and performance
  posture is understood would be backwards; M19 (full acceptance) deliberately comes after infrastructure is
  real, so the certification run happens against the actual deployment shape, not a hypothetical one; M20 is
  terminal by construction.
- **What was at risk of being scheduled wrong, and the correction made.** An earlier draft of this ordering put
  the security review (M15) immediately after M1, on the theory that "security first" is always right. That is
  wrong here specifically: half of M2–M14's scope *creates* new surface area (a new search endpoint and two new
  tables in M3, a new content-page aggregate in M2, a new search-analytics table in M13) that a security review
  run before they exist cannot cover, and Phase 17's own hardening pass already closed the highest-severity
  findings that predate this plan. Security review belongs after the surface area it must cover exists, with a
  second, lighter pass possible post-M20 if the owner wants one before a second wave of customers — this plan
  does not schedule that second pass, because scheduling work with no defined trigger is exactly the "future
  improvement with no phase" pattern `TechnicalDebt.md`'s P3 rows already show is fine to leave unscheduled.
- **Missing launch-critical areas, checked for and addressed.** Search (M3/M13) was not in the original
  roadmap's five-phase tail at all — added as first-class scope per §7. Responsive/UX excellence was implicit
  in "Phase 16 done" but only the seventeen critical journeys were ever proven at scale — M4 makes the
  systematic sweep explicit rather than assumed. Payments' two undecided owner questions (F-8, R-03) were at
  risk of being "finished" by whichever path was easier to code, exactly what `OwnerDecisions.md` explicitly
  says engineering will not do — M5 and M6 are written to keep both paths equally real instead. Legal/content
  (TD-42) was at risk of falling through every phase's cracks as "not really engineering's problem" — M2 gives
  it an explicit home, distinguishing the *capability* (engineering's) from the *content* (the owner's, per
  P-03/P-06's own legal territory).
- **Realism for a modular monolith, and against premature distributed architecture.** M3's search engine is the
  phase most likely to be pushed toward a separate service (Elasticsearch/OpenSearch, a hosted search API) by
  habit rather than evidence; §8's M3 entry is deliberately explicit that SQL Server Full-Text Search (a
  database *feature*, already inside the one database this system already has) is the default, with the
  trigram/computed-column fallback as the second local-and-deterministic option, and an external service named
  only as a *possible future* requiring its own measured-evidence ADR — never as this phase's default. M16
  (performance) is written the same way: caching only where measurement justifies it, no new infrastructure by
  default. Nothing in M1–M20 introduces a message broker, a second database, a second runtime, or a second
  frontend framework; where scale is genuinely a live question (M16, M17), the plan's answer is "measure, then
  decide, with an ADR," which is exactly the process `ExplicitNonGoals.md` already prescribes and this plan
  inherits rather than reinvents.
- **What this plan deliberately leaves outside its own twenty phases.** `ProductRoadmap.md` §11's "can follow
  after launch" list (a complex variant option-matrix UI already superseded by V3's actual delivery, in-app
  customer notifications, shipping zones and carrier APIs, tenant subscription billing, custom per-tenant roles,
  SQL Server RLS as a *default* rather than M15's evaluated option, the dedicated-database-per-tenant seam, a
  public integrations API) is correctly not folded into M1–M20 — building any of it now would be exactly the
  "speculative infrastructure" this plan's §2 forbids, since none of it is required to reach genuine launch
  readiness for the first paying customer.

---

## 10. Change log

| Date | Change |
|---|---|
| 2026-09-17 | Plan created at baseline checkpoint `d45bb53` on `phase/17-production-hardening`, after a fresh read of `AGENTS.md`, the numbered learning path, `ProductRoadmap.md`, `OwnerDecisions.md`, `TechnicalDebt.md`, `ReleaseReadiness.md`, the ADR index and Product Variants V1–V3's final state. No phase executed yet — `current_phase: M1`, `phase_status: not_started`. |
