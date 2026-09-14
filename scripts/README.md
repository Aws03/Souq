# `scripts/` — operational scripts

Plain `bash` + `sqlcmd`, no build step and no runtime beyond what a deployment already has. Full contract,
runbook and the recorded drill: **[docs/09-OPERATIONS/BackupAndRestore.md](../docs/09-OPERATIONS/BackupAndRestore.md)**.

| Script | Does | Safe to run against production? |
|---|---|---|
| `backup.sh` | Database + uploads → one dated set with a manifest and SHA-256 sums | **Yes** — reads only |
| `restore.sh` | Restores a set into a database you name explicitly | **Only deliberately** — see the guards below |
| `rehearse-restore.sh` | Restores into throwaway infrastructure, verifies it, destroys it | **Yes** — it never touches an existing database |
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
