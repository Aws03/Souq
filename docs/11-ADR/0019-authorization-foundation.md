# ADR-0019: Authorization foundation — ICurrentUser, permission policies and ownership in Application

- **Status:** Accepted and implemented in Phase 1B, 2026-09-11. Brings the *mechanism* of [ADR-0010](0010-authentication-authorization.md) forward from Phase 3; the identity split, token rotation and tenant roles stay in Phase 3.
- **Date:** 2026-09-11
- **Related modules:** Identity; Ordering and Payments (ownership and the webhook entry point); Cross-cutting (permission policies)
- **Related ADRs:** brings the mechanism of [ADR-0010](0010-authentication-authorization.md) forward; the roles it anticipates are added by [ADR-0023](0023-sessions-and-credentials.md); platform permissions and the audit trail in [ADR-0024](0024-platform-administration.md); its ownership rule is used by [ADR-0027](0027-customer-profile-and-erasure.md), [ADR-0029](0029-orders-lifecycle.md) and [ADR-0033](0033-review-moderation-and-wishlist.md)

## Context

- Authorization was `[Authorize(Roles = "Admin")]` on controllers. Ownership checks ("is this your order?") lived in `OrdersController`, which also parsed claims into a customer id and wrote it into commands (Phase 0 B7).
- The Stripe webhook reused the customer's `ConfirmOrderPaymentCommand`, so an ownership check could not live in the use case without breaking the webhook — and without it, one customer could confirm another's order and, if that payment was not complete yet, **cancel it**.
- Phase 2/3 add tenant admins, staff and platform roles. Scattering role names across attributes would have to be rewritten.

## Problem

What is the smallest authorization foundation that supports permissions and resource ownership beyond role names, without building an enterprise permission system before it is needed?

## Options considered

| Option | For | Against |
|---|---|---|
| Keep role attributes until Phase 3 | No work now | Every endpoint rewritten later; ownership stays in controllers |
| Database-driven roles and permissions now | Maximum flexibility | No client needs custom roles yet; large surface to secure and test |
| **Permissions as code constants, built-in roles mapped to them, `ICurrentUser` port, ownership in use cases** | Small; endpoints state *what* they need; Phase 3 roles are one line each | Custom per-tenant roles still need work later |
| Runtime fallback policy (everything authenticated unless `[AllowAnonymous]`) | Secure default at runtime | Turns 404 for unknown routes and missing upload files into 401 and interacts badly with static files |

## Decision

1. **`ICurrentUser`** (Application port, implemented from JWT claims in `Souq.API.Security`) is the only way use cases learn who is calling. Commands no longer carry client-bindable customer ids. `RequireUserId()` fails safe with 401.
2. **Permissions are constants** grouped by module (`catalog.manage`, `orders.manage`, `inventory.view`, `promotions.manage`). **`RolePermissions`** is the single table from role to permissions: `Admin` → all, `Customer` → none (customer capabilities are ownership, not permissions).
3. **Endpoints declare permissions** with `[HasPermission(...)]`. A policy provider builds the policy from its name; an unknown permission fails loudly. Anonymous → 401, authenticated without the permission → 403.
4. **Ownership lives in use cases:** `CanAccessOwnedBy(ownerId, managePermission)`. A resource owned by someone else is **404, never 403**.
5. **Payment confirmation** is one service with two entry points and two authorization models: the owning customer (token) and the provider webhook (signature).
6. **Secure by default at the gate:** every endpoint must declare `[AllowAnonymous]`, `[Authorize]` or `[HasPermission]`; the public surface is a reviewed list in a test; every declared permission must exist; controllers may not read claims.

## Why

- Endpoints express intent ("manage orders"), so adding `TenantStaff` with a subset is a table change, not an endpoint change.
- Ownership in the use case holds no matter which entry point calls it.
- The explicit-decision test gives the fallback policy's guarantee without changing runtime 404 semantics.

## Consequences

- Phase 3 adds roles (`PlatformOwner`, `PlatformAdmin`, `TenantAdmin`, `TenantStaff`) and the `tid` claim to `ICurrentUser`; Phase 2 adds tenant context next to it.
- Making an endpoint public requires editing the reviewed list — a deliberate, visible change.

## Revisit when

- A client needs custom roles per tenant: move `RolePermissions` into tenant data behind the same `HasPermission` API.
- A runtime secure default is wanted in addition to the test: add a fallback policy together with an anonymous not-found endpoint for non-matching routes.
