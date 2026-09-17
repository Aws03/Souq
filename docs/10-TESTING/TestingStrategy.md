# Testing strategy

> **What this page is:** what each test suite is for, what belongs in it, how to run it, and what must pass before a change is finished. The inventory of files and counts is generated: [TestInventory.md](TestInventory.md). What each capability is covered by: [Traceability.md](Traceability.md).
> **The rule that outranks the rest:** never weaken a test to make it pass. A failing test is information about the change, not an obstacle ([ADR-0015](../11-ADR/0015-testing-strategy.md)).

## 1. The shape

Souq has six suites. Four are .NET, two are the frontend — Vitest for logic and components, Playwright for browser journeys. They are ordered by how fast they fail and how much they prove. The commands and the gates each one belongs to are listed once, in [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md).

| Suite | Proves | Needs | Typical run |
|---|---|---|---|
| `tests/Souq.Domain.Tests` | Business rules and invariants, in isolation | Nothing | Seconds |
| `tests/Souq.Application.Tests` | Use-case orchestration, permissions and ownership, validation | Nothing (test doubles) | Seconds |
| `tests/Souq.ArchitectureTests` | The boundaries themselves: layers, modules, tenancy, endpoints, and that the documentation matches the code | Nothing | Seconds |
| `tests/Souq.IntegrationTests` | The real API over real SQL Server: HTTP contract, tenant isolation, concurrency, migrations, background work | **Docker** | Two to three minutes |
| `frontend/src/**/*.test.js`, `*.test.jsx` (Vitest) | Frontend logic, components, route guards, accessibility structure, white-label rules | Node 22 | Seconds |
| `frontend/e2e/*.spec.js` (Playwright) | The critical journeys in a real browser, for two stores and the platform console | A live local stack; run by hand | About a minute per file |

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs the four .NET suites and the Vitest suite (with lint, type-check and build) on every push and pull request. It blocks a merge only once branch protection is switched on in GitHub. Playwright is not in CI. Run the gate below yourself anyway — CI is the backstop, not the first place to find out.

## 2. What belongs where

### Domain tests

- **Yes:** every invariant an aggregate guards, its state machine, its failure cases and their error codes, money arithmetic and rounding.
- **No:** anything that needs a database, a clock read, HTTP, or a mock of a repository. If your domain test needs a mock, the rule is probably in the wrong layer.
- **Shape:** arrange an aggregate, call one guarded method, assert the state or the thrown domain exception. `DomainExceptionCodeTests` keeps every exception's code stable, because clients branch on those codes.

### Application tests

- **Yes:** the handler's orchestration — what it loads, which domain method it calls, what it saves; permission and ownership decisions (another customer's id must not be readable); validators; pipeline behaviours.
- **No:** SQL, real providers, or asserting what the aggregate already proves in a Domain test.
- **Doubles:** `tests/Souq.Application.Tests/TestDoubles` holds the shared ones — `FixedClock` for time, `TestCurrentUser` for identity, `TestUnitOfWork`, `TestTenant`, `TestCatalog`, `TestShipping`. NSubstitute covers the rest. Prefer a real aggregate over a mocked one: mocking a domain object usually hides the rule you meant to test.

### Architecture tests

These are executable architecture. They fail the build when a boundary moves without a decision:

| File | Guards |
|---|---|
| `tests/Souq.ArchitectureTests/DependencyRuleTests.cs` | The layer rule, thin controllers, no entity setters, handlers only in Application |
| `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs` | Module contracts and cycles, no entities in contracts, no `IQueryable` leaks, platform requests audited, no client-bound `TenantId` |
| `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` | Tenant ownership, query filters, composite keys, the single reviewed filter bypass, no raw SQL, and bulk writes only in reviewed places |
| `tests/Souq.ArchitectureTests/ClockRuleTests.cs` | No direct clock reads anywhere (IL scan) |
| `tests/Souq.ArchitectureTests/EndpointRuleTests.cs` | Every endpoint declares its access; platform endpoints need a platform permission; one handler per request |
| `tests/Souq.ArchitectureTests/WhiteLabelSourceTests.cs` | No brand or currency literal in product code or committed configuration |
| `tests/Souq.ArchitectureTests/DocumentationTests.cs` | Documentation links, anchors, paths, code names, and ADR completeness |
| `tests/Souq.ArchitectureTests/GeneratedDocsTests.cs` | The generated inventories match the code, including the cross-module dependency ratchet |

When one of these fails, the question is never "how do I silence it?" but "is this crossing a decision I am prepared to write down?".

### Integration tests

- **Yes:** the endpoint as a client sees it — status codes and error codes, authorization matrices, **cross-tenant access (another store's id must answer 404)**, concurrency under real `rowversion`, migrations against realistic data, background work (outbox dispatch, expiry sweeps), query projections and N+1 behaviour, startup configuration rules.
- **No:** rules that a Domain test can prove faster.
- **Infrastructure:** `tests/Souq.IntegrationTests/Infrastructure/SouqApiFactory.cs` boots the real API (`Program.cs`, middleware, migrations, seeding) against SQL Server 2022 in a throwaway Testcontainers container, one container per run. `TestApi` builds clients for a given host (`TestApi.ForStore`), signs accounts in, and accepts invitations. `Capturing.cs` holds `CapturingEmailSender` and `CapturingLoggerProvider`, which is how "no token in the logs" is asserted.
- **Determinism:** the factory runs in the `Testing` environment, disables the background sweeps and the outbox dispatcher (tests call `DispatchNotificationsAsync` explicitly), raises rate limits, and injects fixed secrets. Tests create their own data with unique values instead of resetting the database, so they can run in one collection without fighting each other.

### Frontend tests

- **Yes, pure logic:** request payload builders, query strings, view models, money and date formatting, the tenant model, search routing, page metadata, translation-key presence, and the white-label source rule (`frontend/src/whiteLabel.test.js`).
- **Yes, components (Phase 16):** route guards, the error boundary, the account shell and the order screens. The environment is per file — a test opts into a DOM with `// @vitest-environment jsdom`, so pure-logic tests keep running in Node. `globals: false` means Testing Library's automatic cleanup is not registered for us, so `frontend/src/test/setup.js` registers `afterEach(cleanup)` itself; without it a second render in the same file finds two copies of everything.
- **Source invariants:** `frontend/src/app/moduleInvariants.test.js` asserts that no source file is empty and that every module behind a `lazy()` import has a default export. It was written after finding two routed pages whose files were empty in the repository — a state that builds cleanly and fails only in the visitor's browser.
- **Lint as a test:** `npm run lint` (ESLint 9, flat config) runs before the suite in CI. Three rule families only — `react-hooks`, `jsx-a11y`, and the language basics. Formatting is deliberately not linted. The first thing it caught was a JSX parse error that Vite compiled without complaint.
  `react-hooks/set-state-in-effect` is a **warning**, not an error: its hits (19 when last counted, on 2026-09-17; 12 of them in the admin screens) are exactly the hand-rolled fetch pattern the query layer replaces, so they should disappear by migration rather than by inline disables.
- **Accessibility as a test:** `frontend/src/a11y.test.jsx` runs axe-core over the cart (full and empty), the 404 page, the hero, an open drawer, the account shell, the order list and the error screen, at WCAG 2.0/2.1 A and AA. It is a **structural** check: jsdom has no layout, so colour contrast and real focus order cannot be asserted there and the contrast rule is explicitly disabled rather than silently skipped. Any violation it reports is real; a clean run does not mean the page is accessible.
  axe-core is MPL-2.0, not MIT. That is acceptable because it is a development dependency that is neither shipped nor modified — recorded explicitly because this repository has been caught twice by licence changes (P-02, MediatR pinned at 12).
- **Browser validation (Playwright, run by hand against a live stack).** `frontend/e2e/` holds 77 journeys in 9 files: the storefront's critical journeys, two-store isolation, the store administration and back office, merchant product options and variants, platform provisioning, sessions, and phone-width layouts. It needs a SQL Server reachable from the host (the compose `db` service publishes no port, so it cannot serve a locally run API), the API in Development on port 5200 with its log written to a file, and `npm run dev`; files run one at a time about a minute apart because of the sign-in rate limit. The full runbook — `SOUQ_API_LOG`, the second store, the `phone` project, and the data a run leaves behind — is in [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md). It is **not** in CI or in `scripts/release-gate.sh`: it needs SQL Server, the API and a second provisioned tenant, and a browser suite that is green only when someone remembers to set that up is worse than one that is run deliberately before a release.
  It earns its keep. It found the session defect behind SEC-SESS-01 — a defect no unit test could have seen, because it needed a real rate limiter, a real cookie and ten real page loads.
- **Checkout's money barriers are covered** (`frontend/src/pages/checkout/Checkout.test.jsx`): no order while a line is unavailable, without an address, or with a required shipping method unchosen; the order is built from the basket, the total shown is the server's, and only a coupon the server accepted is sent.
- **Not today:** form-level interaction (the address, profile and review forms), most admin screens, and the providers. A gap in coverage now, not a missing environment.

## 3. The gate: what must pass before a change is complete

```bash
dotnet build                                        # no warnings
dotnet test tests/Souq.Domain.Tests
dotnet test tests/Souq.Application.Tests
dotnet test tests/Souq.ArchitectureTests            # boundaries + documentation
dotnet test tests/Souq.IntegrationTests             # needs Docker
cd frontend && npm run lint && npm run typecheck && npm test && npm run build
```

Which of these each gate requires, and the Playwright runbook for a release candidate: [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md).

Plus, when the change touched a controller, a use case, a module boundary or test files:

```bash
SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"
```

A change that touches money, stock, permissions or tenancy without an integration test is not complete.

**Docker memory, measured rather than estimated.** A single idle SQL Server container of the image this suite
uses holds **1.085 GiB**, and the suite pushes it higher: several tests create their own databases on that
server (migration rehearsal and rollback, seed safety, the best-selling measurement) so peak demand is well
above steady state. **Give Docker at least 3 GiB, and 4 GiB is comfortable.**

The failure is unmistakable once you have seen it and baffling before: SQL Server reports error 701, a
pre-login handshake failure, or `FAIL_PAGE_ALLOCATION`, and dozens of tests fail at once while each passes
alone. **Observed on this machine:** an unrelated build container holding 1.2 GiB of a 2.842 GiB allocation
took the suite from 270 passing to 151 failing; the same suite passed completely once that container exited.
Check with `docker stats --no-stream` before reading the failures as a regression, and never stop containers
you did not start ([Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) §2).

**Do not "fix" this by disabling parallelism or trimming suites.** The tests are not the defect — the machine
is short of memory, and the same suite passes on a machine that has it.

## 4. Writing a good test here

1. **Test at the level of the rule.** A domain invariant gets a domain test. Proving it only through HTTP makes the failure message useless and the suite slow.
2. **Name the behaviour, not the method.** Test names in this repository are Arabic sentences describing the rule; keep that voice.
3. **One reason to fail per test.** A test that asserts six things tells you little when it breaks.
4. **Test the failure path.** The error code a client branches on is part of the contract.
5. **Never assert on a message string** where a code exists.
6. **Cross-tenant first.** For any new endpoint that takes an id, the first integration test should be "another store's id answers 404".
7. **Fix the cause, not the assertion.** If an existing test now fails, either your change is wrong or the decision changed — and then the test changes deliberately, with the reason in the commit message.

## 5. Known gaps

Honest list; each is a candidate for [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md).

| Gap | Consequence |
|---|---|
| CI does not block merges yet, and Playwright is not in it | Until branch protection is on, a red run can be merged; the browser journeys run only when someone runs them |
| Partial frontend component tests | Guards, the error boundary, the account shell, the order screens and checkout's money barriers are covered; form-level interaction and most admin screens are not |
| Stripe and the email providers have no adapter tests | Their behaviour is proven only through the fake gateway and the capturing sender |
| `MigrationRehearsalTests` is one large test | A failure cannot be bisected to a phase |
| No load or performance test | Scaling decisions have no baseline ([ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md)) |
| No outbox purge / lease-expiry / concurrent-dispatcher test | The recovery paths of the outbox are unproven |
| Some commands have no validator, and some rules exist in only one layer | See the gaps section of [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) |
