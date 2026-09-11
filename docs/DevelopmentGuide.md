# Souq: Development Guide

> For anyone (including future you) changing the code. Architecture: [Architecture.md](Architecture.md). Plan and status: [ProductRoadmap.md](ProductRoadmap.md).

## 1. Prerequisites

- .NET SDK **10.0.1xx**, Node **20+**, Docker (for SQL Server and the integration tests), `dotnet-ef` **10.x** (`dotnet tool install -g dotnet-ef`).

## 2. Local setup

```bash
# Secrets (never in appsettings.json)
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<pwd>;Encrypt=True;TrustServerCertificate=True;" --project src/Souq.API
dotnet user-secrets set "Jwt:Key" "<64+ random chars>" --project src/Souq.API

# Optional: your own dev admin (otherwise Development falls back to admin@souq.com / Admin@123)
dotnet user-secrets set "Seed:AdminEmail" "you@example.com" --project src/Souq.API
dotnet user-secrets set "Seed:AdminPassword" "<strong password>" --project src/Souq.API

# Optional providers: Stripe:SecretKey / Stripe:PublishableKey / Stripe:WebhookSecret,
# Resend:ApiKey + Resend:From, Brevo:ApiKey + Brevo:SenderEmail, Gmail:AppPassword + Gmail:Username

dotnet run --project src/Souq.API                  # http://localhost:5200/swagger (applies migrations)
cd frontend && npm install && npm run dev          # http://localhost:5173
```

**Docker (full stack):** `cp .env.example .env`, fill in the values, then `docker compose up --build`. In Production mode no admin exists unless `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD` are set.

## 3. Tests

| Suite | Command | Needs |
|---|---|---|
| Domain + Application unit tests | `dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests` | nothing |
| Architecture rules | `dotnet test tests/Souq.ArchitectureTests` | nothing |
| Integration (real SQL Server, HTTP) | `dotnet test tests/Souq.IntegrationTests` | **Docker running** (the first run pulls/starts `mssql/server:2022-latest`) |
| Everything | `dotnet test` | Docker |
| Frontend unit tests | `cd frontend && npm test` | nothing |

**Rules:**
- Never weaken or delete a test to make it pass. If a test exposes a design problem, fix the design.
- New business rule → a Domain test.
- New use case → an Application test.
- New endpoint → covered by the authorization boundary test automatically (admin endpoints), plus an integration test if it has non-trivial SQL, authorization, or concurrency behaviour.
- From Phase 2: new tenant-owned data → isolation tests.

## 4. Git workflow

- **One branch per phase** (`phase/<id>-<topic>`), merged to `main` after review and approval (decision P-01).
- **Conventional commits:** `feat(scope):`, `fix(scope):`, `refactor:`, `test:`, `docs:`, `chore:`. Prefer several logical commits over one large one.
- Never commit secrets, `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, or IDE folders.

## 5. Where does my code go?

| I'm writing… | Put it in |
|---|---|
| A rule that must always hold (no negative stock, valid transitions) | `Souq.Domain` entity or value object method |
| A use case (place order, cancel order) | `Souq.Application/Features/<Module>/<UseCase>` command/query + handler + validator |
| A read for a screen or listing | query handler + (1B onward) a query service projecting into DTOs |
| A way to talk to Stripe, SMTP, disk, or blob storage | port in `Souq.Application/Common/Interfaces`, adapter in `Souq.Infrastructure` |
| EF mapping, SQL, migrations | `Souq.Infrastructure/Persistence` |
| An HTTP endpoint | a thin controller in `Souq.API/Controllers` that sends a MediatR request |
| UI logic without rendering (payload builders, formatting) | `frontend/src/features/<feature>/*.js` + a `*.test.js` next to it |

**Don't:**
- Put business logic in controllers or React pages.
- Query the database from controllers.
- Return Domain entities from endpoints.
- Add generic repositories or services without two real consumers.
- Log tokens, links, or personal data.
- Hard-code a brand, currency, or tenant.

## 6. Money

- Use `Money` for every amount. Construct from stored or priced values with `new Money(amount, currency)`. The constructor rejects values that don't fit the currency's minor units.
- For computed amounts (percentages, tax), use `Money.FromCalculation(amount, currency)`, which rounds to minor units with commercial rounding.
- Never do money arithmetic on `double`.

## 7. Database migrations

See [DatabaseDesign.md §10](DatabaseDesign.md#10-migration-workflow). Always read the generated migration. Add data-preserving SQL by hand. The integration tests apply every migration to a fresh database.

## 8. Before you open a PR (or end a phase)

- [ ] `dotnet build`: 0 warnings
- [ ] `dotnet test`: all green (Docker running)
- [ ] `cd frontend && npm test && npm run build`
- [ ] No secrets in the diff (`git diff | grep -iE "password|secret|apikey|connectionstring"` reviewed)
- [ ] Docs updated (roadmap status, the relevant architecture doc, an ADR if a decision was made)
- [ ] Architecture tests still pass (no new forbidden dependency)
