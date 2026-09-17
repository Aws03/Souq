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
current_phase: M2
phase_status: not_started
next_phase: M2
blocked_decisions: []           # owner decisions that block a phase currently in flight; see §5 and OwnerDecisions.md
last_verified_date: 2026-09-17
last_verified_head: 37d8008     # the M1 code commit; this docs-reconciliation commit follows it on the same branch
baseline_branch: phase/17-production-hardening
```

**M1 — done.** Closed TD-04 (payment-account use cases moved from Platform's folder to Payments, reaching the
platform admin path through a published contract, `IStorePaymentAccountEditor`, rather than a raw class
reference); corrected TD-04's review-settings half, TD-02's extraction-order claim, and TD-01/TD-03/TD-05 with
today's evidence; crossings 79 → 74 across 15 pairs (was 16); `RiskRegister.md` R-04 closed, R-14/R-15 updated.
Full evidence in M1's own "Completion evidence" field above.

Keep this block current in the same commit that closes a phase: `current_phase`, `phase_status`
(`not_started` | `in_progress` | `blocked` | `done`), `next_phase`, `blocked_decisions` (the exact ID from
[OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), e.g. `P-06`), `last_verified_date` and
`last_verified_head` (the commit hash the phase closed at). A session picking this plan up cold reads this block
**first**, then confirms it against `git log -1` and `git status` before trusting it — the repository is still
the source of truth if this block and the commit history ever disagree (they should never disagree; if they do,
trust the git history and fix this block in the same change).

---

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
- **Technical debt touched.** TD-42 (close, if scoped and built) or re-file with today's evidence if the scope
  decision (`AGENTS.md` §9 — "a product decision on scope") turns out to need the owner after all; note any
  variant-image gap closed or left, updating `ProductVariants.md`'s consequences list.
- **Acceptance criteria.** A store can publish at minimum a privacy policy and terms page without inventing
  legal text (the *capability* is engineering's; the *content* is the store's own, never fabricated by this
  plan); `ProductVariants.md` has no stale "not built" claim this phase actually closed.
- **Completion evidence.** *(fill in on close)*
- **Next-phase trigger.** M3 may start once Catalog's read model (`CatalogQueries`) is stable for this phase —
  M3 builds directly on it.

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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** TD-25 (inventory screen, if migrated here).
- **Acceptance criteria.** No negative stock reachable under concurrent load; low-stock alerts fire per variant
  correctly; the admin inventory screen has no stale-data gap TD-25 already named.
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** TD-06 (close), R-09's residual (close or re-file with evidence).
- **Acceptance criteria.** No coupon-adjacent endpoint can produce a total checkout would refuse; `Coupon.Value`
  either has a currency guard or the unreachability is re-confirmed and dated.
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** TD-29 (close), TD-03 (close or re-file with today's evidence), TD-16's retention
  scope for refresh tokens (re-file with a legal-decision dependency noted, not solved here — retention needs
  the owner per TD-16 itself).
- **Acceptance criteria.** A signed-in customer can change their password from the UI; reuse detection and
  session invalidation are proven by a test that actually attempts the attack, not just documented as existing.
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** TD-25 (close each screen migrated), TD-28 (close), TD-24 (partial, opportunistic —
  re-file the remainder honestly, do not claim it closed).
- **Acceptance criteria.** The lint rule TD-25 cites (`react-hooks/set-state-in-effect`) reports zero hits in the
  screens this phase migrated; one `tone` vocabulary used by all five screens TD-28 named.
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** None expected unless the fresh read finds one.
- **Acceptance criteria.** D-22 and P-07 remain correctly and visibly blocked in `OwnerDecisions.md` (not
  silently built around); everything else in the platform area not blocked on those two is complete.
- **Completion evidence.** *(fill in on close)*
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
- **Technical debt touched.** None expected to open.
- **Acceptance criteria.** Every metric `Dashboards.md` documents is verified correct against the current code;
  V4's scope is written clearly enough that a future phase can build it without re-deriving requirements.
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
- **Completion evidence.** *(fill in on close)*
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
