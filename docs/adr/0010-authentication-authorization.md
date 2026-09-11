# ADR-0010: Authentication and authorization

- **Status:** Accepted, 2026-09-11. Reset-token hardening was implemented in 1A and the rest in Phase 3. The implementation choices are in [ADR-0023](0023-sessions-and-credentials.md). Details: [AuthenticationAndAuthorization.md](../AuthenticationAndAuthorization.md).

## Context

Today:
- Custom JWT (HS256, 120 minutes, stored in `localStorage`) + BCrypt.
- One `Customer` table holds credentials, a role string, and the commerce profile.
- Two roles (`Customer`, `Admin`).
- No refresh tokens or revocation.
- Reset tokens were stored in plaintext (fixed in 1A).

The target needs platform users, tenant admins, staff, and customers, with a hard platform/tenant separation.

## Problem

Which identity implementation and authorization model should the multi-tenant platform use?

## Options considered

| Option | For | Against |
|---|---|---|
| **Evolve the custom implementation** | Already integrated and tested; Domain-owned `User`; tenant-scoped uniqueness is natural; full control | We must implement and test rotation, lockout, and stamps (a bounded, known list) |
| ASP.NET Core Identity | Lockout, 2FA, and token providers built in | Global unique indexes need custom stores for tenancy; its types must be wrapped to stay out of the Domain; migration effort similar |
| External IdP (Auth0, Keycloak, Entra External ID) | SSO, MFA, enterprise features | Per-tenant IdP configuration, vendor cost, heavier local development; overkill for small-store customers |

- **Authorization:** role checks in controllers, versus **permission-based policies** with built-in roles mapped to permissions, versus fully dynamic roles in the database.

## Decision

- **Evolve the custom implementation** into:
  - a `User` aggregate (`TenantId NULL` = platform user);
  - a separate `Customer` profile;
  - **per-tenant accounts**;
  - 15-minute access tokens;
  - rotating, hashed refresh tokens in an `HttpOnly` cookie, with reuse detection;
  - a security stamp for revocation;
  - lockout, email verification, and hashed single-use reset tokens.
- **Authorization:**
  - **Permissions** as code constants.
  - Built-in roles (PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff, Customer) mapped to permissions in one table.
  - `[HasPermission]` policies.
  - Resource ownership checks in Application.
  - Custom per-tenant roles deferred.
- **Tokens are audience-scoped:** platform vs tenant. `tid` must match the resolved host ([ADR-0006](0006-tenant-resolution.md)).

## Why

- It keeps working, tested code.
- The rest of the system depends only on a small **claims contract** (`sub`, `tid`, roles). Swapping in an external IdP later is therefore contained in Infrastructure and API.
- Permissions stop authorization logic from spreading through controllers.

## Consequences

- Phase 3 includes the `Customers` → `Users` + `Customer` migration (customer ids stay stable).
- Security-critical code is owned by us, so it must have tests (authorization matrix, rotation and reuse, lockout) and gets a Phase 20 review.

## Revisit when

- A client requires SSO or SAML, or MFA policies beyond TOTP. Adopt an external IdP behind the same claims contract.
