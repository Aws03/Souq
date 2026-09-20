# ADR-0051: A custom domain has two state machines, not one timestamp — ownership and certificate are separate, and the serving gate reads ownership

- **Status:** Accepted as the design, 2026-09-20. **Not implemented.** The edge that answers a certificate challenge is a deployment's to provide, and the choice between a managed edge and a self-run ACME client is the owner's (`C-11`); this record decides the data model and the port shape, which are the same either way. Supersedes nothing; it makes concrete what [ADR-0045](0045-production-edge-and-observability-stack.md) left as "needs a real edge".
- **Date:** 2026-09-20
- **Related modules:** Platform
- **Related ADRs:** [ADR-0045](0045-production-edge-and-observability-stack.md) (what the repository owns about the production edge and what a deployment owns), [ADR-0006](0006-tenant-resolution.md) (the host is the tenant key), [ADR-0022](0022-tenancy-enforcement.md) (host-bound tokens and tenant-prefixed storage), [ADR-0047](0047-commercial-control-plane.md) (the table shape these records take)

## Context

Every merchant brings a domain, so per-domain certificate issuance *is* the product — and today each one is a
hands-on operation. `TenantDomain` holds `Host` (normalised, unique platform-wide), `IsPrimary` and a single
nullable `VerifiedAt` stamped by `MarkVerified`. There is no challenge token, no certificate state, no
per-domain status, and no TLS of any kind in the repository.

Three facts about the current behaviour matter to the design and are easy to get wrong:

- **`VerifiedAt` gates nothing.** `TenantDirectory.FindByHostAsync` matches on `Host` alone and `TenantInfo`
  does not carry the field, so a domain serves the storefront the moment it is added. The ADR index already
  records that [ADR-0045](0045-production-edge-and-observability-stack.md) overstated this.
- **It is nevertheless read — for display.** The platform read model projects it and the provisioning console
  already shows a `domainVerified` readiness step. So the work is to make an existing displayed flag
  load-bearing, not to invent a verification UI.
- **`MarkVerified` is one-way and idempotent** (`VerifiedAt ??= utcNow`). There is no un-verify and no
  re-stamp, so a domain whose DNS is later removed or repointed stays "verified" forever.

Research into how platforms actually do this found one structural answer repeated everywhere: the mature
implementations keep **ownership** and **certificate** as separate fields with separate state machines. It also
found that the economics changed — certificate lifetimes and authorization reuse have both shortened, so
validation now re-runs on essentially every renewal, and at least one certificate authority has stopped sending
expiry warnings, making expiry monitoring the platform's own job.

## Problem

1. Can one timestamp express the lifecycle?
2. What port shape survives both a self-run ACME client and a managed edge, which are structurally different —
   in the managed case the platform never sees a challenge, a CSR or a private key?
3. What stops an attacker pointing a CNAME at the platform and causing certificate issuance in Souq's name?

## Options considered

| Option | Verdict |
|---|---|
| **Keep `VerifiedAt`, add a second timestamp for the certificate** | Rejected. Timestamps answer "did this ever happen", and every operationally interesting question is "is this true now": is DNS still pointed at us, is the certificate about to expire, did the last renewal fail, is issuance blocked by a CAA record. |
| **One combined status** | Rejected on evidence. All four combinations occur in production — verified with no certificate (issuance blocked by CAA); verified and certified but no longer routed to us; a valid certificate whose ownership has lapsed; neither. A single enum either loses a case or multiplies into a cross-product. |
| **A port shaped like an ACME client** (`newOrder` / `challenge` / `finalize`) | Rejected. It cannot absorb a managed edge, where the platform POSTs a hostname and polls two status fields. Choosing this shape would silently decide `C-11`. |
| **Two state machines behind an attachment port, plus a separate DNS probe** | **Chosen.** |

## Decision

### 1. Two state machines on `TenantDomain`, kept separate

**Ownership:** Pending → Verifying → Verified → Failed / Moved / Revoked.
**Certificate:** None → Pending → Active → Renewing → Failed / Expired.

With the data each needs: the verification token and its expiry; the expected DNS target shown to the merchant,
stored so the instruction is reproducible; last-checked, next-check, attempt count and last failure code;
certificate not-before, not-after and issuer. **Ownership becomes reversible** — a domain that stops resolving
to us moves to *Moved*, which `MarkVerified`'s one-way stamp cannot express today.

These are platform-owned, tenant-keyed rows (shape B), beside `TenantDomain`, which is already that shape.

### 2. Two ports, and the naming is the decision

***IDomainAttachment*** — `Attach(host)` returns a provider reference plus a list of **DNS instructions**, each
carrying record type, name, value and *why* (routing, ownership, or challenge delegation), then a status poll
returning both machines' states. A managed edge and a self-run client both fit; an ACME-shaped port fits only
one.

***IDnsProbe*** — resolve TXT, CNAME, A/AAAA and CAA against authoritative nameservers and compare with an
expected value. This one is genuinely provider-agnostic and serves three jobs at once: verification, the
pre-flight CAA check that predicts an issuance failure before it happens, and the merchant-facing "why is my
domain not working" diagnostic.

### 3. The serving gate reads **ownership**, and it is a security control

Whatever terminates TLS must ask Souq whether a host may be served and certified, and the answer must be yes
**only for a host whose record is in a verified state — never merely present**. Without that, anyone who points
a CNAME at the edge triggers an order in Souq's name, which is both a rate-limit exhaustion and a reputational
exposure. This gate is a query against Souq's own table and is never delegated.

Host handling reuses what exists, and the two normalizers stay distinct on purpose:
`TenantDomain.TryNormalizeHost` **validates** and is used on write; `RequestHost.Canonical` only **unifies** and
is used for anything host-keyed at request time. `IPlatformHosts` already refuses a platform host as a store
domain, and automated onboarding must keep asking it.

### 4. Verification and renewal are scheduled work, not outbox messages

The outbox delivers things that happened, once, with retries. A domain lifecycle is a recurring check with a
`NextCheckAt`, exponential backoff and a give-up policy. Certificate expiry alerting is internal, because the
issuer no longer warns anyone.

### 5. The platform's own subdomains take one wildcard, not one certificate each

Per-registered-domain issuance limits would otherwise cap new-store onboarding at a fixed number per week. A
wildcard needs a DNS challenge on Souq's own zone, which Souq controls — unlike a merchant's.

### 6. Certificate storage is the same problem as uploads

If ACME runs in-process, certificates cannot live on local disk once there is more than one instance: the store
must support atomic operations so it can provide locking. That is TD-20 in a second costume, and it makes
`C4` (multi-instance correctness) a prerequisite for self-run ACME — but not for a managed edge.

## Consequences

**Good.** The four production combinations become representable and diagnosable. The merchant-facing error
message can say which record is missing and why. The port does not pre-decide the managed-versus-self-run
question. A displayed readiness flag stops being decorative. Rate-limit exhaustion by a stranger pointing DNS at
the platform is closed by design rather than noticed later.

**Costs.** A migration on `TenantDomains` and a new scheduled job class. The merchant-facing promise changes
from "point it here once" to "keep this record in place permanently", because short authorization reuse means
validation re-runs on nearly every renewal — a domain whose DNS drifts now breaks in days rather than months.
Every certified merchant domain is published to public Certificate Transparency logs, so the customer list is
enumerable by anyone; that is unavoidable with publicly trusted certificates and belongs in the merchant
agreement. A managed edge adds a per-hostname recurring cost, billed from creation including hostnames that
never validate, which the revenue model must be able to express.

**A gap this record does not close.** Archiving a store tears nothing down, and `Host` is unique platform-wide,
so an archived merchant reserves its domain forever. Offboarding must release it, and that belongs with the
lifecycle work in `C3`, not here.

**Revisit when** apex domains must be supported (which forces either a published fixed address that pins the
platform's infrastructure or a managed edge), or when a merchant asks for a wildcard — which requires delegating
the challenge record, and only one provider may hold that delegation at a time.
