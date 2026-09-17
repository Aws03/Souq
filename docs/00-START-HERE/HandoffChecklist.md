# Handoff checklist

> **What this page is:** the practical checklist for handing Souq to another engineer or team. Tick it in order. Each item links to the document that holds the detail, so this page stays short and doesn't go stale by copying.
> **Read with:** [HandoffGuide.md](HandoffGuide.md), the narrative of what you are receiving and the order to learn it in.
> **Level:** L3. **Last verified against the repository:** 2026-09-17, branch `phase/17-production-hardening`.

## 0. The state you are receiving

At the last verification:
- **Branches.** All work is on `phase/17-production-hardening`. It contains every earlier phase branch (`phase/1a-…`, `phase/1b-…`, `phase/2-15-…`, `phase/16-…`) and is **136 commits ahead of `main`**, which holds only Phase 1A. **Nothing on this branch has been pushed** (it has no upstream) **or merged.** Deciding how it reaches `main` is the first decision a new owner makes.
- **Product.**
  - Roadmap Phases 1A–15 are complete.
  - Phase 16 (Storefront) is 🟡: product variants can't be chosen yet (P-08 decided, V1 groundwork built; V2 and V3 remain).
  - Phase 17 (Tenant admin dashboard) is ✅.
  - Phase 18 (Platform owner dashboard) is 🟡, waiting on owner decisions D-22 and P-07.
  - Phases 19–23 have not started.

  Source: [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md).
- **Release.** Not ready for a first paying customer. The open blockers and their owners are in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

Re-check the branch state yourself with `git branch`, `git log --oneline main..HEAD | wc -l` and `git status -sb`.

## 1. Repository access

- [ ] Access to the Git remote (`origin`), and agreement on who may push and merge. Nothing in this repository is pushed or merged by an agent without an explicit instruction (`AGENTS.md` §0.9).
- [ ] Branch protection on `main`, requiring the CI checks. CI runs but **blocks nothing** until this is switched on. The exact setting: [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md), "Branch protection".
- [ ] Licence and visibility decided: the repository is MIT today with a remote, an open decision (P-03).
- [ ] The receiving team has read `AGENTS.md` (the engineering contract) and [LearningPath.md](LearningPath.md) steps 00–04.

## 2. Tools and runtime versions

- [ ] **.NET 10 SDK.** Projects target `net10.0`; no *global.json* pins a patch version.
- [ ] **Node 22.12 or newer** (Vitest 5 requires it; CI and `frontend/Dockerfile` use Node 22).
- [ ] **Docker**, with enough free memory for SQL Server 2022 (see §6 for the integration tests' needs).
- [ ] **`dotnet-ef` 10.x** (`dotnet tool install -g dotnet-ef`; no tool manifest in the repository).

Detail: [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md) §1.

## 3. Database

- [ ] A SQL Server 2022 reachable on `localhost,1433` for local development. The `docker compose` stack deliberately publishes no database port.
- [ ] Understand that **migrations run automatically at API startup** in every environment: back up before deploying.
- [ ] Understand the three database identities (application, migrations, administrator) and that the compose stack still connects as `sa`. The least-privilege logins are scripted in `scripts/sql/least-privilege-logins.sql` and verified by `scripts/verify-least-privilege.sh`, but not yet applied to a deployment (R-12). See [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md).
- [ ] Know what a fresh database contains, and that the demo catalogue is seeded only in Development/Testing or on request. See [SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md).
- [ ] Read [Migrations.md](../06-DATABASE/Migrations.md) before the first schema change. Destructive migrations need the owner's approval.

## 4. Configuration and secrets

- [ ] **Never** read, print or commit `.env`. It holds the owner's real secrets and is not in the repository.
- [ ] Locally: .NET user-secrets. `ConnectionStrings:Default` and `Jwt:Key` are required ([DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md) §2).
- [ ] In Docker: environment variables, each documented in `.env.example`. The names are `DB_SA_PASSWORD`, `DB_MIGRATIONS_CONNECTION`, `JWT_KEY`, `PAYMENTS_PROVIDER`, `STRIPE_SECRET_KEY`, `STRIPE_PUBLISHABLE_KEY`, `STRIPE_WEBHOOK_SECRET`, `SECRETS_KEY`, `PAYMENTS_ALLOW_TEST_MODE_STORE_ACCOUNTS`, `EMAIL_PROVIDER`, `RESEND_API_KEY`, `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `GMAIL_APP_PASSWORD`, `GMAIL_USERNAME`, `GMAIL_SMTP_PORT`, `FRONTEND_URL`, `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `SEED_DEMO_DATA`, `DEFAULT_TENANT_HOSTS`, `PLATFORM_HOST`, `SEED_PLATFORM_OWNER_EMAIL`, `SEED_PLATFORM_OWNER_PASSWORD`, `TRUSTED_PROXY_NETWORKS`. What each one does: [Configuration.md](../09-OPERATIONS/Configuration.md).
- [ ] **The secrets key** (`SECRETS_KEY`) is handed over securely. Stores' payment keys are encrypted with it; losing it means every store must re-enter its payment keys.
- [ ] Understand that the API refuses to start without a strong JWT key, an email provider (or an explicit log-only choice), a payment provider (or an explicit fake) and a valid secrets key. The messages are listed in [Configuration.md](../09-OPERATIONS/Configuration.md).

## 5. Local development

- [ ] API: `dotnet run --project src/Souq.API`. Frontend: `cd frontend && npm install && npm run dev`.
- [ ] Hosts work: `localhost:5173` (the default store), `{slug}.localhost:5173` (any other store), `admin.localhost:5173` (the platform console).
- [ ] Sign in with the **Development-only** demo accounts listed in [SeedAndBootstrap.md](../09-OPERATIONS/SeedAndBootstrap.md). They are never seeded outside Development.
- [ ] One order placed end to end with the fake gateway, then shipped from the admin (the flow in [RequestLifecycle.md](RequestLifecycle.md)).

## 6. Tests

- [ ] The canonical gate, and what CI runs: [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md).
- [ ] Backend: `dotnet build -warnaserror`, then the Domain, Application, Architecture and Integration suites. Integration tests start SQL Server in Docker through Testcontainers; check free Docker memory first (`docker run --rm alpine free -m`) and never stop containers you did not start.
- [ ] Frontend: `npm run lint`, `npm run typecheck`, `npx vitest run`, `npm run build`.
- [ ] Generated inventories are current: `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"` changes nothing on a clean tree.

## 7. Browser QA

- [ ] The Playwright journeys in `frontend/e2e` run against a live stack. They are **not** in CI and not in the release gate. Runbook: [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md).
- [ ] Know their constraints:
  - one sign-in per file, and files about a minute apart (the server allows ten sign-ins a minute);
  - `SOUQ_API_LOG` for the provisioning and back-office journeys;
  - the `phone` project for the responsive checks;
  - the pre-provisioned second store the two-store journey expects.
- [ ] Know what the journeys leave behind in a QA database: archived QA stores and disabled QA platform accounts (accounts can't be deleted).

## 8. Docker and deployment prerequisites

- [ ] `docker compose up --build` runs the whole stack in **Production** mode: it refuses to start without real provider configuration unless the explicit demo settings are chosen.
- [ ] [Deployment.md](../09-OPERATIONS/Deployment.md) read: topology, startup sequence, health checks (`/health/live`, `/health/ready`), rollback.
- [ ] Hosting, DNS for every store's domain, and the platform host decided. None of these can be recovered from the repository.
- [ ] `TRUSTED_PROXY_NETWORKS` set for the real proxy chain, so client addresses, rate limits and audit IPs are right.
- [ ] The release gate understood: `scripts/release-gate.sh --require-all` checks configuration, backups, suites, dependency audits and a live smoke test. Without arguments, sections are reported *skipped*, never passed.
- [ ] [ProductionReleaseChecklist.md](../09-OPERATIONS/ProductionReleaseChecklist.md) walked through.

## 9. Backups

- [ ] A backup schedule and an off-site copy exist. **Nothing schedules backups today**, though the scripts and the procedure do (R-19).
- [ ] A restore rehearsed with `scripts/rehearse-restore.sh`, and `scripts/backup-verify.sh` scheduled so a silent failure is noticed.
- [ ] Retention decided (an owner choice with legal weight).

Detail: [BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md).

## 10. TLS and HTTP security

- [ ] TLS terminated in front of the stack. **The repository configures none**; nginx listens on plain HTTP (R-16).
- [ ] HSTS scope decided before raising it. The API sends HSTS outside Development with a conservative age; `includeSubDomains` and `preload` are owner choices.
- [ ] The SPA's Content-Security-Policy moved from report-only to enforcing after observing reports.

Detail: [Security.md](../07-SECURITY/Security.md), [SecurityControls.md](../07-SECURITY/SecurityControls.md).

## 11. Payments

- [ ] Access to the **Stripe account**, its keys and the webhook secret, obtained from the previous owner.
- [ ] Webhook endpoint registered for the real host.
- [ ] **P-05 verified before taking JOD** (or any three-decimal currency): the code sends ×100 today, and one test charge on the real account must confirm that's right.
- [ ] **D-13 decided before a second store takes live payments:** each store connects its own Stripe account, or the platform adopts Stripe Connect.
- [ ] **R-03 decided:** may a staff member who manages orders, but not payments, refund money by cancelling a paid order?
- [ ] **F-8 decided:** should a duplicate checkout submission replay the order or be refused?

Detail: [Payments module](../04-MODULES/Payments/README.md), [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md).

## 12. Tax and licensing

- [ ] **Tax (P-06):** no tax exists in the product. Inclusive or exclusive prices, per-store rates and invoice requirements are an owner decision; the pricing pipeline has an empty tax stage ready for it.
- [ ] **Licence (P-03):** decided before the first sale.

## 13. Open owner decisions

- [ ] Every row of [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) has a named decider and a date: P-03, P-05, P-06, D-13, R-03, F-8, D-22 (storefront preview, [brief](../04-MODULES/Platform/StorefrontPreview.md)), P-07 (platform-wide settings), plus the smaller choices listed there.

## 14. External dependencies

- [ ] **Email provider account** (Resend, Brevo or Gmail SMTP) and its **verified sender domain**, obtained from the previous owner.
- [ ] **Stripe** (§11).
- [ ] **DNS and hosting** for the platform host and every store domain.
- [ ] Domain verification is **manual** today: the platform marks a domain verified, and nothing checks DNS or issues certificates (PLANNED, Phase 23).

## 15. Known technical debt and risks

- [ ] [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) read: what is owed, prioritized.
- [ ] [RiskRegister.md](../02-ARCHITECTURE/RiskRegister.md) read; §1 (money) before taking real payments.
- [ ] [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) read, final assessment first: the P0 blockers, which of them engineering can close (least-privilege logins, TLS, backups) and which only the owner or an external account can (P-05, P-06).
- [ ] [ModuleBoundaryAudit.md](../02-ARCHITECTURE/ModuleBoundaryAudit.md) skimmed: the module crossings to retire before any extraction.

## 16. Current roadmap and where to continue

- [ ] [ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md) status line and decision log read.
- [ ] Next product work agreed. At the last verification, the options were:
  - storefront preview once D-22 is decided;
  - product variants: the option model and admin (V2), then the storefront selection (V3), per [ProductVariants.md](../04-MODULES/Catalog/ProductVariants.md);
  - the roadmap's Phase 19 (Testing) and Phase 20 (Security review);
  - the engineering-owned release blockers.
- [ ] Engineering missions (hardening, knowledge passes) take a name, not a roadmap number (`AGENTS.md` §5).

## 17. How to make the next change safely

- [ ] Follow [HowToAddAFeature.md](HowToAddAFeature.md) or [HowToChangeExistingCode.md](HowToChangeExistingCode.md), with the module's `ChangeGuide.md`.
- [ ] Keep [CriticalInvariants.md](CriticalInvariants.md) open. If a test that encodes a boundary fails, the control is working: fix the change, never the test.
- [ ] Stop and ask for anything in `AGENTS.md` §9: destructive migrations, legal or payment decisions, invented business rules, security decisions with real consequences, conflicts with an ADR.
- [ ] Run the gate, review your own diff with [CodeReviewGuide.md](CodeReviewGuide.md), update the documents in the same commit, and use a Conventional Commit message.
