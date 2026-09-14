# Developer quality gates

> **What this page is:** what must pass, and when. Four gates — a finished feature, a finished review, a release candidate, a production deployment — each listing exactly what is run and what it proves.
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
| Integration | `dotnet test tests/Souq.IntegrationTests` | Docker, ~2 GB free | minutes | The real API over real SQL Server |
| Frontend | `cd frontend && npx vitest run` | npm install | seconds | Pure frontend logic |
| Frontend build | `cd frontend && npx vite build` | npm install | seconds | The SPA actually builds |

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
- [ ] `cd frontend && npx vitest run && npx vite build` if you touched the frontend.
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
cd frontend && npx vitest run && npx vite build
```

- [ ] **All green.** A skipped suite is a failed gate — say which and why, do not let it pass silently.
- [ ] No `SOUQ_UPDATE_DOCS` run is needed: the generated inventories already match, or the commit is not clean.
- [ ] [ReleaseReadiness.md](ReleaseReadiness.md) has no open **P0**.
- [ ] The risk and debt registers reflect anything this release closed or opened.
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

But existing is not the same as sufficient, and the difference matters here: the frontend job runs
`vite build`, and Vite strips types with esbuild **without checking them**. A TypeScript migration landing today
would compile and ship with type errors intact, which is precisely the "enforcement deferred indefinitely"
outcome ADR-0037 was avoiding. Adopting TypeScript therefore requires **one addition to this pipeline** — a
`tsc --noEmit` step in the frontend job, blocking — alongside the *tsconfig.json* the repository does not yet
have. That step is deliberately not added now, because a type-check with zero TypeScript files is theatre.

Nothing else is missing: the suites, the audits and the secret scan already cover what a migration would touch.

| Stage | Runs | Fails the build when |
|---|---|---|
| Build | `dotnet build` with warnings as errors | anything does not compile |
| Fast tests | Domain, Application, Architecture | a rule, a use case or a boundary broke |
| Frontend | `npx vitest run`, `npx vite build` | logic or the build broke |
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
