# AI handoff: how to work in this repository

> **Read this before touching anything.** You have no memory of how this system came to be, and neither does the next agent. Everything you need is in the repository; everything you learn that matters must go back into it.
> **Human counterpart:** [HandoffGuide.md](HandoffGuide.md). **The rules themselves:** [AGENTS.md](../../AGENTS.md).

## 1. The first rule

**The repository is the source of truth.** Not your memory of this project, not a summary you were handed, not what a document said before someone changed the code. When a document and the code disagree, the code wins — and fixing the document is part of your change, not a separate task.

Corollary: **never say "as we decided earlier"** unless you can point at a commit, an ADR or a file. There is no earlier conversation.

## 2. Reading order before your first change

1. `AGENTS.md` — the contract: hard rules, what enforces them, when to stop.
2. [SystemOverview.md](SystemOverview.md) — what the system is and what happens on a request.
3. [HowToReadThisRepository.md](HowToReadThisRepository.md) — pick the path that matches your task.
4. [Modules.md](../04-MODULES/Modules.md) → the module you are about to touch → its `ChangeGuide.md`.
5. [DependencyRules.md](../02-ARCHITECTURE/DependencyRules.md) and [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) — what you may call.
6. [docs/11-ADR/README.md](../11-ADR/README.md) — find the decision that already governs your area.
7. The tests that cover it ([TestInventory.md](../10-TESTING/TestInventory.md)), then the code.
8. `git status` and `git log --oneline -10` — know what is already in flight before you add to it.

## 3. What to inspect before modifying code

- **Ownership:** which module owns the concept ([ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md))?
- **Rules:** what does [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) already say, and which test proves it?
- **Decision:** does an ADR cover this? Does your change contradict it?
- **Data:** which tables, who owns them, does this need a migration ([OwnershipMap.md](../06-DATABASE/OwnershipMap.md), [Migrations.md](../06-DATABASE/Migrations.md))?
- **Security and tenancy:** who may call this, and what happens with another store's id?
- **Blast radius:** is the thing you are changing a public contract — an endpoint shape, an error `code`, a database column, an outbox message type?

## 4. Never, without an explicit decision

| Never | Why |
|---|---|
| Weaken or delete a test to make a build pass | The tests are the written memory of decisions |
| Bypass a module boundary because it is quicker | It compiles today and blocks extraction forever; the crossings are counted in [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md) |
| Touch tenant isolation, authorization or payment safety for convenience | These are why the product may exist |
| Introduce a dependency, a service, a broker, a cache or a framework | See [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md); each has a recorded reason and a trigger |
| Silently change an architectural decision | It needs an ADR, and the old one must say what superseded it |
| Invent architecture that "should" be there | Document what **is**; propose what should be |
| Read, print or commit `.env` | It holds the owner's real secrets |
| Push, merge, rebase, squash or rewrite history | Unless explicitly asked |
| Stop other people's containers or processes | The machine is shared |

## 5. Write documentation the way this repository does

- **Backticks are reserved for things that exist** — paths, types, members, routes, config keys. Planned or hypothetical names go in *italics*. A test enforces this, and it is the single rule that keeps the documentation honest.
- **Label anything not implemented:** PLANNED (in the roadmap), DEFERRED (postponed, with the reason), FUTURE (an option), DEPRECATED. Unlabelled means current.
- **No line numbers.** Reference stable paths and symbol names.
- **Explain why**, not what the code already says. The code comments are in Arabic and often carry the reason — read them.
- **Record what you could not verify** rather than guessing. "Not verified" is a useful sentence; a confident wrong claim is not.

## 6. Before you report work as complete

```bash
dotnet build
dotnet test tests/Souq.Domain.Tests tests/Souq.Application.Tests
dotnet test tests/Souq.ArchitectureTests          # boundaries, endpoints, documentation, inventories
dotnet test tests/Souq.IntegrationTests           # needs ~2 GB free Docker memory
cd frontend && npx vitest run && npx vite build
```

Changed a controller, a use case, a module boundary or test files?

```bash
SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"
```

Check Docker memory first with `docker run --rm alpine free -m`. When SQL Server is short of memory every integration test fails with a 500 — that is the environment, not your code ([Troubleshooting.md](../09-OPERATIONS/Troubleshooting.md)).

Report results honestly: if a suite failed, say so and paste the failure; if you skipped a step, say which.

## 7. Recognizing an architectural conflict

You are in one when:

- your change needs a new arrow between modules, or a module wants data another module owns;
- the same rule would have to live in two places to work;
- a test that encodes a boundary fails, and the obvious "fix" is to change the test;
- the frontend wants to decide something the server must decide;
- a background job needs a store it cannot resolve;
- the cheapest implementation requires new infrastructure.

**Then stop and design.** Read the boundaries, propose a contract, and write an ADR if the boundary itself moves. Implementing first and documenting after is how the three-way drift between docs, tests and code happened before.

## 8. When to stop and ask

Do all the safe work first, leave the repository clean, then report precisely what decision is needed and what you would recommend. Stop for:

- a destructive or irreversible data migration;
- a licensing or legal decision;
- anything depending on the owner's real provider accounts (for example the JOD minor-unit question, P-05);
- a business rule where guessing changes commercial behaviour — prices, refunds, limits, tax;
- a security decision with real-world consequences;
- contradictory requirements, or a request that conflicts with an ADR (name the ADR).

## 9. Specific things that will surprise you

- **Comments are in Arabic.** They are architectural, not decorative. Translate them; do not skip them.
- **Four documents are generated** from the code — the endpoint inventory, the use-case catalog, the test inventory and the cross-module dependency list. Never hand-edit them; regenerate.
- **The cross-module dependency file is a ratchet.** If your change adds a crossing, the build fails. That is the design: add a contract instead, or regenerate deliberately so the diff shows what you added.
- **Development conveniences are gated by environment, not configuration** — the `X-Tenant` header and `localhost` resolution cannot be enabled in production, whatever the configuration says.
- **A store that is not active answers 503 to everything**, and a disabled module answers 404. Both are middleware decisions taken before your code runs.
- **MediatR is pinned to 12.x** on purpose (13+ is commercially licensed).
- **`Result` versus exceptions:** expected outcomes return `Result` with a stable code; guarded invariants throw domain exceptions. Both become RFC 7807 responses.

## 10. What to leave behind

When you finish, the repository should be able to answer, without you:

- what you changed and why (the commit message and, if architectural, an ADR);
- which rule now applies (the module document and, if new, the business rules);
- what you deliberately did not do (DEFERRED, with the reason);
- what you could not verify (written down, not assumed);
- what the next agent should read first (a link, not a summary).
