# How to read this repository

> **What this page is:** reading paths, not a file list. Pick the path that matches your situation and follow it in order. Each path says what to read, what to *skip*, and what you should be able to answer at the end.
> **Prerequisite:** [SystemOverview.md](SystemOverview.md) — 10 minutes, and every path below assumes it.
> **Looking for one ordered path from zero to handoff?** That is [LearningPath.md](LearningPath.md) (numbered steps 00–18 with learning levels). This page is for when you arrive with a specific situation.
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.

The repository is organized so that you can answer three questions without grep: **who owns this concept** (modules), **why is it like this** (ADRs), and **what is true regardless of the UI** (business rules).

```
docs/00-START-HERE   entry points: overview, reading paths, mental model, how to add or change code, handoff
docs/01-REQUIREMENTS business rules, traced to code and tests
docs/02-ARCHITECTURE the architecture, its rules, its events, its risks, and what it deliberately is not
docs/03-DOMAIN       how DDD is (and is not) used here
docs/04-MODULES      one folder per business module: what it owns, how to change it
docs/05-API          API conventions + the generated endpoint inventory
docs/06-DATABASE     schema principles, table ownership, migrations
docs/07-SECURITY     security architecture, identity, and the control catalog
docs/08-FRONTEND     frontend architecture, the practical guide, white-label
docs/09-OPERATIONS   setup, configuration, deployment, troubleshooting, scaling
docs/10-TESTING      testing strategy, traceability, the generated test inventory
docs/11-ADR          the decisions, with their alternatives
docs/12-ROADMAP      the plan, the open decisions, the technical debt
docs/archive         historical snapshots — context, not instructions
```

---

## Path A — 30 minutes: what is this system?

1. [SystemOverview.md](SystemOverview.md) — the whole system, including the life of a request.
2. [Modules.md](../04-MODULES/Modules.md) §1 — the thirteen modules and what each owns.
3. [ADR-0001](../11-ADR/0001-target-architecture.md) — why a modular monolith and not microservices.
4. [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) — skim the headings only, to know what not to propose.
5. `docs/05-API/Endpoints.md` — skim: the whole product surface is about 130 endpoints, with their permissions.

**Skip for now:** every module document, the ADR list, the database tables.

**You should be able to answer:** What does Souq sell, and to whom? Why is there one database? What decides which store a request belongs to? Where would I look for "orders"?

---

## Path B — 2 hours: the architecture

1. [Architecture.md](../02-ARCHITECTURE/Architecture.md) — layers, modules, and how they communicate.
2. [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md) — the two dependency rules and what enforces them. **Then open `tests/Souq.ArchitectureTests/DependencyRuleTests.cs`** and read one test: the rules are executable, not aspirational.
3. [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) — who owns what, and the honest list of what is enforced versus conventional.
4. [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md) — the isolation model. **Then open `src/Souq.Infrastructure/Persistence/AppDbContext.cs`** and find the query filter and the write guard.
5. [CQRS.md](../02-ARCHITECTURE/CQRS.md) and [DDD.md](../03-DOMAIN/DDD.md) — how much of each is used, and where it stops.
6. [Events.md](../02-ARCHITECTURE/Events.md) — domain events and the outbox; the difference from event sourcing.
7. [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) and [ADR-0021](../11-ADR/0021-transaction-boundaries.md) — the two decisions that constrain almost every change.

**You should be able to answer:** Where does a new business rule go? What may a module call? What happens if I open a transaction around a Stripe call? Why is there no `TenantId` in any request?

---

## Path C — half a day: one complete feature, end to end

Take **checkout**, because it touches seven modules. Read in this order, each time asking "what decision is being made here?":

| Step | Read | What it shows |
|---|---|---|
| 1. Requirement | [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md), the Orders and Basket sections | The rules the code must keep |
| 2. Decisions | [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md), [ADR-0029](../11-ADR/0029-orders-lifecycle.md), [ADR-0030](../11-ADR/0030-coupon-redemptions.md), [ADR-0026](../11-ADR/0026-inventory-reservations.md) | Why checkout looks like this |
| 3. Trace | [FeatureMaps.md](../04-MODULES/FeatureMaps.md), the checkout map | The whole path, named |
| 4. Module | [Ordering/README.md](../04-MODULES/Ordering/README.md) | What Ordering owns and refuses to own |
| 5. Domain | `src/Souq.Domain/Entities/Order.cs`, `src/Souq.Domain/Entities/OrderTransitions.cs` | The invariants and the state machine |
| 6. Application | `src/Souq.Application/Features/Orders/Commands/CreateOrderHandler.cs` | Orchestration: pricing, reservation, redemption, placement, payment |
| 7. Contracts | `src/Souq.Application/Features/Inventory/Contracts`, `src/Souq.Application/Features/Baskets/Contracts` | How modules ask each other for things |
| 8. Infrastructure | `src/Souq.Infrastructure/Payments`, the repositories and query services | How it is actually done |
| 9. API | `src/Souq.API/Controllers/OrdersController.cs` | How thin a controller is |
| 10. Frontend | `frontend/src/pages/checkout` | What the user sees, and what it never decides |
| 11. Tests | `tests/Souq.Domain.Tests/OrderLifecycleTests.cs`, `tests/Souq.IntegrationTests/OrderLifecycleTests.cs` | The behaviour, written down |

**You should be able to answer:** What is inside the checkout transaction and what is outside, and why? What happens when the gateway times out? Where does the coupon limit hold under concurrency? Which part would break first if Inventory became a separate service?

---

## Path D — before modifying a feature

1. The module's `README.md` — *Business concepts*, *Data ownership*, *Failure modes*.
2. The module's `ChangeGuide.md` — your change is probably a named scenario.
3. [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) — the rules you must not break.
4. The tests that cover it ([TestInventory.md](../10-TESTING/TestInventory.md) locates them).
5. [HowToChangeExistingCode.md](HowToChangeExistingCode.md) — the loop to follow.
6. If the change is architectural: the relevant ADR, then [AGENTS.md](../../AGENTS.md) §9 to check whether it is yours to decide.

---

## Path E — debugging a production problem

1. **Get the correlation id** from the response header or the customer's report; every log line of that request carries it, along with the store and the user.
2. **Classify the failure** by its `code` — the error contract is stable and documented ([ApiDocumentation.md](../05-API/ApiDocumentation.md)).
3. **Check the usual suspects** in [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md): a store that is not active answers 503; a wrong host or a disabled module answers 404; a missing provider key stops startup entirely.
4. **Find the endpoint** in `docs/05-API/Endpoints.md` → it names the use case → [UseCases.md](../04-MODULES/UseCases.md) names the handler and its tests.
5. **Read the handler and the aggregate method**, then reproduce with a test at that level, not through the UI.
6. **Money, stock or delivery involved?** Check the ledger and the histories before changing anything: `StockMovements`, the order's status history, the payment and refund records. They are append-only for exactly this reason.

---

## Path F — your first week as a new engineer

**Day 1 — run it.** [DevelopmentGuide.md](../09-OPERATIONS/DevelopmentGuide.md): database, API, frontend, the seeded demo store. Sign in as the demo admin, place an order with the fake gateway, watch the order move.

**Day 2 — the map.** Path A, then Path B. Open the architecture tests and break one deliberately (add a `using` for EF Core in a handler) to see the guardrail fire; then undo it.

**Day 3 — one feature.** Path C, end to end. Then pick a small module (Reviews or Shipping) and read its module document and its code in one sitting.

**Day 4 — the rules.** [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) and [SecurityControls.md](../07-SECURITY/SecurityControls.md). These are the two documents that tell you what the product promises.

**Day 5 — change something.** Take a small item from [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) marked safe, follow [HowToChangeExistingCode.md](HowToChangeExistingCode.md), run the full gate, and open a pull request. Use [CodeReviewGuide.md](CodeReviewGuide.md) on your own diff first.

**Throughout:** when something surprises you, check whether an ADR explains it before "fixing" it. Most surprises here are decisions.

---

## What not to read (yet)

- `docs/archive/` — historical snapshots: the Phase 0 assessment, the audit of the earlier single-store program, the original Arabic README and an engineering essay. Useful context, not current instructions.
- The generated inventories (`Endpoints.md`, `UseCases.md`, `TestInventory.md`, `ModuleDomainDependencies.md`) are reference tables. Look things up in them; don't read them front to back.
- `docs/12-ROADMAP/ProductRoadmap.md` is long. Read the status line, your phase, and the decision log; skip the rest until you need it.

## A word about the comments

Code comments are in **Arabic** and are mostly architectural: why this is here, what invariant it protects, what must not be added. If you don't read Arabic, run them through a translator rather than skipping them — they carry reasoning that is nowhere else. The rule for writing new ones is in [CodeReviewGuide.md](CodeReviewGuide.md#comment-policy).
