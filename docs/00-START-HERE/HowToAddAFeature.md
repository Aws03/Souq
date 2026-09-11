# How to add a feature

> **Who this is for:** anyone adding new behaviour to Souq. Changing behaviour that already exists? Use [HowToChangeExistingCode.md](HowToChangeExistingCode.md).
> **Before you start:** [EngineeringMentalModel.md](EngineeringMentalModel.md) explains where each kind of logic belongs; this page is the working sequence.

## The sequence

Work in this order. It is not bureaucracy: each step prevents a specific, expensive mistake.

### 1. Understand the requirement in business language

Write one or two sentences describing what a **person** will be able to do, and for whom: a customer, a store's staff, the platform owner. If you cannot name the actor, the requirement is not ready.

Then ask the questions the code will force you to answer anyway:

- Who may do it? (a permission, or the owner of the resource)
- Is it optional per store? (then it is a **module flag**)
- Does it involve money, stock, or personal data? (then the rules are stricter and the tests are mandatory)
- What must *not* happen? (the failure cases are the real specification)

### 2. Find the owning module

One module owns the concept ([Modules.md](../04-MODULES/Modules.md), [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md)). If the feature seems to belong to two, it usually means:

- the concept belongs to one and the other only *reads* it (use a contract), or
- you have found a genuinely new capability (a new module — see step 14).

Read the module's `README.md` and its `ChangeGuide.md` before designing anything.

### 3. Reuse the concepts that already exist

Search the domain before inventing: is there already a status, a snapshot, a limit, a value object for this? Souq has `Money`, `PostalAddress`, `CatalogText`, paging primitives, `Result`/`Error`, an audit behaviour, an outbox. Reusing them keeps behaviour consistent and gives you their tests for free.

### 4. Write down the business rules

State each rule as something that must always be true, and where it is decided. Add them to [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) when they are new. A rule with no owner is a bug waiting to happen.

### 5. Decide whether the domain changes

- A new invariant, a new state, a new guarded transition → the **Domain** changes (a method on an aggregate, maybe a value object).
- Only new coordination of existing rules → no domain change.
- Never add a field to an entity "for the UI". Entities carry business state, not display state.

Write the domain test **first** — it is the cheapest place to discover that the rule is ambiguous.

### 6. Design the use case

One command (it changes state) or one query (it reads). Name it as an intention. Decide:

- input (a DTO — never an entity, never a `TenantId`);
- who may call it (permission and/or ownership);
- what it returns (`Result`, an id, a DTO);
- its failure cases and their **stable error codes** ([ApiDocumentation.md](../05-API/ApiDocumentation.md)).

### 7. Define ports and contracts, if needed

- Needs something from **another module**? Use its contract. If none fits, add one to that module's `Contracts` folder, keep it small, and make sure the arrow is allowed in [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) (adding an arrow is a boundary decision — see step 15).
- Needs something from **outside the system**? Define a port in Application in Souq's own language (no provider types), and implement it in Infrastructure.
- One implementation and no real variation point? Use a concrete application service; not every class needs an interface.

### 8. Implement the Infrastructure side

The EF configuration, the repository method, the query service projection, the adapter. Remember:

- tenant-owned tables implement `ITenantOwned` and get the filter and composite keys automatically enforced by tests;
- reads project into DTOs and are paged;
- provider exceptions are translated at the adapter;
- a schema change means a **migration** ([Migrations.md](../06-DATABASE/Migrations.md)) — additive first, data moves reviewed by hand.

### 9. Add the API surface

A thin controller action: bind, authorize (`[HasPermission]`, `[Authorize]`, or a deliberate `[AllowAnonymous]`), send one request, map the result. Add `[RequiresModule]` if the feature is optional per store. Follow the conventions: plural resources, sub-resource actions instead of invented verbs, paging on every list.

### 10. Add the frontend

The UI reflects server decisions: it never computes prices, stock, permissions or tenancy. Hide what the store disabled (`useModule`), show what the permission allows (`can`), translate every string, and handle the three states every data view needs: loading, error, empty ([FrontendGuide.md](../08-FRONTEND/FrontendGuide.md)).

### 11. Write the tests that matter

| Level | What to cover |
|---|---|
| Domain | Every new invariant, including the failure |
| Application | The orchestration, the permission and ownership checks, the error codes |
| Integration | The endpoint end to end, the authorization matrix, **cross-tenant access (another store's id must answer 404)**, concurrency where relevant |
| Frontend | Pure logic (payload builders, formatting, view models) with Vitest |

A feature that touches money, stock or personal data without an integration test is not finished.

### 12. Update the documentation in the same change

- The module document: business concepts, use cases, data ownership, failure modes.
- [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) for new rules; [Traceability.md](../10-TESTING/Traceability.md) if you added a capability.
- Regenerate the inventories: `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`.

### 13. Write an ADR if the decision was architectural

New dependency, new boundary, new technology, a different consistency or transaction strategy, a new public contract, or a deliberate non-choice worth recording ([docs/11-ADR/README.md](../11-ADR/README.md)).

### 14. If it is a new module

1. Name it after a **capability**, not an entity.
2. Write what it owns and what it must not own, in [Modules.md](../04-MODULES/Modules.md).
3. Add it to the dependency graph — refuse any cycle.
4. Add its feature folder to `ModuleMap` and its allowed contracts to the architecture test.
5. Create `docs/04-MODULES/<Module>/README.md` from the shape the other modules use.
6. Tenant-owned tables implement `ITenantOwned` and get isolation tests.

### 15. Run the quality gates

```bash
dotnet build
dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests
dotnet test tests/Souq.ArchitectureTests
dotnet test tests/Souq.IntegrationTests      # needs Docker
cd frontend && npx vitest run && npx vite build
```

### 16. Review the dependency boundaries, then commit

Ask: did I add an arrow between modules? Did I put a rule outside the domain? Did I make the frontend authoritative for anything? Then commit with a Conventional Commit message that says what changed and why.

## When the sequence is shorter

| Kind of feature | What changes |
|---|---|
| **Read-only screen** | No domain change: a query, a projection in the module's query service, an endpoint, a page. Still paged, still authorized, still tested. |
| **A new setting for stores** | Platform module: the settings document, validation, the storefront config if the frontend needs it, cache invalidation, and a frontend that reads it. No new module. |
| **Frontend-only change** | No backend work — but check that the server already enforces whatever the UI now implies. |
| **A new optional capability** | Add a module flag so stores can turn it off, and enforce it on the server as well as in the UI. |
| **An internal job** | A command plus a hosted service; make it idempotent and give it an explicit tenant scope. |

## When to stop and ask

Before building: anything that changes commercial behaviour by guessing (prices, refunds, limits, tax), stores new personal data, creates a new public endpoint with sensitive data, requires a new dependency, or contradicts an ADR. Do the safe part, then ask ([AGENTS.md](../../AGENTS.md) §9).
