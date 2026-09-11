# ADR-0015: Testing strategy and tooling

- **Status:** Accepted and implemented in Phase 1A, 2026-09-11
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (the test layers every module uses)
- **Related ADRs:** makes the rules of [ADR-0002](0002-modular-monolith-structure.md) and [ADR-0003](0003-clean-hexagonal-boundaries.md) executable; the tenant isolation suite it anticipates is built by [ADR-0022](0022-tenancy-enforcement.md); every ADR from [ADR-0027](0027-customer-profile-and-erasure.md) onward lists its tests in a Verification section

## Context

- There are only unit tests (Domain 70, Application 63), using NSubstitute mocks.
- Concurrency, EF mappings, SQL behaviour, authorization attributes, uploads, and (from Phase 2) tenant isolation **cannot** be proven with mocks.
- The assertion library, FluentAssertions 8.x, requires a **paid license for commercial use** (verified on NuGet, 2026-09-11).
- The frontend has no tests.

## Problem

Which test layers and tools prove the critical behaviour of a commercial multi-tenant product without a slow or flaky suite?

## Options considered

- **Integration database:**
  - EF InMemory: not relational, no constraints, no concurrency tokens. ❌
  - SQLite: a different SQL dialect; no `rowversion`. ❌
  - A shared developer SQL Server: state leaks between runs, not reproducible. ❌
  - **Testcontainers SQL Server**: the real engine, disposable, same image as production. ✅
- **Architecture rules:** code review only · reflection on assembly references · **NetArchTest** (IL-level type dependencies, MIT). Assembly references can't express "controllers must not use Stripe".
- **Assertions:** keep FluentAssertions 8 (license cost and legal risk) · pin FluentAssertions 7.x (Apache-2.0, frozen) · **AwesomeAssertions** (Apache-2.0 community fork, drop-in API, maintained) · Shouldly (a different API, so a rewrite).
- **Frontend:** Jest · **Vitest** (native to Vite, zero config).

## Decision

| Layer | Project | Tools | Proves |
|---|---|---|---|
| Domain unit | `Souq.Domain.Tests` | xUnit + AwesomeAssertions | invariants, money, state machines |
| Application unit | `Souq.Application.Tests` | xUnit + NSubstitute + AwesomeAssertions | use-case orchestration and error paths |
| **Architecture** | `Souq.ArchitectureTests` | NetArchTest | layer rules, controller purity (module rules as modules migrate) |
| **Integration / API** | `Souq.IntegrationTests` | `WebApplicationFactory<Program>` + **Testcontainers.MsSql** (image `mcr.microsoft.com/mssql/server:2022-latest`) | migrations apply; concurrency; money round-trip; authorization boundaries (every admin endpoint); ownership; uploads; password reset; order cancellation stock |
| Frontend unit | `frontend` (`npm test`) | Vitest | pure logic (query and payload builders) |

- There is **one container per test run** (a shared fixture). Tests create their own data with unique values instead of resetting the database, so they are fast and there is no Respawn dependency.
- The integration tests run with `ASPNETCORE_ENVIRONMENT=Testing`, a throwaway JWT key, a temporary uploads folder, and a capturing email adapter.

## Why

- Only the real engine proves `rowversion`, unique indexes, precision, and (later) global query filters.
- NetArchTest turns documented dependency rules into failing builds.
- AwesomeAssertions removes a commercial-license liability with a namespace swap.

## Consequences

- **Docker is required** for `Souq.IntegrationTests`. The first run pulls the image (~2 GB), and SQL Server runs emulated on Apple Silicon (slower start-up).
- The suite takes longer, but it stays one command: `dotnet test`.

## Revisit when

- The suite exceeds about 5 minutes: parallelize with per-class databases.
- CI is added (Phase 23; GitHub Actions has Docker).
- E2E browser tests are added (Phase 19; Playwright is the likely choice).
