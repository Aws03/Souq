# Developer quality gates

> **What this page is:** the canonical list of the suites, their commands, and what must pass when. Four gates — a finished feature, a finished review, a release candidate, a production deployment — each listing exactly what is run and what it proves.
> **Read with:** [AGENTS.md](../../AGENTS.md) §7 (the commands) · [TestingStrategy.md](../10-TESTING/TestingStrategy.md) (what each suite is for) · [ProductionReleaseChecklist.md](ProductionReleaseChecklist.md) (the deployment itself).
>
> **A pipeline now runs these gates** — [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml), implementing §5. Run them locally anyway before you push: the pipeline is the backstop, not the first place you find out. **One step is still manual and cannot live in this repository:** requiring the checks to pass before a merge is a branch-protection setting in GitHub (§5, requirement 4).

## The suites

| Suite | Command | Needs | Typical time | What it protects |
|---|---|---|---|---|
| Build | `dotnet build` | — | seconds | Compiles with no warnings |
| Domain | `dotnet test tests/Souq.Domain.Tests` | — | ~1 s | Business rules and invariants |
| Application | `dotnet test tests/Souq.Application.Tests` | — | ~1 s | Use cases against doubles |
| Architecture | `dotnet test tests/Souq.ArchitectureTests` | — | seconds | Layers, module boundaries, tenancy, endpoints, documentation, generated inventories |
| Integration | `dotnet test tests/Souq.IntegrationTests` | Docker, **3 GiB+ free** (measured: SQL Server alone holds 1.085 GiB idle) | minutes | The real API over real SQL Server |
| Frontend lint | `cd frontend && npm run lint` | npm install | seconds | ESLint 9: hooks rules, `jsx-a11y`, language basics — parse errors Vite would compile silently |
| Frontend type-check | `cd frontend && npm run typecheck` | npm install | seconds | `tsc --noEmit` over the `.js` sources under `frontend/src` (`checkJs` + JSDoc; components in `.jsx` are not yet included — `frontend/tsconfig.json`) |
| Frontend (Vitest) | `cd frontend && npm test` | npm install | seconds | Frontend logic, components, accessibility structure |
| Frontend build | `cd frontend && npm run build` | npm install | seconds | The SPA actually builds |
| Browser journeys (Playwright) | `cd frontend && npx playwright test <file>` | a live local stack ([runbook below](#running-the-playwright-journeys)) | about a minute per file | The critical journeys in a real browser, for two stores and the platform console. **Manual, before a release — not in CI and not in `scripts/release-gate.sh`** |

`scripts/release-gate.sh --suites` runs the build (warnings as errors), every .NET suite, and the frontend lint, type-check, tests and build; with no deployment arguments it reports 5 passed and 3 skipped, and says which ([ProductionReleaseChecklist.md](ProductionReleaseChecklist.md)).

### Running the Playwright journeys

`frontend/e2e/` holds **124 journeys in 19 files** (the live count is generated into [TestInventory.md](../10-TESTING/TestInventory.md); this line said 83 in 10 until M19 checked it). They drive a real browser against a real stack, so they need one running:

1. **SQL Server reachable from the host on `localhost,1433`.** The `docker compose` stack does **not** serve this: its `db` service publishes no port, so a locally run API cannot reach it. Use a SQL Server of your own with a published port, as in [DevelopmentGuide.md](DevelopmentGuide.md) §1.
2. **The API in Development**, with its output written to a file: `dotnet run --project src/Souq.API --urls http://localhost:5200 > /tmp/souq-api.log 2>&1`. Port 5200 is what `launchSettings.json` and the Vite proxy expect. Development seeds the demo default store and the development accounts — admin@souq.com / Admin@123 (store admin) and owner@souq.com / Owner@12345 (platform owner) — which the journeys sign in with.
3. **Vite**: `cd frontend && npm run dev` on port 5173. It proxies `/api` and `/uploads` with the Host header unchanged, which is what makes `http://localhost:5173` the default store, `http://{slug}.localhost:5173` another store and `http://admin.localhost:5173` the platform.
4. **Run one file at a time, about a minute apart:** `npx playwright test e2e/storefront.spec.js`. Sign-in is rate-limited to 10 requests a minute per host and client address (`RateLimiting:Auth:PermitLimit`), and a full run in one go trips it — unless the stack raises the limit for QA, which is what M19's full-suite runs did.
5. **Against the container stack, pass the demo credentials and the published port.** `scripts/demo-up.sh` brings up
   a complete stack with no local SQL Server and no Vite, which makes it the shortest path to a real browser run —
   but its seeded accounts are **not** the development ones, because `DbSeeder` refuses `Admin@123` outside
   Development. The journeys default to the development passwords, so they fail at sign-in with a `waitForURL`
   timeout that looks nothing like a credentials problem:

   ```bash
   SOUQ_E2E_BASE_URL=http://localhost:8081 \
   SOUQ_E2E_ADMIN_PASSWORD='Admin@123456' \
   SOUQ_E2E_OWNER_PASSWORD='Owner@123456' \
     npx playwright test e2e/back-office.spec.js --project=desktop
   ```

   And if port 8081 is taken by another stack, override only the web port rather than stopping a stack you did not
   start — Compose **appends** to `ports`, so the override needs `ports: !override` or the old mapping comes along
   and the bind still fails.
6. **Wait for the right signal, not for something that looks like it.** Three of the "rotating" failures TD-57
   recorded turned out to be three different versions of this mistake, each deterministic once the store was full
   enough. A table's loading skeleton **is** five real `<tr>` elements, so waiting for the first row waits for
   nothing — wait for `[role="region"][aria-busy="false"]`, which `DataTable` now sets. A list that answers a
   *different* question is not busy, so after changing a filter or a period wait for that request's response, not
   for the table to go quiet (`keepPreviousData` keeps the old rows on screen, honestly marked not-busy). And a
   success toast lingers for seconds, so it is still on screen from the *previous* save — never use it to wait for
   the next one; wait for the write's own response, then assert the toast if you want the message checked.
7. **Start a full-suite pass from a known baseline.** The journeys share one store and deliberately do not
   clean up, so it grows every run — and journeys that find their fixture by scanning a list start failing as
   thresholds are crossed (TD-57). `scripts/qa-reset.sh` returns the store to its seed, and
   `scripts/qa-second-store.py --skip-admin` adds the second store `cross-tenant-adversarial.spec.js` needs
   (that flag works on a Production stack; the full fixture, with its administrator and products, still needs a
   Development API for the invitation link). Two consecutive passes from that baseline were clean — 16 files,
   109 tests — where the same suite had never managed one before.
8. **Run serially (`--workers=1`) for a full-suite pass.** M19 measured it: with two workers, five journeys fail that pass alone, because parallel specs mutate the same catalogue and compete for a memory-capped database. Those are not product defects and chasing them as such wastes a day.

What else the files need:

| Need | Files |
|---|---|
| `SOUQ_API_LOG` pointing at the API's log file, so the journey can follow the invitation link the development email adapter writes there. Without it those journeys are skipped, not passed. **Two conditions are easy to miss, and M19 hit both.** The link is written *only* in Development (`ConsoleEmailService.IncludeLinksInLog`): outside it the log says an email "was not sent" with a masked recipient, on purpose, so a secret link never reaches a production log. And **only one API instance may be running against the database** — the outbox is leased, so a container API and a local one will share the messages and the invitation can be dispatched into the *other* instance's log. It is not lost; it is elsewhere | `frontend/e2e/platform-provisioning.spec.js`, `frontend/e2e/back-office.spec.js` |
| A second store on `second.localhost`, named "Second Store", pricing in USD, with three products whose slugs begin `second-widget`. **`scripts/qa-second-store.py` creates it** — idempotently, through the real API including the invitation flow. It used to say "nothing in the repository creates it", and the consequence was exactly what that implies: M19's first full-suite run failed all six of these journeys on a machine where nobody had done the manual step | `frontend/e2e/second-tenant.spec.js` |
| The `phone` project (Pixel 7). Every other file runs in the `desktop` project; `playwright.config.js` routes them automatically | `frontend/e2e/responsive.spec.js` |
| **nginx**, i.e. the container stack — not Vite. The content-security policy is a response header nginx adds, so against `npm run dev` there is no policy to read and the file fails on its first assertion with its own message: «no content policy at all — nginx is not what serves this page». Run it against `http://localhost:8081` | `frontend/e2e/csp.spec.js` |

**Three journeys need a Development API, and that is the product being correct, not a gap.** `platform-provisioning.spec.js`,
`second-tenant.spec.js` and `back-office.spec.js`'s platform-accounts section depend on two Development-only behaviours:
`{slug}.localhost` host resolution (in Production a store is reached through a registered, verified domain — see
`TenantResolutionMiddleware.AllowDevelopmentResolution`) and the logged invitation link above. Run them against
`http://localhost:5173` with the API in Development; run everything else against whichever stack you are certifying.
M19 did exactly that, and the split is why its full-suite pass is reported as two runs rather than one.

**What a run leaves behind.** The journeys create data with unique names and do not reset the database: `qa-…` stores (archived by the provisioning journey itself, and archiving cannot be undone), `QA …` categories and products (a category the journey deletes, a product it archives), customers on `@souq.test` addresses, and invited QA platform administrators that end up **disabled — platform accounts cannot be deleted**, so they accumulate. Run against a development database, never a shared or production one.

## Gate 1 — a feature is complete

Before you call your own work done.

- [ ] `dotnet build` — no errors, no new warnings.
- [ ] `dotnet test tests/Souq.Domain.Tests` and `dotnet test tests/Souq.Application.Tests`.
- [ ] The suite that covers what you touched: architecture rules if you moved a type or added an endpoint; frontend tests if you touched `frontend/src`.
- [ ] **A test at the level of the rule you changed.** A domain rule gets a domain test; a rule needing a lookup gets a handler test; anything a race can break gets an integration test.
- [ ] Changed a controller, a use case, a contract or a test file? Regenerate the inventories:
      `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
- [ ] Documentation updated in the same change: the module document, and [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) if the rule is new.
- [ ] An ADR if the change was architectural, linked from the [ADR index](../11-ADR/README.md).

## Gate 2 — a change is ready for review

Everything in gate 1, plus:

- [ ] `dotnet test tests/Souq.ArchitectureTests` — the full suite, not a filter. This is the gate that catches a broken documentation link, a drifted inventory, a new module crossing or an endpoint with no declared access.
- [ ] `dotnet test tests/Souq.IntegrationTests` if you touched persistence, tenancy, authorization, payments or the request pipeline.
- [ ] `cd frontend && npm run lint && npm run typecheck && npm test && npm run build` if you touched the frontend.
- [ ] **Self-review against [CodeReviewGuide.md](../00-START-HERE/CodeReviewGuide.md)**, specifically: does a business rule sit outside the Domain? does a new endpoint declare its access? does any new query cross a tenant boundary?
- [ ] Security questions answered for anything touching data: who may call this, and what does another store's id return?
- [ ] Commits are Conventional Commits, one coherent change each, and the repository builds at every one.

## Gate 3 — a release candidate

Everything in gate 2, run together on the exact commit, with nothing skipped:

```bash
dotnet build
dotnet test tests/Souq.Domain.Tests
dotnet test tests/Souq.Application.Tests
dotnet test tests/Souq.ArchitectureTests
dotnet test tests/Souq.IntegrationTests
cd frontend && npm run lint && npm run typecheck && npm test && npm run build
```

- [ ] **All green.** A skipped suite is a failed gate — say which and why, do not let it pass silently.
- [ ] No `SOUQ_UPDATE_DOCS` run is needed: the generated inventories already match, or the commit is not clean.
- [ ] [ReleaseReadiness.md](ReleaseReadiness.md) has no open **P0**.
- [ ] The risk and debt registers reflect anything this release closed or opened.
- [ ] The Playwright journeys, file by file, against a local stack ([runbook](#running-the-playwright-journeys)). They are not in CI; this gate is where they run.
- [ ] A manual pass over the money path on a local stack: browse → basket → checkout → pay → email → track → refund.

## Gate 4 — production deployment

Everything in gate 3, plus [ProductionReleaseChecklist.md](ProductionReleaseChecklist.md) in full. The two are deliberately separate: gate 3 proves the *code* is sound, the checklist proves the *deployment* is.

- [ ] A backup exists from **before** this deployment, and it is identified by name.
- [ ] The rollback plan is written down for this specific release.
- [ ] One person owns the deployment and is watching the log after it starts.

## 5. What a CI pipeline must do

**IMPLEMENTED** in [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) as four parallel jobs, so the fast
suites answer in minutes without waiting on the integration run. Two places where the implementation departs from the
specification below, both deliberate:

- **No SQL Server *service container*.** The suite uses Testcontainers — `SouqApiFactory` starts its own `MsSqlContainer`
  and hands the test its connection string — so a service container would sit unused beside the one the tests start.
  What the job needs is Docker, which `ubuntu-latest` has.
- **The npm audit is split.** `dependencies` are blocking at high; `devDependencies` are reported but do not fail the
  build. Vite and Vitest are a build tool and a test runner: their current advisories (including a high) are in the dev
  server or are Windows-specific, and none of them runs in production, where nginx serves static files. Failing the
  build on those would force either a threshold quietly lowered later, or major version bumps imposed by the pipeline
  instead of proposed ([AGENTS.md](../../AGENTS.md)). They stay visible on every run instead of being silently excluded.

### The one manual step, precisely

A third requirement cannot live in this repository at all: **requiring the checks before a merge is a GitHub
branch-protection setting**. Until it is switched on the pipeline reports and does not block, which is
requirement 4 unmet. In *Settings → Branches → Add branch ruleset* for `main`, require a pull request, require
status checks to pass, and select all four — `Build + fast suites`, `Frontend tests + build`,
`Integration suite (real SQL Server)` and `Dependency audit + secret scan`. The job names are stable; they are
the `name:` values in the workflow.

### Does this pipeline support adopting TypeScript?

[ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md) deferred TypeScript with one trigger — "a CI
pipeline exists" — on the reasoning that "a type-check nobody runs is not a control". **That trigger has now
fired**, so the objection recorded there no longer holds.

Existing is not the same as sufficient: `vite build` strips types **without checking them**, so a build alone
would ship type errors intact. That gap is now closed. `frontend/tsconfig.json` exists (`checkJs` over the
`.js` sources — not yet the `.jsx` components — with JSDoc at the boundaries — [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)),
and the frontend job runs `npm run typecheck` (`tsc --noEmit`) as a blocking step before the tests. A move to
TypeScript files would be checked by the same step.

Nothing else is missing: the suites, the audits and the secret scan already cover what a migration would touch.

| Stage | Runs | Fails the build when |
|---|---|---|
| Build | `dotnet build` with warnings as errors | anything does not compile |
| Fast tests | Domain, Application, Architecture | a rule, a use case or a boundary broke |
| Frontend | `npm run lint`, `npm run typecheck`, `npm test`, `npm run build` | a lint error (warnings do not fail), a type error, a test or the build broke |
| Integration | the integration suite over a real SQL Server | behaviour over the real engine broke |
| Documentation | the architecture suite already covers it | a link, a path, a code name, an ADR's structure or a generated inventory drifted |
| Secret scan | `gitleaks` over the working tree, plus a check for live payment keys that no allowlist can suppress | a key, a connection string or a token appears |
| Dependencies | NuGet audit; npm audit split into shipped vs tooling | a vulnerable package reaches **shipped** code (tooling advisories are reported, not blocking) |

Requirements that matter more than the tool chosen:

1. **Every suite runs on every pull request**, not on a schedule. The architecture and documentation tests are the written memory of the decisions; running them late defeats them.
2. **The integration suite must run in CI**, not only locally. It is the only place tenancy, concurrency and the real database are proven.
3. **The generated inventories are verified, never regenerated by CI.** If they drift, the change is incomplete — the fix belongs in the commit, not in the pipeline.
4. **A red main branch blocks merges.** Without that rule the suites become advisory.
5. **Do not add a coverage threshold.** The suites here are behavioural; a percentage would reward tests that assert implementation details, which [TestingStrategy.md](../10-TESTING/TestingStrategy.md) explicitly argues against.

## 6. When a gate fails

- **Never weaken a test to make it pass.** A failing test is information; understand the cause first ([AGENTS.md](../../AGENTS.md) §0).
- A failing **architecture** test usually means the change is real and needs a decision: a new module crossing, a missing contract, an endpoint with no declared access. Fix the design, or record the exception in the test's own allow-list *in the same commit*, with the reason.
- A failing **documentation** test means a link, a path or a name moved. The inventory is regenerated; prose is fixed by hand.
- A failing **integration** test that passes locally but not elsewhere is usually Docker memory. Check it before blaming the test, and never stop containers you did not start ([Troubleshooting.md](Troubleshooting.md)).
