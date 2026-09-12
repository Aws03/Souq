# Developer quality gates

> **What this page is:** what must pass, and when. Four gates — a finished feature, a finished review, a release candidate, a production deployment — each listing exactly what is run and what it proves.
> **Read with:** [AGENTS.md](../../AGENTS.md) §7 (the commands) · [TestingStrategy.md](../10-TESTING/TestingStrategy.md) (what each suite is for) · [ProductionReleaseChecklist.md](ProductionReleaseChecklist.md) (the deployment itself).
>
> **There is no CI in this repository.** Every gate below is run by a person, on their machine. That is a known weakness, not a style choice: a red suite can be committed and nobody is told. §5 states what a pipeline must do when one is built.

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

**PLANNED.** No pipeline exists; this is the specification for the one that should.

| Stage | Runs | Fails the build when |
|---|---|---|
| Build | `dotnet build` with warnings as errors | anything does not compile |
| Fast tests | Domain, Application, Architecture | a rule, a use case or a boundary broke |
| Frontend | `npx vitest run`, `npx vite build` | logic or the build broke |
| Integration | the integration suite with a SQL Server service container | behaviour over the real engine broke |
| Documentation | the architecture suite already covers it | a link, a path, a code name, an ADR's structure or a generated inventory drifted |
| Secret scan | a scanner over the diff | a key, a connection string or a token appears |
| Dependencies | vulnerability audit for NuGet and npm | a known-vulnerable package is introduced |

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
