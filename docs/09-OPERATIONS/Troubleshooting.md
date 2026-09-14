# Troubleshooting

> Symptom first. Each entry: what you see, the likely cause, the commands that confirm it, the safe fix, and what not to do.
> Settings referenced here are documented in [Configuration.md](Configuration.md); the running stack is described in [Deployment.md](Deployment.md).
>
> Two rules that apply to every entry on a shared machine: **never stop or remove containers you did not start** (other people's databases run on the same Docker daemon), and **never weaken a test or a validation rule to make a symptom disappear**.

## 1. SQL Server dies or refuses connections: error 701, or a pre-login handshake failure

**Symptom.** Integration tests or the Docker stack fail with one of:

```text
There is insufficient system memory in resource pool 'default' to run this query.
A connection was successfully established with the server, but then an error occurred during the pre-login handshake.
```

Tests may also fail while starting the Testcontainers container, or pass one moment and fail the next.

**Likely cause.** The Docker VM (or host) is short of memory. SQL Server needs roughly 2 GB to itself; when several agents, test runs or other projects' containers share one Docker daemon, the SQL container is starved or killed mid-handshake. This is an environment problem, not a code problem.

**Diagnostics.**

```bash
docker run --rm alpine free -m        # memory as the Docker VM sees it
docker ps                             # what else is running (look, do not stop)
docker stats --no-stream              # who is using the memory
docker compose logs db | tail -40     # SQL Server's own startup and error output
```

**Safe fix.** Wait for the other work to finish and retry; run one test suite at a time (`dotnet test tests/Souq.Domain.Tests`, then the others) instead of `dotnet test`; close your own containers and builds; if the VM is simply too small, raise Docker Desktop's memory allocation. Retrying is normal here and is usually enough.

**Not this.** Do not `docker stop`/`docker rm`/`docker system prune` to free memory — you will take down containers that belong to other people or other projects. Do not switch the tests to an in-memory database: they exist to test real SQL Server behaviour (`rowversion`, unique constraints, `decimal` precision, migrations, tenant filters — [ADR-0015](../11-ADR/0015-testing-strategy.md)).

## 2. Integration tests fail for environmental reasons

**Symptom.** `dotnet test tests/Souq.IntegrationTests` fails before any assertion: Testcontainers cannot reach Docker, the image pull hangs, or the first test times out.

**Likely cause.** Docker is not running; the SQL Server image (mcr.microsoft.com/mssql/server:2022-latest) has never been pulled; on Apple Silicon the image is amd64 and runs under emulation, so the first start is slow; or the memory problem in §1.

**Diagnostics.**

```bash
docker info | head -5                                   # is the daemon reachable
docker image ls | grep mssql                            # is the image local
dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests tests/Souq.ArchitectureTests   # these need no Docker
```

**Safe fix.** Start Docker, pre-pull the image once, and re-run. Remember the suite starts one container per run (a collection fixture in `tests/Souq.IntegrationTests/Infrastructure/SouqApiFactory.cs`) and creates unique data per test rather than resetting the database, so a single slow first run is expected.

**A second, more confusing shape of the same problem: the suite passes one test at a time but fails in bulk.**

**Symptom.** Dozens of tests fail partway through a full run — often four or five minutes in — while each of them passes on its own, and the failures carry no assertion message. In the container log:

```
Failed allocate pages: FAIL_PAGE_ALLOCATION
There is insufficient system memory in resource pool 'default' to run this query
ContainerNotRunningException: ... exited with code 114
```

**Likely cause.** Docker's memory limit, shared with whatever else you are running. Several tests create their own databases on the shared server (migration rehearsal and rollback, seed safety, the best-selling measurement), so the run's peak demand is well above the steady state, and SQL Server gives up rather than degrading gracefully.

**Diagnostics.**

```bash
docker stats --no-stream --format '{{.Name}}\t{{.MemUsage}}'   # what else is holding memory
```

Observed in practice: an unrelated build container holding 1.2 GB of a 2.8 GB Docker allocation was enough to take the suite from 270 passing to 151 failing, and the same suite passed completely once that container exited. **Check this before reading the failures as real** — and never stop containers you did not start.

**Safe fix.** Give Docker more memory (4 GB is comfortable), or wait for the other workload to finish.

**Not this.** Do not point the tests at your development database — the factory builds its own container and deletes it, and its seeding path deliberately mirrors production.

## 3. The API refuses to start with a configuration message

**Symptom.** The process exits during startup. The message names a configuration key and never prints its value. `docker compose logs api | head -40` shows it.

**Likely cause and fix**, by message:

| Message (excerpt) | Cause | Fix |
|---|---|---|
| `سلسلة الاتصال 'Default' غير مضبوطة (ConnectionStrings:Default)` | no connection string | `dotnet user-secrets set "ConnectionStrings:Default" "…" --project src/Souq.API`, or `ConnectionStrings__Default` in the environment |
| `Jwt:Key غير مضبوط` | no signing key | set `Jwt:Key` (user secrets) or `JWT_KEY` (`.env`) |
| `Jwt:Key أقصر من 32 بايت (256 بت)` | key shorter than the HS256 minimum | generate one: `openssl rand -base64 48` |
| `Jwt:Issuer مطلوب` / `Jwt:Audience مطلوب` | an environment override blanked them | restore the `appsettings.json` values (`Souq`, `SouqClient`) |
| `Jwt:ExpiryMinutes … بين 5 و60 دقيقة` / `Jwt:RefreshTokenDays … بين 1 و90 يوماً` | out-of-range lifetimes | use a value in range; short access tokens are deliberate ([ADR-0010](../11-ADR/0010-authentication-authorization.md)) |
| `لا بوّابة دفع مضبوطة في بيئة {Environment}` | production-like environment with no Stripe key | set `STRIPE_SECRET_KEY`, or `PAYMENTS_PROVIDER=Fake` **only** for a demo with no real customers |
| `Payments:Provider=Stripe لكن Stripe:SecretKey غير مضبوط` | provider forced without a key | provide the key or clear the provider |
| `Payments:Provider غير معروف: '{Value}'` | typo | the only values are `Stripe` and `Fake` |
| `Stripe:PublishableKey مطلوب خارج Development` | Stripe selected without the browser key | set `STRIPE_PUBLISHABLE_KEY` |
| `لا مزوّد بريد مضبوط في بيئة {Environment}` | no provider key outside Development/Testing | set one of `RESEND_API_KEY`, `BREVO_API_KEY`, `GMAIL_APP_PASSWORD`, or `EMAIL_PROVIDER=Log` for a demo where nothing is delivered |
| `Secrets:Keys:{Id} يجب أن يكون base64 لـ 32 بايت بالضبط` | malformed encryption key | `openssl rand -base64 32` |
| `Secrets:ActiveKeyId '{Id}' غير موجود في Secrets:Keys` / `Secrets:ActiveKeyId مطلوب حين تُضبط Secrets:Keys` | key id and key set inconsistently | set both, matching |
| `Storage:Local:RootPath يجب أن يكون مساراً مطلقاً` | relative upload path | use an absolute path, or unset it to get `wwwroot/uploads` |
| `Inventory:…` / `Basket:…` / `Notifications:…` range messages | interval or lifetime outside its range | see the ranges in [Configuration.md](Configuration.md) §12 |
| `كلمة مرور البذرة (Seed:…) ضعيفة: يلزم 12 حرفاً على الأقل` | weak or development bootstrap password | use ≥ 12 characters, different from the development default. **Note:** this one throws *after* migrations have already been applied |

**Diagnostics.** `docker compose exec api printenv | grep -E '^(Jwt|Stripe|Email|Resend|Brevo|Gmail|Secrets|Seed|Payments)__' | sed 's/=.*/=<set>/'` shows which keys are present without printing secrets.

**Not this.** Do not put the value in `appsettings.json` to "get past it" — both committed appsettings files are secret-free on purpose, and a phase-gate check greps the diff. Do not set `PAYMENTS_PROVIDER=Fake` or `EMAIL_PROVIDER=Log` on anything customers can reach: the first marks orders paid without money, the second silently delivers nothing.

## 4. Migrations fail while the API starts

**Symptom.** The startup log shows the adapters line, then an exception from the migration step, and the process exits. The database is partly migrated.

**Likely cause.** A migration hits real data (a duplicate that a new unique index forbids, a not-null column without a backfill), the login lacks rights, or the database was restored from an older or newer point than the image expects.

**Diagnostics.**

```bash
docker compose logs api | tail -60
dotnet ef migrations list --project src/Souq.Infrastructure --startup-project src/Souq.API   # applied vs pending
```

```sql
SELECT TOP 20 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;
```

**Safe fix.** Read the failing migration in `src/Souq.Infrastructure/Migrations`, fix the data (or add the data-preserving SQL the migration needs) on a **copy** first, then re-run. `MigrationRehearsalTests` is the pattern for rehearsing a migration against realistic data. If the deployment is already broken, restore the pre-deployment backup and redeploy the matching image ([Deployment.md](Deployment.md) §11).

**Not this.** Never edit a migration that has already been applied anywhere, and never insert or delete rows in `__EFMigrationsHistory` to skip a step: the next migration will then run against a schema it does not expect. Do not delete the database in an environment that has real data.

## 5. `dotnet ef migrations remove` fails while the database container is stopped

**Symptom.** The command fails with a connection error instead of removing the migration you just created.

**Likely cause.** `migrations remove` checks the database to see whether the last migration has been applied; with the SQL Server container stopped it cannot connect.

**Diagnostics.**

```bash
docker ps -a --filter name=sql          # is your own SQL container stopped
dotnet ef migrations list --project src/Souq.Infrastructure --startup-project src/Souq.API
```

**Safe fix.** Start **your own** database container and re-run the command. If the migration was never applied to any database, `dotnet ef migrations remove --force …` continues with the code-side rollback when it cannot reach the database (check `dotnet ef migrations remove --help` for your tool version before relying on it).

**Not this.** Do not start or stop a container you do not own to unblock yourself. Do not delete the migration's `.cs` files by hand: `AppDbContextModelSnapshot.cs` would keep the removed model and the next migration would be generated against the wrong baseline.

## 6. Every request answers `404` with code `StoreNotFound`

**Symptom.** ProblemDetails body with `"code": "StoreNotFound"`; the SPA shows its "unknown store" screen (`bootOutcome` in `frontend/src/app/tenantModel.js`).

**Likely cause.** The request's Host header maps to no store. `TenantResolutionMiddleware` resolves the store from the host only — there is no fallback store in production. Common triggers: opening the Docker stack through `127.0.0.1` or a LAN IP while only `localhost` is bound; a new domain not yet added to the store; a request arriving with a proxy's internal host.

**Diagnostics.**

```bash
curl -i -H "Host: localhost"   http://localhost:5201/api/storefront/config
curl -i -H "Host: 127.0.0.1"   http://localhost:5201/api/storefront/config   # compare
docker compose logs api | grep "No store is mapped to host"
```

```sql
SELECT t.Slug, d.Host, d.IsPrimary FROM TenantDomains d JOIN Tenants t ON t.Id = d.TenantId ORDER BY t.Slug;
```

**Safe fix.** Add the host to the store through the platform API (`POST /api/platform/tenants/{id}/domains`, then `…/domains/{host}/primary` if it should own the links). For the **default** store in the Docker stack, add it to `DEFAULT_TENANT_HOSTS` (comma-separated, for example `localhost,127.0.0.1,192.168.1.20`) and restart. A change made through the API applies at once on that instance and within about 60 seconds elsewhere (the tenant directory cache).

**Not this.** Do not add a fallback store or accept a store id from the request body or query string — the host is the only input to the decision, and a token issued for another store is rejected on top of that.

## 7. A store answers `503` with code `StoreUnavailable`

**Symptom.** Everything for one store returns 503; the SPA shows the "closed" screen.

**Likely cause.** The store is not `Active`. `TenantAvailabilityMiddleware` allows, by status: `Active` everything; `Provisioning` only endpoints marked available during provisioning, available when closed, or carrying a permission requirement (so staff can prepare the store); `Suspended` and `Archived` only endpoints marked available when the store is closed (the storefront config, so the closed page can still show the store's identity).

**Diagnostics.** `GET /api/platform/tenants/{id}` on the platform host, or:

```sql
SELECT Id, Slug, Status FROM Tenants;   -- 0 Provisioning, 1 Active, 2 Suspended, 3 Archived
```

**Safe fix.** Activate it: `POST /api/platform/tenants/{id}/status` with `{"action":"Activate"}`. A newly created store starts in `Provisioning` deliberately — finish branding, domains and the admin invitation first.

**Not this.** Do not mark an endpoint available when the store is closed just to get a screen working; that attribute is a deliberate, reviewed exception.

## 8. `404` on the platform host (or on a store host)

**Symptom.** A route you know exists answers 404 with code `NotFound`, on the right deployment, even with a valid token.

**Likely cause.** Host/endpoint mismatch. Platform actions (`api/platform/*`) are served **only** on a host listed in `Tenancy:PlatformHosts`; every other action is served **only** on a store host. A few actions (sign-in and session endpoints) are marked available on all hosts. The check runs before authentication, so a valid token does not change the answer, and 404 rather than 403 is deliberate: the platform area's existence is not advertised on store hosts.

**Diagnostics.**

```bash
curl -i -H "Host: admin.localhost" http://localhost:5200/api/storefront/config   # store endpoint on platform host → 404
curl -i -H "Host: localhost"       http://localhost:5200/api/platform/stats      # platform endpoint on store host → 404
docker compose exec api printenv | grep Tenancy__PlatformHosts
```

**Safe fix.** Call platform routes on `PLATFORM_HOST` (in Development, `admin.localhost` is added automatically), and store routes on a store host. If the platform host is missing in production, set `Tenancy__PlatformHosts__0`.

**Not this.** Do not add the platform host to a store's `TenantDomains`, and do not remove `PlatformEndpoint` from a controller to make it reachable on a store host.

## 9. `X-Tenant` or `{slug}.localhost` stopped working

**Symptom.** The header or the localhost subdomain selects a store on your machine but not on a deployed environment.

**Likely cause.** Correct behaviour. `AllowDevelopmentResolution` is computed from the environment in `Program.cs` and overwrites whatever configuration says, so the `X-Tenant` header, `localhost` → default store, and `{slug}.localhost` only ever work in `Development` and `Testing`. In production, a header that chooses a tenant would be a one-line cross-tenant hop.

**Diagnostics.** `docker compose exec api printenv ASPNETCORE_ENVIRONMENT`; in Development, `curl -H "X-Tenant: <slug>" http://localhost:5200/api/storefront/config`.

**Safe fix.** Use real hosts outside Development: add a domain to the store and call it by that host.

**Not this.** Do not set `Tenancy:AllowDevelopmentResolution=true` in production (it is ignored, and trying is a signal that the real fix is a domain), and do not run a production deployment as `Development`.

## 10. Telling `401`, `403` and `404` apart

| Status | `code` | What it means | Typical cause |
|---|---|---|---|
| 401 | `Unauthenticated` | the token was not accepted | missing or expired access token (15 minutes, 30-second skew); signature invalid after a `Jwt:Key` change; the token's store does not match the host; the account's security stamp changed (password change, account disabled, refresh-token reuse detected) |
| 401 | `InvalidCredentials`, `AccountLocked` | sign-in refused | wrong password; lockout after repeated failures |
| 403 | `Forbidden` | authenticated, but the permission policy said no | role lacks the permission behind `HasPermission` |
| 403 | `CustomerAccountRequired`, `CustomerBlocked` | authenticated, but not allowed to act as this customer | a staff or platform account on a customer route; a blocked customer trying to order or review |
| 404 | `StoreNotFound` | the host maps to no store | §6 |
| 404 | `NotFound` | platform/store host mismatch, an unknown route, **or a resource that belongs to another store** | §8; the tenant query filter makes another store's row indistinguishable from a missing one, on purpose |
| 404 | `ModuleDisabled` | the feature is switched off for this store | §11 |
| 503 | `StoreUnavailable` | the store is not active | §7 |
| 503 | `PaymentsUnavailable` | the store's own payment account cannot be used | §15 |

**Diagnostics.** Read the `code` and `traceId` from the ProblemDetails body, then `docker compose logs api | grep <traceId>`. Every response also carries `X-Correlation-Id` with the same value.

**Not this.** Do not "fix" a cross-store 404 by bypassing the query filter; that is the isolation guarantee ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md)).

## 11. `404` with code `ModuleDisabled`

**Symptom.** Coupons, reviews or wishlist routes answer 404 for one store while working for another, and the storefront does not show the feature.

**Likely cause.** Optional modules are per-store flags (decision D-11). The allowed values are in `StoreModules`: `promotions` (coupons), `reviews`, `wishlist`. The check runs in `TenantAvailabilityMiddleware`, before authentication; use cases that touch a module check again (an inactive coupon module, for example, makes the pricing pipeline report `ModuleDisabled` for a coupon code).

**Diagnostics.** `GET /api/storefront/config` for that host shows the store's enabled modules; `GET /api/platform/tenants/{id}` on the platform host shows the same from the admin side.

**Safe fix.** `PUT /api/platform/tenants/{id}/modules` with `{"modules":["promotions","reviews","wishlist"]}` — the call replaces the whole list, and unknown names are rejected.

**Not this.** Do not remove `RequiresModule` from the endpoint; the flag is the enforcement point, and the frontend hiding the feature is only cosmetic.

## 12. The refresh cookie is never sent back (sessions die after 15 minutes)

**Symptom.** Sign-in works, then `POST /api/auth/refresh` answers 401 and the app drops to the visitor state. Frequent in Safari, or when browsing the stack over http on a LAN address.

**Likely cause.** The refresh cookie (`souq_refresh`) is `HttpOnly`, `SameSite=Strict`, path `/api/auth`, and `Secure` by default. Browsers only store a `Secure` cookie on https — with the exception of `localhost` and `*.localhost`, which Chrome and Firefox treat as secure contexts on http. Safari does not, and no browser makes that exception for `http://192.168.x.x`. The guest basket cookie shares the same option.

**Diagnostics.** In the browser's dev tools, check whether `souq_refresh` was stored after sign-in; confirm the host you are using (`localhost` versus an IP) and the scheme.

**Safe fix.** For local work in Safari or over a LAN IP: `dotnet user-secrets set "Auth:RefreshCookie:Secure" "false" --project src/Souq.API`. For the Docker stack over plain http on a LAN address, the same setting has to be added to `docker-compose.yml` as an environment variable for `Auth:RefreshCookie:Secure` — compose does not expose it today. For anything public, serve https and leave it `true`.

**Not this.** Do not ship `Secure=false` to an internet-facing deployment, and do not move the refresh token into `localStorage` or the response body to dodge the cookie — keeping it out of JavaScript is the point ([ADR-0023](../11-ADR/0023-sessions-and-credentials.md)).

## 13. `429 TooManyRequests`

**Symptom.** Sign-in, refresh, coupon preview or basket writes start answering 429 with a `Retry-After` header, sometimes for everyone at once.

**Likely cause.** Fixed-window limits per `host|client IP`: auth 10/min, refresh 30/min, coupon preview 30/min, basket writes 120/min. "Everyone at once" means the API is seeing the proxy's IP instead of the client's, because `ForwardedHeaders:KnownNetworks` does not include the proxy network, so every visitor shares one partition.

**Diagnostics.**

```bash
curl -i -X POST http://localhost:5200/api/auth/login -H 'Content-Type: application/json' -d '{}'   # look for Retry-After
docker compose exec api printenv ForwardedHeaders__KnownNetworks
docker compose logs api | grep '"StatusCode":429'
```

**Safe fix.** For a script or a local test loop, raise the limits for that environment (`RateLimiting:Auth:PermitLimit`, and so on) — this is exactly what the integration factory does. For the "everyone throttled together" case, set `TRUSTED_PROXY_NETWORKS` to the proxy's network.

**Not this.** Do not set `TRUSTED_PROXY_NETWORKS=0.0.0.0/0` (`.env.example` says so explicitly): any client could then forge `X-Forwarded-For` and evade the limits entirely. Do not remove `EnableRateLimiting` from an endpoint that accepts a password or sends an email.

## 14. Emails never arrive

**Symptom.** Password reset, verification, invitation or order emails do not reach the recipient. The API itself reports success — by design, the request path never waits on the email provider ([ADR-0034](../11-ADR/0034-notifications-outbox.md)).

**Likely cause**, in the order worth checking:

1. No provider is configured, so the log adapter was selected (`EMAIL_PROVIDER=Log`, or Development).
2. The dispatcher is disabled (`Notifications:DispatchIntervalSeconds=0`).
3. The provider rejects the messages (bad key, unverified sender, missing sender address).
4. The messages are being retried, or are already dead after 8 attempts.

**Diagnostics.**

```bash
docker compose logs api | grep "Adapters selected"                    # which provider was chosen
docker compose logs api | grep "Outbox dispatcher is disabled"
docker compose logs api | grep -E "rejected email|SMTP delivery|Outbox message"
docker compose logs api | grep "was not sent"                         # the log-only adapter
```

```sql
-- what is queued, retrying, or dead
SELECT TOP 50 Id, TenantId, Type, Attempts, NextAttemptAt, LockedUntil, ProcessedAt, FailedAt, LastError
FROM OutboxMessages ORDER BY Id DESC;

-- dead messages only (kept deliberately, for diagnosis)
SELECT Id, TenantId, Type, Attempts, FailedAt, LastError
FROM OutboxMessages WHERE FailedAt IS NOT NULL ORDER BY FailedAt DESC;
```

`LastError` holds the exception type and a truncated message — never a recipient, link or token. In Development, the link itself appears in the API log as a `[development only]` line once the dispatcher runs (within about 5 seconds).

**Safe fix.** Set a real provider key **and** its sender address (`BREVO_SENDER_EMAIL` or `GMAIL_USERNAME`; for Resend, a verified `Resend:From`), clear the `GMAIL_APP_PASSWORD` placeholder if Gmail is not your provider, and restart. If Gmail times out with a network error, your ISP may block port 587 — set `GMAIL_SMTP_PORT=465` for implicit TLS. For messages already dead, the supported path is to have the user request a new reset or verification: handlers issue the token at dispatch, so a fresh request produces a fresh, valid link. An operator screen for dead messages is **PLANNED** (Phase 17 or 23).

**Not this.** Do not leave `EMAIL_PROVIDER=Log` in production — nothing is delivered and only a warning marks it. Do not delete outbox rows to "clear" a backlog; they are the record of what was and was not delivered. Do not move an email send into a request handler to make it synchronous: an architecture test keeps `IEmailSender` inside the Notifications module.

## 15. Payments fail or silently do nothing

**Symptom.** Checkout cannot be completed, orders stay pending, or a payment "succeeds" without money moving.

**Likely cause and fix**, by shape:

| What you see | Cause | Fix |
|---|---|---|
| Every payment succeeds instantly, with no card form | the fake gateway is active (`Adapters selected: payments Fake`) | set real Stripe keys; the log carries the explicit demo warning whenever the fake gateway runs outside Development/Testing |
| The card form never appears and the order is cancelled | Stripe selected with **no** publishable key. Outside Development the start fails; inside Development it is allowed, and the frontend then believes the gateway is fake while the server waits for a real intent | set `Stripe:PublishableKey` in Development too; verify with `GET /api/payments/config` |
| Orders stay pending when the customer closes the tab | no `Stripe:WebhookSecret`, so webhook events are acknowledged without action and confirmation depends on the browser alone (a startup warning says this) | set the webhook secret and register the endpoint in Stripe |
| `400` with code `InvalidSignature` on `POST /api/payments/webhook` | the payload's signature does not match the configured secret (wrong secret, or a proxy altered the body) | re-copy the signing secret; make sure nothing rewrites the raw body |
| `503` with code `PaymentsUnavailable` | a store's own gateway keys cannot be decrypted — usually a `Secrets:Keys` entry was removed or `SECRETS_KEY` changed | restore the old key id, then rotate properly ([Configuration.md](Configuration.md) §10). There is deliberately no silent fallback to the deployment account |
| `503` with code `PaymentUnavailable` when placing an order | the gateway could not create a payment intent; the order was cancelled and no stock was reserved | check the gateway's status and the log line naming the order |
| A store cannot save its Stripe keys | test-mode keys outside Development/Testing | use live keys, or set `PAYMENTS_ALLOW_TEST_MODE_STORE_ACCOUNTS=true` for a demo only (it logs a warning) |
| Charges look like a tenth of the price in JOD | the P-05 question: `StripeAmountConverter` sends JOD as ×100 | confirm with Stripe how your account treats JOD **before** live JOD payments |

**Diagnostics.** `docker compose logs api | grep -E "Adapters selected|Configuration warning"`; Stripe's dashboard for event delivery attempts; `SELECT OrderId, Gateway, Status, Amount, Currency FROM Payments ORDER BY Id DESC;`.

**Not this.** Do not point a production deployment at the fake gateway to unblock a demo on the same host, and do not disable webhook signature verification.

## 16. Uploaded images 404 in the Docker stack

**Symptom.** Product images and branding assets 404 through http://localhost:8081 while the API serves them on its own port.

**Likely cause.** A `/uploads/` location that proxies without overriding `Host`: nginx then sends `Host: api:8080`, tenant resolution runs on `/uploads` too, that host maps to no store, and the answer in Production is `404 StoreNotFound`.

The shipped `frontend/nginx.conf` **no longer has this defect** — it was fixed in Phase 17 (F-24 in [ReleaseReadiness.md](ReleaseReadiness.md)), and the `/uploads/` location now forwards `Host`, `X-Forwarded-For` and `X-Forwarded-Proto` exactly as `/api/` does. So if you see this symptom, you are looking at a customised proxy, an older image, or a TLS terminator in front that drops the header.

**Diagnostics.**

```bash
curl -i http://localhost:8081/uploads/tenants/1/images/<file>      # through nginx
curl -i -H "Host: localhost" http://localhost:5201/uploads/tenants/1/images/<file>   # straight to the API
docker compose logs api | grep "No store is mapped to host"
```

**Safe fix.** Ensure `proxy_set_header Host $http_host;` is on the `/uploads/` location, exactly as on `/api/`, and rebuild the web image. Check every hop: a TLS terminator or load balancer ahead of nginx can replace `Host` before nginx ever sees it.

**Not this.** Do not bind `api` as a store domain to make the wrong host resolve, and do not exempt `/uploads` from tenant resolution — that check is what stops one store's host from serving another store's files.

## 17. Frontend build fails, or the dev proxy does not reach the API

**Symptom.** `npm run build` or the Docker web build fails; or the dev server loads but every API call fails, with `[api-proxy]` or `[uploads-proxy]` lines in the terminal.

**Likely cause.** For builds: `npm ci` (used by `frontend/Dockerfile`) requires `package-lock.json` to match `package.json` exactly, and the image builds on Node 20. For the proxy: `frontend/vite.config.js` forwards `/api` and `/uploads` to `http://127.0.0.1:5200`, so the API must be running on port 5200 — the port is deliberately 5200 (macOS reserves 5000 for AirPlay) and the target is deliberately `127.0.0.1` (resolving `localhost` inside Node can hang the proxy).

**Diagnostics.**

```bash
node -v && npm -v
cd frontend && npm ci            # reproduces the Docker build's install step
curl -i http://127.0.0.1:5200/api/storefront/config -H "Host: localhost"
```

**Safe fix.** Run `npm install` and commit the updated lock file when dependencies changed; start the API before the dev server; keep the proxy target as it is. Remember the dev server forwards the browser's Host header, which is what makes `{slug}.localhost:5173` select a store.

**Not this.** Do not add `changeOrigin` or rewrite the Host in the dev proxy — the API resolves the store from that header. Do not commit the frontend's built dist folder.

## 18. The white-label checks fail

**Symptom.** `npm test` fails in `frontend/src/whiteLabel.test.js`, or `dotnet test tests/Souq.ArchitectureTests` fails in `WhiteLabelSourceTests`, naming a file you just touched.

**Likely cause.** You hard-coded something that belongs to one store. The frontend test scans every `.js`, `.jsx`, `.css`, `.json` under `frontend/src` (test files excluded) plus `frontend/index.html` and rejects: the demo brand name (`Marka` / the Arabic form), a currency literal (`JOD`, the Arabic symbol, the word for dinar), and the demo store's contact details (`+962`, `marka.example`). The backend test scans every `.cs` file and every `appsettings*` under `src` — migrations excluded, and only `src/Souq.Infrastructure/Persistence/DbSeeder.cs` and `src/Souq.Domain/ValueObjects/CurrencyInfo.cs` are allowed to contain them.

**Safe fix.** Take the value from the store instead of writing it down:

- frontend: the store's name, currency, languages and contact details come from the store configuration through the tenant provider; format money with the store's currency rather than a literal.
- backend: use the store's currency from its tenant record (order lines and money values carry their currency), and the store's name and contact details from its settings.
- translation files: keep currency out of `frontend/src/i18n/locales/ar.json` and `frontend/src/i18n/locales/en.json`; the symbol is rendered from the store's currency at runtime.
- test fixtures may use anything: `*.test.js` files are excluded, and the backend test only scans `src`.

**Not this.** Do not add your file to the allow-list, do not widen the exclusions, and do not split or re-encode a string to slip past the regex. The rule exists because one build must render any store ([WhiteLabel.md](../08-FRONTEND/WhiteLabel.md), [ADR-0035](../11-ADR/0035-white-label-runtime.md)).

## 19. Swagger is not available on the Docker stack

**Symptom.** http://localhost:5201/swagger returns 404, although `docker-compose.yml` and the root `README.md` mention Swagger on that port.

**Likely cause.** Swagger is mapped only when the environment is `Development`; the compose stack runs as `Production`. The comment and the README table are stale.

**Safe fix.** Use Swagger against a locally run API (http://localhost:5200/swagger), or call the deployed API with `curl` and the right Host header.

**Not this.** Do not enable Swagger in production by changing the environment name — that would also re-enable the development tenant resolution, the default admin, the implicit fake gateway and log-only email.

## 20. The `api` container never becomes healthy (and `web` never starts)

**Symptom.** `docker compose ps` shows `api` as `starting` and then `unhealthy`; `web` never starts at all,
because it waits on `condition: service_healthy`.

**First, read the probe's own output** — it records every attempt:

```bash
docker inspect --format '{{json .State.Health}}' souq-api-1 | python3 -m json.tool
docker compose logs api | tail -50
```

**Likely causes, in the order worth checking.**

- **It is still starting.** Migrations and seeding run before the first request is served, so early failures are
  normal; the 90 s `start_period` exists for exactly this and failures inside it do not count against the
  retries. A fresh database on a slow machine can need longer than 90 s — raise the start period rather than
  the retry count.
- **The API never started.** A configuration failure stops the process before any endpoint exists (§3). The
  probe then reports "not ready" for a cause that has nothing to do with readiness — the logs say which key.
- **The database is unreachable or its schema is older than the image.** Both make `/health/ready` fail while
  `/health/live` still answers. Compare them:

  ```bash
  curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5201/health/live    # 200 = process is fine
  curl -s http://localhost:5201/health/ready                                     # {"status":"Unhealthy",...}
  ```

  Live 200 + ready unhealthy means the process is healthy and its database is not. A **stale schema** is the
  case to take seriously: it usually means the database was restored from a backup older than the deployed
  image ([Deployment.md](Deployment.md) §6 and §11). The reason is deliberately kept out of the HTTP body —
  `docker compose logs api | grep Readiness` names it.

**Not this.** Do not delete the `HEALTHCHECK` or drop `web`'s `service_healthy` condition to get the stack up:
that restores the old behaviour where nginx starts first and answers the first visitor with `502`, and it hides
a real failure rather than fixing it. Do not "fix" a stale schema by pointing the API at a different database.

