# The Souq learning path

> **What this page is:** the numbered answer to *"where do I start, and what do I read next?"* Nineteen steps, from "what is this system?" to "how do I hand it over?". Each step names the one or two documents to read, the code to open next to them, the tests that prove it, the decision behind it, and the questions you should be able to answer before moving on.
> **Who it serves:** a new engineer joining the team, a team taking the project over, and a developer learning how production concepts look in a real codebase. The three differ only in [which steps they read first](#three-ways-through-the-path).
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`. Every link and backticked path on this page is checked by `DocumentationTests`; the claims are checked by reading the code, not by editing the page.

## How the path works

**Levels.** Each step is marked with the level at which it becomes useful. Don't read an L3 step before you can answer the L1 questions: the advanced material assumes the basic request flow.

| Level | Meaning | You can… |
|---|---|---|
| **L0** | Orientation | say what Souq is, run it, and find your way around |
| **L1** | Beginner developer | follow one request through every layer and say where a rule belongs |
| **L2** | Working developer | change or add a feature safely, with its tests and documents |
| **L3** | Advanced / architectural | reason about isolation, operations, trade-offs and decisions |

**One canonical source per topic.** This page links; it doesn't restate. If a step and the document it points to disagree, the document wins, and this page is wrong.

**The loop inside every step:** read the document → open the code it names → read one test that proves it → answer the questions. The tests matter most: they are the executable part of the documentation.

---

## 00 — Start here · L0

**Read:** [SystemOverview.md](SystemOverview.md), then [ProjectMap.md](ProjectMap.md).
**Open:** `README.md` (the front page) and `AGENTS.md` §0 (the rules that override everything).
**You can now answer:** What does Souq sell, and to whom? What are a store, a platform owner and a customer? Why is there one API, one database and one frontend build for many stores? Which of the thirteen modules would own "orders"?
**Next:** step 01.

## 01 — Run it · L0

**Read:** [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md) §1–§4, then [SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md) for what a fresh database contains and the Development-only demo accounts.
**Open:** `src/Souq.API/Program.cs` (the composition root: what starts, in which order, and what refuses to start) and `frontend/vite.config.js` (why `{slug}.localhost:5173` reaches a different store).
**Try:** sign in to the demo store's `/admin` on `localhost:5173`, then to the platform console on `admin.localhost:5173`. Place an order with the fake gateway and watch its status in the admin.
**You can now answer:** Which host serves which store? Why does the API refuse to start without an email or payment provider? Where do secrets live locally?
**Next:** step 02.

## 02 — Project structure · L0

**Read:** [RepositoryMap.md](RepositoryMap.md) (what lives where) and the folder table in [HowToReadThisRepository.md](HowToReadThisRepository.md). Keep [Glossary.md](Glossary.md) open.
**Open:** the four projects `src/Souq.Domain`, `src/Souq.Application`, `src/Souq.Infrastructure`, `src/Souq.API`, and `frontend/src`.
**You can now answer:** Where are entities, use cases, EF configurations, controllers and React pages? Why is a module a folder and a namespace rather than a project ([ADR-0002](../11-ADR/0002-modular-monolith-structure.md))?
**Next:** step 03.

## 03 — Architecture fundamentals · L1

**Read:** [EngineeringMentalModel.md](EngineeringMentalModel.md) §1–§4 (where each kind of logic belongs), then [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md).
**Open:** `tests/Souq.ArchitectureTests/DependencyRuleTests.cs`. The layer rule is a test, not advice.
**Why:** [ADR-0001](../11-ADR/0001-target-architecture.md) (modular monolith), [ADR-0003](../11-ADR/0003-clean-hexagonal-boundaries.md) (ports and adapters). The short version: [WhyItIsBuiltThisWay.md](WhyItIsBuiltThisWay.md).
**You can now answer:** Why may `Souq.Domain` reference nothing? Where does a rule go if it would survive replacing the database and the payment provider? What stops a handler from using EF Core directly?
**Next:** step 04.

## 04 — The life of a request · L1

**Read:** [RequestLifecycle.md](RequestLifecycle.md): one real request, a staff member shipping an order, followed from the button to SQL Server and back. Then [HowToReadTheCode.md](HowToReadTheCode.md) to learn to do the same tracing yourself, with three more examples (sign-in, the storefront catalogue, suspending a store).
**Open:** every file those two pages name, in their order.
**You can now answer:** Which three decisions are taken before any business code runs? Where is the permission checked, and where is the business rule checked? Why is a refused transition a `422` and a missing order a `404`? Which part of that request leaves the system after the commit, and how?
**Next:** step 05.

## 05 — Domain and business rules · L1 → L2

**Read:** [DDD.md](../03-DOMAIN/DDD.md) (the aggregates, and where modelling is deliberately light), then skim [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) by section: every rule the system enforces, where it lives and which test proves it.
**Open:** `src/Souq.Domain/Entities/Order.cs` and `src/Souq.Domain/Entities/OrderTransitions.cs` (a state machine), `src/Souq.Domain/Entities/InventoryItem.cs` (stock that never goes negative), `src/Souq.Domain/ValueObjects/Money.cs` (currency-aware rounding).
**Prove it:** `tests/Souq.Domain.Tests/OrderLifecycleTests.cs`.
**Why:** [ADR-0009](../11-ADR/0009-ddd-usage.md), [ADR-0014](../11-ADR/0014-money-precision.md), [ADR-0029](../11-ADR/0029-orders-lifecycle.md).
**You can now answer:** Why does `Order` have no public setters? Which rules need a lookup and therefore cannot live in an aggregate? Where would "a coupon may not be deleted once used" go, and where does it actually live?
**Next:** step 06.

## 06 — Use cases: the Application layer · L2

**Read:** [CQRS.md](../02-ARCHITECTURE/CQRS.md) (commands through aggregates, reads through query services) and [Events.md](../02-ARCHITECTURE/Events.md) (domain events and the outbox).
**Open:** `src/Souq.Application/Features/Orders/Commands/UpdateOrderStatusCommand.cs` (orchestration across modules through contracts), `src/Souq.Application/Common/Behaviors` (the logging, validation and audit pipeline), `src/Souq.Application/Features/Inventory/Contracts` (how one module asks another).
**Prove it:** `tests/Souq.Application.Tests/Orders/UpdateOrderStatusHandlerTests.cs`.
**Why:** [ADR-0008](../11-ADR/0008-cqrs-strategy.md), [ADR-0021](../11-ADR/0021-transaction-boundaries.md), [ADR-0034](../11-ADR/0034-notifications-outbox.md).
**You can now answer:** What does a handler decide, and what does it leave to the aggregate? Why is the refund issued after the transaction commits and not inside it? What is the difference between a contract and a port?
**Next:** step 07.

## 07 — Infrastructure and the database · L2

**Read:** [DatabaseDesign.md](../06-DATABASE/DatabaseDesign.md), [OwnershipMap.md](../06-DATABASE/OwnershipMap.md) (who owns each table), [Migrations.md](../06-DATABASE/Migrations.md).
**Open:** `src/Souq.Infrastructure/Persistence/AppDbContext.cs` (the tenant query filter, and domain events captured into the outbox), `src/Souq.Infrastructure/Persistence/Interceptors/TenantWriteGuardInterceptor.cs` (the write guard), `src/Souq.Infrastructure/Persistence/Configurations`, `src/Souq.Infrastructure/Persistence/Queries/CatalogQueries.cs` (a read projected straight into DTOs), `src/Souq.Infrastructure/Migrations`.
**Prove it:** `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` runs against a real SQL Server in a container.
**Why:** [ADR-0007](../11-ADR/0007-database-strategy.md), [ADR-0013](../11-ADR/0013-optimistic-concurrency.md).
**You can now answer:** Why is `IQueryable` never returned outside Infrastructure? What happens when two people edit the same order at once? Why are data-moving migrations written by hand?
**Next:** step 08.

## 08 — The API, authentication and authorization · L2

**Read:** [ApiDocumentation.md](../05-API/ApiDocumentation.md) (conventions, the error contract, paging, UTC instants), then [AuthenticationAndAuthorization.md](../07-SECURITY/AuthenticationAndAuthorization.md). Look endpoints up in the generated [Endpoints.md](../05-API/Endpoints.md) rather than reading it.
**Open:** `src/Souq.API/Controllers/AuthController.cs`, `src/Souq.API/Security/PermissionAuthorization.cs`, `src/Souq.Application/Common/Security/Permissions.cs` (which role grants which permission), `src/Souq.API/Middleware/GlobalExceptionHandler.cs`.
**Prove it:** `tests/Souq.IntegrationTests/AuthSessionTests.cs`, `tests/Souq.IntegrationTests/ErrorContractTests.cs`, `tests/Souq.ArchitectureTests/EndpointRuleTests.cs` (no endpoint without an explicit access decision).
**Why:** [ADR-0010](../11-ADR/0010-authentication-authorization.md), [ADR-0017](../11-ADR/0017-error-contract.md), [ADR-0019](../11-ADR/0019-authorization-foundation.md), [ADR-0023](../11-ADR/0023-sessions-and-credentials.md).
**You can now answer:** Why does the client branch on `code` and never on the message? Where does a refresh token live, and why can a script not read it? Why does another customer's order answer `404`, not `403`?
**Next:** step 09.

## 09 — Multi-tenancy and security · L2 → L3

**Read:** [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) (the four isolation mechanisms), then [SecurityControls.md](../07-SECURITY/SecurityControls.md) (every control → its implementation → its test → its gap). [Security.md](../07-SECURITY/Security.md) and [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md) when you need the threat model or the database identities.
**Open:** `src/Souq.API/Tenancy/TenantResolutionMiddleware.cs`, `src/Souq.API/Tenancy/TenantAvailability.cs`, `src/Souq.API/Security/AccessTokenValidation.cs` (a token only works on the host it was issued for), `src/Souq.Infrastructure/Persistence/Queries/PlatformQueries.cs` (the single reviewed cross-store read).
**Prove it:** `tests/Souq.IntegrationTests/TenantIsolationTests.cs` (every endpoint with a resource id, tried from another store), `tests/Souq.ArchitectureTests/TenancyRuleTests.cs`.
**Why:** [ADR-0005](../11-ADR/0005-multi-tenancy-model.md), [ADR-0006](../11-ADR/0006-tenant-resolution.md), [ADR-0022](../11-ADR/0022-tenancy-enforcement.md), [ADR-0024](../11-ADR/0024-platform-administration.md).
**You can now answer:** Why can no request type carry a `TenantId`? What would still stop a cross-store write if someone forgot the query filter? Why can the platform owner provision a store but not use its admin?
**Next:** step 10.

## 10 — The frontend · L2

**Read:** [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md) (the practical guide, including how to add a screen), [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) (one build, every store), [DesignSystem.md](../08-FRONTEND/DesignSystem.md) (tokens, both modes, right-to-left, accessibility). [FrontendArchitecture.md](../08-FRONTEND/FrontendArchitecture.md) is the history and the migration plan.
**Open:** `frontend/src/main.jsx` (provider order), `frontend/src/App.jsx` (the store and platform route trees), `frontend/src/app/TenantProvider.jsx` (the boot from the storefront configuration), `frontend/src/api/client.js` (every server call), `frontend/src/pages/platform/Accounts.jsx` (a reference screen: paged query, mutations, confirmation).
**Prove it:** `frontend/src/a11y.test.jsx`, `frontend/src/app/tenantModel.test.js`, and the browser journeys in `frontend/e2e`.
**Why:** [ADR-0035](../11-ADR/0035-white-label-runtime.md), [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md).
**You can now answer:** Why is a hidden button never a permission? Where does a store's currency, language and colour come from at runtime? How does one string work in Arabic and English without breaking punctuation?
**Next:** step 11.

## 11 — Modules and business capabilities · L2

**Read:** [Modules.md](../04-MODULES/Modules.md) (the thirteen capabilities), [FeatureMaps.md](../04-MODULES/FeatureMaps.md) (capabilities end to end), then the `README.md` of whichever module you are about to touch. [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) says what is enforced; [ModuleBoundaryAudit.md](../02-ARCHITECTURE/ModuleBoundaryAudit.md) (L3) classifies every existing crossing honestly.
**Open:** `tests/Souq.ArchitectureTests/ModuleMap.cs` (which folder belongs to which module) and the generated [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md).
**Why:** [ADR-0004](../11-ADR/0004-module-boundaries.md), and each module's ADR in the [index](../11-ADR/README.md).
**You can now answer:** Which module owns a store's payment account, and where do its use cases live? Why is checkout the place where seven modules meet? Which crossings exist today that the target architecture does not want?
**Next:** step 12.

## 12 — Testing · L2

**Read:** [TestingStrategy.md](../10-TESTING/TestingStrategy.md) (what each suite is for), [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md) (the commands, and what CI blocks on). Look things up in [Traceability.md](../10-TESTING/Traceability.md) and the generated [TestInventory.md](../10-TESTING/TestInventory.md).
**Open:** one test per suite: a domain test, an application handler test, `tests/Souq.ArchitectureTests/DocumentationTests.cs`, an integration test, a Vitest page test (`frontend/src/pages/platform/Audit.test.jsx`), a browser journey (`frontend/e2e/back-office.spec.js`).
**Why:** [ADR-0015](../11-ADR/0015-testing-strategy.md).
**You can now answer:** Which suite should prove a new invariant, an authorization rule, a cross-store refusal, a translated label? Why do integration tests use a real SQL Server? Why do browser journeys run a minute apart?
**Next:** step 13.

## 13 — Operations, deployment and release · L3

**Read:** [Deployment.md](../09-OPERATIONS/Deployment.md), [Configuration.md](../09-OPERATIONS/Configuration.md) (every setting), [BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md), then [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) (what stops a release, triaged) and [ProductionReleaseChecklist.md](../09-OPERATIONS/ProductionReleaseChecklist.md).
**Open:** `docker-compose.yml`, `src/Souq.API/Dockerfile`, `frontend/nginx.conf`, `scripts/release-gate.sh`, `.github/workflows/ci.yml`.
**Why:** [ADR-0018](../11-ADR/0018-observability.md), [ADR-0020](../11-ADR/0020-configuration-and-secrets.md), [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md).
**You can now answer:** Which release blockers can engineering close, and which only the owner can? What does the release gate check, and what does "skipped" mean? What happens to stores' payment keys if the secrets key is lost?
**Next:** step 14.

## 14 — Decisions: why it is built this way · L3

**Read:** [WhyItIsBuiltThisWay.md](WhyItIsBuiltThisWay.md) (the important "why" questions, each answered in a few lines and linked to its record), then the [ADR index](../11-ADR/README.md) and [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) (what was rejected, and what evidence would change the answer).
**You can now answer:** Why not microservices, event sourcing or a database per store? What evidence would justify extracting a module? Why is the storefront preview waiting for the owner instead of being built?
**Next:** step 15.

## 15 — Roadmap and current state · L1 (status) · L3 (depth)

**Read:** the status line and the Phase 16–18 sections of [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md), its decision log (§7), [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) and [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md).
**State at the last verification:**
- Phases 1A–15 are complete.
- Phase 16 (Storefront) is 🟡: product variants can't be chosen yet. P-08 is decided, and the groundwork (V1) and the option model with its merchant admin (V2) are built; the storefront selection (V3) remains ([ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md)).
- Phase 17 (Tenant admin dashboard) is ✅.
- Phase 18 (Platform owner dashboard) is 🟡, waiting only on owner decisions D-22 (storefront preview) and P-07 (platform-wide settings).

The roadmap is the authority, not this summary.
**You can now answer:** What is unfinished, and is it blocked by engineering or by a decision? Which risks must be closed before a first paying customer?
**Next:** step 16.

## 16 — Changing Souq safely · L2

**Read, in this order:**
1. [CriticalInvariants.md](CriticalInvariants.md): what you must not break, and where each rule is enforced.
2. [HowToAddAFeature.md](HowToAddAFeature.md): building a feature, walked through a real one.
3. [HowToChangeExistingCode.md](HowToChangeExistingCode.md): changing behaviour that already exists.
4. The module's `ChangeGuide.md`.
5. [CodeReviewGuide.md](CodeReviewGuide.md) on your own diff.

**Before you start:** `AGENTS.md` §6 (inspect) and §9 (when to stop and ask).
**You can now answer:** Which tests must a feature that touches money, stock or personal data have? When does a change need an ADR? What do you do when the change needs a business rule nobody has written down?
**Next:** step 17.

## 17 — Troubleshooting · L2

**Read:** [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) (named symptoms and their causes), [IncidentResponse.md](../09-OPERATIONS/IncidentResponse.md) (what to do while production is broken), and Path E in [HowToReadThisRepository.md](HowToReadThisRepository.md#path-e--debugging-a-production-problem).
**Open:** `src/Souq.API/Observability` (the correlation id on every response and log line) and `src/Souq.API/Http/ProblemDetailsConventions.cs` (status → stable code).
**You can now answer:** A customer reports an error: which one value do you ask for first? Why does a store answer `503` on every page? Why do integration tests fail with SQL error 701 on a busy machine?
**Next:** step 18.

## 18 — Handoff and reference · L3

**Read:** [HandoffGuide.md](HandoffGuide.md) (taking over the system), [HandoffChecklist.md](HandoffChecklist.md) (the practical checklist), [AIHandoff.md](AIHandoff.md) (for AI agents working here).
**Reference, looked up rather than read:** [Glossary.md](Glossary.md), [RepositoryMap.md](RepositoryMap.md), [Endpoints.md](../05-API/Endpoints.md), [UseCases.md](../04-MODULES/UseCases.md), [TestInventory.md](../10-TESTING/TestInventory.md), [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md).
**You can now answer:** What must be obtained from the previous owner that the repository cannot give you? Where do you continue?

---

## Three ways through the path

| You are | Read | Then |
|---|---|---|
| **A new engineer** | 00 → 12 in order, over your first week; 16 before your first change | 13–18 as your work reaches them |
| **Taking the project over** | 00, 01, 15, 18, then 13 and 09 | 03–04 and 14 to understand what you inherited; the rest when you change something |
| **Learning software engineering from a real project** | 00 → 11 slowly, with the code open; use the concept index below to jump to a technique | 14, then read two ADRs end to end and argue with them |

## Concept index: where each technique appears in Souq

If you are here to learn, start from a concept and go to the real code rather than an abstract explanation.

| Concept | Where it is explained | Where it is in the code |
|---|---|---|
| Clean / hexagonal architecture | [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md), [EngineeringMentalModel.md](EngineeringMentalModel.md) | `tests/Souq.ArchitectureTests/DependencyRuleTests.cs`, `src/Souq.Application/Common/Interfaces` |
| Modular monolith | [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md), [ADR-0001](../11-ADR/0001-target-architecture.md) | `tests/Souq.ArchitectureTests/ModuleMap.cs`, `src/Souq.Application/Features` |
| Aggregate and invariant | [DDD.md](../03-DOMAIN/DDD.md) | `src/Souq.Domain/Entities/Order.cs`, `src/Souq.Domain/Entities/InventoryItem.cs` |
| Value object | [DDD.md](../03-DOMAIN/DDD.md) | `src/Souq.Domain/ValueObjects/Money.cs` |
| State machine | [Ordering module](../04-MODULES/Ordering/README.md) | `src/Souq.Domain/Entities/OrderTransitions.cs` |
| CQRS (selective) | [CQRS.md](../02-ARCHITECTURE/CQRS.md) | `src/Souq.Application/Features/Products/Queries/ICatalogQueries.cs`, `src/Souq.Infrastructure/Persistence/Queries/CatalogQueries.cs` |
| Pipeline behaviours (cross-cutting concerns) | [CQRS.md](../02-ARCHITECTURE/CQRS.md) | `src/Souq.Application/Common/Behaviors` |
| Domain events and the transactional outbox | [Events.md](../02-ARCHITECTURE/Events.md) | `src/Souq.Infrastructure/Persistence/Outbox`, `src/Souq.Infrastructure/BackgroundJobs/OutboxDispatcherService.cs` |
| Multi-tenancy (shared database) | [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) | `src/Souq.Infrastructure/Persistence/AppDbContext.cs`, `src/Souq.API/Tenancy` |
| Permission-based authorization | [AuthenticationAndAuthorization.md](../07-SECURITY/AuthenticationAndAuthorization.md) | `src/Souq.Application/Common/Security/Permissions.cs`, `src/Souq.API/Security/PermissionAuthorization.cs` |
| Refresh-token rotation and reuse detection | [ADR-0023](../11-ADR/0023-sessions-and-credentials.md) | `src/Souq.Application/Features/Auth`, `tests/Souq.IntegrationTests/AuthSessionTests.cs` |
| Optimistic concurrency | [ADR-0013](../11-ADR/0013-optimistic-concurrency.md) | `src/Souq.Infrastructure/Persistence/Configurations/PersistenceConventions.cs` |
| Idempotency | [ApiDocumentation.md](../05-API/ApiDocumentation.md), [ADR-0036](../11-ADR/0036-payment-intent-state-machine.md) | `src/Souq.Application/Features/Payments` |
| Money precision | [ADR-0014](../11-ADR/0014-money-precision.md) | `src/Souq.Domain/ValueObjects/Money.cs`, `src/Souq.Infrastructure/Services/StripeAmountConverter.cs` |
| Error contract (RFC 7807) | [ADR-0017](../11-ADR/0017-error-contract.md) | `src/Souq.API/Http/ProblemDetailsConventions.cs` |
| Rate limiting | [SecurityControls.md](../07-SECURITY/SecurityControls.md) | `src/Souq.API/Security/RateLimiting.cs` |
| Audit trail | [Platform module](../04-MODULES/Platform/README.md) | `src/Souq.Application/Common/Behaviors/AuditBehavior.cs`, `frontend/src/pages/platform/Audit.jsx` |
| Feature flags per tenant | [ADR-0024](../11-ADR/0024-platform-administration.md) | `src/Souq.Domain/Platform/StoreModules.cs`, `useModule` in `frontend/src/app/TenantProvider.jsx` |
| White-label theming at runtime | [WhiteLabel.md](../08-FRONTEND/WhiteLabel.md) | `frontend/src/app/tenantModel.js`, `frontend/src/app/storeTheme.js` |
| Server state in the browser | [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md), [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md) | `frontend/src/app/queryKeys.js`, `frontend/src/pages/platform/Accounts.jsx` |
| Internationalization and right-to-left | [DesignSystem.md](../08-FRONTEND/DesignSystem.md) | `frontend/src/i18n/index.js`, `frontend/src/i18n/locales` |
| Accessibility testing | [DesignSystem.md](../08-FRONTEND/DesignSystem.md) | `frontend/src/a11y.test.jsx`, `frontend/e2e/back-office.spec.js` |
| Architecture as executable tests | [TestingStrategy.md](../10-TESTING/TestingStrategy.md) | `tests/Souq.ArchitectureTests` |
| Documentation as tested code | this page | `tests/Souq.ArchitectureTests/DocumentationTests.cs` |
| Backup and restore drills | [BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md) | `scripts/backup.sh`, `scripts/rehearse-restore.sh` |
