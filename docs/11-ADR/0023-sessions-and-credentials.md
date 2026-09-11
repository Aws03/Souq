# ADR-0023: Sessions and credentials: implementation details

- **Status:** Accepted (implemented in Phase 3), 2026-09-11. Refines [ADR-0010](0010-authentication-authorization.md); it does not change that decision.
- **Date:** 2026-09-11
- **Related modules:** Identity; Customers (the profile a session points at)
- **Related ADRs:** refines [ADR-0010](0010-authentication-authorization.md); host binding from [ADR-0006](0006-tenant-resolution.md) and [ADR-0022](0022-tenancy-enforcement.md); permissions from [ADR-0019](0019-authorization-foundation.md); narrows the access-token lifetime validated by [ADR-0020](0020-configuration-and-secrets.md); its reset token is reused for invitations by [ADR-0024](0024-platform-administration.md); erasure ends its sessions in [ADR-0027](0027-customer-profile-and-erasure.md); its tokens are issued at dispatch by [ADR-0034](0034-notifications-outbox.md)

## Context

ADR-0010 chose to evolve the custom JWT + BCrypt into:
- a `User` aggregate;
- rotating refresh tokens;
- a security stamp;
- lockout and email verification.

Building it raised questions ADR-0010 left open:
- how rows from the single `Customers` table become users without losing credentials or ids;
- how refresh tokens behave when two tabs refresh at once;
- how platform and store tokens stay apart;
- where email links point when every store has its own host;
- how rate limits are partitioned.

## Problem

Which concrete mechanisms implement ADR-0010 safely, without redesigning it?

## Options considered

| Question | Chosen | Rejected (why) |
|---|---|---|
| Keeping platform and store tokens apart | The `tid` claim bound to the host. On a store host, `tid` must equal the resolved store. On a platform host, there must be no `tid`. | A separate `aud` per area: a second axis that must agree with `tid` anyway. One rule is easier to test. |
| Roles per user | One `Role` column on `User` | A `RoleAssignment` table. No current requirement gives an account several roles, and custom roles are deferred. |
| Customer id in the token | An explicit `cid` claim | Sharing the primary key between `User` and `Customer`. New registrations get independent identity values, and forcing equality needs a non-identity key. |
| Two tabs refreshing at once | A 10-second grace: a token used within the last 10 s mints another token in the same family | Strict single use: the second tab logs the user out, and it looks like theft. A server-side lock: needs distributed state. |
| Refresh lifetime | 30 days, sliding (`Jwt:RefreshTokenDays`, 1–90). Every rotation resets it. | 14 days sliding + 60 days absolute. The absolute cap needs a family start column; deferred to the Phase 20 review. |
| Security stamp check | On every request, cached 30 s in process. The instance that changes the stamp drops its cache entry at once. | A database read per request (cost). No check (a disabled account would keep working for 15 minutes). |
| Email links | Built from the request's scheme and host | One configured frontend URL: wrong for every store but one. A per-store configured URL: not available until Phase 4, and it duplicates `TenantDomains`. |
| Rate-limit partition | Fixed window per `host|client IP` | Per IP only: one store's traffic starves another behind the same NAT. Per account: unknown until the credentials are checked. |
| Data migration | Copy each `Customers` row into `Users` **with the same id** (`IDENTITY_INSERT`), set `Customers.UserId = Id`, then drop the old columns | EF's generated order: it drops `PasswordHash` before copying, so every password is lost. |

## Decision

1. **Tokens.**
   - Access JWT, 15 minutes (`Jwt:ExpiryMinutes`, validated 5–60).
   - Claims: `sub`, `role`, `email`, `name`, `sstamp`; `tid` for store accounts; `cid` for accounts with a customer profile.
   - Refresh token: 256-bit random. Only its SHA-256 hash is stored.
   - It lives in the cookie `souq_refresh`: `HttpOnly`, `Secure` (configurable for local http), `SameSite=Strict`, `Path=/api/auth`.
   - It is rotated on every use, inside a family.
2. **Validation on every authenticated request:**
   - signature, issuer, audience and lifetime (30 s skew);
   - `tid` against the host;
   - `sstamp` against the account's current stamp.
3. **Revocation.**
   - Logout revokes the family.
   - Changing or resetting the password rotates the stamp, so every access token dies, and revokes the refresh tokens.
   - Reusing a token rotated more than 10 s ago revokes the family, rotates the stamp, and logs a warning.
4. **Account security.**
   - Lockout after 5 failures, for 15 minutes.
   - For unknown emails, a BCrypt comparison against a decoy hash, so timing doesn't reveal which emails exist.
   - Single-use hashed tokens: reset (2 hours) and email verification (48 hours).
   - Passwords of 8–128 characters with letters and digits. Bootstrap accounts outside Development need at least 12 characters.
5. **Areas.**
   - Platform accounts (`TenantId NULL`) exist only on platform hosts.
   - The named tenant filter returns store accounts in a store scope and platform accounts in the platform scope. A login on the wrong host therefore finds no account.
   - Self-registration on a platform host answers `403 RegistrationNotAllowed`.
6. **Customer vs staff.** Only accounts with a customer profile (`cid`) can shop. Staff calling a customer use case get `403 CustomerAccountRequired`.
7. **Rate limits.**
   - Per `host|IP`, configurable: auth endpoints 10/min, refresh 30/min, coupon preview 30/min.
   - The client IP comes from `X-Forwarded-For` only when the proxy is in `ForwardedHeaders:KnownNetworks`.

## Why

- Every choice keeps ADR-0010's claims contract. Nothing outside Identity depends on these details.
- The `tid`-to-host rule already existed (Phase 2). Extending it to "no `tid` on platform hosts" separates the two areas with one tested rule.
- The migration keeps ids and password hashes. Existing sessions end (their tokens have no `sstamp`), but every account can sign in with its current password.

## Consequences

- An active session never expires as long as it is used at least every 30 days. An absolute cap is a Phase 20 review item.
- With several API instances, a stamp change reaches the other instances within 30 s (the cache lifetime). Today there is one instance.
- Email verification is recorded but not yet required at checkout. The requirement is a store setting (Phase 4), enforced by checkout (Phase 9).
- `Secure` cookies work over `http://localhost` in Chrome and Firefox. Safari needs `Auth:RefreshCookie:Secure=false` locally.

## Revisit when

- The API runs as several replicas. Move the stamp cache out of process, or shorten its lifetime.
- A client needs several roles per account, or custom roles. Introduce `RoleAssignment`.
- SSO or MFA is required (ADR-0010's revisit condition).
