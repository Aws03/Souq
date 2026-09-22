# What is real, what is a local stand-in, and what a production deployment would add

> **Read this before judging anything else in the repository.** Souq is finished as a **portfolio-quality
> demonstration of a commercial SaaS architecture**, not as a launched business. Every architectural claim here
> is meant to be checkable; no operational claim is made that the code cannot back.
>
> The short version: **the engineering system is complete and demonstrable end to end. A real commercial
> deployment would need external infrastructure, provider contracts and legal review — none of which is faked
> here, and all of which has a seam waiting.**

**Last verified against the code:** 2026-09-22

## 1. Implemented — this genuinely works

These are not mocks. They run, they are covered by tests against a real SQL Server, and most are exercised by
browser journeys against the container stack.

| Capability | Where to look |
|---|---|
| **Multi-tenancy by host**, with store data isolated by a query filter and a write guard, not by discipline | [ADR-0022](../11-ADR/0022-tenancy-enforcement.md) · `TenancyRuleTests` · `TenantIsolationTests` |
| **Catalog** — products, variants and options, categories, media, search with Arabic normalisation | [Catalog](../04-MODULES/Catalog/README.md) |
| **Shopping and checkout** — basket merge on sign-in, pricing pipeline, coupons, shipping, tax, stock reservation | [ADR-0026](../11-ADR/0026-inventory-reservations.md) · [Shopping](../04-MODULES/Shopping/README.md) |
| **Orders** — lifecycle, status history, refunds, abandoned-checkout sweep, concurrency-safe transitions | [Ordering](../04-MODULES/Ordering/README.md) |
| **Payments architecture** — a flow-agnostic port, per-store account routing, refund idempotency, account identity | [ADR-0048](../11-ADR/0048-payment-provider-abstraction.md) · [ADR-0061](../11-ADR/0061-a-payment-records-which-account-took-it.md) |
| **Platform control plane** — provisioning, plans and entitlements, quotas enforced by a counter row, suspension | [ADR-0047](../11-ADR/0047-commercial-control-plane.md) · [ADR-0049](../11-ADR/0049-tenant-quota-enforcement.md) |
| **Merchant billing** — invoices, credit notes, billing periods, manual collection, dunning | [ADR-0056](../11-ADR/0056-platform-invoices-and-manual-collection.md) · [ADR-0058](../11-ADR/0058-dunning-and-automated-suspension.md) |
| **Tax as a configurable capability** — jurisdiction profiles, versions, effective dates, frozen snapshots | [ADR-0055](../11-ADR/0055-tax-as-a-configurable-capability.md) |
| **Store customization** — theme presets, an ordered home-section registry, wording overrides | [ADR-0059](../11-ADR/0059-theme-presets-vary-form-not-colour.md) · [0060](../11-ADR/0060-home-page-sections-as-an-ordered-registry.md) · [0062](../11-ADR/0062-store-text-overrides-are-a-closed-list.md) |
| **Recommendations** from the store's own delivered orders, with a reason shown to the shopper | [ADR-0064](../11-ADR/0064-recommendations-from-first-party-data.md) |
| **Notifications** — a transactional outbox, templates, Arabic/English, in-app inbox | [ADR-0034](../11-ADR/0034-notifications-outbox.md) |
| **Analytics architecture** — event envelope, bounded non-blocking write path, rollups, retention | [ADR-0050](../11-ADR/0050-behavioural-event-foundation.md) |
| **Cross-instance coordination** — a lease table and a shared cache generation | [ADR-0057](../11-ADR/0057-cross-instance-coordination.md) |
| **Security** — sessions, refresh rotation, per-store revocation, upload validation by magic bytes, rate limits | [ADR-0023](../11-ADR/0023-sessions-and-credentials.md) · [ADR-0016](../11-ADR/0016-upload-validation.md) |

## 2. Local stand-ins — deliberate, deterministic, and never pretending

Each of these sits behind the same port a real provider would implement. They are chosen, not missing.

| Area | What runs here | Why this rather than a real service |
|---|---|---|
| **Shopper payments** | `DemoPaymentGateway` — deterministic outcomes driven by the order total, no card, no network, no money ([ADR-0063](../11-ADR/0063-the-demo-payment-adapter.md)) | A provider needs an account and a contract, and a public repository should not rot when a key is rotated. The demo shows decline, processing, cancellation and retry — paths a sandbox makes *harder* to demonstrate, not easier |
| **Merchant billing collection** | Invoices are issued and payments are recorded **by a human**. No online collection | The billing concepts are the interesting part; pretending to collect money is not |
| **Email** | A log sink. Nothing is sent. Outside Development it must be enabled explicitly, and the API refuses to boot if neither a provider nor the sink is configured | No mail account is required to run or review the project, and no message can silently vanish |
| **File storage** | Local filesystem, per-tenant paths, magic-byte validation, traversal-proof names, serving guarded by tenant | Cloud object storage is a DI swap; the interesting engineering (isolation, validation) is here either way |
| **Tax values** | The *capability* is real. Any profile shipped for the demo is marked **unverified**, and collection is refused under an unverified version | Inventing a legal rate would be the one genuinely dishonest thing this repository could do |
| **Behavioural capture** | **Off, and it stays off.** The architecture is complete; no visitor identifier is stored and no cookie is set | A public demonstration must not quietly collect identifiers from people who are just looking at a portfolio |

## 3. Production extension points — what a real deployment would add

None of this is half-built. Each is a decision plus an adapter, and the seam is already in place.

| To go live you would | What it touches |
|---|---|
| Choose and contract a **payment provider**, then write its adapter | One class implementing `IPaymentGateway`. `StartPaymentResult` already carries redirect and browser-post shapes, so a redirect-first regional provider needs no Application change |
| Add the **attempt aggregate, event-log totals and webhook inbox** | Deferred with a named trigger: they answer the shopper *leaving* the process, which only a redirect-first adapter causes ([ADR-0048](../11-ADR/0048-payment-provider-abstraction.md) implementation notes) |
| Point storage at **cloud object storage** | `IFileStorage` has one method; the Domain's URL-prefix rule (TD-22) is the one place that also needs relaxing |
| Configure a **mail provider** | `Resend`, `Brevo` or SMTP are already implemented behind `IEmailSender` — this is configuration, not code |
| Automate **custom-domain certificates** | Ownership verification exists as a manual operator action; a managed edge or an ACME client is the missing half ([ADR-0051](../11-ADR/0051-custom-domain-lifecycle.md)) |
| Answer the **data-protection questions** and turn capture on | Lawful basis, retention period, data residency. The switch refuses to boot if enabled without them |
| Have **tax values verified** by a named professional | The workflow exists; verification is a human act the software deliberately cannot perform |
| Build **outbound webhooks** if a customer asks | Designed in [ADR-0052](../11-ADR/0052-bounded-extension-model.md); the decision is *webhooks only, never customer code* |

## 4. What this repository does **not** claim

Stated plainly, because a portfolio piece that overclaims is worse than one that does less:

- It does **not** process real payments, and has never been connected to a payment provider.
- It is **not** certified or audited for PCI, GDPR, or any other regime. Where those regimes are discussed in
  the docs, they are discussed as constraints on design — not as compliance that has been achieved.
- Its tax capability contains **no verified legal rate** for any jurisdiction.
- It does **not** send email in its default configuration.
- It has **not** operated as a merchant of record, and no money has moved through it.
- Its security is engineered with care and tested, but has had **no external audit or penetration test**.

## 5. How to check any of this yourself

```bash
dotnet build -warnaserror                    # zero warnings
dotnet test tests/Souq.Domain.Tests          # ~671
dotnet test tests/Souq.Application.Tests     # ~532
dotnet test tests/Souq.ArchitectureTests     # ~127 — the rules above, enforced
dotnet test tests/Souq.IntegrationTests      # ~544, real SQL Server via Testcontainers
cd frontend && npm ci && npm run lint && npm run typecheck && npm test && npm run build
```

The architecture suite is the one worth reading first: it is where "tenant isolation is enforced" stops being a
sentence and becomes a failing build.
