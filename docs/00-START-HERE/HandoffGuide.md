# Handoff guide: taking over Souq

> **The scenario this page is written for:** the people who built Souq are gone, and your team has the repository. Everything you need to run, understand, change, deploy and extend the system is in the repository — this page is the order to take it in, and the honest list of what is missing.
> **First hour:** [SystemOverview.md](SystemOverview.md), then this page, then [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md) to get it running.

## 1. What you have received

A **white-label, multi-tenant e-commerce platform**: one ASP.NET Core API, one SQL Server database and one React build serving many independent stores, each on its own domain with its own branding, currency, languages, catalog and customers.

| | |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core 10, SQL Server 2022, MediatR 12, FluentValidation |
| Frontend | React 18, Vite 5, React Router 6, i18next, plain JavaScript, CSS Modules |
| Integrations | Stripe (payments), Resend / Brevo / Gmail SMTP (email), local disk (uploads) |
| Tests | five suites; the boundaries and the documentation are themselves tested |
| Documentation | `docs/`, numbered by purpose; this is the primary knowledge base |

## 2. Read in this order

1. [SystemOverview.md](SystemOverview.md) — what the system is and what happens on a request.
2. [HowToReadThisRepository.md](HowToReadThisRepository.md) — pick Path A, then B.
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

[Deployment.md](../09-OPERATIONS/Deployment.md) has the current topology, the startup sequence, the production checklist and the rollback considerations. Read [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) once before you need it.

**Be aware, before your first production deployment:**

- Migrations run automatically at startup, in every environment. Back up first.
- There are **no health checks, no automated backups and no CI pipeline**. Add them early.
- The application connects to SQL Server as `sa`, the API container runs as root, and the repository contains no TLS or security-header configuration. Fix before public traffic.
- The seeder creates a demo store and demo catalog in any fresh database.

## 8. What is incomplete

- **Roadmap:** [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) — Phases 2–15 are implemented; the storefront rebuild, the tenant admin dashboard and the platform console (Phases 16–18) are not. The platform area today is a shell: sign-in and a placeholder.
- **Debt:** [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) — 39 recorded items, prioritized.
- **Risks:** [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) — read §1 (money) before taking real payments.
- **Tests:** no CI, no frontend component tests, no adapter tests for Stripe or email, no load test.
- **Release blockers:** [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) — what actually stops a first paying customer, triaged. Five P0 items are open. Two engineering missions have run outside the roadmap (a knowledge pass, then production hardening, which completed at `6a520b7`); the next recommended one is **operational readiness**, and it is not a roadmap phase.

## 9. Decisions waiting for an owner

These cannot be made from the code; they are commercial or legal:

| ID | Decision | Blocks |
|---|---|---|
| **P-05** | JOD (and other three-decimal currencies) minor units at Stripe — verify against the real account | Live payments in those currencies |
| **D-13** | Every store connects its own Stripe account, or the platform adopts Stripe Connect | A second store taking live payments |
| **P-06** | Tax: inclusive or exclusive prices, per-store rates, invoice requirements | Selling where tax must be shown |
| **P-03** | License and repository visibility (MIT today, with a public remote) | Selling the product |
| ~~D-19~~ | ~~TypeScript and a server-state library for the frontend~~ | **Decided in Phase 17** ([ADR-0037](../11-ADR/0037-frontend-server-state-and-types.md)): a query library is the target, adopted at the first screen rebuilt; TypeScript waits for a CI pipeline |

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
