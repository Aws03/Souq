# ADR-0048: The payment port becomes flow-agnostic — a payment attempt, a discriminated start result, and a webhook inbox

- **Status:** Accepted as the design, 2026-09-20. **Not implemented**, and deliberately not implementable until **D-13** and the launch-provider choice are answered, because the first adapter sets the port's vocabulary. Supersedes no decision; it re-shapes the port that [ADR-0031](0031-payments-and-refunds.md) introduced and [ADR-0036](0036-payment-intent-state-machine.md) refined.
- **Date:** 2026-09-20
- **Related modules:** Payments, Ordering, Platform
- **Related ADRs:** [ADR-0031](0031-payments-and-refunds.md) (the payment path and per-store accounts), [ADR-0036](0036-payment-intent-state-machine.md) (reading the intent's state rather than a boolean), [ADR-0014](0014-money-precision.md) (money and minor units), [ADR-0003](0003-clean-hexagonal-boundaries.md) (a port only at a real variation point), [ADR-0047](0047-commercial-control-plane.md) (where the platform's own money path lives)

## Context

`IPaymentService` was built in Phase 11 to keep the Application layer free of provider SDKs, and at that job it
succeeds: no Stripe type crosses the boundary, and swapping the *account* a charge goes to is already a runtime
decision made by `PaymentGatewayRouter`.

It is nevertheless **not** provider-agnostic, and the gap is structural rather than cosmetic. The port is a
two-step intent flow: `CreateIntentAsync` returns a `ClientSecret`, `GetClientConfigAsync` returns a
`PublishableKey`, and `PaymentIntentState` is a rename of one provider's intent statuses. That models one
provider's client-side confirmation shape.

Research against current official documentation established the constraint that makes this urgent: **Stripe does
not operate in Jordan** — its own availability page lists the UAE as the only MENA country — and the providers
that do serve this market are predominantly **redirect-first**. They return a hosted page URL, or an
auto-submitted signed form, or a widget session id. None of them has a client secret to return. Cash on delivery
and bill-payment rails have no provider call at all.

Prior art agrees. Seven independent e-commerce platforms were examined; every one of them carries an untyped
provider bag beside a narrow typed surface, and none of them fits a redirect flow and a direct API flow into one
clean method — one models a redirect as a thrown exception, another as a second void method, another as an
entire out-of-band protocol.

## Problem

1. How does one port serve an intent/confirm flow, a redirect-first hosted page, a client-side widget, an
   auto-submitted form POST, and cash on delivery, without the core knowing which it is?
2. Where does provider-specific state live when it must be persisted and cannot be typed?
3. How is a webhook routed to the right store when the callback carries no store host?
4. What must **not** be abstracted, because abstracting it would state something false?

## Options considered

| Option | Verdict |
|---|---|
| **Keep the intent-shaped port and adapt redirect providers into it** | Rejected. The adapter would have to invent a client secret and the core would still believe a browser confirms against a provider SDK. It encodes one provider's shape as the universal one. |
| **A second, parallel port for redirect providers** | Rejected. Two ports means the core chooses, which is exactly the provider knowledge the port exists to remove. |
| **Throw a "redirect" signal (one prior-art project's approach)** | Rejected. Control flow by exception for an ordinary outcome, and it cannot carry a form POST or an offline instruction. |
| **A second void method the core calls afterwards (another project's approach)** | Rejected. The core must then know which providers need the second call; the project that did this also needed a capability enum and an escape hatch to make it work. |
| **One entry verb returning a discriminated result, plus a persisted attempt and an opaque bag** | **Chosen.** Two projects independently converged on the status-value form, and it is the only one that carries a URL, a widget payload, a signed form and an offline instruction equally. |

## Decision

### 1. `StartPayment` replaces `CreateIntent`, and returns a discriminated result

The core carries the result without understanding it:

`Redirect(url)` · `ClientScript(sessionId, widgetConfig)` · `BrowserPost(url, signedFields)` ·
`CompletedSynchronously(status)` · `DeferredOutOfBand(instructions)`

The last is not an edge case in this market: it covers cash on delivery and bill-payment rails, where there is
no provider call and settlement is confirmed by a human or by a later reconciliation.

### 2. A *PaymentAttempt* is a first-class persisted aggregate

Distinct from `Order` and from the provider's reference, keyed by (tenant, order, provider key, provider
reference), with an opaque JSON column for the provider's own state. Redirect-first means the shopper **leaves
the process**, so the attempt must be addressable when they return, when the webhook arrives, and when a
reconciler polls — in any order, possibly concurrently. Three ingress paths converge on one transition and all
three must be idempotent and order-independent; the browser return is the least trustworthy of them.

The opaque bag is deliberate. Every prior-art project has one and none succeeded in typing it. It is treated as
storage, not as a hole in the abstraction.

### 3. Money totals derive from an append-only event log

Not from a mutable status column. This is the only surveyed design that survives out-of-order webhooks, partial
captures, partial refunds and duplicate deliveries, and it is what makes authorized/captured/refunded/pending
balances answerable rather than asserted.

### 4. The webhook is an inbox, not a handler

Persist `(provider, providerEventId)` under a unique index, acknowledge fast, process asynchronously. Two
details are load-bearing:

- **Verification takes raw bytes and headers**, not `(payload, signature)`. At least one regional provider
  *decrypts* the body (AES-GCM, with the IV and tag in headers) rather than verifying a signature, and a
  signature-shaped port cannot express that. Another computes its hash over an amount formatted to the
  currency's decimal count — so a minor-unit bug there fails *verification*, not just arithmetic, which puts
  currency handling on the security path.
- **The route carries the tenant.** A provider callback has no store host, and Souq resolves tenants by host.
  The webhook route carries a provider key and an opaque tenant reference, and verification is per-tenant.

### 5. Capabilities are data the adapter declares

The core asks before offering an operation: auth-then-capture, partial capture, partial refund, stored
instruments, transaction-time split, programmatic sub-merchant onboarding, supported currencies, and **amount
granularity**. Granularity is not formatting: one provider documents that VISA requires three-decimal amounts to
end in zero, which makes 10 fils the effective minimum increment for a card sale and reaches back into pricing.

### 6. Refund is its own entity; idempotency is Souq's

A refund has its own lifecycle and its own provider reference, because under a split it may require a second
reversal that fails independently. Idempotency keys are minted from Souq's own aggregate identity and
de-duplicated locally, because most regional providers document no idempotency mechanism at all.

### 7. What is deliberately not abstracted

Charge topology and who is merchant of record; how a fee is expressed and in which currency it settles; KYC
field names; payout scheduling; reserve and negative-balance machinery; dispute evidence workflows; report
formats; minor-unit encoding; 3-D Secure step-up timing; national e-invoicing clearance. These live in the
adapter, and the two that decide *liability* live as **stored data on the tenant**, because one platform can
legitimately have stores on both sides of that line.

## Consequences

**Good.** The port can hold a redirect provider, a widget provider, an API-first provider and an offline method
without the core branching. Provider state has a defined home instead of leaking as typed fields. The webhook
path stops depending on host-based tenancy, which it never could have satisfied. Capability mismatches surface
as a refusal to offer an operation rather than as an unhandled provider error at checkout.

**Costs.** This is a larger change than TD-52's injectable factory, and it touches a payment path that
[AGENTS.md](../../AGENTS.md) §0 rule 3 says is never changed silently. It must land with tests, behind the
prerequisites below, and not inside an audit phase. The checkout UI is currently provider-specific and will
need a per-result-kind renderer. `PaymentIntentState` has no exhaustive switch today, so a new state silently
falls through to a generic failure — that must be fixed while the vocabulary is being changed anyway.

**Prerequisites, not follow-ups.** TD-50 (a payment records the *kind* of account, never its identity, so
replacing a store's keys strands every refund the old account took — and a failed refund cannot be retried) and
TD-52 (the router builds its gateway inline, so the routing rules rest on reading the code). Both must be closed
before a second provider exists, because a second provider makes account replacement routine.

**Revisit when** a second adapter has shipped. A port validated against one provider is a hypothesis; the honest
test is whether the second one fits without changing Application. Validate against a redirect-first provider and
an offline method, **not** against two API-first card providers.
