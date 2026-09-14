# Backups and restore

> **Status.** The mechanism exists and has been rehearsed: `scripts/backup.sh`, `scripts/restore.sh` and
> `scripts/rehearse-restore.sh`, with a recorded restore drill in §8. **Two things are still yours to decide and
> operate:** where copies are kept off this machine, and how often the job runs (§7). Until those exist, this
> repository gives you a *working procedure*, not a *running backup*.
>
> Read [Deployment.md](Deployment.md) first for the topology these commands act on.

## 1. Why this page exists

R-19 in [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) is the only release blocker whose failure mode is
**unrecoverable**. Everything else on the blocker list costs money, trust or time; losing the database loses the
business. It was also the easiest risk to pretend to solve — writing "take nightly backups" in a checklist
changes nothing. The test applied here is deliberately harsher: a backup counts only if it has been **restored**
and the restored system has been **observed serving traffic**.

## 2. What must be captured, or the restore is incomplete

| Asset | Where it lives | Lose it and… |
|---|---|---|
| The `SouqDb` database | `souq_db_data` volume | orders, stores, users, the outbox and the encrypted store payment keys are gone |
| Uploaded media | `souq_uploads` volume | every product image and store logo 404s; the database still references them |
| Secrets | your secret store, **never the backup set** | see §3 — `SECRETS_KEY` is the one that cannot be regenerated |

These are three separate systems with three different lifetimes. A database backup alone restores a shop whose
every image is broken; media alone restores nothing. `scripts/backup.sh` captures the first two into one dated
**set** so they cannot drift apart.

## 3. Secrets are deliberately not in the backup set

The backup set contains no secret, and no option adds one. A backup is the artifact most likely to be copied to
a laptop, a shared drive or an object store with loose permissions; putting the key **and** the ciphertext in
the same file means the encryption of store payment keys protects nothing the moment one copy leaks.

What the set *does* record is `secrets_key_id_required` — the key's **id**, not its value. That is what tells a
restoring operator which key they need to go and fetch.

| Secret | If you still have it | If you lost it |
|---|---|---|
| `SECRETS_KEY` | store payment keys decrypt normally | **permanent.** Every store's Stripe key is unreadable; each store must re-enter its keys before it can take payments |
| `JWT_KEY` | sessions survive | every signed-in user is signed out; no data lost |
| Database password, provider keys | normal operation | reissue from the provider |

**So the real rule is:** the secret store must be backed up too, on its own schedule, in its own place. A
restore drill that does not include "fetch `SECRETS_KEY` from where it actually lives" has not rehearsed the
part most likely to fail at 3 a.m.

## 4. The contract

What an operator is committing to. **The three rows marked ⚠ are not engineering decisions** — they set how
much data the business accepts losing, and they need the owner's sign-off
([ProductionReleaseChecklist.md](ProductionReleaseChecklist.md) §18).

| Question | This repository's answer |
|---|---|
| **What** | The database and the uploads volume, as one dated set with a manifest and SHA-256 sums |
| **How often** | ⚠ **Owner decision.** The scripts are schedule-agnostic. See the RPO note below |
| **Where** | ⚠ **Deployment-specific.** `backup.sh` writes to a local directory only. Copying it off the machine is §7 and is the step this repository cannot make for you |
| **How many copies** | ⚠ **Owner decision.** A copy on the same host as the database is not a backup — it dies with the host |
| **Protected how** | The set is readable by whoever can read the directory. It contains customer names, addresses, emails and order history. Treat it as the production database, because it is |
| **Retention** | ⚠ **Owner decision**, and a legal one where customer data is involved. Nothing in the scripts deletes anything |
| **Integrity** | Enforced, not assumed: `BACKUP … WITH CHECKSUM`, then `RESTORE VERIFYONLY` before the backup is accepted; SHA-256 per file; `restore.sh` re-checks every sum **before** it touches the server |
| **Restore procedure** | §6, with guards described there |
| **Who** | Whoever holds both the backup location *and* the secret store. Neither alone can complete a restore (§3) |
| **RPO** (data you accept losing) | = your backup interval. Nightly means "up to a day of orders." There is no log shipping or point-in-time recovery here: the database runs in the default recovery model and only full backups are taken |
| **RTO** (time to be back up) | Measured in the drill: the database restored and the application served traffic in well under a minute on a small dataset (§8). For real volumes, measure it — do not extrapolate |
| **Tenant vs platform data** | One database holds every store, so a restore is **all-or-nothing across all tenants**. Restoring one store to an earlier point while others keep running is **not supported** and should not be improvised during an incident — see §9 |
| **Migrations after restore** | The application migrates on startup, so an older database is upgraded the moment the API boots. `/health/ready` reports a schema older than the image as **not ready**, which is what stops a half-restored deployment from taking traffic ([Deployment.md](Deployment.md) §6) |

## 5. Taking a backup

```bash
export SOUQ_SQL_PASSWORD='…'          # never on the command line; it would be visible in ps
export SECRETS_KEY_ID='primary'       # recorded in the manifest — the id, not the key

./scripts/backup.sh \
  --container souq-db-1 \
  --uploads-container souq-api-1 \
  --out /var/backups/souq
```

Against a SQL Server that is not in a container, drop `--container` and pass `--server`, `--user` and a
`--server-backup-dir` that both the server and this machine can see.

It exits non-zero unless `RESTORE VERIFYONLY` passed, so a failed job is loud rather than a directory of
unreadable files discovered months later. The result:

```
souq-backup-20260914T185001Z/
├── database.bak        BACKUP … WITH INIT, CHECKSUM, COMPRESSION
├── uploads.tar.gz
├── manifest.txt        what it is, and what to compare after restoring
└── SHA256SUMS
```

The manifest is the part that makes a restore verifiable rather than hopeful — it records the migration head,
the row counts of the seven tables that matter, the source server, the SQL Server version and the required
secrets key id.

## 6. Restoring

```bash
./scripts/restore.sh \
  --set /var/backups/souq/souq-backup-20260914T185001Z \
  --target-database SouqDb_restored \
  --container souq-db-1 \
  --restore-uploads-to /srv/souq/uploads
```

After restoring it compares the migration head and row counts against the manifest and **exits non-zero if they
disagree**, so "it restored" and "it restored correctly" are not the same exit code.

**The guards, and why each exists.** Every one of these was tested by trying to defeat it:

- **No default target database.** The name is typed every time. There is no flag that means "the usual place",
  because the failure being prevented is an operator restoring last week's data over a live database at speed.
- **An existing target is refused** unless `--replace` is passed deliberately.
- **Without `--yes` you must type the target's name.** Anything else aborts with nothing changed.
- **Checksums are verified before the server is touched**, so a corrupted or modified set fails while the live
  database is still intact.

**Restoring over a live production database is an operational decision, not a script step.** Stop the API
first — it migrates and runs background jobs on startup, and both write. The order is: stop `api` → restore →
restore uploads → fetch the secrets → start `api` → watch the startup log and `/health/ready`.

## 7. The deployment-specific step (the part this repository cannot do)

`backup.sh` writes to a local directory and stops. That boundary is deliberate: the copy step depends on a
hosting provider this repository does not know, and inventing one would mean inventing credentials, a region and
a retention policy on the owner's behalf.

**What is left to wire up, once, wherever this is deployed:**

1. **Copy the set off this machine** — object storage, a different host, anything not sharing a failure domain
   with the database. One line appended to the job (`aws s3 sync`, `rclone copy`, `scp`).
2. **Encrypt at rest** if the destination is not already encrypted and access-controlled. The set is customer
   data in the clear (§4).
3. **Schedule it**, e.g. nightly:

   ```cron
   0 3 * * *  cd /srv/souq && SOUQ_SQL_PASSWORD="$(cat /run/secrets/db)" \
              ./scripts/backup.sh --container souq-db-1 --uploads-container souq-api-1 \
              --out /var/backups/souq >> /var/log/souq-backup.log 2>&1
   ```

4. **Alert when it fails.** The script's exit code is the signal; nothing here delivers it to a human.
5. **Apply the retention policy** from §4 — nothing deletes old sets.

**If you use a managed database** (Azure SQL, RDS, Cloud SQL), its own automated backups and point-in-time
restore replace the `database.bak` half of this, and are better than it — they give point-in-time recovery,
which full backups alone cannot. `BACKUP DATABASE … TO DISK` is typically unavailable there. **The uploads and
the secrets remain entirely yours**, and are the half most often forgotten: turn on the provider's backups,
then keep using this page for §2's other two assets and for the drill in §8.

## 8. The drill

A backup you have not restored is a hypothesis. `scripts/rehearse-restore.sh` turns it into evidence, and is
safe to run whenever you like, because it never touches an existing database: it creates a SQL Server container
with no persistent storage, restores into it, checks it, optionally boots the application against it, and
destroys everything — including when it fails or is interrupted.

```bash
./scripts/rehearse-restore.sh --set /var/backups/souq/souq-backup-… --api-image souq-api
```

Beyond the manifest comparison it asserts that the composite tenant foreign keys survived, that no order row
lost its tenant, that no constraint came back untrusted, and that the database is `ONLINE`. With `--api-image`
it goes further than any SQL check can: it runs the real application against the restored database and waits
for `/health/ready`, which passes only if the schema matches the code.

**Recorded drill — 2026-09-14.** Set `souq-backup-20260914T185001Z` (20 migrations, head
`20260911200431_Phase14Notifications`; `Tenants:1, Users:2, Customers:1, Products:8`), taken from a throwaway
stack that included a customer registered *after* seeding, so the drill proved more than "the seeder runs".

**The source stack was destroyed before the restore**, so the set stood alone. Then: all SHA-256 sums matched;
the restore completed; migration head and all seven row counts matched the manifest exactly; the five structural
checks passed; the API booted against the restored database and reported ready in **3 s**; a storefront request
resolved its store by host name and returned **200** with the restored catalog. The disposable infrastructure
was torn down, and the repository's own `souq_db_data` volume was never involved.

Two real defects were found and fixed by running this rather than reasoning about it: `USE [db]` prints an
informational line that was being captured as a query result and silently corrupted three manifest fields, and
`docker cp` leaves a file owned by a host uid with mode 640, which SQL Server (running as `mssql`) cannot read —
producing "Access is denied" on a file plainly present.

**Re-run the drill** on a schedule and after any change to the schema, the container images or the storage
layout. A restore path is only known to work on the day it was last exercised.

## 9. What this does not give you

- **No point-in-time recovery.** Full backups only; your exposure is one backup interval (§4). Log backups
  would change that and are a deliberate future step, not an oversight.
- **No per-tenant restore.** One database serves every store, so recovery is all-or-nothing. Restoring one
  store's rows into a live database means surgical, hand-written DML across tenant-scoped tables with composite
  keys — under incident pressure, on production. If per-tenant recovery becomes a product requirement, it needs
  a design (export per tenant, or a tenant-aware archive), not a heroic script. Raise it as a decision.
- **No automated scheduling, off-site copy, encryption or alerting** — §7, and the reason R-19 is not yet
  closed in [ReleaseReadiness.md](ReleaseReadiness.md).
- **No test that the *secret store* is recoverable.** That lives outside this repository (§3).
