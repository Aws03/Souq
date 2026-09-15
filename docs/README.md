# Souq documentation

> The repository is the primary source of engineering knowledge for this product. These documents explain **what** the system does, **why** it is built this way, and **how** to change it safely. They are tested: links, paths, code names and ADR structure are checked by `DocumentationTests`, and four inventories are generated from the code itself.

## Start here

| If you are | Read |
|---|---|
| New to the project | [SystemOverview.md](00-START-HERE/SystemOverview.md) → [HowToReadThisRepository.md](00-START-HERE/HowToReadThisRepository.md) |
| About to write code | [AGENTS.md](../AGENTS.md) → [EngineeringMentalModel.md](00-START-HERE/EngineeringMentalModel.md) → [HowToAddAFeature.md](00-START-HERE/HowToAddAFeature.md) |
| Changing existing behaviour | [HowToChangeExistingCode.md](00-START-HERE/HowToChangeExistingCode.md) + the module's change guide |
| Reviewing a change | [CodeReviewGuide.md](00-START-HERE/CodeReviewGuide.md) |
| Taking over the system | [HandoffGuide.md](00-START-HERE/HandoffGuide.md) |
| An AI agent | [AIHandoff.md](00-START-HERE/AIHandoff.md) |
| Looking for a word's meaning | [Glossary.md](00-START-HERE/Glossary.md) |
| Looking for a file | [RepositoryMap.md](00-START-HERE/RepositoryMap.md) |

## The map

### 00 · Start here
[SystemOverview](00-START-HERE/SystemOverview.md) · [HowToReadThisRepository](00-START-HERE/HowToReadThisRepository.md) · [EngineeringMentalModel](00-START-HERE/EngineeringMentalModel.md) · [HowToAddAFeature](00-START-HERE/HowToAddAFeature.md) · [HowToChangeExistingCode](00-START-HERE/HowToChangeExistingCode.md) · [CodeReviewGuide](00-START-HERE/CodeReviewGuide.md) · [HandoffGuide](00-START-HERE/HandoffGuide.md) · [AIHandoff](00-START-HERE/AIHandoff.md) · [Glossary](00-START-HERE/Glossary.md) · [RepositoryMap](00-START-HERE/RepositoryMap.md)

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
[DevelopmentGuide](09-OPERATIONS/DevelopmentGuide.md) (run it locally) · [Configuration](09-OPERATIONS/Configuration.md) (every setting) · [Deployment](09-OPERATIONS/Deployment.md) · [SeedAndBootstrap](09-OPERATIONS/SeedAndBootstrap.md) (what a fresh database gets, and the first-run bootstrap) · [BackupAndRestore](09-OPERATIONS/BackupAndRestore.md) (the contract, the runbook, the drill) · [Troubleshooting](09-OPERATIONS/Troubleshooting.md) (named symptoms) · [IncidentResponse](09-OPERATIONS/IncidentResponse.md) (what to do while it is broken) · [ScalingStrategy](09-OPERATIONS/ScalingStrategy.md) · [ReleaseReadiness](09-OPERATIONS/ReleaseReadiness.md) (what stops a release, triaged) · [OwnerDecisions](09-OPERATIONS/OwnerDecisions.md) (what only the owner can answer) · [ProductionReleaseChecklist](09-OPERATIONS/ProductionReleaseChecklist.md) · [DeveloperQualityGates](09-OPERATIONS/DeveloperQualityGates.md)

### 10 · Testing
[TestingStrategy](10-TESTING/TestingStrategy.md) (what each suite is for and the gate) · [Traceability](10-TESTING/Traceability.md) (capability → tests) · [TestInventory](10-TESTING/TestInventory.md) *(generated)*

### 11 · Decisions
[ADR index](11-ADR/README.md) — 35 records, grouped by area, with what later decisions changed and which statements have since drifted.

### 12 · Roadmap
[ProductRoadmap](12-ROADMAP/ProductRoadmap.md) (phases, status, the decision log) · [TechnicalDebt](12-ROADMAP/TechnicalDebt.md)

### archive
Historical snapshots, kept for context and not maintained: the Phase 0 architecture assessment, the audit of the earlier single-store program, the original Arabic README, and an Arabic engineering-thinking essay.

## Conventions in these documents

- **Backticks mean it exists** in the repository — a path, a type, a member, a route, a configuration key. Planned or hypothetical names are written in *italics*. A test enforces this.
- **Status labels:** unlabelled statements are current. Otherwise **PLANNED** (in the roadmap), **DEFERRED** (postponed, with a reason), **FUTURE** (an option), **DEPRECATED**.
- **Generated files** (`Endpoints.md`, `UseCases.md`, `TestInventory.md`, `ModuleDomainDependencies.md`) are produced from the code. Never edit them by hand:
  `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
- **Decisions belong in ADRs**, not in prose: if you change how the system is built, add a record.
