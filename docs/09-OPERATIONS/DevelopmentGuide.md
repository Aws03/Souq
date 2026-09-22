# Development guide

> For anyone (including future you) changing the code on a local machine.
> Architecture: [Architecture.md](../02-ARCHITECTURE/Architecture.md) · Plan and status: [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md).
> Every setting in full: [Configuration.md](Configuration.md) · Running a deployment: [Deployment.md](Deployment.md) · When something breaks: [Troubleshooting.md](Troubleshooting.md).

## 1. Prerequisites

- **.NET 10 SDK.** The projects target `net10.0` and reference 10.0.x packages. No *global.json* pins a version, so the newest 10.0 SDK is fine.
- **Node 22** (22.12 or newer — Vitest 5 requires it). `frontend/Dockerfile` builds on node:22-alpine and CI uses Node 22; Vite 8 and Vitest 5 are the toolchain (`frontend/package.json`).
- **Docker**, for SQL Server and for the integration tests. Leave it at least 2 GB of memory for SQL Server, or expect the failures in [Troubleshooting.md](Troubleshooting.md) §1.
- **`dotnet-ef` 10.x**: `dotnet tool install -g dotnet-ef` (there is no tool manifest in the repository).
- **A SQL Server you can reach on `localhost,1433`.** Any local instance works — for example a container of your own:

  ```bash
  docker run -d --name souq-sql -p 1433:1433 \
    -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<strong password>' \
    mcr.microsoft.com/mssql/server:2022-latest
  ```

  If the machine already runs a shared SQL Server container for other projects, use it — and never stop a container you did not start. The `docker compose` stack is separate and deliberately publishes no database port, so the two never collide.

## 2. Local setup

Secrets live in user secrets, never in `src/Souq.API/appsettings.json`. Only the first two are required.

```bash
# Required
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<pwd>;Encrypt=True;TrustServerCertificate=True;" \
  --project src/Souq.API
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)" --project src/Souq.API   # 32+ bytes, or the API refuses to start
```

| Optional key | Why you would set it |
|---|---|
| `Seed:AdminEmail`, `Seed:AdminPassword` | your own store admin instead of the Development default |
| `Seed:PlatformOwnerEmail`, `Seed:PlatformOwnerPassword` | your own platform owner instead of the Development default |
| `Stripe:SecretKey`, `Stripe:PublishableKey`, `Stripe:WebhookSecret` | work on real payments instead of the fake gateway |
| `Resend:ApiKey` (+ `Resend:From`), or `Brevo:ApiKey` + `Brevo:SenderEmail`, or `Gmail:AppPassword` + `Gmail:Username` | send real email instead of logging it |
| `Secrets:ActiveKeyId`, `Secrets:Keys:dev` | let stores connect their own Stripe account (§6) |
| `Auth:RefreshCookie:Secure` | `false` when testing in Safari or over a LAN IP (§5) |
| `Tenancy:LocalDefaultTenant` | make `localhost` serve a store other than the seeded default |
| `Payments:Demo:WebhookSecret` | exercise signed webhooks against the fake gateway |
| `RateLimiting:Auth:PermitLimit` and friends | stop a local script from hitting the limits |

Without `Seed:*`, Development falls back to **admin@souq.com / Admin@123** (store admin) and **owner@souq.com / Owner@12345** (platform owner, platform host only). These exist in Development only — `DbSeeder` refuses a weak or default bootstrap password anywhere else.

Run both halves:

```bash
dotnet run --project src/Souq.API      # http://localhost:5200 — Swagger at /swagger; applies migrations and seeds on start
cd frontend && npm install && npm run dev   # http://localhost:5173 — proxies /api and /uploads to 127.0.0.1:5200
```

**What startup does, and what stops it.** Settings are validated before the database is touched: a missing connection string, a `Jwt:Key` shorter than 32 bytes, a bad options range or a non-absolute upload path stop the API with a message naming the key (never its value). Then migrations run, then seeding. In Development the fake payment gateway and the log-only email adapter are selected automatically when no keys are set; outside Development each needs an explicit opt-in. The full matrix, and every message, is in [Configuration.md](Configuration.md); [ADR-0020](../11-ADR/0020-configuration-and-secrets.md) explains why.

**Email locally.** With no provider key, messages are written to the API log by the console adapter, and the link is included (Development only). Messages go through the outbox, so the line appears after the dispatcher's next cycle — within about 5 seconds by default.

**Payments locally.** With no Stripe key you get the fake gateway: every payment succeeds. If you do set `Stripe:SecretKey`, set `Stripe:PublishableKey` too — Development is the one environment that does not enforce it, and without it the frontend shows the fake-gateway flow while the server waits for a real payment intent, which cancels the order.

**Background sweeps.** Reservation expiry (every 60 s), basket cleanup (every 60 min) and the outbox dispatcher (every 5 s) run inside the API process. Set the matching interval to `0` to silence one while you debug ([Configuration.md](Configuration.md) §12).

## 3. Several stores on your machine (Phase 2, [ADR-0006](../11-ADR/0006-tenant-resolution.md))

The store always comes from the Host header. These conveniences exist **only** in Development and Testing — `Program.cs` computes that from the environment and ignores any configuration that says otherwise.

- `http://localhost:5173` is the seeded default store. `Tenancy:LocalDefaultTenant` points it at another slug.
- `http://{slug}.localhost:5173` is any other store; browsers resolve `*.localhost` to the loopback address with no setup, and the Vite proxy forwards the Host header unchanged.
- `http://admin.localhost:5173` is the platform area. It has a real sign-in and, since Phase 18, the platform console: stores, the provisioning wizard, accounts and the activity log.
- API tools can send `X-Tenant: <slug>` instead of using a host.

**Provisioning a store locally.** The usual way is the platform console: sign in on `http://admin.localhost:5173` as the platform owner and open `/platform/stores/new`; the wizard walks identity, branding, domains, modules, administrator and activation. The API remains an alternative for scripts:

1. Sign in as the platform owner on the platform host: `POST http://admin.localhost:5200/api/auth/login`.
2. `POST /api/platform/tenants` with `{ "name", "slug", "currency", "defaultCulture", "timeZone" }`. The new store starts in `Provisioning`.
3. `POST /api/platform/tenants/{id}/domains` with `{ "host": "{slug}.localhost" }`. A domain is required before the next step: inviting an admin fails with `TenantHasNoDomain`, because the invitation link opens on the store's own domain.
4. `POST /api/platform/tenants/{id}/admins` with `{ "fullName", "email" }`. In Development the invitation link appears in the API log.
5. Optionally `PUT /api/platform/tenants/{id}/modules` with `{ "modules": ["promotions", "reviews", "wishlist"] }` — the call replaces the whole list.
6. `POST /api/platform/tenants/{id}/status` with `{ "action": "Activate" }`.
7. Open `http://{slug}.localhost:5173`.

Every step is recorded in `GET /api/platform/audit`.

## 4. The Docker stack

> **Stop this stack before running the integration suite.** The suite starts its own SQL Server through
> Testcontainers, and a Docker VM sized around 3–4 GiB cannot hold that beside this stack's SQL Server (and
> anything else on the machine). What it looks like when it runs out is **not** an out-of-memory message: it is
> *ResourceReaper* failing to start and taking every test with it, or — earlier, and more confusingly — one or
> two concurrency tests failing while everything else passes and they pass again in isolation. Measured on
> 2026-09-22: with the demo stack up, intermittent concurrency failures and then 421 reaper failures; with it
> stopped, 532/532 and a minute faster. `docker compose -p souq-demo ... stop` is enough — nothing needs
> rebuilding afterwards.


```bash
cp .env.example .env    # fill it in
docker compose up --build
```

The frontend is on http://localhost:8081 and the API on http://localhost:5201. The stack runs as **Production**, which changes the rules: no admin exists unless `SEED_ADMIN_EMAIL` and `SEED_ADMIN_PASSWORD` are set (≥ 12 characters), no platform owner without `SEED_PLATFORM_OWNER_EMAIL` and `SEED_PLATFORM_OWNER_PASSWORD`, the API refuses to start without Stripe keys unless `PAYMENTS_PROVIDER=Fake`, and it refuses to start without an email provider unless `EMAIL_PROVIDER=Log`. Set `SECRETS_KEY` if you want stores to connect their own Stripe accounts. The default store is bound to `localhost` through `DEFAULT_TENANT_HOSTS`; there is no fallback store for unknown hosts, so reaching the stack through `127.0.0.1` or a LAN IP needs that host added to the list. Swagger is **not** served there (it is Development-only), despite the comment in `docker-compose.yml`. Details and the production checklist: [Deployment.md](Deployment.md).

## 5. Sessions locally (Phase 3)

- The refresh cookie is `Secure`. Chrome and Firefox accept it on `http://localhost` and `*.localhost`; Safari does not, and no browser does over a LAN IP. Set `Auth:RefreshCookie:Secure` to `false` in user secrets when you need those. The guest basket cookie follows the same option.
- The access token lives 15 minutes and only in memory in the browser; the refresh cookie is `HttpOnly`, `SameSite=Strict` and scoped to `/api/auth`.
- Auth endpoints are rate-limited (`RateLimiting:*`). Raise the limits locally if a script trips them.

## 6. Store payment keys (Phase 11, [ADR-0031](../11-ADR/0031-payments-and-refunds.md))

Stores may connect their own Stripe account; their keys are encrypted with AES-256-GCM using `Secrets:Keys:{id}` (base64 of exactly 32 bytes), under the id named by `Secrets:ActiveKeyId`.

```bash
dotnet user-secrets set "Secrets:ActiveKeyId" "dev" --project src/Souq.API
dotnet user-secrets set "Secrets:Keys:dev" "$(openssl rand -base64 32)" --project src/Souq.API
```

The setting is optional: without it no store can connect an account and every store is charged through the deployment account. A malformed key or a missing id stops the start.

**Rotating the key** (the procedure `.env.example` points at; full version in [Configuration.md](Configuration.md) §10):

1. Add a second key under a new id, keeping the old one.
2. Point `Secrets:ActiveKeyId` at the new id and restart — old ciphertexts still decrypt.
3. Have every store with its own account **re-enter** its Stripe secret and webhook secret; a blank field keeps the old ciphertext, so nothing is re-encrypted by accident.
4. Remove the old key only when `SELECT TenantId FROM StorePaymentAccounts WHERE SecretKeyCipher LIKE 'v1.<oldId>.%'` returns nothing. Removing a key its ciphertexts still need makes those stores' payments answer `503 PaymentsUnavailable`.

Test-mode Stripe keys for store accounts are accepted in Development and Testing only, unless `Payments:AllowTestModeStoreAccounts` is true (which logs a warning at every start).

## 7. Tests

The canonical list of suites, commands and gates — including the frontend lint and type-check, and the Playwright browser journeys — is [DeveloperQualityGates.md](DeveloperQualityGates.md). The everyday commands:

| Suite | Command | Needs |
|---|---|---|
| Domain and Application unit tests | `dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests` | nothing |
| Architecture rules | `dotnet test tests/Souq.ArchitectureTests` | nothing |
| Integration (real API over SQL Server in Testcontainers) | `dotnet test tests/Souq.IntegrationTests` | **Docker running**, with 3 GiB+ free; the first run pulls mcr.microsoft.com/mssql/server:2022-latest |
| Everything .NET | `dotnet test` | Docker |
| Frontend lint and type-check | `cd frontend && npm run lint && npm run typecheck` | nothing |
| Frontend unit tests (Vitest) | `cd frontend && npm test` | nothing |
| Browser journeys (Playwright) | `cd frontend && npx playwright test <file>` | a live local stack — manual, not in CI; the runbook is in [DeveloperQualityGates.md](DeveloperQualityGates.md) |

The integration suite boots the real `Program` as `Testing`: your user secrets are not loaded, the admin is seeded from explicit settings exactly as in production, uploads go to a temporary directory, all three background intervals are `0` so tests drive the work directly, and the rate limits are raised. When it fails for reasons that are not your change, start with [Troubleshooting.md](Troubleshooting.md) §1–2.

**Rules**

- Never weaken or delete a test to make it pass. If a test exposes a design problem, fix the design.
- New business rule → a Domain test. New use case → an Application test.
- New endpoint → declare `[AllowAnonymous]`, `[Authorize]` or `[HasPermission]` (the boundary tests fail otherwise, and a new public endpoint also goes into their reviewed list), plus an integration test when it has non-trivial SQL, authorization or concurrency behaviour.
- New list → paged: the query implements `IPagedQuery`, its validator inherits `PagedQueryValidator`, and the query service ends with `ToPageAsync` and an `Id` tiebreaker. Prove ordering and filters against SQL Server.
- New error code → add its translation to both `frontend/src/i18n/locales/ar.json` and `frontend/src/i18n/locales/en.json` (a test keeps the key sets identical).
- New tenant-owned data (Phase 2):
  - the entity implements `ITenantOwned`, with no setter for `TenantId`;
  - references to other tenant rows use composite `(TenantId, XId)` keys;
  - handlers check referenced ids through the (filtered) repositories;
  - every id-bearing endpoint goes into the `TenantIsolationTests` table — its completeness test fails otherwise;
  - integration tests reach other stores with `CreateStoreAsync` and `TestApi.ForStore`.
- Nothing store-specific in product code: a brand name, a currency literal or the demo store's contact details fail `WhiteLabelSourceTests` and `frontend/src/whiteLabel.test.js`. Read the value from the store's configuration instead ([Troubleshooting.md](Troubleshooting.md) §18).

## 8. Git workflow

- **One branch per phase** (`phase/<id>-<topic>`), merged to `main` after review and approval (decision P-01).
- **CI runs on every push to `main` and `phase/**` and on pull requests** ([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)), but it does not block a merge until branch protection is switched on in GitHub — see [OwnerDecisions.md](OwnerDecisions.md), "Branch protection". Until then a red run can still be merged, so read the result.
- **Conventional commits:** `feat(scope):`, `fix(scope):`, `refactor:`, `test:`, `docs:`, `chore:`. Prefer several logical commits over one large one.
- Never commit secrets, `.env`, `bin/`, `obj/`, `node_modules/`, `dist/`, or IDE folders.

## 9. Where does my code go?

| I'm writing… | Put it in |
|---|---|
| A rule that must always hold (no negative stock, valid transitions) | an entity or value object in `src/Souq.Domain` |
| A use case (place order, cancel order) | a command or query plus handler and validator, in the module's feature folder under `src/Souq.Application/Features` |
| A read for a screen or listing | a query and handler in the module's feature folder; the module's read port (`ICatalogQueries`, `IOrderQueries`…) in Application; the no-tracking projection in `src/Souq.Infrastructure/Persistence/Queries` ([ADR-0008](../11-ADR/0008-cqrs-strategy.md)) |
| An expected failure the use case decides (not found, duplicate, stale edit, provider down) | `Result.Failure(...)` with `Error.NotFound`, `Error.Conflict`, `Error.Unavailable` — the `ErrorKind` picks the HTTP status ([ADR-0017](../11-ADR/0017-error-contract.md)) |
| A rule an entity always enforces | a `DomainException` subclass with a stable `Code` (422). Handlers never catch it |
| "Who is calling?" / "may they touch this?" | inject `ICurrentUser`; use `RequireUserId()` and `CanAccessOwnedBy(ownerId, permission)`. Never put a user id in a command |
| A new setting | a typed options class with a validator and `ValidateOnStart`, in the layer that uses it; secrets only in user secrets or environment variables ([Configuration.md](Configuration.md)) |
| "Now" | inject `TimeProvider` (never `DateTime.UtcNow`; an architecture test scans for it) |
| A way to talk to Stripe, SMTP, disk or blob storage | a port in `src/Souq.Application/Common/Interfaces`, an adapter in `src/Souq.Infrastructure` ([Architecture.md](../02-ARCHITECTURE/Architecture.md) §11) |
| EF mapping, SQL, migrations | `src/Souq.Infrastructure/Persistence` |
| An HTTP endpoint | a thin controller in `src/Souq.API/Controllers` that sends a MediatR request |
| UI logic without rendering (payload builders, formatting) | a module under `frontend/src/features`, with a matching `*.test.js` file next to it |

**Don't:**

- Put business logic in controllers or React pages, query the database from a controller, or return Domain entities from an endpoint.
- Add generic repositories or services without two real consumers.
- Log tokens, links, personal data, request payloads, headers or query strings.
- Hard-code a brand, currency or tenant.
- Catch a `DomainException` in a handler, or return an unbounded list.
- Call an *update* method on a tracked entity (there is none — the unit of work saves changes), or hold a transaction open across a network call ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)).

## 10. Money

- Use `Money` for every amount. Construct stored or priced values with `new Money(amount, currency)`; the constructor rejects values that do not fit the currency's minor units.
- For computed amounts (percentages, tax), use `Money.FromCalculation(amount, currency)`, which rounds to minor units with commercial rounding.
- Never do money arithmetic on `double`.
- Currency is always explicit — there is no default ([ADR-0014](../11-ADR/0014-money-precision.md)).

## 11. Database migrations

```bash
dotnet ef migrations add <PascalCaseIntent> --project src/Souq.Infrastructure --startup-project src/Souq.API
```

Read the generated migration before committing it, and add data-preserving SQL by hand; the integration tests apply every migration to a fresh database, and `MigrationRehearsalTests` rehearses the data-shaping ones against realistic rows. Migrations currently run at application startup — moving them to a deployment step is Phase 23 work. Workflow details: [DatabaseDesign.md](../06-DATABASE/DatabaseDesign.md) §10 and [Migrations.md](../06-DATABASE/Migrations.md). `dotnet ef migrations remove` needs a reachable database — see [Troubleshooting.md](Troubleshooting.md) §5.

## 12. Before you open a PR (or end a phase)

- [ ] `dotnet build`: 0 warnings (CI builds with warnings as errors)
- [ ] `dotnet test`: all green (Docker running)
- [ ] `cd frontend && npm run lint && npm run typecheck && npm test && npm run build`
- [ ] No secrets in the diff (`git diff | grep -iE "password|secret|apikey|connectionstring"` reviewed)
- [ ] Docs updated (roadmap status, the relevant architecture doc, an ADR if a decision was made; a new setting also belongs in [Configuration.md](Configuration.md))
- [ ] Architecture tests still pass (no new forbidden dependency)
