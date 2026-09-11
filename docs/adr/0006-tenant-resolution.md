# ADR-0006: Tenant resolution

- **Status:** Accepted, 2026-09-11. Implementation: Phase 2.

## Context

Each tenant is served on its own custom domain or subdomain. The platform owner has a separate administration area. The brief requires that the frontend can never choose an arbitrary `TenantId` and gain access.

## Problem

Where does the server learn which tenant a request belongs to, in a way a client can't forge into cross-tenant access?

## Options considered

1. **Host header → tenant domain map.** Custom domains work naturally.
2. **Path prefix** (`/t/{tenant}/…`). Works everywhere, but URLs are ugly and custom domains still need host mapping.
3. **Request header** (`X-Tenant-Id`). Trivially forgeable; acceptable only as a development convenience.
4. **JWT claim only.** Anonymous storefront traffic has no token.

## Decision

- Resolve from the **Host** through the cached `TenantDomains` map (**option 1**).
- For authenticated requests, the token's **`tid` claim must equal** the resolved tenant; otherwise 401.
- Platform endpoints are served only on the **platform host**, which has no tenant context, and use platform-audience tokens.
- In Development only, `{slug}.localhost` or an `X-Tenant` header selects the tenant.
- `TenantId` from request bodies or query strings is never used for scoping.

## Why

- A host can only ever select *its own* tenant's public data, so forging it gains nothing.
- The claim-equals-host check stops token replay across tenants.
- A separate platform host keeps platform endpoints and cookies off every tenant domain.

## Consequences

- DNS and TLS for custom domains become an operations concern (Phase 23).
- Local development uses `*.localhost` subdomains.
- Background jobs must set the tenant context explicitly.
- Tests can drive tenants by setting the Host header on the test client.

## Revisit when

- A channel without a host appears, such as a native mobile app. Resolve through the token claim plus an app-specific tenant key, validated server-side.
