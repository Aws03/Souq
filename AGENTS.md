# AGENTS.md: the contract for anyone changing this repository

This file is the working contract for **AI agents and engineers** in the Souq repository. Read it before you change anything. It is short on purpose; every section links to the document that holds the detail.

**Souq** is a white-label, multi-tenant e-commerce SaaS: one ASP.NET Core API and one SQL Server database serve many stores, and one React build renders any store from that store's configuration. It is a commercial product, not a demo.

## 0. The rules that override everything else

1. **The repository is the source of truth.** Never assume a previous conversation, a chat summary, or your memory of this project. If a document disagrees with the code, the code wins — then fix the document in the same change.
2. **Never weaken a test to make it pass** — and never weaken the *control* the test protects. If a tenancy, authorization, validation or payment test fails, the control is working and your change is wrong. Deleting the assertion, widening the filter, relaxing the guard or adding an exemption to make a suite green is the single most damaging thing you can do here. A failing test is information: understand it, then fix the cause.
3. **Never weaken tenant isolation, authorization or payment safety** for convenience. These are the product's licence to exist ([SecurityControls.md](docs/07-SECURITY/SecurityControls.md)). **Payment behaviour is never changed silently:** amounts, capture, cancellation, refunds, retries and idempotency are commercial behaviour. Change them deliberately, with an ADR and tests, or not at all.
4. **Never invent a business rule.** If a rule is not in [BusinessRules.md](docs/01-REQUIREMENTS/BusinessRules.md), a test, or an ADR, it does not exist — find it or stop and ask (§9). Guessing a rule that touches pricing, discounts, refunds, limits or eligibility makes a commercial decision by accident, and it will look deliberate to everyone who reads it afterwards.
5. **Don't redesign the architecture** because another style is fashionable. The architecture and its rejected alternatives are recorded in [ADR-0001](docs/11-ADR/0001-target-architecture.md) and [ExplicitNonGoals.md](docs/02-ARCHITECTURE/ExplicitNonGoals.md). **Named non-goals — microservices, Kafka or any message broker, event sourcing, a database per module or per tenant, distributed transactions, Kubernetes, a second ORM, replacing SQL Server or React — may not be introduced at all** without an ADR that supersedes the recorded reason. "It would scale better" is not evidence; a measured limit is.
6. **Never make a destructive or irreversible database change** — dropping a column or table, narrowing a type, deleting rows, or any migration that loses data — without explicit approval from the owner. Write it, do not run it, and report what it would destroy (§9, [Migrations.md](docs/06-DATABASE/Migrations.md)).
7. **Stop and ask** when a decision is genuinely the owner's: see §9.
8. **Never read, print or commit `.env`** (the owner's real secrets). `.env.example` documents the variables.
9. **Never push or merge** unless you were explicitly asked to. Don't rewrite existing history.

## 1. Read this before you write code

| You are about to | Read first |
|---|---|
| Anything at all | [docs/00-START-HERE/SystemOverview.md](docs/00-START-HERE/SystemOverview.md), then the numbered [LearningPath.md](docs/00-START-HERE/LearningPath.md) |
| Work as an AI agent in this repo | [docs/00-START-HERE/AIHandoff.md](docs/00-START-HERE/AIHandoff.md) |
| Find your way around | [docs/00-START-HERE/HowToReadThisRepository.md](docs/00-START-HERE/HowToReadThisRepository.md) · [RepositoryMap.md](docs/00-START-HERE/RepositoryMap.md) |
| Trace a feature through the code | [HowToReadTheCode.md](docs/00-START-HERE/HowToReadTheCode.md) · [RequestLifecycle.md](docs/00-START-HERE/RequestLifecycle.md) |
| Add a feature | [HowToAddAFeature.md](docs/00-START-HERE/HowToAddAFeature.md) · [CriticalInvariants.md](docs/00-START-HERE/CriticalInvariants.md) |
| Change existing behaviour | [HowToChangeExistingCode.md](docs/00-START-HERE/HowToChangeExistingCode.md) + the module's `ChangeGuide.md` |
| Touch a module | [docs/04-MODULES/](docs/04-MODULES/) — the module's `README.md` |
| Touch the database | [docs/06-DATABASE/OwnershipMap.md](docs/06-DATABASE/OwnershipMap.md) · [Migrations.md](docs/06-DATABASE/Migrations.md) |
| Touch auth, tenancy or payments | [docs/07-SECURITY/](docs/07-SECURITY/) · [MultiTenancy.md](docs/02-ARCHITECTURE/MultiTenancy.md) |
| Review code | [CodeReviewGuide.md](docs/00-START-HERE/CodeReviewGuide.md) |
| Understand a decision | [docs/11-ADR/README.md](docs/11-ADR/README.md) |

## 2. Architecture in one screen

- **Modular monolith.** One deployable API + one database, split into 13 business **modules** that own their data and talk through explicit contracts ([ModuleBoundaries.md](docs/02-ARCHITECTURE/ModuleBoundaries.md)).
- **Clean Architecture dependency rule:** `Souq.API` → `Souq.Infrastructure` → `Souq.Application` → `Souq.Domain`. The Domain references nothing ([DependencyRules.md](docs/02-ARCHITECTURE/DependencyRules.md)).
- **Ports and adapters:** every external system (payments, email, storage, tokens, hashing, time) sits behind an interface owned by the core and implemented in `src/Souq.Infrastructure`.
- **Vertical slices:** one use case per folder under `src/Souq.Application/Features/<Folder>`, with its command/query, handler and validator.
- **Selective CQRS:** commands go through aggregates; reads go through projection query services ([CQRS.md](docs/02-ARCHITECTURE/CQRS.md)).
- **Selective DDD:** aggregates where invariants are rich, plain entities elsewhere ([DDD.md](docs/03-DOMAIN/DDD.md)).
- **Modules are namespaces, not projects** (ADR-0002). Renaming that decision is an ADR, not a refactor.

## 3. Hard rules, and what enforces them

Every rule below is enforced by a test unless the last column says otherwise. Breaking one fails `dotnet test`.

| # | Rule | Enforced by |
|---|---|---|
| 1 | The Domain depends on nothing: no EF Core, ASP.NET, MediatR, FluentValidation, Stripe | `DependencyRuleTests` |
| 2 | The Application knows no technology: no EF Core, ASP.NET, provider SDKs, `Souq.Infrastructure` | `DependencyRuleTests` |
| 3 | Controllers are thin: no `DbContext`, no repositories, no provider SDKs, no claims parsing, no ownership decisions | `DependencyRuleTests` |
| 4 | Business rules live in the Domain; entities expose no public setters | `DependencyRuleTests`, code review |
| 5 | A module reaches another module only through `Features/<Folder>/Contracts`, and the contract graph has no cycles | `ModuleAndContractRuleTests` |
| 6 | No domain entity appears in a request or response contract | `ModuleAndContractRuleTests` |
| 7 | No `IQueryable` crosses the Application or Domain surface | `ModuleAndContractRuleTests` |
| 8 | No client-bindable request carries a `TenantId` (outside the platform area) | `ModuleAndContractRuleTests` |
| 9 | Every tenant-owned entity has the tenant query filter and a foreign key to `Tenants`; cross-row foreign keys carry the tenant | `TenancyRuleTests` |
| 10 | `IgnoreQueryFilters` only in the reviewed platform read path; no raw SQL outside migrations; `ExecuteUpdate`/`ExecuteDelete` only in reviewed places, because they skip `SaveChanges` and therefore the write guard | `TenancyRuleTests` |
| 11 | Use cases read the tenant, never set it | `TenancyRuleTests` |
| 12 | Every platform-area request is audited (`IAuditable`) | `ModuleAndContractRuleTests` |
| 13 | No direct clock reads (`DateTime.UtcNow`); inject `TimeProvider` | `ClockRuleTests` |
| 14 | Only notification handlers depend on `IEmailSender`; requests never wait on a provider | `ModuleAndContractRuleTests` |
| 15 | Every endpoint declares its access explicitly; platform endpoints require a `platform.*` permission | `EndpointRuleTests` |
| 16 | Every command and query has exactly one handler | `EndpointRuleTests` |
| 17 | No brand or currency literal in product code or committed configuration | `WhiteLabelSourceTests`, `whiteLabel.test.js` |
| 18 | Documentation links, paths, code names and ADR structure stay valid; every current document is reachable from the entry pages; the learning path stays numbered 00–18; navigation pages carry a dated "last verified" line | `DocumentationTests` |
| 19 | The generated inventories match the code | `GeneratedDocsTests` |
| 20 | No transaction is held open across a network call | ADR-0021, code review |
| 21 | No secret in committed configuration; no token, password, card data or personal data in logs | ADR-0020, `StartupAndSecurityTests`, code review |

## 4. What must never be bypassed

- **Tenant resolution from the host.** The frontend never sends a tenant id, and no request type may carry one. Background work runs inside an explicit tenant scope.
- **The tenant query filter and the write guard** in `AppDbContext`.
- **Authorization attributes on endpoints** plus the ownership check inside the use case (another customer's id answers 404, not 403).
- **The outbox** for anything that leaves the system after a commit (email, notifications). Handlers, not request paths, talk to providers.
- **Server-side pricing.** Totals, discounts, shipping and stock come from the server; the client displays them.
- **Payment safety:** amounts computed server-side, idempotency keys on refunds, webhook signatures verified, no card data, the fake gateway never implicit outside Development/Testing.
- **Migrations that move data** are written and reviewed by hand ([Migrations.md](docs/06-DATABASE/Migrations.md)).

## 5. Conventions

- **Layering by intent:** a rule true regardless of UI or storage → Domain. Orchestrating one use case → Application. How a technology does it → Infrastructure. HTTP translation → API.
- **Naming:** commands end in `Command`, queries in `Query`, handlers in `Handler`, validators in `Validator`, EF configurations in `Configuration`, query services in `Queries`. Folder = feature, namespace = module.
- **API:** REST, plural nouns, sub-resource actions instead of invented verbs, enums as strings, every list paged with a sort allowlist. Errors are RFC 7807 with a **stable `code`** — clients branch on the code, never on the message ([ApiDocumentation.md](docs/05-API/ApiDocumentation.md)).
- **Errors:** expected failures are `Result`/domain exceptions with codes; unexpected ones bubble to the global handler. Never swallow an exception silently.
- **Comments are in Arabic** and explain *why*, an invariant, a security concern, or non-obvious behaviour — never what the line already says ([code comment policy](docs/00-START-HERE/CodeReviewGuide.md#comment-policy)).
- **Frontend:** the server is authoritative; guards are UX only; no business rules; every user-visible string goes through i18n; no brand or currency literals ([FrontendGuide.md](docs/08-FRONTEND/FrontendGuide.md)).
- **Dependencies:** MediatR stays pinned to 12.x (13+ is commercially licensed). A new dependency is a decision — propose it, don't add it quietly.
- **Phases:** roadmap phases in [ProductRoadmap.md](docs/12-ROADMAP/ProductRoadmap.md) §6 describe **product capability** and are never renumbered or reused. Engineering missions — hardening, release readiness, knowledge passes — are separate: they run on their own branch, close against the registers, and **take no roadmap number**. Give such a mission a name, not the next free phase. The number in a branch like `phase/17-production-hardening` is a branch sequence, not roadmap Phase 17 (the tenant admin dashboard), which is a different thing.
- **Commits:** Conventional Commits (`feat(module): …`). One coherent change per commit; the repository must build at every commit.

## 6. Before you change code: inspect

1. Which **module** owns the concept? (`docs/04-MODULES/`, [ModuleBoundaries.md](docs/02-ARCHITECTURE/ModuleBoundaries.md))
2. Which **business rules** already apply? ([BusinessRules.md](docs/01-REQUIREMENTS/BusinessRules.md))
3. Which **ADR** decided this area, and is your change consistent with it? ([docs/11-ADR/README.md](docs/11-ADR/README.md))
4. Which **tests** cover it today? Read them — they encode the expected behaviour.
5. What is the **data ownership and migration** impact? ([OwnershipMap.md](docs/06-DATABASE/OwnershipMap.md))
6. What is the **security and tenancy** impact? Who may call this, and what happens for another store's id?
7. Is there a **change guide** for the module? Follow it.

## 7. Before you say the work is done: run

The canonical gate, and exactly what CI runs, is [DeveloperQualityGates.md](docs/09-OPERATIONS/DeveloperQualityGates.md). In short:

```bash
dotnet build -warnaserror                      # backend builds with no warnings
dotnet test tests/Souq.Domain.Tests            # fast: business rules
dotnet test tests/Souq.Application.Tests       # fast: use cases
dotnet test tests/Souq.ArchitectureTests       # layer, module, tenancy, endpoint and documentation rules
dotnet test tests/Souq.IntegrationTests        # real API + SQL Server (needs Docker; see Troubleshooting.md)
cd frontend && npm run lint && npm run typecheck && npx vitest run && npm run build
```

- Changed a user flow? Run its browser journey in `frontend/e2e` against a live stack (runbook in DeveloperQualityGates.md). The journeys are not in CI.

- Changed a controller, a use case or a test file? Regenerate the inventories:
  `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
- Integration tests need roughly 2 GB of free Docker memory. Check with `docker run --rm alpine free -m`, and never stop containers you did not start ([Troubleshooting.md](docs/09-OPERATIONS/Troubleshooting.md)).

## 8. Documentation is part of the change

- Changed **behaviour**? Update the module document and, if the rule is new, [BusinessRules.md](docs/01-REQUIREMENTS/BusinessRules.md).
- Changed **an endpoint, a use case or the schema**? Regenerate the inventories and update the affected guide.
- Made an **architectural decision** — a new dependency, a new boundary, a new technology, a different persistence or transaction strategy, anything future engineers would otherwise have to reverse-engineer? Write an ADR in `docs/11-ADR/` (copy the structure of a recent one: Status, Date, Related modules, Related ADRs, Context, Problem, Options considered, Decision, Consequences) and link it from the index.
- Deferred something on purpose? Record it as DEFERRED with the reason, in the module document and, when it carries risk, in [TechnicalDebt.md](docs/12-ROADMAP/TechnicalDebt.md) or [RiskRegister.md](docs/02-ARCHITECTURE/RiskRegister.md).
- Writing style: backticks are reserved for things that exist in the repository; planned things are written in italics. `DocumentationTests` enforces this.

## 9. When to stop instead of guessing

Do the safe work first, leave the repository clean, then report and ask. Stop for:

- a **destructive or irreversible data migration**;
- a **licensing or legal** decision (the repository is MIT today — open product decision P-03);
- **real payment-account behaviour** that needs the owner's Stripe account (open decision P-05: JOD minor units) or the merchant-of-record model (D-13);
- a **tax** decision (P-06);
- a **business rule where guessing changes commercial behaviour** (pricing, refunds, limits);
- a **security decision with real-world consequences** (weakening isolation, storing new personal data, new public endpoints);
- **contradictory requirements**, or a request that conflicts with an ADR — say which ADR and why.

## 10. Recognizing an architectural conflict

You are in one when: a change needs a new dependency arrow between modules; a use case wants data another module owns; a rule needs to live in two places; a test that encodes a boundary fails and the "fix" is to delete it; the frontend wants to decide something the server must decide; a background job needs a tenant it cannot resolve. In all of these, **stop and design first**: read the module boundaries, propose a contract, and write an ADR if the boundary itself changes.

---

Project-specific instructions for Claude Code live in [CLAUDE.md](CLAUDE.md); they defer to this file for engineering rules.
