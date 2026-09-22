# ADR-0020: Configuration and secrets — validated options, fail-fast startup, no silent dev fallbacks

- **Status:** Accepted and implemented in Phase 1B, 2026-09-11, partially superseded by [ADR-0023](0023-sessions-and-credentials.md) (access-token lifetime) and [ADR-0034](0034-notifications-outbox.md) (email provider required at startup)
- **Date:** 2026-09-11
- **Related modules:** Cross-cutting (configuration and startup); Payments and Notifications (provider selection)
- **Related ADRs:** ports and development fakes from [ADR-0003](0003-clean-hexagonal-boundaries.md); partially superseded by [ADR-0023](0023-sessions-and-credentials.md) (narrower access-token lifetime) and [ADR-0034](0034-notifications-outbox.md) (a missing email provider now refuses startup instead of warning); per-store gateway secrets are added by [ADR-0031](0031-payments-and-refunds.md)

## Context

- Secrets already came from user-secrets or environment variables (Phase 0/1A), but **validation was ad hoc**: the JWT key was only checked for being non-empty (a short key failed later, at the first login), and storage options were configured in `Program.cs` (Phase 0 D10).
- **A development convenience had become a production vulnerability:** with no Stripe key, `FakePaymentService` was selected automatically in every environment. The Docker stack runs as Production, so any visitor could place an order that was marked *Paid* without paying.
- Email providers used a static `HttpClient` with the default 100-second timeout inside the request path.

## Problem

How should configuration be structured, validated and classified so that missing or weak settings stop the app before it serves traffic, and development shortcuts never run silently in production?

## Options considered

- Keep manual checks in `Program.cs` — scattered, easy to forget, fail late.
- Validate only the connection string and JWT key — leaves provider selection unsafe.
- **Typed options with validators, validated before the database is touched, and explicit selection of any development fallback** — chosen.
- A secret manager (Key Vault, AWS Secrets Manager) now — an operations decision for Phase 23; the options layer does not change when it arrives.

## Decision

1. **Every configuration section is a typed options class** bound once, with an `IValidateOptions` validator or `Validate(...)` rule and `ValidateOnStart()`.
2. **Fail fast:** `Program.cs` calls `IStartupValidator.Validate()` right after `Build()`, before migrations, seeding or requests. Messages name the key (`Jwt:Key`) and never print its value.

   | Required | Rule |
   |---|---|
   | `ConnectionStrings:Default` | present |
   | `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpiryMinutes` | key ≥ 32 bytes (HS256), issuer and audience present, expiry 5–1440 min |
   | Payment provider | Stripe key, or `Payments:Provider=Demo` explicitly (outside Development/Testing) |
   | `Stripe:*` when Stripe is selected | secret key; publishable key outside Development |
   | `Storage:Local:RootPath` | absolute (defaults to `wwwroot/uploads`) |
3. **Development conveniences run implicitly only in Development and Testing:** the fake payment gateway, reset links in the console log, the dev admin. Elsewhere the fake gateway must be requested explicitly and is logged as a warning at every start.
4. **Operational warnings** (fake gateway, no email provider, missing webhook secret) are collected while registering services and logged once at startup.
5. **Provider HTTP clients** come from `IHttpClientFactory` with a 15-second timeout.
6. **Classification:**

   | Kind | Where | Examples |
   |---|---|---|
   | Application configuration (same everywhere, not secret) | `appsettings.json` | JWT issuer/audience, log levels, sender display names |
   | Environment-specific configuration | `appsettings.{Environment}.json`, environment variables | JSON log format, `FRONTEND_URL`, `Storage:Local:RootPath`, `Payments:Provider` |
   | Secrets | user-secrets (dev), environment variables / secret store (deployed) — **never in git** | connection string, `Jwt:Key`, Stripe keys, email API keys, seed admin password |

## Why

- A misconfigured deployment should fail at deploy time with a clear message, not at the first customer request.
- The fake-gateway rule turns a silent money leak into an explicit, logged, deliberate choice.

## Consequences

- **Behaviour change:** the Docker stack now refuses to start without Stripe keys unless `PAYMENTS_PROVIDER=Fake` is set in `.env` — an existing local `.env` needs that line for demos.
- Startup-refusal tests run without a database, which proves validation happens before any data access.

## Revisit when

- Phase 23: move deployed secrets to a managed secret store and rotate them.
- Phase 11: per-tenant gateway secrets (D-13) need encrypted storage; they follow the same "typed, validated, never logged" rule.
