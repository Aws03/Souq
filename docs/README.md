# Souq documentation

> The repository is the primary source of engineering knowledge for this product. **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`. These documents explain **what** the system does, **why** it is built this way, and **how** to change it safely. They are tested: links, paths, code names and ADR structure are checked by `DocumentationTests`, and four inventories are generated from the code itself.

## Start here

**The numbered path:** [LearningPath.md](00-START-HERE/LearningPath.md) answers *"where do I start, and what do I read next?"* in nineteen steps, from 00 (what is this?) to 18 (handoff). Each step is marked with a learning level (L0 orientation → L3 architectural), and names the document to read, the code to open, the tests that prove it and the decision behind it.

| If you are | Read |
|---|---|
| New to the project | [LearningPath.md](00-START-HERE/LearningPath.md), steps 00 → 12 over your first week |
| About to write code | [AGENTS.md](../AGENTS.md) → [CriticalInvariants.md](00-START-HERE/CriticalInvariants.md) → [HowToAddAFeature.md](00-START-HERE/HowToAddAFeature.md) |
| Trying to understand how a feature works | [RequestLifecycle.md](00-START-HERE/RequestLifecycle.md) → [HowToReadTheCode.md](00-START-HERE/HowToReadTheCode.md) |
| Changing existing behaviour | [HowToChangeExistingCode.md](00-START-HERE/HowToChangeExistingCode.md) + the module's change guide |
| Reviewing a change | [CodeReviewGuide.md](00-START-HERE/CodeReviewGuide.md) |
| Taking over the system | [HandoffGuide.md](00-START-HERE/HandoffGuide.md) + [HandoffChecklist.md](00-START-HERE/HandoffChecklist.md) |
| Asking "why is it like this?" | [WhyItIsBuiltThisWay.md](00-START-HERE/WhyItIsBuiltThisWay.md) |
| Learning software engineering from a real codebase | [LearningPath.md](00-START-HERE/LearningPath.md) and its concept index |
| An AI agent | [AIHandoff.md](00-START-HERE/AIHandoff.md) |
| Looking for a word's meaning | [Glossary.md](00-START-HERE/Glossary.md) |
| Looking for a file | [RepositoryMap.md](00-START-HERE/RepositoryMap.md) |

## The map

The folder numbers below group documents **by purpose** (01 requirements … 12 roadmap); they are not a reading order. The reading order is the step numbers in [LearningPath.md](00-START-HERE/LearningPath.md), which point into these folders.

### 00 · Start here
- **Orientation:** [SystemOverview](00-START-HERE/SystemOverview.md) · [ProjectMap](00-START-HERE/ProjectMap.md) (the whole system and its module dependencies on one page) · [LearningPath](00-START-HERE/LearningPath.md) (the numbered reading order) · [HowToReadThisRepository](00-START-HERE/HowToReadThisRepository.md) (reading paths by situation).
- **Understanding the code:** [RequestLifecycle](00-START-HERE/RequestLifecycle.md) (one request, every layer) · [HowToReadTheCode](00-START-HERE/HowToReadTheCode.md) (the tracing method, three traces) · [EngineeringMentalModel](00-START-HERE/EngineeringMentalModel.md) (where each kind of logic belongs) · [WhyItIsBuiltThisWay](00-START-HERE/WhyItIsBuiltThisWay.md) (the decisions, indexed by question).
- **Changing the code:** [CriticalInvariants](00-START-HERE/CriticalInvariants.md) (what must not break, and what enforces it) · [HowToAddAFeature](00-START-HERE/HowToAddAFeature.md) (build your first feature, walked through a real one) · [HowToChangeExistingCode](00-START-HERE/HowToChangeExistingCode.md) · [CodeReviewGuide](00-START-HERE/CodeReviewGuide.md).
- **Handoff and reference:** [HandoffGuide](00-START-HERE/HandoffGuide.md) · [HandoffChecklist](00-START-HERE/HandoffChecklist.md) · [AIHandoff](00-START-HERE/AIHandoff.md) · [Glossary](00-START-HERE/Glossary.md) · [RepositoryMap](00-START-HERE/RepositoryMap.md).

### 01 · Requirements
[BusinessRules](01-REQUIREMENTS/BusinessRules.md) — every rule the system enforces, with where it lives and which test proves it.

### 02 · Architecture
[Architecture](02-ARCHITECTURE/Architecture.md) (the target) · [ArchitectureEvaluation](02-ARCHITECTURE/ArchitectureEvaluation.md) (why this and not the alternatives) · [ModuleBoundaries](02-ARCHITECTURE/ModuleBoundaries.md) (who owns what) · [ModuleBoundaryAudit](02-ARCHITECTURE/ModuleBoundaryAudit.md) (every crossing, classified) · [DependencyRules](02-ARCHITECTURE/DependencyRules.md) (what may reference what) · [MultiTenancy](02-ARCHITECTURE/MultiTenancy.md) · [CQRS](02-ARCHITECTURE/CQRS.md) · [Events](02-ARCHITECTURE/Events.md) · [ExplicitNonGoals](02-ARCHITECTURE/ExplicitNonGoals.md) (what we deliberately do not use) · [RiskRegister](02-ARCHITECTURE/RiskRegister.md) · [ModuleDomainDependencies](02-ARCHITECTURE/ModuleDomainDependencies.md) *(generated)*

### 03 · Domain
[DDD](03-DOMAIN/DDD.md) — the aggregates, and where the modelling is deliberately lightweight.

### 04 · Modules
[Modules](04-MODULES/Modules.md) (the catalog) · [FeatureMaps](04-MODULES/FeatureMaps.md) (capabilities end to end) · [UseCases](04-MODULES/UseCases.md) *(generated)*

One folder per module, each with a `README.md` and most with a `ChangeGuide.md`:
[Platform](04-MODULES/Platform/README.md) · [Identity](04-MODULES/Identity/README.md) · [Catalog](04-MODULES/Catalog/README.md) · [Inventory](04-MODULES/Inventory/README.md) · [Customers](04-MODULES/Customers/README.md) · [Shopping](04-MODULES/Shopping/README.md) · [Ordering](04-MODULES/Ordering/README.md) · [Payments](04-MODULES/Payments/README.md) · [Promotions](04-MODULES/Promotions/README.md) · [Shipping](04-MODULES/Shipping/README.md) · [Reviews](04-MODULES/Reviews/README.md) · [Notifications](04-MODULES/Notifications/README.md) · [Reporting](04-MODULES/Reporting/README.md) (+ [Dashboards](04-MODULES/Reporting/Dashboards.md): the three dashboards, what every metric means, and what is deliberately not measured)

### 05 · API
[ApiDocumentation](05-API/ApiDocumentation.md) (conventions, errors, paging) · [Endpoints](05-API/Endpoints.md) *(generated: every endpoint with its permission, host, module flag and use case)*

### 06 · Database
[DatabaseDesign](06-DATABASE/DatabaseDesign.md) · [OwnershipMap](06-DATABASE/OwnershipMap.md) (who owns each table) · [Migrations](06-DATABASE/Migrations.md)

### 07 · Security
[Security](07-SECURITY/Security.md) (assets, threats, controls) · [DatabasePrivileges](07-SECURITY/DatabasePrivileges.md) (the three database identities, measured) · [AuthenticationAndAuthorization](07-SECURITY/AuthenticationAndAuthorization.md) · [SecurityControls](07-SECURITY/SecurityControls.md) (control → implementation → test → gap)

### 08 · Frontend
[FrontendGuide](08-FRONTEND/FrontendGuide.md) (the practical guide) · [FrontendArchitecture](08-FRONTEND/FrontendArchitecture.md) (structure and the migration plan) · [WhiteLabel](08-FRONTEND/WhiteLabel.md) (how one build serves every store) · [DesignSystem](08-FRONTEND/DesignSystem.md) (the visual language: tokens, both modes, motion, product presentation, right-to-left, accessibility, the performance budget)

### 09 · Operations
[DevelopmentGuide](09-OPERATIONS/DevelopmentGuide.md) (run it locally) · [Configuration](09-OPERATIONS/Configuration.md) (every setting) · [Deployment](09-OPERATIONS/Deployment.md) · [SeedAndBootstrap](09-OPERATIONS/SeedAndBootstrap.md) (what a fresh database gets, and the first-run bootstrap) · [BackupAndRestore](09-OPERATIONS/BackupAndRestore.md) (the contract, the runbook, the drill) · [Troubleshooting](09-OPERATIONS/Troubleshooting.md) (named symptoms) · [IncidentResponse](09-OPERATIONS/IncidentResponse.md) (what to do while it is broken) · [ScalingStrategy](09-OPERATIONS/ScalingStrategy.md) · [ReleaseReadiness](09-OPERATIONS/ReleaseReadiness.md) (what stops a release, triaged) · [OwnerDecisions](09-OPERATIONS/OwnerDecisions.md) (what only the owner can answer) · [ProductionReleaseChecklist](09-OPERATIONS/ProductionReleaseChecklist.md) · [DeveloperQualityGates](09-OPERATIONS/DeveloperQualityGates.md) (the commands and gates — canonical, what CI mirrors, and the browser-journey runbook)

### 10 · Testing
[TestingStrategy](10-TESTING/TestingStrategy.md) (what each suite is for and the gate) · [Traceability](10-TESTING/Traceability.md) (capability → tests) · [TestInventory](10-TESTING/TestInventory.md) *(generated)*

### 11 · Decisions
[ADR index](11-ADR/README.md) — every record, grouped by area, with what later decisions changed and which statements have since drifted.

### 12 · Roadmap
[ProductRoadmap](12-ROADMAP/ProductRoadmap.md) (phases, status, the decision log) · [TechnicalDebt](12-ROADMAP/TechnicalDebt.md) · [SouqMasterPlan](12-ROADMAP/SouqMasterPlan.md) (the durable execution contract for the twenty engineering-mission phases — `M1`–`M20` — that carry the product from here to launch readiness; a fresh Claude session recovers exactly where things stand from its §0 status block alone)

### archive
Historical snapshots, kept for context and not maintained: the Phase 0 architecture assessment, the audit of the earlier single-store program, the original Arabic README, and an Arabic engineering-thinking essay.

## Conventions in these documents

- **Backticks mean it exists** in the repository — a path, a type, a member, a route, a configuration key. Planned or hypothetical names are written in *italics*. A test enforces this.
- **Status labels:** unlabelled statements are current. Otherwise **PLANNED** (in the roadmap), **DEFERRED** (postponed, with a reason), **FUTURE** (an option), **DEPRECATED**.
- **Generated files** (`Endpoints.md`, `UseCases.md`, `TestInventory.md`, `ModuleDomainDependencies.md`) are produced from the code. Never edit them by hand:
  `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
- **Decisions belong in ADRs**, not in prose: if you change how the system is built, add a record.
- **"Last verified against the code"** at the top of a navigation page gives the date and branch at which someone checked its claims by reading the code, not the date it was last edited. A page is only as current as that line. Links, anchors, backticked paths and backticked code names are checked mechanically on every test run (`DocumentationTests`); prose claims are not, so re-verify them when you rely on one.
- **Learning levels** (L0–L3) mark when a document becomes useful, not how important it is: see [LearningPath.md](00-START-HERE/LearningPath.md).
- **One canonical source per topic.** Navigation pages link rather than copy. The canonical homes of the topics most often copied: commands and gates, [DeveloperQualityGates.md](09-OPERATIONS/DeveloperQualityGates.md); roadmap status, [ProductRoadmap.md](12-ROADMAP/ProductRoadmap.md); owner decisions, [OwnerDecisions.md](09-OPERATIONS/OwnerDecisions.md); release blockers, [ReleaseReadiness.md](09-OPERATIONS/ReleaseReadiness.md); security controls, [SecurityControls.md](07-SECURITY/SecurityControls.md).
