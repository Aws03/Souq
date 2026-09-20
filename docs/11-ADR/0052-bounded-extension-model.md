# ADR-0052: Customer-specific extension is integration, not execution — outbound webhooks and bounded configuration, and no merchant code in this process

- **Status:** Accepted as the design, 2026-09-20. **Not implemented.** It deliberately does **not** meet the condition [ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §14 sets for revisiting per-tenant custom code, and says so. Supersedes nothing; it answers the question ADR-0011 left open once a paying customer asks for something the product does not do.
- **Date:** 2026-09-20
- **Related modules:** Platform, Notifications, and a proposed *Billing*
- **Related ADRs:** [ADR-0011](0011-white-label-architecture.md) (configuration-driven branding; no arbitrary CSS or scripts), [ADR-0035](0035-white-label-runtime.md) (how the SPA boots from store configuration), [ADR-0034](0034-notifications-outbox.md) (the durable delivery this reuses), [ADR-0024](0024-platform-administration.md) (settings as one validated document)

## Context

The moment there is a paying customer there is a request the product does not do, and the three obvious answers
— a fork, a per-tenant branch, or `if (tenant == X)` — are each the fastest way to destroy a white-label
product. ADR-0011 already refuses all three, and a repository-wide sweep during the commercial audit found
exactly one tenant-keyed conditional in the whole codebase: a boot-time warning that the default store still
carries its seed name. Customization today is genuinely data-driven.

[ExplicitNonGoals.md](../02-ARCHITECTURE/ExplicitNonGoals.md) §14 names per-tenant custom code, themes and CSS
injection as a non-goal, and names the condition that would revisit it: *"a paid tier with sandboxed extension
points, designed as a product feature available to everyone"*. A commercial platform makes that condition
plausible for the first time, so it needs an answer rather than silence.

Research into how mature platforms bound customer-specific code found a consistent cost, not a consistent
technique: running merchant code needs a sandbox with an execution budget, an egress proxy, determinism
restrictions, resource metering and a separate privilege boundary — and the platforms that do it publish
per-transaction governor limits precisely because one tenant's code can otherwise monopolise shared resources.
That is a platform in its own right, with its own security surface, its own upgrade policy and its own support
boundary.

It also found the alternative that mature platforms reach for first, and it is not a sandbox: **let the
merchant's own system react to events.** The code then runs on the merchant's infrastructure, under their
credentials, at their risk.

## Problem

1. What is the answer to "our biggest customer needs X" that is neither a fork nor a sandbox?
2. Where is the line between configuration Souq will accept and code it will not run?
3. What does the platform owe a merchant who integrates — delivery guarantees, security, and a contract?

## Options considered

| Option | Verdict |
|---|---|
| **A fork or a per-tenant branch** | Rejected, unchanged. Every customer becomes a branch that must be upgraded by hand, and the upgrade cost compounds with each one. |
| **`if (tenant == X)` in product code** | Rejected. It is a fork that hides in the diff, and it is how a codebase stops being able to answer "what does the product do?". |
| **Arbitrary CSS or script injection per store** | Rejected. It is a cross-site-scripting vector in a shared origin, and the SPA's content-security policy would have to be widened to nothing to permit it. Note the policy ships in report-only mode today, so a violation is currently observed and not blocked — which makes this worse, not safer. |
| **A sandboxed runtime for merchant code** | Rejected **now**, with the cost named rather than implied: a sandbox, an egress proxy, execution metering, determinism bans, a privilege boundary, a versioned host API, a review process and a support boundary. It is a product, not a feature, and nothing in the current demand justifies it. |
| **Outbound webhooks plus bounded configuration, with "product-ize or decline" for the rest** | **Chosen.** |

## Decision

**Integration, not execution.** Three mechanisms, in the order they should be reached for:

### 1. Bounded configuration — the default answer

Anything that can be a validated, server-enforced setting becomes one: branding and presets, a section registry
that makes the storefront's composition an ordered descriptor list rather than fixed markup, store-authored
content, per-store string overrides, entitlements from the plan. The pattern already exists and is the right
one — allowlists live in the Domain and are **published to the frontend through the existing options endpoint**,
so the client never holds a divergent copy of the rules.

The test is simple: *can this be expressed as data the Domain validates?* If yes, it is configuration, and it is
available to every store on the plan that includes it.

### 2. Outbound webhooks — the answer when the merchant needs to *react*

A per-tenant subscription to Souq's own domain facts, delivered to the merchant's endpoint with a per-tenant
secret, HMAC signing over the raw body, a timestamp to bound replay, an idempotency key, bounded retries with
backoff and a dead state. The transactional outbox already provides the durable half — a message written in the
same transaction as the fact that caused it, a lease that makes two dispatchers safe, and a retry schedule that
ends in a recorded failure rather than silence.

Two constraints come from the outbox and must be respected rather than discovered: an unregistered message type
**throws inside `SaveChanges`**, failing the business transaction rather than dead-lettering; and payloads are
capped, so a webhook carries references and not documents.

The merchant's code then runs on the merchant's infrastructure. Souq owes delivery and signing; it does not owe
execution.

### 3. A scoped API — the answer when the merchant needs to *ask*

Read and write under the merchant's own credentials, subject to the same permissions, quotas and rate limits as
any other caller.

### 4. Everything else is product-ized or declined

A request that fits none of the three becomes a feature for **every** store — designed, documented, tested and
available on a plan — or it is declined. That is the same answer ADR-0011 gives, applied to a commercial
context, and it is the sentence that keeps the product upgradeable.

### 5. What would change this

This record does **not** meet ExplicitNonGoals §14's condition, deliberately. Meeting it would require a paid
tier whose extension points are designed as a product feature available to everyone, and a new ADR covering the
sandbox, its resource limits, the versioned host API, the upgrade policy and the support boundary. Until then,
"we could sandbox it" is not an available answer to a customer request.

## Consequences

**Good.** No merchant code runs in Souq's process, so there is no sandbox to escape, no execution budget to
exhaust and no noisy-neighbour class introduced. The upgrade story is unchanged: one build, every store. The
durable delivery half is already built and audited. A merchant who needs genuine custom behaviour gets a real
answer rather than a refusal, and pays for it in their own infrastructure rather than in Souq's risk.

**Costs.** Webhooks are a public contract: event names and payload shapes become something merchants depend on,
so they need versioning and a deprecation policy from the first release. Outbound HTTP to merchant-controlled
URLs is a server-side request forgery surface and needs egress rules, a timeout and a failure budget. A merchant
whose requirement is genuinely synchronous — change a price *during* checkout — cannot be served by this model,
and that limitation must be said out loud in the sales conversation rather than discovered in delivery.

**Revisit when** a paid tier with designed extension points is a real product decision, and the demand is
demonstrated by more than one customer — at which point the sandbox gets its own ADR, and the first question it
must answer is not "which runtime?" but "what is the support boundary when a merchant's extension breaks their
store?"
