# Handoff guide: taking over Souq

> **The scenario this page is written for:** the people who built Souq are gone, and your team has the repository. Everything you need to run, understand, change, deploy and extend the system is in the repository — this page is the order to take it in, and the honest list of what is missing.
> **First hour:** [SystemOverview.md](SystemOverview.md), then this page, then [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md) to get it running.
> **The practical checklist** — access, versions, secrets, backups, TLS, payments, open decisions — is [HandoffChecklist.md](HandoffChecklist.md). The numbered reading order is [LearningPath.md](LearningPath.md).
> **Last verified against the repository:** 2026-09-17, branch `phase/17-production-hardening`.

## 1. What you have received

A **white-label, multi-tenant e-commerce platform**: one ASP.NET Core API, one SQL Server database and one React build serving many independent stores, each on its own domain with its own branding, currency, languages, catalog and customers.

| | |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core 10, SQL Server 2022, MediatR 12, FluentValidation |
| Frontend | React 18, Vite 8, React Router 6, TanStack Query, i18next, JavaScript type-checked with JSDoc, CSS Modules |
| Integrations | Stripe (payments), Resend / Brevo / Gmail SMTP (email), local disk (uploads) |
| Tests | six suites: Domain, Application, Architecture and Integration (.NET), Vitest, and Playwright browser journeys run by hand; the boundaries and the documentation are themselves tested |
| Documentation | `docs/`, numbered by purpose; this is the primary knowledge base |

## 2. Read in this order

1. [SystemOverview.md](SystemOverview.md) and [ProjectMap.md](ProjectMap.md) — what the system is, and how its parts relate.
2. [LearningPath.md](LearningPath.md) — the numbered reading order; a new owner reads steps 00, 01, 15, 18, then 13 and 09.
3. [AGENTS.md](../../AGENTS.md) — the rules, and what enforces each one.
4. [Modules.md](../04-MODULES/Modules.md) — the thirteen capabilities and who owns what.
5. [docs/11-ADR/README.md](../11-ADR/README.md) — the decisions, grouped, with what later ADRs changed.
6. [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) and [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) — what is unfinished and what can hurt you.

## 3. Run it

Full instructions: [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md). In short:

- **Docker:** copy `.env.example` to `.env`, fill the required variables, `docker compose up --build`. The stack runs in Production mode, so it refuses to start without real provider configuration unless you opt into the explicit demo settings.
- **Local:** put secrets in .NET user-secrets, `dotnet run --project src/Souq.API`, then `cd frontend && npm install && npm run dev`. Migrations and seeding run at startup; `localhost` serves the seeded demo store, `{slug}.localhost` any other, `admin.localhost` the platform area.

## 4. Where configuration and secrets live

| | |
|---|---|
| **Never** | in committed configuration. `src/Souq.API/appsettings.json` holds non-secret defaults only, and a test forbids brand and currency literals there |
| Locally | .NET user-secrets |
| In Docker | environment variables, documented one by one in `.env.example` and [Configuration.md](../09-OPERATIONS/Configuration.md) |
| Store payment keys | entered through the API, **encrypted in the database** with the deployment's secrets key. Losing that key makes those stores' payments fail until the keys are re-entered |
| The owner's real `.env` | not in the repository, and never to be committed |

**External accounts you must obtain from the previous owner:** the Stripe account (and its webhook secret), the email provider account and its verified sender domain, the DNS for every store's domain, and the hosting. None of them can be recovered from this repository.

## 5. How the architecture works, in five sentences

One deployable application, split into thirteen business modules that own their data and talk through explicit contracts. Source dependencies point inwards: API → Infrastructure → Application → Domain, and the Domain depends on nothing. Every external system sits behind a port implemented by an adapter, so Stripe or the email provider can be replaced without touching business rules. Each request is bound to exactly one store, resolved from its host name on the server, and four independent mechanisms keep stores apart. Anything that must happen after a commit — email, notifications — leaves through a transactional outbox, so nothing is lost and no request waits on a provider.

## 6. How to change it safely

- [HowToAddAFeature.md](HowToAddAFeature.md) and [HowToChangeExistingCode.md](HowToChangeExistingCode.md) are the working sequences.
- Each module has a `ChangeGuide.md` listing the changes people actually make, with the invariants to respect and the tests to update.
- Before declaring work done, run the gate in [TestingStrategy.md](../10-TESTING/TestingStrategy.md) §3. The architecture and documentation tests will tell you if you crossed a boundary or left a document lying.

## 7. Deploy it

**"How do I safely operate Souq?" — in the order you will need them:**

| | |
|---|---|
| 1. Understand the shape | [Deployment.md](../09-OPERATIONS/Deployment.md) — topology, startup sequence, health checks, rollback |
| 2. Set it up | [Configuration.md](../09-OPERATIONS/Configuration.md) (every setting) · [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md) (the three database identities) |
| 3. First run | [SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md) — what a fresh database gets, and the eight-step bootstrap |
| 4. Before opening | [ProductionReleaseChecklist.md](../09-OPERATIONS/ProductionReleaseChecklist.md) · `./scripts/release-gate.sh --require-all` (config, backups, suites, audits, smoke in one verdict) |
| 5. Keep it recoverable | [BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md) — rehearse the restore, and put `./scripts/backup-verify.sh` on a schedule so silence is detected |
| 6. When it breaks | [IncidentResponse.md](../09-OPERATIONS/IncidentResponse.md) (which layer, and what not to touch) · [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) (named symptoms) |
| 7. Before it grows | [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) — including the measured limits |

Read [IncidentResponse.md](../09-OPERATIONS/IncidentResponse.md) and [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) once **before** you need them; §9 of the first lists what this repository deliberately does not give you.

**Be aware, before your first production deployment:**

- Migrations run automatically at startup, in every environment. Back up first.
- **CI runs but does not block merges yet** — switch on branch protection for `main` so a red run cannot be merged ([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)).
- There is **no scheduled backup**. A rehearsed backup/restore procedure exists ([BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md)) but nothing runs it for you — wire up the schedule and an off-site copy before real data exists. Health checks exist (`/health/live`, `/health/ready` — [Deployment.md](../09-OPERATIONS/Deployment.md) §6); nothing outside the stack watches them yet.
- The compose stack connects to SQL Server as `sa` (least-privilege logins are scripted and verified, not yet applied — [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md)), the API container runs as root, and **no TLS is configured** anywhere in the repository. Security headers exist in the API and nginx; the SPA's Content-Security-Policy is report-only. Fix before public traffic.
- A fresh database always contains store id 1, and outside Development it keeps the seeded demo name with no catalog until you adopt or archive it ([SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md)). The demo *catalog* is seeded only in Development/Testing, or on explicit request.

## 8. What is incomplete

- **Roadmap:** [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) is the authority. At the last verification:
  - Phases 1A–15 are complete.
  - Phase 16 (Storefront) is 🟡: product variants can't be chosen, a model and API change.
  - Phase 17 (Tenant admin dashboard) is ✅.
  - Phase 18 (Platform owner dashboard) is 🟡, waiting only on owner decisions D-22 (storefront preview) and P-07 (platform-wide settings).
  - Phases 19–23 (testing, security review, performance, documentation, production) have not started.
- **Branches:** all of it is on `phase/17-production-hardening`, far ahead of `main` and neither pushed nor merged ([HandoffChecklist.md](HandoffChecklist.md) §0).
- **Debt:** [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) — the recorded items, prioritized.
- **Risks:** [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) — read §1 (money) before taking real payments.
- **Tests:** what each suite covers and its known gaps are in [TestingStrategy.md](../10-TESTING/TestingStrategy.md) §5. The browser journeys are manual and not in CI; CI runs but doesn't block merges until branch protection is on.
- **Release blockers:** [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) — what actually stops a first paying customer, triaged. Read its **final assessment** first. Several engineering missions ran outside the roadmap (a knowledge pass, production hardening, operational readiness, enforcement and monitoring, storefront experience, back-office completion); none took a roadmap number.

## 9. Decisions waiting for an owner

These cannot be made from the code; they are commercial or legal. Each one's full record — the exact question, what it costs to get wrong, what evidence exists and what is still missing — is in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md):

| ID | Decision | Blocks |
|---|---|---|
| **P-05** | JOD (and other three-decimal currencies) minor units at Stripe — verify against the real account | Live payments in those currencies |
| **D-13** | Every store connects its own Stripe account, or the platform adopts Stripe Connect | A second store taking live payments |
| **P-06** | Tax: inclusive or exclusive prices, per-store rates, invoice requirements | Selling where tax must be shown |
| **P-03** | License and repository visibility (MIT today, with a public remote) | Selling the product |
| **D-22** | Storefront preview: who may preview a closed store, which states, read-only or not, lifetime, and how the credential crosses hosts ([brief](../04-MODULES/Platform/StorefrontPreview.md)) | Phase 18's storefront preview |
| **P-07** | Which platform-wide settings exist (`platform.settings.manage` has nothing to guard) | A platform settings screen |
| **R-03** | May a staff member who manages orders, but not payments, refund money by cancelling a paid order? | Least-privilege store roles |
| **F-8** | Should a duplicate checkout submission replay the order or be refused? | Idempotent checkout |
| ~~D-19~~ | ~~TypeScript and a server-state library for the frontend~~ | **Decided:** TanStack Query adopted and JSDoc type-checking enforced in CI; a full TypeScript conversion was evaluated and not adopted ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md), [ADR-0038](../11-ADR/0038-query-layer-adopted-and-type-checking.md)) |

## 10. What must never be changed casually

1. **Tenant isolation** — host-only resolution, the query filter, the write guard, composite keys, 404 for another owner's id.
2. **Authorization** — explicit access on every endpoint, permissions checked in the use case as well as at the edge.
3. **Money paths** — server-side totals, frozen order snapshots, idempotent payment and refund handling.
4. **Stock** — reservations, the ledger, and the invariant that available never goes negative.
5. **The outbox** — nothing that must survive a crash leaves outside it.
6. **The architecture tests** — they are the memory of decisions; deleting one deletes the decision.

## 11. How to evaluate a proposed architecture change

Ask, in this order:

1. **What problem does it solve here, with evidence from this system?** A measurement, an incident, or a written requirement — not a resemblance to someone else's architecture.
2. **Is it already answered?** [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) records what was rejected, why, and what evidence would change the answer.
3. **What does it cost?** Operations, testing, hiring, and the paths it closes.
4. **Is there a cheaper step first?** [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) orders the cheap moves before the expensive ones.
5. **What breaks if it is wrong, and can we reverse it?**
6. **Write the ADR before the code.** Context, problem, options, decision, consequences.

A proposal that cannot answer 1 and 3 is not ready, however fashionable the technology.

## 12. If you are an AI agent

Read [AIHandoff.md](AIHandoff.md) first. It states what you must inspect before changing anything, and what you must never do without a decision.
