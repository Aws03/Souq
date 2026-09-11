# ADR-0012: Future service extraction strategy

- **Status:** Accepted, 2026-09-11

## Context

Microservices are rejected for now ([ADR-0001](0001-target-architecture.md)), but some capabilities may later need independent scaling, reliability, or compliance isolation.

## Problem

How do we stay "monolith first" without becoming an inseparable ball of mud?

## Options considered

1. Ignore extraction until needed. The risk is that cross-module coupling makes extraction a rewrite.
2. Build service-ready infrastructure now (broker, per-module databases). The risk is premature complexity.
3. **Keep extraction-enabling design rules now, and pay for infrastructure only on evidence.**

## Decision

**Option 3.** The design rules that apply now:
1. Modules own their data; other modules reference it by id and snapshot what they need.
2. Cross-module calls go through contracts. Calls that would need a saga after extraction are designed as **reserve → commit/release** from the start (Inventory, Promotions).
3. Side effects with multiple consumers go through an **outbox** (Phase 14). Messages are tenant-scoped and handlers are idempotent.
4. External systems stay behind ports.
5. There are no cross-module FKs except `TenantId`.

**Scaling order before any extraction:**
1. Queries and indexes.
2. Caching.
3. Horizontal app instances.
4. Read replicas.
5. Dedicated tenant databases.

**Candidates, in likely order:** Notifications → Media processing → Search → Reporting → Payments → Inventory. The per-candidate boundary, contract, data, and messages are listed in [Architecture.md §9](../Architecture.md#9-future-scaling-and-service-extraction).

## Why

- These rules cost little today (they are good modular design anyway) and turn a future extraction into a mechanical move instead of a rewrite.

## Consequences

- Some duplication (snapshots) and a few extra interfaces.
- The outbox arrives before the first asynchronous side effect.

## Revisit when (the triggers for extracting a module)

- A module's load profile or failure modes hurt the rest of the system (e.g. email bursts).
- Compliance requires isolation (PCI scope reduction for Payments).
- A dedicated team needs its own release cadence.
- A capability needs a technology SQL Server can't reasonably provide (e.g. relevance search).
