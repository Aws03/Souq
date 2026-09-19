# `scripts/` — operational scripts

Plain `bash` + `sqlcmd`, no build step and no runtime beyond what a deployment already has. Full contract,
runbook and the recorded drill: **[docs/09-OPERATIONS/BackupAndRestore.md](../docs/09-OPERATIONS/BackupAndRestore.md)**.

| Script | Does | Safe to run against production? |
|---|---|---|
| `backup.sh` | Database + uploads → one dated set with a manifest and SHA-256 sums. `--prune-older-than <days>` deletes completed older sets; without it nothing is deleted, because retention is the owner's policy and a legal question, not a default | **Yes** — reads only, unless `--prune-older-than` is passed |
| `restore.sh` | Restores a set into a database you name explicitly | **Only deliberately** — see the guards below |
| `release-gate.sh` | Composes the checks below into one verdict, and **counts anything unchecked as unchecked** rather than passing | **Yes** — read-only unless `--suites` |
| `audit-config.sh` | Answers whether an env file would produce a *safe* deployment for a named environment | **Yes** — reads a file, connects to nothing |
| `backup-verify.sh` | Answers whether the backups are *alive*: present, fresh, complete, checksum-clean, verifiable, and recently rehearsed | **Yes** — read-only, touches no database |
| `rehearse-restore.sh` | Restores into throwaway infrastructure, verifies it, destroys it | **Yes** — it never touches an existing database |
| `smoke-test.sh` | Runs the first-deployment checks against a running stack: health, tenant resolution and isolation, auth, catalog, basket, order, payment posture, proxy headers, HSTS behind TLS termination, correlation ids in logs, and no secrets in logs | **Yes** — it writes one test customer and one test order |
| `verify-least-privilege.sh` | Measures what the application actually needs from SQL Server, and proves the runtime identity is restricted | **Yes** — throwaway infrastructure only |
| `deploy.sh` | Puts a **named version** on an environment, waits for `/health/ready`, and rolls back — automatically when the schema did not move, and deliberately **not** when it did | **No** — it deploys, migrates and restarts a real stack |
| `demo-up.sh` | Brings up a **local demo stack** from a clean clone: generates its own secrets into a gitignored `.env.demo`, builds, waits for `/health/ready`, and prints the documented demo logins. Exists because the compose stack runs as `Production`, where `DbSeeder` refuses to create any administrator unless `Seed:*` is set — so `docker compose up` alone produces a stack nobody can sign in to | **No** — fake payment gateway, log-only email, demo catalogue; never for a stack real customers reach |
| `ci-local.sh` | Runs CI's fast job on **Linux** in a container, against exactly what CI would check out | **Yes** — a throwaway export and a container |
| `qa-second-store.py` | Provisions the "second store" fixture the cross-store browser journeys need — idempotently, through the real API including the invitation flow | **No** — it creates a store, an administrator and products |
| `sql/least-privilege-logins.sql` | Creates the runtime and migration identities (run once, by an admin) | Run deliberately, against the target server |
| `load-test.py` | Measures p50/p95/p99 and error rate per route against a running stack, and fails on a missed target. Python standard library only, so it needs no install — a tool that must be installed first is a tool that is not run (M16) | **No** — it generates load; run it against staging |
| `lib.sh` | Shared helpers; not run directly | — |

**Conventions these follow, and that anything added here should follow too:**

- **The password never appears on a command line** (`ps` is readable by other users). It comes from
  `SOUQ_SQL_PASSWORD` and is passed to `sqlcmd` through the environment.
- **No default destination for a destructive action.** `restore.sh` has no default target database, refuses an
  existing one without `--replace`, and without `--yes` makes you type the name. The failure being designed
  against is an operator restoring over live data quickly and confidently.
- **Verify before trusting.** A backup is rejected unless `RESTORE VERIFYONLY` passes; a restore checks every
  SHA-256 sum *before* touching the server, and compares row counts and the migration head *after*.
- **Destructive rehearsal happens only on infrastructure the script created**, and the teardown runs on failure
  and interruption too.
- **No secret is ever written into a backup set** — see §3 of the runbook for why.
- **Nothing here knows your hosting provider.** The off-site copy and the schedule are deliberately left out
  (§7 of the runbook) rather than guessed at.
