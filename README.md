# Souq

**A white-label, multi-tenant e-commerce platform.** One deployment — one ASP.NET Core API, one SQL Server database, one React build — serves many independent online stores. Each store has its own domain, branding, currency, languages, catalog, customers and orders, and can see nothing of any other store.

It is a commercial product, not a sample application: real payments, real multi-tenancy, and the boundaries that keep both safe are enforced by tests.

| | |
|---|---|
| **Backend** | .NET 10 · ASP.NET Core · EF Core 10 · SQL Server 2022 · MediatR 12 · FluentValidation |
| **Frontend** | React 18 · Vite 5 · React Router 6 · i18next · CSS Modules (RTL and LTR) |
| **Integrations** | Stripe · Resend / Brevo / Gmail SMTP · local file storage |
| **Architecture** | Modular monolith · Clean Architecture · ports and adapters · selective CQRS and DDD |
| **Tests** | Domain · Application · Architecture · Integration (real SQL Server via Testcontainers) · Vitest |

## Where to start reading

| Question | Answer |
|---|---|
| What is this system, and what happens on a request? | [docs/00-START-HERE/SystemOverview.md](docs/00-START-HERE/SystemOverview.md) |
| How do I find my way around? | [docs/00-START-HERE/HowToReadThisRepository.md](docs/00-START-HERE/HowToReadThisRepository.md) |
| What are the rules for changing code? | [AGENTS.md](AGENTS.md) |
| Where is the documentation map? | [docs/README.md](docs/README.md) |

## Run it

**Docker (the whole stack):**

```bash
cp .env.example .env      # fill in the required values; the file documents each one
docker compose up --build
```

The stack runs in Production mode and refuses to start with missing or unsafe configuration. Frontend on `http://localhost:8081`, API on `http://localhost:5201`.

**Locally:**

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<your connection string>" --project src/Souq.API
dotnet user-secrets set "Jwt:Key" "<64+ random characters>" --project src/Souq.API
dotnet run --project src/Souq.API                 # applies migrations and seeds on startup
cd frontend && npm install && npm run dev         # http://localhost:5173
```

`localhost` serves the seeded demo store, `{slug}.localhost` any other store, and `admin.localhost` the platform area. Full instructions, including several stores at once: [docs/09-OPERATIONS/DevelopmentGuide.md](docs/09-OPERATIONS/DevelopmentGuide.md).

## The architecture in one screen

```
src/Souq.API             HTTP: controllers, middleware, auth policies, composition root
src/Souq.Infrastructure  adapters: EF Core, Stripe, email, storage, hosted services
src/Souq.Application     use cases: commands, queries, handlers, validators, ports
src/Souq.Domain          the business model: aggregates, value objects, domain events
```

Dependencies point inwards only; the Domain depends on nothing. Inside those layers, thirteen **business modules** own their data and talk through explicit contracts. Every request is bound to one store, resolved from its host on the server, and four independent mechanisms keep stores apart.

- Layers and what enforces them: [docs/02-ARCHITECTURE/DependencyRules.md](docs/02-ARCHITECTURE/DependencyRules.md)
- Modules and ownership: [docs/04-MODULES/Modules.md](docs/04-MODULES/Modules.md) · [docs/02-ARCHITECTURE/ModuleBoundaries.md](docs/02-ARCHITECTURE/ModuleBoundaries.md)
- Isolation: [docs/02-ARCHITECTURE/MultiTenancy.md](docs/02-ARCHITECTURE/MultiTenancy.md)
- What we deliberately did **not** build: [docs/02-ARCHITECTURE/ExplicitNonGoals.md](docs/02-ARCHITECTURE/ExplicitNonGoals.md)

## Where things are

| You want | Go to |
|---|---|
| A module (catalog, orders, payments…) | [docs/04-MODULES/](docs/04-MODULES/Modules.md) — purpose, data, use cases, tests, change guide |
| One feature end to end | [docs/04-MODULES/FeatureMaps.md](docs/04-MODULES/FeatureMaps.md) |
| The API surface | [docs/05-API/Endpoints.md](docs/05-API/Endpoints.md) (generated) · [conventions](docs/05-API/ApiDocumentation.md) · Swagger at `/swagger` in Development |
| The rules the business relies on | [docs/01-REQUIREMENTS/BusinessRules.md](docs/01-REQUIREMENTS/BusinessRules.md) |
| Decisions and their alternatives | [docs/11-ADR/README.md](docs/11-ADR/README.md) |
| Database tables and their owners | [docs/06-DATABASE/OwnershipMap.md](docs/06-DATABASE/OwnershipMap.md) |
| Security controls and their tests | [docs/07-SECURITY/SecurityControls.md](docs/07-SECURITY/SecurityControls.md) |
| The frontend | [docs/08-FRONTEND/FrontendGuide.md](docs/08-FRONTEND/FrontendGuide.md) |
| Deployment and troubleshooting | [docs/09-OPERATIONS/Deployment.md](docs/09-OPERATIONS/Deployment.md) · [Troubleshooting.md](docs/09-OPERATIONS/Troubleshooting.md) |

## Making a change

1. Read the owning module's document and its change guide.
2. Follow [HowToAddAFeature.md](docs/00-START-HERE/HowToAddAFeature.md) or [HowToChangeExistingCode.md](docs/00-START-HERE/HowToChangeExistingCode.md).
3. Run the gate:

```bash
dotnet build
dotnet test                                        # integration tests need Docker
cd frontend && npx vitest run && npx vite build
```

The architecture tests fail the build when a layer, a module boundary, tenant isolation, an endpoint's authorization or the documentation drifts from the code. That is deliberate: those tests are the written memory of the decisions ([docs/10-TESTING/TestingStrategy.md](docs/10-TESTING/TestingStrategy.md)).

## Known limitations

Honest and maintained, rather than discovered later:

- **Open product decisions:** JOD minor units at Stripe (P-05), the payment-account model (D-13), tax (P-06), licensing (P-03), the frontend stack (D-19) — see [the roadmap's decision log](docs/12-ROADMAP/ProductRoadmap.md).
- **Not built yet:** the storefront rebuild, the store dashboard and the platform console (roadmap Phases 16–18; the platform area is a shell today).
- **Operations gaps:** no CI pipeline, no health checks, no automated backups.
- **Everything else:** [docs/12-ROADMAP/TechnicalDebt.md](docs/12-ROADMAP/TechnicalDebt.md) and [docs/02-ARCHITECTURE/RiskRegister.md](docs/02-ARCHITECTURE/RiskRegister.md).

## License

[MIT](LICENSE) — under review before the first sale (decision P-03).
