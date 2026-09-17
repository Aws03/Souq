# How to change existing code

> **Who this is for:** anyone — human or AI — about to modify behaviour that already works and already has customers depending on it. Adding something new? Read [HowToAddAFeature.md](HowToAddAFeature.md) instead.
> **The principle:** the existing code is a set of decisions, most of them deliberate. Your job is to find the decision before you change it.
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.

## The loop

```
Understand  →  Locate the owner  →  Trace the execution  →  Read the tests
    →  Name the invariants  →  Make the smallest correct change
    →  Update the tests  →  Run the gates  →  Review your own diff  →  Update the docs  →  Commit
```

Skipping straight to the edit is what turns a small change into an incident. The steps below take minutes; a cross-tenant leak or a silent pricing change costs far more.

## 1. Understand what is being asked

Write down, in one sentence, the **behaviour** that should be different, in business terms: "a customer may cancel an order that has already shipped", not "add a case to the switch". If you cannot state it that way, you do not yet know what to change.

Then check whether the current behaviour is deliberate:

- [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) — is there a rule for it, with a test?
- The module document's *Known limitations* and the [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) register — is it a known gap?
- The ADR index ([docs/11-ADR/README.md](../11-ADR/README.md)) — was it decided, and why?

If an ADR says the opposite of what you are about to do, stop and read §9.

## 2. Locate the owner

Find the **module** that owns the concept ([Modules.md](../04-MODULES/Modules.md), [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md)). Changing stock rules means Inventory, even if the request arrives through checkout. A change that seems to need edits in three modules usually means the rule is in the wrong place — say so before you spread it further.

Then read that module's `README.md`, and its `ChangeGuide.md` if it has one: common changes are already written down there, with the invariants they must not break.

## 3. Trace the execution before editing

Follow one real request end to end. The fastest route:

1. [Endpoints.md](../05-API/Endpoints.md) — which endpoint, which permission, which use case does it send?
2. [UseCases.md](../04-MODULES/UseCases.md) — which handler and validator?
3. Read the handler: what does it load, which aggregate methods does it call, which contracts, what does it save?
4. Read the aggregate method: that is where the rule lives.
5. [FeatureMaps.md](../04-MODULES/FeatureMaps.md) has the full trace for the main flows (checkout, payment, refunds, notifications…).

## 4. Read the tests first

The tests are the executable specification of the current behaviour:

- Domain tests state the invariants.
- Application tests state the orchestration, including who may do it.
- Integration tests state the HTTP contract, the status codes, tenant isolation and concurrency.

If a test contradicts your change, you have found the decision. Either your change is wrong, or the decision changed — and then the test changes **deliberately, with the reason in the commit message**. Never adjust an assertion just to make a build green.

## 5. Name the invariants you must not break

Before editing, list what must still be true afterwards. Typically:

- **Tenancy:** the change must be impossible to use across stores. Ask: what happens when the id belongs to another store? (Expected: 404.)
- **Authorization:** who may call this, and is the check in the use case as well as on the endpoint?
- **Money:** totals are computed on the server; rounding follows the currency's minor units; a discount never exceeds the subtotal.
- **Stock:** available never goes negative; every change writes a ledger entry; a release is idempotent.
- **Order history:** snapshots are immutable after placement; transitions follow the table.
- **Concurrency:** rows with `rowversion` may conflict; the retry or the 409 is part of the design.
- **Privacy:** no token, password, card data or personal data in logs.

## 6. Make the smallest correct change

- Put the change **where the rule lives**: an invariant in the aggregate, orchestration in the handler, a technology detail in the adapter, HTTP translation in the controller ([EngineeringMentalModel.md](EngineeringMentalModel.md)).
- Don't refactor unrelated code in the same commit. A mixed diff hides the behaviour change from the reviewer.
- Don't add an abstraction for a single use. Don't remove one that a test depends on.
- Keep the public contract stable unless changing it is the point: an endpoint's shape, an error `code`, a database column and a message type in the outbox are all contracts someone depends on.
- If you must break a contract, do it in two steps (add the new, migrate, remove the old), and say so in the commit.

## 7. Update the tests in the same change

- A changed rule needs a changed or new test at the same level as the rule (Domain rule → Domain test).
- A bug fix starts with a test that fails for the old behaviour.
- A new failure path needs a test for the error code, not only the happy path.
- Cross-tenant and permission checks belong in the integration suite.

## 8. Run the gates

The canonical gate — the commands and exactly what CI runs — is [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md). In short:

```bash
dotnet build -warnaserror
dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests
dotnet test tests/Souq.ArchitectureTests          # boundaries, endpoints, documentation, inventories
dotnet test tests/Souq.IntegrationTests           # needs free Docker memory for SQL Server
cd frontend && npm run lint && npm run typecheck && npx vitest run && npm run build
```

Changed a user flow? Run its browser journey in `frontend/e2e` against a live stack (the runbook is in DeveloperQualityGates.md).

Changed a controller, a use case, or test files? Regenerate the inventories:

```bash
SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"
```

If the integration suite fails in ways unrelated to your change, check [Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md) before assuming your code is at fault — SQL Server in Docker fails loudly when the machine is short of memory.

## 9. Review your own diff, as a reviewer would

Read [CodeReviewGuide.md](CodeReviewGuide.md) and apply it to yourself. The questions that catch the most damage:

- Does anything here trust the client for a tenant, a price, a permission or a stock number?
- Could this row be read or written by another store?
- Does this leave a transaction open across a network call?
- Does this log something that should never be logged?
- Would a new engineer understand *why* from the code and its comment, or only *what*?

## 10. Update the documentation in the same commit

- Behaviour changed → the module document, and [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) if the rule is new or different.
- Endpoint, use case or schema changed → regenerate the inventories; check [ApiDocumentation.md](../05-API/ApiDocumentation.md) and [OwnershipMap.md](../06-DATABASE/OwnershipMap.md).
- A decision was made (a boundary, a dependency, a strategy) → write an ADR ([docs/11-ADR/](../11-ADR/README.md)).
- Something was deliberately left undone → record it as DEFERRED with the reason, in the module document and in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) when it carries risk.

## When to stop instead of changing

Stop, leave the repository clean, and ask when the change would:

- migrate or delete data irreversibly;
- change commercial behaviour by guessing (prices, refunds, limits, tax);
- weaken tenant isolation, authorization, or payment safety;
- contradict an ADR (name the ADR and explain what changed in the world);
- require a new dependency or a new piece of infrastructure.

See [AGENTS.md](../../AGENTS.md) §9 for the full list.
