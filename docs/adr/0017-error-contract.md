# ADR-0017: Error contract — RFC 7807 ProblemDetails with typed errors and stable codes

- **Status:** Accepted and implemented in Phase 1B, 2026-09-11. Implements decision D-08.

## Context

After Phase 1A:
- Errors were a custom `{ "error", "code" }` body. One helper mapped `Result` failures to HTTP, but it did so by **matching code strings** (`"NotFound"`, `"Conflict"`), and `AuthController`/`ReviewsController` still had their own mappings (Phase 0 D5).
- Some handlers caught domain exceptions and returned a `Result`; others let them bubble to the middleware (D6). The same rule could surface as two different responses.
- Framework-generated errors (401/403 from authentication, 404 for unknown routes, 400 from JSON binding) had **no body at all**, or ASP.NET's own shape — and the JSON reader echoed internal type names (`could not be converted to Souq.…`).
- Server messages are Arabic literals, so English-UI users saw Arabic text (A9).

## Problem

What single error shape should every API failure use, how do failures map to status codes, and what can the frontend rely on?

## Options considered

| Option | For | Against |
|---|---|---|
| Keep `{ error, code }` | No change | Non-standard; no `traceId`; framework errors still inconsistent |
| RFC 7807 ProblemDetails, message only | Standard, built into ASP.NET Core | The frontend would parse human text to decide behaviour |
| **ProblemDetails + stable `code` + typed `Error` with a kind** | Standard shape; the code is a machine contract; one mapping table | A code catalogue to maintain |
| Exceptions for every failure | One mechanism | Expected outcomes (not found, duplicate) become control flow by exception |

## Decision

1. **Every error body is `application/problem+json`** with `type`, `title`, `status`, `detail`, plus two extensions:
   - `code` — a stable identifier (`InvalidCredentials`, `StockChanged`, `InsufficientStock`…). **This is the contract.** `detail` is human-readable and may change.
   - `traceId` — the W3C trace id of the request, equal to the `X-Correlation-Id` header and the log scope ([ADR-0018](0018-observability.md)).
   - Validation errors add `errors`: field → messages, keyed by the **JSON path the client sent** (`items[0].quantity`).
2. **Two failure paths, one rule each:**
   - A use case returns `Result.Failure(Error)` for outcomes **it decides**: not found, duplicate, precondition conflict, provider unavailable. `Error` carries `Code`, `Message` and `ErrorKind`.
   - A rule **guarded by an entity** throws a `DomainException`, which carries its own stable `Code`. Handlers never catch domain exceptions.
3. **One mapping table** (`ProblemDetailsConventions`):

   | Kind / exception | Status | Example codes |
   |---|---|---|
   | `Validation`, FluentValidation, JSON binding | 400 | `ValidationFailed`, `FileTooLarge`, `UnsupportedMediaType` |
   | `Unauthorized`, missing/invalid token | 401 | `InvalidCredentials`, `Unauthenticated` |
   | `Forbidden`, authenticated without permission | 403 | `Forbidden` |
   | `NotFound` — also for resources owned by someone else | 404 | `NotFound` |
   | `Conflict`, `rowversion`, unique index | 409 | `StockChanged`, `ConcurrencyConflict`, `DuplicateValue`, `EmailTaken` |
   | `BusinessRule`, any `DomainException` | 422 | `InsufficientStock`, `InvalidOrderOperation`, `InvalidMoney` |
   | `Unavailable` | 503 | `PaymentUnavailable` |
   | anything else | 500 | `ServerError` — generic text, details only in the log |
4. **Implementation uses the framework:** `AddProblemDetails` (with one `CustomizeProblemDetails`), an `IExceptionHandler`, and `UseStatusCodePages` for empty framework responses. Input-formatter exception messages are disabled.
5. **The frontend** reads ProblemDetails through one tested helper. When the UI language differs from the server's (Arabic), it shows the translation of `code`; otherwise it shows the more specific `detail`. Unknown codes fall back to `detail`.

## Why

- A single shape means the frontend has one error path, and support has a `traceId` for every failure.
- The kind — not a string comparison — decides the status, so adding a failure never touches a controller.
- 422 separates "your request is well-formed but the business says no" from "your request is malformed" (400), which the UI treats differently (fix a field vs. explain a rule).
- One rule per failure source ends the D6 ambiguity.

## Consequences

- **Deliberate status changes:** business-rule rejections moved from 400 to 422 (cancel twice, too many decimals for the currency, reused reset link); uniqueness and "in use" conflicts moved from 400 to 409. Integration tests assert the new status **and** the code.
- Codes are a contract: domain exception codes are pinned by a test, and the Arabic/English translation keys must stay identical (frontend test).
- Unexpected exceptions never reveal their message, type or stack trace.

## Revisit when

- Server-side localization arrives (tenant languages, Phase 5 / D-10): `detail` could follow `Accept-Language`, and the frontend would stop translating.
- A public API consumer exists: publish per-code `type` URIs and a versioned code catalogue.
