# Incident response

> **For the person holding the pager at 03:00.** Short, ordered, and written to be read while something is
> broken. It does not repeat [Troubleshooting.md](Troubleshooting.md) — that page diagnoses named symptoms, this
> one decides *what kind of trouble you are in* and what must not be done while you find out.
>
> **The one rule that outranks the rest:** capture evidence before you change anything (§2). A restart that
> fixes the symptom and destroys the cause turns a one-hour incident into a recurring one.

## 1. First five minutes: which layer is broken?

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://API/health/live     # process alive?
curl -s                                    https://API/health/ready  # can it serve?
docker compose ps                                                   # what is actually running
```

| live | ready | Means | Go to |
|---|---|---|---|
| 200 | 200 | The API is healthy — the problem is the proxy, DNS, TLS, or the browser | §5 |
| 200 | `Unhealthy` | The process is fine, its database is not — **do not restart the API** | §4 |
| no answer | — | The process is down or never started | §3 |
| 200 on the web container but the store is broken | — | You are probing nginx, not the API. `/health/ready` there returns the SPA with 200 forever | [Deployment.md](Deployment.md) §6 |

**Check the backups before you touch anything you might need to undo:**

```bash
./scripts/backup-verify.sh --dir /var/backups/souq --require-drill-within-days 30
```

**Then run the smoke test** — it is faster than guessing, and it says which of thirty-one things is wrong:

```bash
./scripts/smoke-test.sh --base-url https://STORE --api-url http://API:8080 --store-host STORE
```

## 2. Capture before you touch

In this order, because each step is destroyed by the next person's fix:

```bash
docker compose logs --no-color api > incident-api.log        # before any restart
docker compose logs --no-color web > incident-web.log
docker inspect --format '{{json .State.Health}}' souq-api-1 > incident-health.json
```

From the customer, get the **`X-Correlation-Id`** — it is on every response including errors, and it is the
same id in the log line, the error body's `traceId` and the log scope. One grep gives the whole request:

```bash
grep '<correlation-id>' incident-api.log
```

If there is no id (the customer only has a screenshot), search by store and time: every log line inside a
request carries `TenantId` and, when signed in, `UserId`.

## 3. The API will not start

Read the first fifty lines of the log — startup failures are deliberately loud and name the setting
([Configuration.md](Configuration.md) §2). In order of likelihood:

| Line mentions | Cause | Fix |
|---|---|---|
| `Jwt:Key`, `ConnectionStrings:Default`, `Payments:Provider`, `Resend:ApiKey` | a missing or placeholder setting | set it; the API refuses to start rather than run degraded |
| `Jwt:Key` **and** the value looks like the example | `.env.example` was copied without editing | generate a real key — the placeholder is rejected on purpose |
| a login failure for `souq_app` / `souq_migrator` | credentials or the database do not exist yet | [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md) §5 — neither identity may create a database |
| a migration error | the schema is mid-change | §4, and **do not** deploy again on top |

## 4. Ready is failing: the database

`/health/ready` reports `Unhealthy` for exactly two reasons, and they need opposite responses.

- **Unreachable.** The database is down, full, or unreachable from the API. Fix the database. The API will
  recover on its own; restarting it changes nothing.
- **Schema older than the image.** Almost always **a backup was restored that predates the deployed build**.
  The detail is in the log (`grep Readiness`), never in the HTTP body. **Do not** point the API at another
  database to make the error go away. Either deploy the image matching that backup, or let the newer image
  migrate it forward — after taking a backup of the restored state ([Migrations.md](../06-DATABASE/Migrations.md) §7).

**If data is missing or wrong**, stop and read [BackupAndRestore.md](BackupAndRestore.md) §6 before touching
anything. Restoring over a live database is an operational decision, not a script step, and the API must be
stopped first because it migrates and runs background jobs on startup — both of which write.

## 5. The store is down but the API is healthy

Work outwards: TLS terminator → nginx → API.

- **502 from nginx:** the API container is not ready. `web` waits for health on start, but not after it.
- **Every page 404 with `StoreNotFound`:** the host is not mapped to a store. The store is resolved from the
  Host header alone ([Troubleshooting.md](Troubleshooting.md) §6).
- **`503 StoreUnavailable`:** the store is suspended or archived — a deliberate state, not a fault.
- **Links in emails say `http://`, or HSTS never appears:** the terminator is not passing
  `X-Forwarded-Proto: https` ([Security.md](../07-SECURITY/Security.md) §4).
- **Images 404:** uploads are served from a volume; check it is mounted and that the request reaches the API
  rather than the SPA fallback.

## 5a. The deployment itself is the incident

The commonest incident is the one that just happened: a version was deployed and the site got worse.

- **`scripts/deploy.sh` already tried.** Read its last lines before doing anything: it waits for
  `/health/ready`, and on failure it either **rolled back automatically** (printing `DEPLOY FAIL (rolled back to
  …)`) or **refused to** and printed the restore path. It refuses when the schema moved or could not be read,
  and that refusal is deliberate — an old image against a newer schema reads columns that are gone, which is
  worse than the outage you are escaping ([ADR-0046](../11-ADR/0046-continuous-delivery-and-rollback.md)).
- **If it rolled back**, the environment is already on the previous tag and ready; the incident is now "why did
  that version fail", not "restore service". Capture `docker compose -p <project> logs api` for the failed
  version before redeploying anything.
- **If it refused**, do not swap the image tag by hand. The order is: stop `api`, restore the pre-deployment
  backup (`scripts/restore.sh --set …`), then deploy the version that matches that schema
  ([Deployment.md](Deployment.md) §11 and §13).
- **Which version is actually running:** `docker inspect --format '{{.Config.Image}}' $(docker compose -p
  <project> ps -q api)`. The tag is the answer, not a deployment log — a running container does not lie about
  what it runs.
- **A deployment that never started** leaves the previous version running and healthy. That is the designed
  behaviour of stop-then-start with a health gate, not a partial deployment.

## 6. Money incidents

These have a different priority: **money that moved but is not recorded is worse than an outage**, because the
outage is visible and this is not.

| Symptom | What it means | What to do |
|---|---|---|
| Customer charged, order still `Pending` | the confirmation did not arrive | the webhook is the authority; check it reaches the API and is signed. The customer's own page-close does not lose the payment |
| `Succeeded` payment on a `Cancelled` order | a late capture after the order closed | **expected and handled**: it is recorded rather than swallowed so it can be refunded ([ADR-0036](../11-ADR/0036-payment-intent-state-machine.md)). Log line is at error level on purpose — it wants a human, it is not a bug |
| Two orders from one checkout | the known duplicate-submit behaviour (F-8) | no double charge; the duplicate expires and returns its stock and coupon. Confirm no second capture, then leave it |
| Payments unavailable for one store | that store's saved keys cannot be decrypted | `SECRETS_KEY` is missing or changed. **Do not rotate it further** — the old key is the only way to read existing keys ([BackupAndRestore.md](BackupAndRestore.md) §3) |

**Never** resolve a money incident by editing rows directly. The order and payment state machines have audited
transitions; a hand-edited row is invisible to them and to the next person.

## 7. Security incidents

| Situation | Immediate action |
|---|---|
| A secret is believed leaked (`Jwt:Key`) | rotate it — every access token dies, every user signs in again. Cheap, do it early |
| `SECRETS_KEY` leaked | rotate the key **and** have every store re-enter its payment keys. Rotating alone makes existing ciphertext unreadable |
| A store's Stripe key leaked | revoke it at Stripe first, then re-enter it in the store |
| Suspected unauthorised access to a store | disable the account (sessions drop immediately — the security stamp rotates and refresh tokens are revoked in the same save), then read the audit log |
| A secret reached a log or a backup | treat the log/backup as the secret. Backups deliberately contain none ([BackupAndRestore.md](BackupAndRestore.md) §3) |

**Do not disable a store's last administrator** to "lock it down": that is refused by design, and it would
leave the store unmanageable. Suspend the **store** instead.

## 8. After it is over

1. **Write down what actually happened**, including the wrong turns — those are the useful part.
2. **If a log, a health check or an error message would have shortened it, add it** while you still remember
   what you needed. That is what [ADR-0018](../11-ADR/0018-observability.md) is for.
3. **If the fix was manual, ask why it could not have been automatic**, and put the answer in the right
   register rather than in a chat message: [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) for something
   that can hurt you again, [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) for something that made the fix
   slow.
4. **If it touched money or customer data,** check whether it changes an answer in
   [OwnerDecisions.md](OwnerDecisions.md).

## 9. What this repository does not give you

Said plainly so nobody discovers it mid-incident:

- **No alerting.** Nothing watches the health endpoints or the error rate; the first report will come from a
  customer (R-20).
- **Four counters exist; nothing exports them** (M17). `SouqMetrics` emits `souq.outbox.dead_lettered`,
  `souq.tenancy.cross_tenant_write_blocked`, `souq.auth.login_failed` (tagged by outcome) and
  `souq.search.log_dropped` through `System.Diagnostics.Metrics`, so **`dotnet-counters` can read them from a
  running process today** without deploying anything. What is missing is a destination, not instrumentation.
  Each also has a log line at the same instant — the counter answers "how often this hour", the log answers
  "what happened in this request".
- **No traces are exported**, though the W3C trace id is already in every log line and every error body.
- **No on-call rotation, escalation path or status page** — those are the owner's to define.
- **No automated backup.** If nobody scheduled `scripts/backup.sh`, the most recent backup is the last one a
  person took (R-19) — `scripts/backup-verify.sh` will tell you which, and how old it is.

**What the application does tell you, at every start.** Read the first lines of the log before assuming a
misconfiguration is invisible: it names the payment and email adapters actually chosen, warns if the database
identity can change the schema (least privilege not applied), warns if the default store is still unadopted,
and warns once if a proxy sent `X-Forwarded-Proto` that was not trusted — which is the silent cause of missing
HSTS and `http://` links in email.
