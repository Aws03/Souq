# ADR-0003: Clean dependency rule + ports and adapters

- **Status:** Accepted, 2026-09-11

## Context

The solution already follows API → Infrastructure → Application → Domain through project references. Phase 0 still found leaks:
- The Stripe SDK is used inside `PaymentsController` (D1).
- Stripe is configured through a static global (D2).
- Some exceptions escape the core as provider or framework types (C11).

## Problem

How strictly should technology be kept out of the core, and how many interfaces are "enough"?

## Options considered

1. **Pragmatic Clean:** the Application layer may reference EF Core (`IAppDbContext`). Less code, but the core is bound to the ORM.
2. **Strict Clean + ports everywhere:** every service gets an interface. Maximum isolation, abstraction for its own sake.
3. **Strict dependency rule + ports only at real variation points.** A port exists when there is (or will certainly be) more than one implementation, or when the dependency is an external system.

## Decision

**Option 3.**
- The **Domain** references nothing.
- The **Application** references Domain, MediatR, and FluentValidation only (no EF Core, ASP.NET Core, or provider SDKs).
- **Ports** (interfaces) live in `Application/Common/Interfaces` for payment, email/notifications, file storage, password hashing, token issuing, clock, current user, and tenant context. Aggregate repository ports stay in the Domain.
- **Adapters** live in Infrastructure (driven) and API (driving: controllers, webhooks, background triggers).
- Provider exceptions are translated at the adapter boundary. For example, `DbUpdateConcurrencyException` becomes `ConcurrencyConflictException`.

## Why

- It keeps the core framework-free and unit-testable (the existing strength) while avoiding pass-through interfaces.
- It directly fixes D1/D2: webhook parsing and client configuration move behind `IPaymentService`, and Stripe uses a `StripeClient` instance.
- "Real variation point" is an objective test that stops interface creep.

## Consequences

- Every new external integration starts with a port and a fake adapter for tests and development (the `FakePaymentService`/`ConsoleEmailService` pattern).
- Read queries need a port too: query services, [ADR-0008](0008-cqrs-strategy.md).
- `tests/Souq.ArchitectureTests` enforces the dependency rules. A violation fails the build.

## Revisit when

- An ORM-specific feature is so valuable in the Application layer that wrapping it costs more than it protects. Decide per case, in a new ADR.
