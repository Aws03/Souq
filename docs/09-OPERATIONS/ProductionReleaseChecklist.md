# Production release checklist

> **What this page is:** the list you work through before letting real customers reach a Souq deployment, and again before each later release. Every item is verifiable — a command, a log line or a response, not an opinion.
> **Read first:** [ReleaseReadiness.md](ReleaseReadiness.md) — nothing here matters while a P0 blocker is open. **Settings:** [Configuration.md](Configuration.md) · **Topology:** [Deployment.md](Deployment.md) · **When it breaks:** [Troubleshooting.md](Troubleshooting.md).

**Marking**

| | Meaning |
|---|---|
| **REQUIRED** | Do not serve real customers without it. Money, data, security or law |
| **RECOMMENDED** | Strongly advised; skipping it is a decision to write down |
| **OPTIONAL** | Depends on scale or the deal |

"Log" means the API's startup output: `docker compose logs api | head -40`.

---

## 1. Infrastructure

- [ ] **REQUIRED** The host has at least 2 GB of memory free for SQL Server alone, plus headroom for the API and nginx.
- [ ] **REQUIRED** The `5201:8080` API port mapping is **removed** from `docker-compose.yml` for a public deployment. A client reaching the API directly appears to come from the Docker bridge, which is inside the default trusted proxy range, and can then forge `X-Forwarded-For`.
- [ ] **REQUIRED** The database port is not published (it is not, by default — keep it that way).
- [ ] **RECOMMENDED** Image tags are pinned to digests rather than floating (`2022-latest`, `sdk:10.0`, `aspnet:10.0`, `nginx:1.27-alpine`, `node:20-alpine`), so two deployments of one commit cannot differ.
- [ ] **RECOMMENDED** The API container runs as a non-root user (`src/Souq.API/Dockerfile` has no `USER` instruction today).
- [ ] **OPTIONAL** Resource limits and restart policies are set per service.

## 2. Configuration

- [ ] **REQUIRED** `ASPNETCORE_ENVIRONMENT` is `Production`: `docker compose exec api printenv ASPNETCORE_ENVIRONMENT`. Every "local only" convenience keys off this.
- [ ] **REQUIRED** `.env` exists, is git-ignored (`git check-ignore .env`), and every value is unique to this environment.
- [ ] **REQUIRED** The startup log's `Adapters selected:` line names the adapters you expect.
- [ ] **REQUIRED** Every `Configuration warning:` line in the log is read and understood. Each one means "this works, but is not safe for real customers".
- [ ] **RECOMMENDED** `TRUSTED_PROXY_NETWORKS` covers the proxy network and nothing else. Never `0.0.0.0/0`.
- [ ] **RECOMMENDED** `FRONTEND_URL` is the storefront's real public origin — its scheme and port are used for links in messages that have no request behind them.

## 3. Secrets

- [ ] **REQUIRED** `JWT_KEY` is at least 32 bytes of fresh randomness (`openssl rand -base64 48`), not reused from another environment. A short key refuses the start by design.
- [ ] **REQUIRED** `DB_SA_PASSWORD` is strong and unique.
- [ ] **REQUIRED** No secret appears in `src/Souq.API/appsettings.json` or `src/Souq.API/appsettings.Production.json`. Both ship empty secret values.
- [ ] **REQUIRED** `SECRETS_KEY` (base64 of 32 bytes) is set if any store will connect its own payment account, and is **backed up somewhere other than the server**. Losing it makes every stored store key permanently undecryptable.
- [ ] **REQUIRED** `SECRETS_KEY` is never changed in place; rotation follows [Configuration.md](Configuration.md) §10.
- [ ] **RECOMMENDED** Secrets are delivered by the platform's secret mechanism rather than a file on disk.

## 4. Database

- [ ] **REQUIRED** The application connects with a **least-privilege login** owning only `SouqDb` — not `sa`. This is an open blocker; see [ReleaseReadiness.md](ReleaseReadiness.md) R-12.
- [ ] **REQUIRED** The database volume is on durable storage, not an ephemeral container filesystem.
- [ ] **RECOMMENDED** Collation and timezone behaviour are confirmed: the application stores UTC and injects its clock, so the database's own timezone should not matter — verify rather than assume.

## 5. Migrations

- [ ] **REQUIRED** A backup exists **before** the deployment that will apply migrations. Migrations run at startup, so deploying *is* migrating.
- [ ] **REQUIRED** The migration set has been applied once to a copy of production data, not only to an empty database.
- [ ] **REQUIRED** Exactly one API instance starts first on a release that carries migrations. Two instances starting together race.
- [ ] **RECOMMENDED** The startup log shows no migration error, and the expected migration is the last applied one.
- [ ] **RECOMMENDED** You know which migrations in this release are **not** additive, because an older image cannot roll them back — only a restore can.

## 6. Seeding and admin accounts

- [ ] **REQUIRED** No demo data reaches production. Confirm the catalog is empty of the demo products after the first start.
- [ ] **REQUIRED** `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` (≥ 12 characters) and `SEED_PLATFORM_OWNER_EMAIL` / `SEED_PLATFORM_OWNER_PASSWORD` are set for the **first start only**.
- [ ] **REQUIRED** Both accounts sign in successfully — the platform owner on the platform host, the store admin on a store host.
- [ ] **REQUIRED** All four variables are then removed from `.env` and the stack restarted. Seeding never rewrites an existing account's password, so the accounts survive; the "No … account seeded" warnings that follow are expected.
- [ ] **RECOMMENDED** The seeded default store is adopted, cleaned or archived deliberately (`POST /api/platform/tenants/{id}/status` with `Archive`).

## 7. Payments

- [ ] **REQUIRED** The log says `payments Stripe`, never `payments Fake`. The fake gateway marks every order paid without money.
- [ ] **REQUIRED** `STRIPE_SECRET_KEY` and `STRIPE_PUBLISHABLE_KEY` are **live** keys.
- [ ] **REQUIRED** `STRIPE_WEBHOOK_SECRET` is set. Without it the log carries the "confirmation depends on the customer's browser" warning, and a shopper who closes the tab leaves an order pending forever.
- [ ] **REQUIRED** The Stripe dashboard's webhook endpoint points at a **store host** of this deployment, `POST /api/payments/webhook`, over https.
- [ ] **REQUIRED** `PAYMENTS_ALLOW_TEST_MODE_STORE_ACCOUNTS` is unset or `false` — no warning line in the log.
- [ ] **REQUIRED** If any store prices in a **three-decimal currency** (JOD, BHD, IQD, KWD, LYD, OMR, TND): the minor-unit question is settled against the real account before the first live charge. Getting it wrong charges a tenth of the price. See [ReleaseReadiness.md](ReleaseReadiness.md) R-01.
- [ ] **REQUIRED** One end-to-end live test: a real card, a real order, then a refund of it from the order screen.
- [ ] **RECOMMENDED** A declined card is tested too — the order must stay open for a retry, and must not be cancelled while its intent is alive ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)).

## 8. Email

- [ ] **REQUIRED** The log says `email Resend`, `email Brevo` or `email Gmail` — never `email Log` in production.
- [ ] **REQUIRED** Exactly one provider key is set. `GMAIL_APP_PASSWORD` is empty unless Gmail is genuinely the provider.
- [ ] **REQUIRED** The sender address is real and verified for the provider's domain.
- [ ] **REQUIRED** A real password reset arrives in a real inbox, and its outbox row shows `ProcessedAt` set.
- [ ] **REQUIRED** A real order confirmation arrives with the correct total and its lines.
- [ ] **RECOMMENDED** SPF and DKIM are configured for the sending domain, or messages will be filtered.

## 9. Tenant setup and domains

- [ ] **REQUIRED** `PLATFORM_HOST` is the real platform domain, resolves to this deployment, and store endpoints answer 404 there.
- [ ] **REQUIRED** Every store domain is added through the platform API (`POST /api/platform/tenants/{id}/domains`, then `…/domains/{host}/primary`) and DNS points at the deployment.
- [ ] **REQUIRED** An unmapped host answers `404 StoreNotFound` — there is no fallback store in production.
- [ ] **REQUIRED** A token issued for store A is rejected on store B's host.
- [ ] **RECOMMENDED** `DEFAULT_TENANT_HOSTS` is set deliberately; it binds hosts to the **default** store only.

## 10. HTTPS

- [ ] **REQUIRED** TLS terminates in front of the stack and the browser reaches the storefront over https.
- [ ] **REQUIRED** The API sees `https`. nginx sets `X-Forwarded-Proto` from **its own** plain-http listener, which overwrites an outer terminator's header — verify by requesting a password reset and reading the link's scheme.
- [ ] **REQUIRED** `Auth:RefreshCookie:Secure` stays `true`. The refresh and basket cookies are `Secure` and will not be sent over plain http.
- [ ] **RECOMMENDED** HSTS and the security header set are configured at the terminator (none is set by this repository).

## 11. Uploads

- [ ] **REQUIRED** A product image uploaded through the admin is then **retrievable** through the public storefront URL. This is the single check that catches the proxy's host forwarding.
- [ ] **REQUIRED** The uploads volume is durable and is part of the same backup as the database — the files are not in the database.
- [ ] **RECOMMENDED** A store's file is confirmed **not** served on another store's host.

## 12. Logging and observability

- [ ] **REQUIRED** Logs are captured somewhere durable; the containers' stdout is not a retention policy.
- [ ] **REQUIRED** A spot check of the log shows no token, reset link, password or `Authorization` value.
- [ ] **REQUIRED** `X-Correlation-Id` is present on responses, and the same id appears in the log line for that request.
- [ ] **RECOMMENDED** The proxy's access log is treated as sensitive, or configured not to record query strings and token-bearing paths.
- [ ] **OPTIONAL** Metrics and traces are exported.

## 13. Health checks and monitoring

- [ ] **REQUIRED** Something outside the stack checks that the storefront answers, and alerts a human when it does not. The API exposes **no health endpoint**; the closest smoke check is `GET /api/storefront/config` for a known store host.
- [ ] **RECOMMENDED** An alert fires on a sustained rise in 5xx responses.
- [ ] **RECOMMENDED** Someone reviews dead outbox rows periodically — there is no screen and no alert for them.
- [ ] **RECOMMENDED** A query watches for the reconciliation signal "a `Succeeded` payment on a `Cancelled` order" ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)).

## 14. Backups

- [ ] **REQUIRED** A backup exists and covers **all three** together: the database, the uploads volume, and the secrets. Any one alone is an incomplete restore.
- [ ] **REQUIRED** A restore has been **rehearsed** onto a separate environment, and the result was checked by signing in and opening an order.
- [ ] **REQUIRED** A retention period is decided and the backups are stored off the host.
- [ ] **RECOMMENDED** The restore procedure is written where an on-call person will find it at 3 a.m.

## 15. Rollback

- [ ] **REQUIRED** You know that deploying an older image does **not** roll the schema back — `Migrate` only moves forward.
- [ ] **REQUIRED** The rollback plan is "restore the backup, then deploy the matching image", and the backup from before this deployment is identified.
- [ ] **RECOMMENDED** The no-deployment levers are known: turn an optional module off for one store, suspend one store, or set a background sweep's interval to `0`.

## 16. Security

- [ ] **REQUIRED** Tenant isolation is spot-checked in the live deployment: a second store, its own admin, and a request for the first store's order id answers 404.
- [ ] **REQUIRED** Rate limits are in place at the edge if the API port is reachable from anywhere but the proxy.
- [ ] **REQUIRED** The platform host is not publicly advertised and its owner account uses a strong unique password.
- [ ] **RECOMMENDED** A dependency vulnerability scan has been run against both the .NET and npm dependency sets.
- [ ] **RECOMMENDED** The `.env` file's permissions restrict it to the deploying user.

## 17. Testing before the release

- [ ] **REQUIRED** The full gate passes on the exact commit being deployed — see [DeveloperQualityGates.md](DeveloperQualityGates.md).
- [ ] **REQUIRED** A manual pass over the money path on the real deployment: browse, add to basket, check out, pay, receive the email, track the order, refund it.
- [ ] **RECOMMENDED** The same pass as a **second** store on its own domain, to prove isolation and branding in the deployed stack rather than in tests.

---

## Sign-off

A release is ready when every **REQUIRED** box is ticked, every skipped **RECOMMENDED** box has a written reason, and [ReleaseReadiness.md](ReleaseReadiness.md) shows no open P0. Record who signed off and against which commit.
