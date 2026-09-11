# ADR-0002: Physical structure of the modular monolith

- **Status:** Accepted, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (how modules are represented in each layer project)
- **Related ADRs:** builds on [ADR-0001](0001-target-architecture.md); the module list it names comes from [ADR-0004](0004-module-boundaries.md); the tests that enforce it are chosen in [ADR-0015](0015-testing-strategy.md); the allowed contract references between feature folders are added by [ADR-0026](0026-inventory-reservations.md), [ADR-0028](0028-basket-and-pricing-pipeline.md) and [ADR-0032](0032-shipping-methods.md)

## Context

[ADR-0001](0001-target-architecture.md) chose a modular monolith. The code currently lives in four **layer** projects, and module boundaries don't exist yet in code.

## Problem

How should modules be represented physically so that their boundaries are real, without imposing ceremony that a one-person team can't sustain?

## Options considered

| Option | Boundary strength | Cost |
|---|---|---|
| A. **Keep the 4 layer projects; modules = namespaces/folders across them; enforce with architecture tests** | Layer rule enforced by the compiler; module rule by tests (fails the build) | Minimal churn; ~6 projects total |
| B. One project per module per layer (`Catalog.Domain`, `Catalog.Application`, `Catalog.Infrastructure`, `Catalog.Contracts` …) | Both rules enforced by the compiler | 50–60 projects; slow restores and builds; heavy for one developer |
| C. One project per module (internal layers as folders) + a Contracts project per module | Module rule enforced by the compiler; layer rule by tests | ~30 projects; a restructure of all working code |

## Decision

**Option A.** Modules are top-level namespaces inside each layer:
- `Souq.Domain.<Module>`
- `Souq.Application.Features.<Module>` (with a `Contracts` sub-namespace)
- `Souq.Infrastructure.*.<Module>`

`tests/Souq.ArchitectureTests` enforces the layer rules now, and module rules as each module is migrated into its namespace.

## Why

- The compiler keeps guarding the rule learners break most: layer direction.
- Architecture tests are as strict as a compile error in practice, because they run in every `dotnet test` and will run in CI.
- Moving existing code happens **progressively**: a module moves when its phase touches it. There is no big-bang rename.
- If option C becomes necessary, the move is mechanical, because namespaces already equal modules.

## Consequences

- Module isolation depends on the tests staying comprehensive. Every new module adds its dependency test (checklist in [Modules.md §4](../04-MODULES/Modules.md#5-adding-a-module)).
- `internal` visibility can't hide module internals from other modules in the same project. Tests are the guard instead.

## Revisit when

- More than 3–4 developers work in parallel.
- A module is scheduled for extraction.
- Architecture tests keep catching the same violation.
- Build times suffer from one very large Application project.

Then move to option C.
