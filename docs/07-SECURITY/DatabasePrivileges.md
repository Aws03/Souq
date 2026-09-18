# Database privileges

> **Status.** The mechanism and the measurement exist: three identities, a script that creates two of them, and
> a harness that proves what the application actually needs (§4). **Applying them is a deployment action** — the
> Docker Compose bundle is a demo and still connects as `sa` (§6). R-12 stays open until a real deployment uses
> these identities.

## 1. The problem this solves

The application connected to SQL Server as `sa`: full control of every database on the server. In that shape a
SQL injection, a leaked connection string or a compromised container is not a bad day — it is the whole server,
including databases belonging to anything else hosted beside it. The blast radius of every other vulnerability
is set by this one setting.

## 2. Three identities, because they need different things at different times

| Identity | Used by | Needs | Roles |
|---|---|---|---|
| **Runtime** (`souq_app`) | the application, every request, always | read and write rows | `db_datareader`, `db_datawriter` |
| **Migration** (`souq_migrator`) | the startup migrator only | change the schema **and** move data | `db_ddladmin`, `db_datareader`, `db_datawriter` |
| **Administrative** | humans, restores, creating the above | everything | not in any application's configuration |

Why the migrator also needs read/write: ten migrations in this repository move data with raw
`migrationBuilder.Sql(…)` — backfilling `TenantId`, splitting `Customers` into `Users`, moving product columns
into translations and variants. DDL permission alone would fail partway through a release.

Why the migrator is **not** `db_owner`: it cannot drop the database, grant permissions, or change another
principal. It changes shape, nothing else.

## 3. Runtime and migration can differ even though migrations run at startup

Migrations are applied by the application when it boots (R-18), so both connections live in the same process.
That is *not* the same as needing the same identity. `ConnectionStrings:Migrations` is used **only** by the
startup migrator; everything afterwards uses `ConnectionStrings:Default`.

```
ConnectionStrings__Default    = …User Id=souq_app;…        ← every request, for the life of the process
ConnectionStrings__Migrations = …User Id=souq_migrator;…   ← used once, at startup, then not again
```

**Leave `ConnectionStrings:Migrations` unset and nothing changes** — it falls back to `Default`, exactly the
behaviour before the split. An existing deployment does not break by upgrading; it opts in when ready. The
startup log says which identity was used, so "we meant to split them" and "we did" are distinguishable.

One consequence worth knowing before it surprises you: **neither identity can create a database.** Creating it
is an administrative act (`scripts/sql/least-privilege-logins.sql` does it). Previously a missing database was
created silently by the first boot; now it is a loud startup failure, which is the better outcome — a silently
created empty database looks healthy and serves nothing.

## 4. Measured, not asserted

The roles above are not a guess at what "least privilege" should look like. `scripts/verify-least-privilege.sh`
measures it, on throwaway infrastructure it destroys afterwards:

1. Creates a server, applies `scripts/sql/least-privilege-logins.sql`, and starts the real application image
   with the two identities.
2. Exercises real paths: storefront config, product list, customer registration, sign-in, an authenticated
   admin read across modules, and a basket write.
3. **Greps the log for permission denials**, which is what catches the failures nobody sees — the outbox
   dispatcher and the periodic sweeps fail in the background, not in a response.
4. **Proves the identity is actually restricted**, which is the half that makes the rest mean anything: a
   passing workload proves nothing about privilege, since `db_owner` passes it too.

**Result, 2026-09-14.** Migrations applied and the application reported ready in 8 s. All six paths returned
200. **Zero permission denials in the log.** And the runtime identity was refused when it tried to create a
table, drop `Orders`, add itself to `db_owner`, create a database, and read a second application database on
the same server; it could not read any other login's password hash.

Two things it *can* do, both expected and neither worth chasing:

- **See that `sa` exists.** `sa` owns the database, and SQL Server always shows a database's owner to its users.
  `sa` exists on every SQL Server; this is not a disclosure.
- **Read general catalog metadata in `master`.** The `guest` user is enabled there by default for every login.
  No application data lives in `master`, and this is SQL Server's default posture rather than something these
  grants control.

**Re-verified 2026-09-18 (M17), and it was due.** This page asks for a re-run "after any schema change, new
background job, or anything that introduces raw SQL", and since the measurement above the codebase gained M13's
`SearchQueryLogs` table together with **two new background services** — the batch writer that drains the search
log into each store's tenant scope, and the retention sweep that deletes from it. Those are precisely the shape
the warning describes: a background job that needs a permission nobody granted fails silently, in no response.

The harness passed unchanged. Migrations applied under the migrator identity, the application was ready in 11 s,
all six paths returned 200, **zero permission denials in the log** — the new background services included — and
the reverse checks still refused the runtime identity a table create, a table drop, a self-grant, a database
create, another application database, and any other login's password hash. So `db_datareader + db_datawriter`
remains sufficient for everything M13 and M15 added, including a bulk `ExecuteDelete` (the retention purge) and
a write path that runs outside any request.

Re-run the harness after any schema change, new background job, or anything that introduces raw SQL — those are
exactly the changes that quietly need a permission nobody granted.

## 5. Applying it

```bash
sqlcmd -S <server> -U <admin> -C -i scripts/sql/least-privilege-logins.sql \
       -v AppPassword="…" -v MigratorPassword="…"
```

Passwords are passed in, never stored in the file — a default password in a Git-tracked script is precisely
what this page exists to remove. Then point the deployment at the two identities (§3) and **stop using the
administrative login in any application configuration**.

## 6. What is still open

- **The Compose bundle still connects as `sa`.** It is a self-contained demo that creates its own database on
  first boot ([Deployment.md](../09-OPERATIONS/Deployment.md) §1), and least privilege requires an
  administrative step *before* the application starts. Do not take it as the production posture.
- **No deployment uses these identities yet**, which is why R-12 is open rather than fixed in
  [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).
- **Password rotation is not solved here** — these are SQL logins with passwords in a connection string.
  Managed identities (Azure AD / IAM authentication) remove the password entirely and are the better answer
  where the host supports them; that is a deployment choice, not a code change.
