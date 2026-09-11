# ADR-0018: Observability — structured logs, W3C correlation and log scopes

- **Status:** Accepted and implemented in Phase 1B, 2026-09-11. Implements decision D-16.
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (every request and use case)
- **Related ADRs:** shares its correlation id with [ADR-0017](0017-error-contract.md); the tenant it adds to the log scope is resolved by [ADR-0006](0006-tenant-resolution.md) and [ADR-0022](0022-tenancy-enforcement.md); startup warnings are collected by [ADR-0020](0020-configuration-and-secrets.md)

## Context

- Logs were plain console text. There was no per-request summary, no correlation id a customer could quote, and nothing tied a log line to the user or use case that produced it.
- Phase 1A removed secrets and personal data from logs (reset links, raw emails, provider bodies).
- A multi-tenant SaaS is debugged from logs: "which tenant, which user, which request, how slow?" must be answerable without reproducing the problem.

## Problem

What logging and correlation foundation should every later module inherit, without adopting infrastructure the project cannot yet operate?

## Options considered

| Option | For | Against |
|---|---|---|
| Serilog + sinks | Rich enrichers and sinks | A second logging model next to `ILogger`; more packages to keep current |
| OpenTelemetry now (traces, metrics, exporters) | Industry standard, end to end | Needs a collector/backend to be useful — an operations decision for Phase 23 |
| **Built-in `ILogger` + JSON console + scopes + W3C trace ids** | Zero new runtime dependencies; structured output any log platform ingests; the same `Activity` ids OpenTelemetry uses later | Fewer conveniences than Serilog |
| A custom `X-Correlation-Id` accepted from clients | Clients can pass their own id | Arbitrary client text written into our logs; W3C `traceparent` already solves propagation |

## Decision

1. **Correlation id = the request's W3C trace id.** ASP.NET Core creates an `Activity` per request and honours an incoming `traceparent`. The same 32-hex value appears in:
   - the `X-Correlation-Id` response header (every response, success or error);
   - `traceId` in every ProblemDetails body ([ADR-0017](0017-error-contract.md));
   - the `CorrelationId` log scope (and the built-in `TraceId` scope field).
   Client-chosen correlation ids are not accepted.
2. **One log line per HTTP request** (`RequestLoggingMiddleware`): method, path **without the query string**, route template (for grouping), status, duration. 5xx at Error, everything else at Information, client aborts as 499.
3. **Scopes carry context to every log inside the request:** `CorrelationId`, `UserId` (when authenticated), and `UseCase` (the MediatR request name, from `UseCaseLoggingBehavior`). Handler logs, provider logs and EF command logs all inherit them. **Phase 2 adds `TenantId` to the same request scope.**
4. **Slow use cases** (> 500 ms) log a warning with name and duration.
5. **Never logged:** request or response bodies, query strings, headers (including `Authorization`), passwords, tokens, reset links, card data. Payloads are never attached to use-case logs. EF Core SQL text is at Warning by default (parameters are never logged).
6. **Production output is JSON** with scopes and UTC timestamps (`appsettings.Production.json`). Development keeps readable text.

## Why

- Built-in logging gives structured, correlated logs today with no new dependencies, and the ids are the ones OpenTelemetry will export later — nothing is thrown away.
- Scopes put context in one place instead of every log call, so Phase 2's tenant context is one line in one middleware.
- A header the customer can quote turns "it failed at checkout" into one log search.

## Consequences

- Log volume grows by one line per request. Acceptable; revisit with sampling if cost matters.
- Tests prove the header/`traceId` equality, the request line without query strings, scope propagation into SQL logs, and the absence of passwords, JWTs and `Authorization` values.

## Revisit when

- **Phase 23 (production readiness):** add OpenTelemetry tracing and metrics with an exporter, and alerting. The Activity-based ids carry over unchanged.
- Log ingestion cost or volume becomes a problem: add sampling for 2xx request lines.
