# ADR-0045: The production edge and observability stack — what this repository decides, and what it deliberately leaves to a deployment

- **Status:** Accepted, 2026-09-18. Implements the M17 scope item calling for "an ADR for the TLS/CDN/observability stack choices" ([SouqMasterPlan.md](../12-ROADMAP/SouqMasterPlan.md)). Extends [ADR-0018](0018-observability.md), which chose structured logging and deferred metrics and traces to this phase.
- **Date:** 2026-09-18
- **Related modules:** none in particular — this is about everything around them
- **Related ADRs:** [ADR-0018](0018-observability.md) (logging, correlation ids); [ADR-0020](0020-configuration-and-secrets.md) (no silent fallbacks); [ADR-0007](0007-database-strategy.md) and R-18 (the migration step, decided in M17); [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §5

## Context

M17's job is everything in the production picture that is genuinely engineering. The difficulty is that most of an edge and observability stack is not: a certificate needs a domain, a metrics backend needs an account, a CDN needs a bill. [OwnerDecisions.md](../09-OPERATIONS/OwnerDecisions.md) already carves those out.

What this ADR does is draw the line precisely, so that what remains is a configuration step rather than a code change — and so nobody later mistakes "we deferred this" for "we forgot this".

## Problem

1. Metrics and traces: adopt OpenTelemetry now, or not?
2. TLS termination and automated certificates for merchant domains: what does the repository own?
3. A CDN for uploads and assets: decide or defer?
4. What must be true for each of these to become a one-line deployment change?

## Decision

### 1. Metrics: instrument now with the runtime's own API; no exporter until there is a destination

[ADR-0018](0018-observability.md) rejected OpenTelemetry with the reason "needs a collector/backend to be useful — an operations decision for Phase 23". That reasoning still holds for the **exporter**, and does not hold for the **instrumentation**, and M17 separates the two:

- **What is measured is an engineering decision, taken here.** `SouqMetrics` defines four counters with `System.Diagnostics.Metrics` — part of the .NET runtime, **no new dependency**. They emit whether or not anything is listening, `dotnet-counters` can read them today, and any exporter added later picks them up through configuration rather than code.
- **Where they are sent is a deployment decision**, and is not taken here.

The four were chosen against one test: *would this wake a person?* Everything else would be a dashboard nobody reads.

| Counter | The question it answers | Why it matters |
|---|---|---|
| `souq.outbox.dead_lettered` | How many messages will never be delivered? | An order confirmation or a password-reset link that silently never arrives |
| `souq.tenancy.cross_tenant_write_blocked` | Is anything trying to write across stores? | Either an isolation defect or an attempt; both wake someone |
| `souq.auth.login_failed` (tagged by outcome) | Is this forgetfulness or credential stuffing? | The rate is the whole difference, and M15 added the log lines this aggregates |
| `souq.search.log_dropped` | Is the search buffer overflowing? | A measurement being lost silently, which is how a measurement lies |

Each already had a log line. A log answers "what happened in this request"; a counter answers "how often did this happen this hour", and only the second is something an alert can be built on. High-cardinality tags (store id, user id) are deliberately excluded — they multiply time series in any backend, and the specific store is in the log line written at the same instant. The metric is for alerting; the log is for investigating.

**Traces are not instrumented.** ASP.NET Core already creates an `Activity` per request and honours an incoming `traceparent`, and its 32-hex id is already in every log line and every error body (ADR-0018). Exporting spans needs a collector; until one exists, adding the SDK buys nothing that the trace id in the logs does not already give.

### 2. TLS: the repository owns the behaviour, the deployment owns the certificate

Already built and tested: HSTS outside Development with a configurable max-age and deliberately no `includeSubDomains`/`preload` (stores bring their own domains, and preload is close to permanent); `X-Forwarded-Proto` honoured from trusted networks only, with a startup diagnostic when it arrives untrusted — and, since M15, a second diagnostic for a proxy chain longer than `ForwardLimit`; the API's own enforcing CSP; the SPA's CSP with a browser pass across sixteen routes.

**Not owned here, and not pretended otherwise:** the terminator and the certificate. `nginx.conf` listens on port 80 and the compose stack has no TLS, which [Deployment.md](../09-OPERATIONS/Deployment.md) states plainly. The recommended topology is to terminate TLS at the edge in front of nginx — and M15's finding that `ForwardedHeaders:ForwardLimit` defaults to 1 is exactly the trap that topology sets, now configurable and detected.

**Automated TLS for merchant domains (R7) is not built.** It needs a real edge that can answer an ACME challenge for a domain a merchant points at it — infrastructure this repository has no access to. What it *can* do is already done: `TenantDomains` records each host, a domain is verified before it is trusted, and the store map resolves from the host. An on-demand-TLS edge (Caddy, or a managed equivalent) reads that list; that integration is a deployment's, and the data it needs exists.

### 3. CDN: deferred, and with a measurement rather than a shrug

Uploads are served by the API from local disk under a strict `default-src 'none'; sandbox` policy, with an eight-extension allow-list. A CDN would reduce origin bandwidth and improve distant latency.

M16 measured the origin: under concurrency the API container sat at **0.54% CPU** while the database ran at 47%. Serving assets is not what this system is short of, so a CDN today is cost and a cache-invalidation surface for a bottleneck that does not exist. The condition that changes it is bandwidth or distant-latency measurement, not intuition — the same rule ADR-0044 applies to caching.

TD-20 (uploads on local disk) is the related and more pressing item: local disk stops working when there is more than one instance, which is Stage 2 of [ScalingStrategy.md](../09-OPERATIONS/ScalingStrategy.md). `IFileStorage` is already the seam, so that is a new implementation rather than a change to any caller.

### 4. What makes each of these a configuration step

- **Metrics:** add an exporter package and a destination. Instrument names, tags and meaning do not change.
- **TLS:** put a terminator in front, set `ForwardedHeaders:KnownNetworks` to its address and `ForwardLimit` to the hop count. Both are settings; both have a startup diagnostic when they are wrong.
- **Migrations:** `Database:MigrateOnStartup=false` plus the bundle step (M17, R-18).
- **Database identity:** `scripts/sql/least-privilege-logins.sql` and two connection strings — re-verified against today's code, including M13's background services.
- **Backups:** the schedule, an off-site copy and an alert destination; the script, the verifier, the drill and now an opt-in retention prune all exist.

## Alternatives considered

- **Adopt OpenTelemetry now, exporter and all.** Rejected: the exporter has nowhere to send. That is the same reason [ADR-0018](0018-observability.md) gave, and it is still true — what changed is recognising that it applies to the exporter and not to the instrumentation, which is why four counters exist here with no dependency at all.
- **Instrument nothing until a backend exists.** Rejected: it defers the part that is actually an engineering decision. *What* to measure — and what not to, such as high-cardinality tags — is a judgement about this system, and postponing it until someone is buying a metrics product means it gets made in a hurry by whoever is buying.
- **Ship TLS in the compose stack with a self-signed certificate.** Rejected: it would make the local stack look production-shaped while teaching browsers to accept a warning, and it does not exercise anything the real terminator will do. `Deployment.md` saying plainly that there is no TLS here is more useful than a certificate that proves nothing.
- **Adopt a CDN now, since it is standard for a storefront.** Rejected on M16's measurement: the API sat at 0.54% CPU under load. This is exactly the "it would scale better" reasoning the plan's §2 forbids without evidence.
- **Move uploads to blob storage in this phase (TD-20).** Deferred rather than rejected: `IFileStorage` is already the seam, but a blob implementation needs a cloud account to be worth writing, and local disk is correct until Stage 2 puts a second instance behind a load balancer. It is the first thing to build when that happens.

## Consequences

- No new runtime dependency is added in M17. Four counters exist and emit; nothing exports them yet, and that is recorded rather than implied.
- The remaining production work is a known, listed set of deployment actions, each of which has a mechanism that has been rehearsed here rather than only described.
- **The honest limitation:** none of this has run against a real domain, a real certificate, a real metrics backend or a real off-site copy, because no such target exists yet. Everything above was exercised against the container stack. That is a genuine difference and [ReleaseReadiness.md](../09-OPERATIONS/ReleaseReadiness.md) keeps those items open rather than closing them on a rehearsal.
