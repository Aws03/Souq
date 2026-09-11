# ADR-0001: Target architecture

- **Status:** Accepted, 2026-09-11
- **Deciders:** Platform owner (product), Claude (engineering)

## Context

Souq is moving from a working single-store application to a white-label multi-tenant SaaS sold to many small clients.

The codebase is a 4-project Clean Architecture solution (Domain, Application, Infrastructure, API) with:
- MediatR CQRS handlers;
- a rich Domain;
- 133 unit tests;
- a React SPA.

The team is one developer today. The product will grow to about 13 business capabilities, and correctness matters most in checkout, inventory, and payments.

## Problem

Which architecture carries the product through ~20 more phases without either:
- (a) collapsing into a ball of mud as capabilities multiply; or
- (b) paying distributed-systems costs before they buy anything?

## Options considered

Ten styles are evaluated one by one in [ArchitectureEvaluation.md](../ArchitectureEvaluation.md):
- layered monolith;
- Clean;
- Hexagonal;
- modular monolith;
- microservices;
- vertical slices;
- DDD;
- CQRS;
- event-driven;
- event sourcing.

The realistic candidates were:
1. Plain Clean Architecture, no module boundaries.
2. Vertical slices only, dropping the layers.
3. Microservices.
4. **A modular monolith using Clean/Hexagonal principles, selective DDD, vertical slices, and selective CQRS.**

## Decision

Adopt option 4:
- **Modular monolith** as the deployment and runtime shape.
- The **Clean Architecture** dependency rule for all code.
- **Hexagonal ports and adapters** at real integration points.
- **DDD** strategic design for module boundaries, and tactical patterns only where invariants are rich.
- **Vertical slices** to organize use cases.
- **CQRS** as separate command and query handlers plus projection-based reads, with read models only for reporting.
- **Event-driven** only at the edges (outbox, Phase 14).
- **No event sourcing.**

## Why

- It is the **smallest** design that gives strong boundaries. The layers already exist, and modules add business boundaries without new processes.
- **Checkout stays transactional** (stock, order, and coupon in one database transaction). Correctness is not traded for decoupling.
- **Operational cost stays near zero per tenant:** one app, one database, one pipeline.
- **The exit path stays open.** Contracts, owned data, and an outbox are what later extraction needs ([ADR-0012](0012-service-extraction-strategy.md)).
- It **preserves the working code** (the 133 tests) and evolves it instead of rewriting it.

## Consequences

- **Positive:**
  - A clear place for every piece of code.
  - Business modules can be changed independently.
  - The core is testable.
  - Simple operations.
- **Negative:**
  - Discipline is required: module contracts, reference-by-id, architecture tests.
  - Some duplication (snapshots).
  - The whole application is released together.
- **Follow-up:**
  - Module structure ([ADR-0002](0002-modular-monolith-structure.md)).
  - Boundaries ([ADR-0004](0004-module-boundaries.md)).
  - Enforcement through `tests/Souq.ArchitectureTests`.

## Revisit when

- Several independent teams need independent release cadences.
- One capability's scaling, reliability, or compliance profile diverges sharply from the rest (see the candidates in [Architecture.md §9](../Architecture.md#9-future-scaling-and-service-extraction)).
- Release coordination becomes the bottleneck.
