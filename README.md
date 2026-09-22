# Souq

**A white-label, multi-tenant e-commerce platform.** One deployment — one ASP.NET Core API, one SQL Server database, one React build — serves many independent online stores. Each store has its own domain, branding, currency, languages, catalog, customers and orders, and can see nothing of any other store.

> ### Read this first
>
> Souq is finished as a **portfolio-quality demonstration of a commercial SaaS architecture**, not as a launched business. The engineering system is complete and runs end to end; the things a real deployment would buy — a payment provider, cloud storage, a mail account, verified tax values, legal review — are **deliberately local stand-ins behind real ports**, never faked.
>
> **It does not process real payments and has never been connected to a payment provider.** It claims no compliance certification and contains no verified legal tax rate.
>
> **[docs/00-START-HERE/PortfolioScope.md](docs/00-START-HERE/PortfolioScope.md)** is the honest three-way split: what genuinely works, what is a local stand-in and why, and what a production deployment would add.

| | |
|---|---|
| **Backend** | .NET 10 · ASP.NET Core · EF Core 10 · SQL Server 2022 · MediatR · FluentValidation |
| **Frontend** | React 18 · Vite · React Router 6 · TanStack Query · i18next (Arabic/English, RTL and LTR) · CSS Modules · JSDoc type-checking |
| **Adapters** | Demo payment gateway (local, deterministic) · log / Resend / Brevo / SMTP email · local filesystem storage |
| **Architecture** | Modular monolith · Clean Architecture · ports and adapters · selective CQRS and DDD · 15 business modules |
| **Scale** | ~2,700 automated tests · 65 architecture decision records · 37 migrations · 24 browser journey specs |

## Why it exists

Most e-commerce samples are one store with a shopping cart. The interesting problems start at the second store: how do you keep two merchants' data apart when they share a database and a process; who is the merchant of record; what happens to an order when the payment gateway times out mid-checkout; how does a merchant customise a storefront without becoming a code fork.

Souq is an attempt to answer those properly, and to make the answers **checkable** — the architecture tests fail the build when tenant isolation, a module boundary, an endpoint's authorization, or the documentation drifts from the code.

## Run it

**The container stack** — closest to a real deployment:

```bash
cp .env.example .env      # the file documents every value it needs
docker compose up --build
```

Frontend on `http://localhost:8081`, API on `http://localhost:5201`. The stack runs in **Production mode and refuses to start** with missing or unsafe configuration — no silent fallbacks.

**Locally, for development:**

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<your connection string>" --project src/Souq.API
dotnet user-secrets set "Jwt:Key" "<64+ random characters>" --project src/Souq.API
dotnet run --project src/Souq.API                 # applies migrations and seeds on startup
cd frontend && npm install && npm run dev         # http://localhost:5173
```

`localhost` serves the seeded demo store, `{slug}.localhost` any other store, and `admin.localhost` the platform area. Full instructions, including several stores at once: [DevelopmentGuide.md](docs/09-OPERATIONS/DevelopmentGuide.md).

**Demo sign-in.** The seed creates `admin@souq.com` / `Admin@123` (store admin) and `owner@souq.com` / `Owner@12345` (platform owner). These exist **only in Development and Testing**, or when demo seeding is switched on deliberately; a production environment refuses to seed an administrator unless you supply your own. They are examples, not secrets.

**Demo payment.** Checkout runs a local adapter that moves no money and says so on the screen. The order total picks the outcome — ending `…01` declines (retry works), `…02` stays processing, `…03` is cancelled at the gateway, anything else succeeds ([ADR-0063](docs/11-ADR/0063-the-demo-payment-adapter.md)).

## The architecture in one screen

```
src/Souq.API             HTTP: controllers, middleware, auth policies, composition root
src/Souq.Infrastructure  adapters: EF Core, payment gateway, email, storage, hosted services
src/Souq.Application     use cases: commands, queries, handlers, validators, ports
src/Souq.Domain          the business model: aggregates, value objects, domain events
```

Dependencies point inwards only; the Domain depends on nothing. Inside those layers, fifteen **business modules** own their data and talk through explicit contracts — a module reaching into another's internals fails the build.

- Layers and what enforces them: [DependencyRules.md](docs/02-ARCHITECTURE/DependencyRules.md)
- Modules and ownership: [Modules.md](docs/04-MODULES/Modules.md) · [ModuleBoundaries.md](docs/02-ARCHITECTURE/ModuleBoundaries.md)
- What was deliberately **not** built: [ExplicitNonGoals.md](docs/02-ARCHITECTURE/ExplicitNonGoals.md)

## Multi-tenancy, and the four mechanisms that keep stores apart

Every request is bound to exactly one store, **resolved from the host on the server** — never from a header, a claim or a body the client controls.

1. **A global query filter** on every store-owned entity, so a forgotten `WHERE` cannot leak another store's rows.
2. **A write guard** that stamps the owner on insert and refuses a cross-tenant write.
3. **Three table shapes with different rules** — store-owned, platform-owned but tenant-keyed, and platform-global — because a single rule would be wrong for two of them ([ADR-0047](docs/11-ADR/0047-commercial-control-plane.md)).
4. **Architecture tests** that fail the build when a new entity, query or endpoint escapes any of the above.

Details: [MultiTenancy.md](docs/02-ARCHITECTURE/MultiTenancy.md) · [ADR-0022](docs/11-ADR/0022-tenancy-enforcement.md).

## The parts worth looking at

| Area | What makes it interesting | Start here |
|---|---|---|
| **Payments** | A flow-agnostic port: `StartPayment` returns a *discriminated result* (client script, redirect, browser post, completed, deferred), so a redirect-first regional provider is an adapter change, not an interface change. Refunds route to the account that actually took the money | [ADR-0048](docs/11-ADR/0048-payment-provider-abstraction.md) · [ADR-0061](docs/11-ADR/0061-a-payment-records-which-account-took-it.md) |
| **Tax** | A configurable, jurisdiction-aware capability — versioned profiles, effective dates, inclusive/exclusive, store overrides, an immutable snapshot on every order. **Tax is collected only under a version a named professional marked verified**, and engineering cannot set that state | [ADR-0055](docs/11-ADR/0055-tax-as-a-configurable-capability.md) |
| **Store customization** | Three axes, none of them a fork: theme presets that vary *form* and never colour; an ordered registry of home-page sections; wording overrides from a closed list that excludes error and accessibility strings | [0059](docs/11-ADR/0059-theme-presets-vary-form-not-colour.md) · [0060](docs/11-ADR/0060-home-page-sections-as-an-ordered-registry.md) · [0062](docs/11-ADR/0062-store-text-overrides-are-a-closed-list.md) |
| **Merchant billing** | The platform invoices its own merchants: one number series allocated inside the issuing transaction, an issued invoice frozen and corrected only by a credit note, and automated dunning that closes a store only once the grace period has passed **and** every reminder was sent | [0056](docs/11-ADR/0056-platform-invoices-and-manual-collection.md) · [0058](docs/11-ADR/0058-dunning-and-automated-suspension.md) |
| **Analytics** | A complete behavioural foundation that ships **switched off** — no lawful basis is claimed, so nothing is collected. Recommendations therefore run on the store's own delivered orders instead | [0050](docs/11-ADR/0050-behavioural-event-foundation.md) · [0064](docs/11-ADR/0064-recommendations-from-first-party-data.md) |
| **Notifications** | A transactional outbox: the message is written in the same transaction as the fact, so nothing is lost and no request waits on a mail provider | [ADR-0034](docs/11-ADR/0034-notifications-outbox.md) |
| **Concurrency** | Stock reservations, quota counters that no isolation level can defeat, and a lease table that makes background work single-instance without a coordination service | [0026](docs/11-ADR/0026-inventory-reservations.md) · [0049](docs/11-ADR/0049-tenant-quota-enforcement.md) · [0057](docs/11-ADR/0057-cross-instance-coordination.md) |

## Security

Engineered with care and covered by tests — and **not** externally audited, which is stated plainly rather than implied away.

- Sessions with refresh rotation and per-store revocation; account lockout; rate limits per (host, address).
- Uploads validated by **magic bytes**, not extension or declared type; server-generated filenames; per-tenant paths; a serving guard that refuses another store's files.
- **No card data anywhere** — enforced by a test that pins the exact column list of every payment table.
- Secrets never in the repository: store payment keys are AES-256-GCM encrypted with purpose binding.

[SecurityControls.md](docs/07-SECURITY/SecurityControls.md) lists each control against the test that enforces it.

## Testing

| Suite | Roughly | What it protects |
|---|---|---|
| Domain | 671 | Business rules, in isolation, with no database |
| Application | 532 | Use cases, orchestration, compensation paths |
| **Architecture** | 127 | Layering, module boundaries, tenant isolation, authorization surface, documentation accuracy |
| **Integration** | 544 | Real SQL Server via Testcontainers: HTTP in, database out, including concurrency races |
| Frontend | 865 | Vitest, plus lint and JSDoc type-checking |
| Browser | 24 specs | Playwright journeys against the container stack, run by hand |

The architecture suite is the one to read first: it is where "tenant isolation is enforced" stops being a sentence in a document and becomes a failing build.

```bash
dotnet build -warnaserror
dotnet test                                        # integration tests need Docker
cd frontend && npm ci && npm run lint && npm run typecheck && npm test && npm run build
```

> Running the integration suite **with the demo stack stopped** — it starts its own SQL Server, and a small Docker VM cannot hold both. [DevelopmentGuide.md §4](docs/09-OPERATIONS/DevelopmentGuide.md) explains what it looks like when it runs out.

## Where things are

| You want | Go to |
|---|---|
| The honest scope: real vs demo vs production | [PortfolioScope.md](docs/00-START-HERE/PortfolioScope.md) |
| A guided reading path | [LearningPath.md](docs/00-START-HERE/LearningPath.md) |
| A module (catalog, orders, payments…) | [docs/04-MODULES/](docs/04-MODULES/Modules.md) |
| One feature end to end | [FeatureMaps.md](docs/04-MODULES/FeatureMaps.md) |
| The API surface | [Endpoints.md](docs/05-API/Endpoints.md) (generated) · Swagger at `/swagger` in Development |
| Decisions and their alternatives | [docs/11-ADR/README.md](docs/11-ADR/README.md) |
| Database tables and their owners | [OwnershipMap.md](docs/06-DATABASE/OwnershipMap.md) |
| The frontend | [FrontendGuide.md](docs/08-FRONTEND/FrontendGuide.md) |
| Deployment and troubleshooting | [Deployment.md](docs/09-OPERATIONS/Deployment.md) · [Troubleshooting.md](docs/09-OPERATIONS/Troubleshooting.md) |

## Known limitations

Kept current rather than discovered later. The full lists are [TechnicalDebt.md](docs/12-ROADMAP/TechnicalDebt.md) and [RiskRegister.md](docs/02-ARCHITECTURE/RiskRegister.md).

- **No external service is connected.** Payments, email, storage and tax values all run on local stand-ins — see [PortfolioScope.md](docs/00-START-HERE/PortfolioScope.md) §2 for each one and why.
- **Behavioural capture is off** and stays off; anything that would depend on it is built to work without it.
- **Custom domains verify ownership manually.** Certificate automation is designed but not built — it needs either a managed edge or an ACME client ([ADR-0051](docs/11-ADR/0051-custom-domain-lifecycle.md)).
- **Outbound webhooks are decided but not built.** The decision is *webhooks only, never customer code*; the mechanism waits for a customer who needs it ([ADR-0052](docs/11-ADR/0052-bounded-extension-model.md)).
- **Uploads live on the instance's disk**, so a second instance needs a shared mount until storage moves to a blob store.
- **One known intermittent defect:** under real contention, a concurrent basket read at the sign-in merge moment can answer `500` instead of retrying. Reproduced on CI, instrumented so the next failure names its own cause, and tracked as F-33 in [ReleaseReadiness.md](docs/09-OPERATIONS/ReleaseReadiness.md) — recorded rather than quietly retried away.

## License

[MIT](LICENSE).
