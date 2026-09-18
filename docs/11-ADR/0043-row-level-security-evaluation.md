# ADR-0043: SQL Server Row-Level Security is evaluated and **not adopted** — the application-level filter is build-enforced, RLS's marginal value is not

- **Status:** Accepted, 2026-09-18. Implements the M15 scope item that RLS be "evaluated as defense-in-depth and explicitly accepted or rejected with evidence, not left implicit" ([SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md)). Supersedes nothing.
- **Date:** 2026-09-18
- **Related modules:** all — this is about the isolation every module rests on
- **Related ADRs:** [ADR-0022](0022-tenancy-enforcement.md) defines the isolation this would sit beneath; [ADR-0008](0008-cqrs-strategy.md) for the read ports RLS would silently change the results of; [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) for the standing rule against infrastructure whose benefit is not measured

## Context

Souq is a shared-database, shared-schema multi-tenant system. Every business row carries a `TenantId`, and isolation is enforced at the application level in three layers ([MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md)):

1. **Reads** — an EF Core global query filter applied by reflection to every `ITenantOwned` entity in `AppDbContext`. With no store in scope it *throws* (`TenantContextMissingException`) rather than degrading to "all rows".
2. **Writes** — `TenantWriteGuardInterceptor` stamps `TenantId` on insert from the ambient context and rejects any attempt to save a row belonging to another store, logging Critical.
3. **The paths around both** — enforced at build time by `TenancyRuleTests`, which scans the compiled Infrastructure IL.

SQL Server Row-Level Security would add a fourth layer *inside the database*: a security policy whose predicate compares each row's `TenantId` against a per-session value, so that even a query which never passed through EF sees only its own store's rows.

The question M15 asks is whether that fourth layer earns its place.

## Problem

1. What would RLS actually protect against that is not already prevented?
2. What does it cost to run correctly under EF Core's connection pooling?
3. How does it interact with the platform area, which must read across every store by design?
4. Is the decision reversible, and what would change it?

## Decision

**Not adopted.** The application-level filter stays the single isolation mechanism, and this ADR records why, together with the conditions that would reopen the question.

### 1. The gap RLS closes is build-enforced shut

RLS defends against a query that reaches the database without the application filter. In this repository, every way that can happen is already a failing build:

| Path around the filter | What stops it today | Enforced by |
|---|---|---|
| `IgnoreQueryFilters` | Allowed in exactly one reviewed type (`PlatformQueries`), and every bypass there carries an explicit `TenantId` predicate or is an aggregate count | `TenancyRuleTests` scans Infrastructure IL and fails on any other caller |
| Raw SQL (`FromSql*`, `ExecuteSql*`, `SqlQuery*`) | There is none, anywhere in `src/` | `TenancyRuleTests` — zero occurrences allowed outside migrations |
| Bulk writes (`ExecuteUpdate`/`ExecuteDelete`), which skip `SaveChanges` and therefore the write guard | Four reviewed call sites, each listed with its reason | `TenancyRuleTests.ReviewedBulkWrites` |
| A new endpoint that forgets isolation | Every non-platform route with a resource id must appear in the isolation table | `TenantIsolationTests` walks the live endpoint table and fails on an unlisted one |
| A cross-store write that slips through a read | The interceptor throws and logs Critical — proven live, not inferred | `TenantIsolationTests` deliberately bypasses the read filter and asserts the write still fails |

This is the substance of the argument. RLS is usually recommended where the application filter is a convention that a developer can forget. Here it is a **checked property**: forgetting it does not produce a leak, it produces a red build. Adding a database layer to defend against a class of mistake that cannot reach `main` buys defence in depth against a threat the build already refuses to compile.

It does not cover *everything* — a human with a database connection, or a future service reading the same schema directly, is outside the build's reach. That residual is real and is recorded below.

### 2. The cost is a footgun, not a line of DDL

An RLS predicate needs a per-session value to compare against — `SESSION_CONTEXT` or `CONTEXT_INFO`. EF Core pools connections, so that value must be set **on every connection open**, and reset when the connection returns to the pool. The failure modes are not loud:

- Set too late (after the first query on a reused connection) and a request reads *the previous request's store* — a leak that only appears under concurrency, which is exactly when nobody is watching.
- Not set at all and the predicate matches nothing, so the store sees an empty catalogue and an empty order list — a silent, total data loss to the user's eye, with no error to trace.

Both are worse than the present state, because the present state fails loudly: `CurrentTenantId` throws when there is no store in scope. Replacing a mechanism that throws with one that silently returns the wrong rows is a step backwards unless the new mechanism is perfect, and the perfection has to be maintained by everyone who touches connection handling afterwards.

### 3. The platform area contradicts the predicate by design

`PlatformQueries` reads across every store — that is what the platform administration area *is*. Under RLS, every one of those reads needs the session to be in a second mode ("no tenant predicate"), which means the session value now encodes two different things and the correctness argument becomes "the mode is right" rather than "the filter is applied". The tables the platform reads without a filter (`Tenants`, `TenantDomains`, `AuditEntries`, `OutboxMessages`) are deliberately unfiltered and would each need an exemption in the policy.

So RLS here would not be a blanket "the database enforces isolation". It would be a policy with a mode switch and four exemptions — which is a second copy of the application's own rules, kept in a different language, that has to be changed in step with them. Two copies of a rule that must agree is a familiar way to end up with neither being trusted.

### 4. Least-privilege logins are the better next investment

The threat RLS best addresses here — a connection that is not the application — is addressed more directly and at lower risk by the work already scripted but not yet applied: `scripts/sql/least-privilege-logins.sql` and [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md). The application currently connects as `sa` in the shipped compose file, which means the account it uses could disable an RLS policy anyway (`ALTER ANY SECURITY POLICY`). Adding RLS while the application holds sysadmin is defence that the defender can switch off. Fixing the login first is both cheaper and a precondition for RLS being meaningful at all.

## Consequences

- Tenant isolation continues to rest on the application filter, the write guard, and the build-time rules that keep both honest. No schema change, no migration, no per-connection state.
- The residual risk is written down rather than implied: **a direct database connection, or a second service reading this schema, is outside every mechanism listed above**. Today there is no such consumer; the day there is one, this decision is void.
- Recorded in [SecurityControls.md](../07-SECURITY/SecurityControls.md) §12 as an accepted gap rather than an oversight, and in [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md).

**This decision is revisited if any of the following becomes true:**

1. A second application, reporting tool or ETL job reads this schema directly — then the build-time argument no longer covers the readers.
2. The application stops connecting with an account that could disable the policy (i.e. least-privilege logins are applied), *and* a compliance requirement asks for database-level isolation explicitly.
3. The architecture tests in `TenancyRuleTests` are weakened or removed — they are what makes the present argument true, and their removal would invert this decision rather than simply relax it.

## Alternatives considered

- **Adopt RLS now, alongside the application filter.** Rejected: the failure modes under connection pooling are silent, the platform area needs a mode switch, and the account the application uses could disable the policy. The cost is real and present; the benefit is against a class of mistake the build currently refuses.
- **Database-per-tenant.** Out of scope and contrary to the shared-schema design in [ADR-0022](0022-tenancy-enforcement.md); it would change provisioning, migrations, backup and cost per store. Not a security decision to be made inside a security review.
- **Do nothing and say nothing.** Rejected because that is the state M15 exists to end: an unmade decision reads, later, as an unnoticed gap.
