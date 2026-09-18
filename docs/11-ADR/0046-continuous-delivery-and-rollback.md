# ADR-0046: Continuous delivery — a tagged release, a deliberate migration step, and a rollback that refuses when it would make things worse

- **Status:** Accepted, 2026-09-18. Implements the M18 scope item calling for "an ADR for the CD strategy chosen" ([SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md)). Extends [ADR-0045](0045-production-edge-and-observability-stack.md), which drew the same line for the edge and observability stack.
- **Date:** 2026-09-18
- **Related modules:** none — this is about how every module reaches a server
- **Related ADRs:** [ADR-0007](0007-database-strategy.md) and R-18 (migrations as a deployment step); [ADR-0020](0020-configuration-and-secrets.md) (a setting that changes a deployment is chosen knowingly); [ADR-0045](0045-production-edge-and-observability-stack.md) (what the repository owns versus what a deployment owns)

## Context

`ci.yml` answers "is this change sound?". Nothing answered "how does it reach production, and how is it undone?".

The constraint that shapes every answer below is already measured and written down: **this system's data-moving migrations expand, migrate and contract in a single step**, so two application versions cannot share the database across one ([Migrations.md](../06-DATABASE/Migrations.md) §7, [Deployment.md](../09-OPERATIONS/Deployment.md) §11). Rolling deployments, blue-green and "just redeploy the old image" are therefore not available in general — which rules out most of what a CD design would otherwise reach for first.

## Problem

1. What triggers a release, and what names the thing released?
2. Where do migrations run, given R-18?
3. What does rollback mean here, when the schema may have moved?
4. How much of this can be *proven* when there is no server to deploy to?

## Decision

### 1. A release is a SemVer tag; images carry the version and the commit

`release.yml` triggers on `v*`. The tag must match `vMAJOR.MINOR.PATCH[-prerelease]` or the run fails at the first step — a free-form tag becomes an image name that cannot be ordered or compared.

Each image is pushed twice: `:v1.4.0` and `:sha-<commit>`. The version is what a person says out loud; the digest-pinned commit tag is what an investigation uses when there is any doubt that a tag was moved.

**`docker-compose.yml` now carries `image:` with a version variable** (`${SOUQ_IMAGE_API:-souq-api}:${SOUQ_VERSION:-dev}`). Before this, deployment meant "build from whatever source is present", and that is *unrollbackable by construction*: what was running a minute ago never had a name.

### 2. The gate runs before anything is built, and it is not the only gate that matters

`release.yml`'s first job builds with warnings as errors and runs all four suites including the integration suite over real SQL Server. Nothing is built or pushed from a red tree.

**This does not close TD-31.** Branch protection is a GitHub setting no file in this repository can contain; without it a red run can still be merged to `main` and then tagged. The workflow says so in its own header rather than letting its existence read as a closure. The owner action is in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md).

### 3. Migrations are a release artifact, and the deliberate step is the supported path

The migration bundle is built **in the pipeline**, not on the deployment host: the host has no SDK and no source, and must not have them. The bundle is self-contained and needs nothing but a connection string, which is exactly what makes `Database:MigrateOnStartup=false` usable — *migrate, verify, then roll out*, rather than the three collapsing into the single event of deploying (R-18).

This was **rehearsed, not assumed**: the bundle was built for Linux, run inside the container network against the running database, stepped the schema back one migration and forward again, and then driven through `deploy.sh --migrate-bundle` with `MigrateOnStartup=false` end to end.

### 4. Rollback is conditional on the schema, and says so out loud

`scripts/deploy.sh` reads `__EFMigrationsHistory` before and after the deployment, and then:

| Situation | What happens | Why |
|---|---|---|
| Deployment healthy | reports success; warns if the schema moved, because rollback *from here on* needs a restore | the warning arrives while there is still time to act on it |
| Failed, schema unchanged | **automatic rollback** to the previously running tag, then re-verified | the old image and the current schema match; this is safe and fast |
| Failed, schema moved **or unreadable** | **refuses to roll back**, and prints the restore path and the previous version | an old image on a newer schema reads columns that no longer exist — worse than the outage being escaped |

The third row is the whole point. A rollback that always fires looks better in a diagram and is the more dangerous system. "Unreadable" is deliberately grouped with "moved": a deployment that cannot establish what the schema did has not earned the right to act automatically.

"Healthy" means `/health/ready` answered 200 — and, when a base URL is given, that the smoke test passed too. Container startup is not health; `docker compose up` reports that a container started, which is a different claim from the application serving a request, and the difference is the entire subject of a failed deployment.

### 5. A local Linux gate, because the macOS/Linux gap is where defects were actually hiding

`scripts/ci-local.sh` runs the `fast` job's steps inside the .NET SDK container against `git ls-files` content — exactly what CI would check out. It exists because M18 found **four** real defects that a local `dotnet test` could never surface:

1. A regex counting frontend tests returned different results on macOS and Linux for identical bytes on identical .NET, because a greedy `.*` inside an *optional* group is resolved differently by the engine's auto-atomicity optimization. The generated inventory is committed, so CI was red on every push.
2. Two duplicate `using` directives — warnings locally, errors under `-warnaserror`.
3. `backup-verify.sh` computed backup age with `python3` and, on a host without it, **skipped the age check and reported success** — the backup alarm failing open on exactly the kind of minimal host a backup job runs on.
4. A flaky test: `SouqMetricsTests` asserted exact equality on measurements from a process-global `Meter` that parallel tests also emit into.

None of these was visible on the development machine. That is the argument for the harness, and it is an argument from evidence rather than from principle.

## Alternatives considered

- **Deploy on every merge to `main` (continuous deployment).** Rejected: with expand-migrate-contract migrations, every deployment is potentially a schema event, and a schema event that nobody chose the moment for is the one that finds out the backup was stale. A tag is a human saying "now", which is the correct amount of ceremony for this system's migration shape. Revisit if migrations become additive-only by policy.
- **Blue-green or rolling deployment.** Rejected on measurement, not taste: [Migrations.md](../06-DATABASE/Migrations.md) §7 establishes that two versions cannot share this database across a data-moving migration, and five such migrations already exist. Blue-green here would mean two versions live simultaneously — precisely the unsafe state. This becomes available only if migrations are made backward-compatible by construction (expand and contract split across two releases), which is a real option later and a much larger change than a pipeline.
- **Always roll back automatically on failure.** Rejected: see §4. It is the behaviour that reads best and the one that turns a bad deployment into a data incident.
- **Kubernetes, Helm, ArgoCD, or a managed PaaS.** Rejected for now: the deployment topology is one host running compose ([Deployment.md](../09-OPERATIONS/Deployment.md) §1), and Stage 2 of [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md) is not reached. Adopting an orchestrator would add a platform to operate in order to solve problems this system does not yet have, against the standing rule in [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §5. `deploy.sh` is deliberately a script that any orchestrator can later call or replace.
- **Deploy by `git pull` on the server and rebuild there.** Rejected: it puts source and an SDK on the production host, makes every deployment a build that can fail differently than CI's, and leaves the previous state unnamed — the thing that makes rollback impossible.
- **Skip the local Linux gate and rely on CI.** Rejected by circumstance and by evidence: GitHub Actions is currently billing-blocked, so CI cannot run at all, and the four defects above show that even a working CI catches these only *after* a push. The harness is about twenty lines of Docker invocation.

## Consequences

- A tagged commit produces named, reproducible artifacts: two images (version + commit) and a migration bundle kept for 90 days.
- Rollback is a single command with a defined, verified behaviour — including the case where it declines to act.
- **The honest limitation, stated plainly:** the SSH deployment step in `release.yml` has **never executed**, because there is no server and no secrets. Everything it calls has been exercised against the container stack — a real deployment, a real failure, a real automatic rollback, a real refusal to roll back, and the real migration bundle. What is missing is the host, not the mechanism. `release.yml`'s deploy job is gated on `vars.DEPLOY_HOST` so that a green run never implies a deployment that did not happen, and [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) keeps the item open.
- **Also unverified: the pipeline itself.** GitHub Actions has refused to start every job since before M13 for billing reasons, so `release.yml` has not run. This is an owner action, recorded in [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), and it is the reason `ci-local.sh` exists rather than a convenience.
