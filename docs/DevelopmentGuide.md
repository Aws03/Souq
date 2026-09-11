# Souq: Development Guide

> For anyone (including future you) changing the code. Architecture: [Architecture.md](Architecture.md). Plan and status: [ProductRoadmap.md](ProductRoadmap.md).

## 1. Prerequisites

- .NET SDK **10.0.1xx**, Node **20+**, Docker (for SQL Server and the integration tests), `dotnet-ef` **10.x** (`dotnet tool install -g dotnet-ef`).

## 2. Local setup

```bash
# Secrets (never in appsettings.json)
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<pwd>;Encrypt=True;TrustServerCertificate=True;" --project src/Souq.API
dotnet user-secrets set "Jwt:Key" "<64+ random chars>" --project src/Souq.API

# Optional: your own dev accounts. Otherwise Development falls back to admin@souq.com / Admin@123
# (default store admin) and owner@souq.com / Owner@12345 (platform owner, platform host only)
dotnet user-secrets set "Seed:AdminEmail" "you@example.com" --project src/Souq.API
dotnet user-secrets set "Seed:AdminPassword" "<strong password>" --project src/Souq.API
dotnet user-secrets set "Seed:PlatformOwnerEmail" "owner@example.com" --project src/Souq.API
dotnet user-secrets set "Seed:PlatformOwnerPassword" "<strong password>" --project src/Souq.API

# Optional providers: Stripe:SecretKey / Stripe:PublishableKey / Stripe:WebhookSecret,
# Resend:ApiKey + Resend:From, Brevo:ApiKey + Brevo:SenderEmail, Gmail:AppPassword + Gmail:Username

dotnet run --project src/Souq.API                  # http://localhost:5200/swagger (applies migrations)
cd frontend && npm install && npm run dev          # http://localhost:5173
```

**Startup validation:** settings are checked before the database is touched. A missing connection string or a `Jwt:Key` shorter than 32 bytes stops the API with a message naming the key. Development uses the fake payment gateway automatically when no Stripe key is set; other environments need Stripe keys or an explicit `Payments:Provider=Fake` ([ADR-0020](adr/0020-configuration-and-secrets.md)). `Inventory:ReservationMinutes` (5–1440, default 30) is how long an unpaid checkout holds stock, and `Inventory:SweepIntervalSeconds` (0 = off, else 10–3600, default 60) is how often the expiry sweep runs. Integration tests turn the sweep off and send `ExpireStaleCheckoutsCommand` directly ([ADR-0026](adr/0026-inventory-reservations.md)).

**Docker (full stack):** `cp .env.example .env`, fill in the values, then `docker compose up --build`. The stack runs in Production mode: no admin exists unless `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD` are set, and the API refuses to start without Stripe keys unless `PAYMENTS_PROVIDER=Fake` is set for a demo. The default store is bound to `localhost` explicitly through `DEFAULT_TENANT_HOSTS`. Production has no fallback store for unknown hosts.

**Several stores locally (Phase 2):** the store comes from the host ([ADR-0006](adr/0006-tenant-resolution.md)).
- In Development, `localhost` is the default store (`Tenancy:LocalDefaultTenant`, `marka`).
- `http://{slug}.localhost:5173` is any other store; browsers resolve `*.localhost` to 127.0.0.1 with no setup.
- `http://admin.localhost:5173` is the platform area.
- API tools can send `X-Tenant: <slug>` instead.
- None of these conveniences exist outside Development/Testing.

**Sessions locally (Phase 3):**
- The refresh cookie is `Secure`. Chrome and Firefox accept it on `http://localhost` and `*.localhost`; Safari does not. When testing in Safari, set `Auth:RefreshCookie:Secure=false` in user-secrets.
- The platform owner signs in on the platform host (`admin.localhost`), through the API until the platform UI lands.
- Auth endpoints are rate-limited (`RateLimiting:*`). Raise the limits locally if a script hits them.

**Provisioning a store locally (Phase 4, API only until the platform UI in Phase 18):**
1. Sign in as the platform owner on the platform host: `POST http://admin.localhost:5200/api/auth/login`.
2. Create the store: `POST /api/platform/tenants` with `{ "name", "slug", "currency", "defaultCulture", "timeZone" }`. It starts in Provisioning.
3. Add a domain, then invite its admin:
   - `POST /api/platform/tenants/{id}/domains` with `{ "host": "{slug}.localhost" }`. `*.localhost` resolves to your machine, and the invitation link needs a domain.
   - `POST /api/platform/tenants/{id}/admins`. In Development, the console email adapter logs the link.
4. Activate the store: `POST /api/platform/tenants/{id}/status` with `{ "action": "Activate" }`.
5. Open `http://{slug}.localhost:5173`.

Every step appears in `GET /api/platform/audit`.

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
- New endpoint → declare `[AllowAnonymous]`, `[Authorize]` or `[HasPermission]` (the boundary tests fail otherwise; a new public endpoint also goes into their reviewed list), plus an integration test if it has non-trivial SQL, authorization, or concurrency behaviour.
- New list → paged: the query implements `IPagedQuery`, its validator inherits `PagedQueryValidator`, and the query service ends with `ToPageAsync` and an `Id` tiebreaker. Prove ordering and filters against SQL Server.
- New error code → add its translation to both `frontend/src/i18n/locales/*.json` files (a test keeps the keys identical).
- **New tenant-owned data (Phase 2):**
  - The entity implements `ITenantOwned`, with no setter for `TenantId`.
  - References to other tenant rows use composite `(TenantId, XId)` keys.
  - Handlers check referenced ids through the (filtered) repositories.
  - Every id-bearing endpoint goes into the `TenantIsolationTests` table (its completeness test fails otherwise).
  - Integration tests reach other stores with `_factory.CreateStoreAsync()` and `new TestApi(factory).ForStore(store)`.

## 4. Git workflow

- **One branch per phase** (`phase/<id>-<topic>`), merged to `main` after review and approval (decision P-01).
- **Conventional commits:** `feat(scope):`, `fix(scope):`, `refactor:`, `test:`, `docs:`, `chore:`. Prefer several logical commits over one large one.
- Never commit secrets, `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, or IDE folders.

## 5. Where does my code go?

| I'm writing… | Put it in |
|---|---|
| A rule that must always hold (no negative stock, valid transitions) | `Souq.Domain` entity or value object method |
| A use case (place order, cancel order) | `Souq.Application/Features/<Module>/<UseCase>` command/query + handler + validator |
| A read for a screen or listing | query + handler in `Features/<Module>/Queries`; the module's read port (`ICatalogQueries`, `IOrderQueries`…) in Application; the `AsNoTracking` projection in `Infrastructure/Persistence/Queries` ([ADR-0008](adr/0008-cqrs-strategy.md)) |
| An expected failure the use case decides (not found, duplicate, stale edit, provider down) | `Result.Failure(Error.NotFound(...) / Error.Conflict(code, ...) / …)` — the `ErrorKind` picks the HTTP status ([ADR-0017](adr/0017-error-contract.md)) |
| A rule an entity always enforces | a `DomainException` subclass with a stable `Code` (422). Handlers never catch it. |
| "Who is calling?" / "may they touch this?" | inject `ICurrentUser`; use `RequireUserId()` and `CanAccessOwnedBy(ownerId, permission)`. Never put a user id in a command. |
| A new setting | a typed options class + validator + `ValidateOnStart` in the layer that uses it; secrets only in user-secrets or environment variables |
| "Now" | inject `TimeProvider` (never `DateTime.UtcNow`; an architecture test scans for it) |
| A way to talk to Stripe, SMTP, disk, or blob storage | port in `Souq.Application/Common/Interfaces`, adapter in `Souq.Infrastructure` ([Architecture.md §11](Architecture.md#11-external-integration-conventions-ports-and-adapters)) |
| EF mapping, SQL, migrations | `Souq.Infrastructure/Persistence` |
| An HTTP endpoint | a thin controller in `Souq.API/Controllers` that sends a MediatR request |
| UI logic without rendering (payload builders, formatting) | `frontend/src/features/<feature>/*.js` + a `*.test.js` next to it |

**Don't:**
- Put business logic in controllers or React pages.
- Query the database from controllers.
- Return Domain entities from endpoints.
- Add generic repositories or services without two real consumers.
- Log tokens, links, personal data, request payloads, headers or query strings.
- Hard-code a brand, currency, or tenant.
- Catch a `DomainException` in a handler, or return an unbounded list.
- Call `Update()` on a tracked entity (it no longer exists — the unit of work saves changes) or hold a transaction open across a network call ([ADR-0021](adr/0021-transaction-boundaries.md)).

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
