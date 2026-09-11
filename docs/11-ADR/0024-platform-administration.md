# ADR-0024: Platform administration: store settings, modules, audit, and cross-tenant access

- **Status:** Accepted (implemented in Phase 4), 2026-09-11.
- **Decisions implemented:** D-11 (module flags), D-12 (store configuration and white-label runtime), D-17 (auditing).
- **Builds on:**
  - [ADR-0005](0005-multi-tenancy-model.md), [ADR-0011](0011-white-label-architecture.md) and [ADR-0022](0022-tenancy-enforcement.md), which it does not change;
  - [ADR-0023](0023-sessions-and-credentials.md), whose reset token it reuses for invitations.
- **Date:** 2026-09-11
- **Related modules:** Platform; Identity (invitations and account rules); Cross-cutting (the audit behavior)
- **Related ADRs:** builds on [ADR-0005](0005-multi-tenancy-model.md), [ADR-0011](0011-white-label-architecture.md), [ADR-0022](0022-tenancy-enforcement.md) and [ADR-0023](0023-sessions-and-credentials.md), and keeps the transaction rule of [ADR-0021](0021-transaction-boundaries.md); its module flags are checked in use cases by [ADR-0033](0033-review-moderation-and-wishlist.md) and hidden in the UI by [ADR-0035](0035-white-label-runtime.md); per-store payment accounts are added by [ADR-0031](0031-payments-and-refunds.md); its store branding is used by the email templates of [ADR-0034](0034-notifications-outbox.md)

## Context

Phase 4 gives the platform owner an API to:
- create and run stores;
- manage domains, branding, store settings and optional modules;
- invite store administrators and manage platform accounts.

It also adds three pieces:
- a public storefront configuration that the white-label frontend (Phase 15) builds on;
- server-side module enforcement;
- an audit log.

The earlier ADRs fixed the principles:
- one shared database;
- a named tenant filter that throws without a tenant, and a write guard;
- platform bypass only in one reviewed place, audited.

The mechanisms were still open.

## Problem

How do we store settings and modules, write a trustworthy audit trail, and let the platform act inside a store, without weakening tenant isolation?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Settings storage | One JSON document column on `Tenants`, mapped through an Infrastructure document type. Domain value objects validate on write, and rebuilding from storage skips validation. | A column per setting: about 30 columns and a migration per new field. EF owned JSON types: no dictionaries for per-language text, and EF shape constraints leak into the Domain. A separate settings table: a join on every read, for a document that is always read whole. |
| Module flags | A comma-separated column on `Tenants`, carried in the cached `TenantInfo`. Enforced by endpoint metadata in the availability middleware, and by use-case checks where a module is used indirectly (a coupon at checkout). | A `TenantModules` table (the first draft in Modules.md): a join or a second cache on every request, for three booleans. |
| Audit write | A MediatR behavior stages the entry into the current unit of work *before* the handler runs. It is saved by the handler's first commit, flushed after success if the handler saved nothing, and discarded on failure. | Writing after the handler: two commits, and a crash between them loses the audit row. Wrapping the handler in a transaction: it would hold a transaction open across email sends ([ADR-0021](0021-transaction-boundaries.md)). |
| Audit target of a create | The natural key (slug, email); the id does not exist yet when the entry is staged. | A second write to patch in the id: that is an update to an append-only row. |
| Platform writes inside a store | `ITenantScopeRunner`: a new DI scope whose tenant context is the target store, so the write guard and the storage prefix apply as for any request on that store. | Letting the platform scope bypass the write guard: it removes the second line of defence. |
| Cross-tenant reads | `PlatformQueries`, the single reviewed `IgnoreQueryFilters` class (enforced by an architecture test). It uses an explicit `TenantId` predicate or aggregate counts only, and every caller is audited. | Opening a tenant scope per store (N scopes for statistics). |
| Invitations | The hashed, single-use reset token with a 72-hour lifetime, and the reset page. The link opens on the store's primary domain. | A separate invitation table and token type: the same mechanics, with more code. |
| Storefront config caching | The in-process tenant directory cache, with the same invalidation. The ETag is a SHA-256 of the body, with `Cache-Control: no-cache`. | A long `max-age`: stale branding after an edit. No cache: a database read on every page load. |
| Colour validation | WCAG 2.x contrast, checked when saved (see Decision 5). | Free colours: unreadable stores. Fixed palettes only: too restrictive for brands. |
| Domain verification | A manual flag, set by the platform owner. | A DNS TXT check: it needs a DNS library and an operational decision about TLS (Phase 23). |

## Decision

1. **Settings** (`StoreSettings`) are a JSON document on `Tenants`:
   - per-language display name, announcement and SEO;
   - enabled languages;
   - colours, a curated typography preset and a theme preset;
   - contact details and allowlisted social links.

   Logo, favicon and social image are uploaded files. Their paths are generated by the server under `tenants/{id}/branding/`; the client never sends a URL. Currency, domains, modules and status are platform-only.
2. **Modules** (`promotions`, `reviews`, `wishlist`):
   - A disabled module's endpoints answer `404 ModuleDisabled`.
   - Use cases that touch a module check it too. Checkout with a coupon when promotions are disabled answers `422 ModuleDisabled`.
3. **Audit:**
   - Requests implementing `IAuditable` describe their own action, target and safe metadata; the request body is never copied.
   - The behavior adds the actor, role, area, tenant, client IP and correlation id.
   - Every request in `Features.Platform` and `Features.Reporting` is auditable, enforced by an architecture test.
   - `AuditEntries` is append-only: the write guard rejects any update or delete.
4. **Platform area:**
   - Endpoints are marked `[PlatformEndpoint]` and are served only on platform hosts.
   - They are behind `platform.*` permissions.
   - Only these requests may carry a `TenantId`, enforced by an architecture test.
5. **WCAG contrast** is checked on every save:
   - text on background ≥ 4.5:1;
   - button text on the primary and accent colours ≥ 4.5:1, using white or the text colour, whichever is better;
   - primary colour on background ≥ 3:1.
6. **Account rules** are shared by store staff and platform accounts:
   - an administrator cannot disable their own account;
   - the last active holder of the top role (TenantAdmin, PlatformOwner) cannot be disabled;
   - disabling rotates the security stamp and revokes every refresh token.

## Consequences

- **Some audit rows are committed separately.** Two platform commands do their work inside a tenant scope: inviting an administrator and uploading branding. Their audit row is committed separately from that work, because two contexts are involved. A crash between the two commits can leave a change without its row. The outbox (Phase 14) can close this gap.
- **Audit rows for creates carry the natural key,** not the database id.
- **Tightening a settings rule never breaks loading** a store that saved its settings earlier. The new rule applies at the next save.
- **Store-side staff management shipped now,** instead of with the tenant dashboard (Phase 17). It shares the invitation mechanism, and otherwise a store had no way to create TenantStaff accounts.
- **The platform UI is Phase 18.** Until then, the platform area is driven through the API and Swagger.

## Revisit when

- Settings must be queried inside the document (for example, searching stores by a setting): promote those fields to columns.
- The API runs as several replicas: use a shared cache, or shorter lifetimes, for the directory and the storefront config.
- Compliance requires a guaranteed audit row for cross-scope commands: move audit writes into the outbox transaction.
