# Identity module

> **Code:** `src/Souq.Domain/Identity`, `src/Souq.Application/Features/Auth`, `src/Souq.Application/Features/Staff`, `src/Souq.Application/Common/Accounts`, `src/Souq.Application/Common/Security`, `src/Souq.API/Security`, `src/Souq.Infrastructure/Services` (JWT, BCrypt, session validation) · **Decisions:** [ADR-0010](../../11-ADR/0010-authentication-authorization.md), [ADR-0019](../../11-ADR/0019-authorization-foundation.md), [ADR-0023](../../11-ADR/0023-sessions-and-credentials.md), [ADR-0024](../../11-ADR/0024-platform-administration.md) · **Change guide:** [ChangeGuide.md](ChangeGuide.md)

## Purpose

Identity answers two questions for every request: **who is calling**, and **what are they allowed to do**. It is its own module because it is a security boundary: credentials, sessions and the role-to-permission table are the one place where a mistake is not a bug but a breach. It is deliberately separate from [Customers](../Customers/README.md): a login account is not a shopper profile, which is what lets a store's staff and the platform's own people exist without ever being able to shop.

The module spans two worlds that never meet. A **store account** lives inside one store and signs in on that store's hosts. A **platform account** has no store at all and signs in only on platform hosts. The same email address can hold unrelated accounts in ten different stores, and that is the point: each store owns its own customer relationship.

## Responsibilities

- Accounts: creation by self-registration (customers only) or by invitation (everyone else), renaming, enabling and disabling, and the erasure hook Customers calls.
- Credentials: password hashing and verification, the password policy, lockout, single-use hashed tokens for reset, email verification and invitations.
- Sessions: issuing short-lived access tokens and rotating refresh tokens, detecting refresh-token reuse, and revoking sessions when anything invalidates them.
- Authorization: the permission catalogue, the role-to-permission table, the `[HasPermission]` policies, and the `ICurrentUser` port use cases ask.
- Store staff administration: listing, inviting, enabling and disabling a store's administrators and staff.
- The shared account building blocks the platform area reuses for platform accounts.

## Not this module's job

| Not here | Owner |
|---|---|
| The shopper profile, addresses, order history, erasure policy | [Customers](../Customers/README.md) — Identity provides `User.Erase` and revokes the sessions |
| Which store a request belongs to, and whether that store is open | [Platform](../Platform/README.md) |
| Sending the emails that carry reset, verification and invitation links | [Notifications](../Notifications/README.md) — the token itself is minted there, at dispatch, by calling back into this module's aggregate |
| Recording who did what | [Platform](../Platform/README.md) (the audit trail). Authentication events are **not** audited today |
| Deciding whether a specific order or review belongs to the caller | The owning module, using `ICurrentUser.CanAccessOwnedBy` |

## Business concepts

- **Account (`User`)** — someone who can sign in. Store account (`TenantId` set) or platform account (`TenantId` null).
- **Role** — one per account: PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff, Customer.
- **Permission** — a named capability such as `orders.manage`. Roles grant permissions; endpoints and use cases ask for permissions, never for role names.
- **Security stamp** — a random value that changes whenever every existing session must die.
- **Access token** — a short-lived JWT the browser keeps in memory.
- **Refresh token / family** — a long-lived random secret in an `HttpOnly` cookie, rotated on every use; all tokens descended from one sign-in share a family id.
- **Reuse detection** — presenting an already-consumed refresh token outside a short grace window is treated as theft.
- **Lockout** — a temporary block after consecutive failed sign-ins.
- **Invitation** — an administrative account created without a password, plus a single-use link to choose one.
- **Area** — `Store` or `Platform`, reported to the frontend so it knows which application to render.

## Domain model

| Type | Kind | Path | Invariants it guards |
|---|---|---|---|
| `User` | aggregate root | `src/Souq.Domain/Identity/User.cs` | Email is normalized once for storage and lookup and must contain `@` and fit 256 characters; name required, ≤150 characters; role must be known; a role change may never move an account between the platform and a store; the fifth consecutive failure locks the account for 15 minutes and resets the counter; a password change, reset, disable, role change or erasure rotates the security stamp; reset, verification and invitation tokens are 256-bit random values of which only the SHA-256 hash is stored, single-use, with lifetimes of 2, 48 and 72 hours; a reset also clears the lockout and confirms the email; an invitation may not be issued for the Customer role; an account with an empty password hash cannot sign in (`IsInvitationPending`) |
| `RefreshToken` | aggregate root (referenced by `UserId`) | `src/Souq.Domain/Identity/RefreshToken.cs` | Only the hash is stored; the raw value is returned exactly once at issue; `IsActive` requires not used, not revoked, not expired; `IsWithinReuseGrace` allows the same token for 10 seconds after rotation; `MarkUsed` and `Revoke` are idempotent and keep the first reason |
| `UserStatus` | enum | `src/Souq.Domain/Identity/UserStatus.cs` | — |
| `Roles` | static catalogue | `src/Souq.Domain/Common/Roles.cs` | The five role names; `IsPlatform` and `IsStoreStaff` define the two worlds |
| `Permissions`, `RolePermissions` | static catalogues | `src/Souq.Application/Common/Security/Permissions.cs` | Store permissions are granted only to store roles and platform permissions only to platform roles; Customer grants nothing |
| `UserInfo`, `AuthResponse`, `AuthSession` | Application records | `src/Souq.Application/Features/Auth/AuthModels.cs` | The refresh token never appears in `AuthResponse`; permissions are reported for display only |

**Aggregate boundaries.** `User` and `RefreshToken` are separate aggregates joined by `UserId` (with a cascade delete in the database). Sessions are revoked by loading the tokens through `IRefreshTokenRepository`, never through a navigation property on `User`.

**Concurrency.** `Users` carries a `rowversion`: the failure counter, the lockout and the stamp are all written under load. `RefreshTokens` deliberately does not — the 10-second reuse grace, not optimistic concurrency, is what makes two tabs refreshing at once safe.

**Lifecycle.** An account is either created active with a password (self-registration, seeding) or invited: active, but with an empty password hash until the invitation link is used. `Disable` blocks sign-in and refresh immediately; `Enable` restores it. `Erase` (called by Customers) replaces the name and email with non-identifying values, empties the password hash, clears the tokens and disables the account, keeping the row so orders and reviews still resolve.

## Use cases

| Use case | Command or query | Handler | Who may call it | Endpoint |
|---|---|---|---|---|
| Register a customer | `RegisterCommand` | `RegisterHandler` | anonymous, store hosts only | `POST /api/auth/register` |
| Sign in | `LoginCommand` | `LoginHandler` | anonymous | `POST /api/auth/login` |
| Refresh the session | `RefreshSessionCommand` | `RefreshSessionHandler` | the refresh cookie alone | `POST /api/auth/refresh` |
| Sign out | `LogoutCommand` | `LogoutHandler` | anonymous (always 204) | `POST /api/auth/logout` |
| Who am I | `GetCurrentUserQuery` | `GetCurrentUserHandler` | any authenticated account | `GET /api/auth/me` |
| Change password | `ChangePasswordCommand` | `ChangePasswordHandler` | any authenticated account | `POST /api/auth/change-password` |
| Request a reset link | `ForgotPasswordCommand` | `ForgotPasswordHandler` | anonymous | `POST /api/auth/forgot-password` |
| Reset the password / accept an invitation | `ResetPasswordCommand` | `ResetPasswordHandler` | anonymous, with the token | `POST /api/auth/reset-password` |
| Confirm an email | `VerifyEmailCommand` | `VerifyEmailHandler` | anonymous, with the token | `POST /api/auth/verify-email` |
| Resend the confirmation | `ResendVerificationCommand` | `ResendVerificationHandler` | any authenticated account | `POST /api/auth/resend-verification` |
| List store staff | `ListStaffQuery` | `ListStaffHandler` | `store.staff.manage` | `GET /api/admin/staff` |
| Invite store staff or an administrator | `InviteStaffCommand` | `InviteStaffHandler` | `store.staff.manage` | `POST /api/admin/staff` |
| Enable or disable store staff | `SetStaffStatusCommand` | `SetStaffStatusHandler` | `store.staff.manage` | `POST /api/admin/staff/{id}/status` |
| List platform accounts | `ListPlatformUsersQuery` | `ListPlatformUsersHandler` | `platform.users.manage` | `GET /api/platform/users` |
| Invite a platform account | `InvitePlatformUserCommand` | `InvitePlatformUserHandler` | `platform.users.manage` | `POST /api/platform/users` |
| Enable or disable a platform account | `SetPlatformUserStatusCommand` | `SetPlatformUserStatusHandler` | `platform.users.manage` | `POST /api/platform/users/{id}/status` |
| Invite a store's first administrator | `InviteTenantAdminCommand` | `InviteTenantAdminHandler` | `platform.tenants.manage` | `POST /api/platform/tenants/{id}/admins` |

The last four live in `src/Souq.Application/Features/Platform` (they are platform-area requests and therefore audited), but the rules they run are this module's, through `AccountInvitations` and `AccountStatusChanger`.

**Shared account rules** (`src/Souq.Application/Common/Accounts/Accounts.cs`), identical for store staff and platform accounts:

- Inviting an email that already holds an **active** account fails with `EmailTaken`; inviting one that holds a **pending** invitation renews it, updating the name and role and invalidating the previous link immediately.
- Nobody can disable their own account (`CannotDisableSelf`).
- The last active holder of the top role in the scope — TenantAdmin in a store, PlatformOwner on the platform — cannot be disabled (`LastAdministrator`).
- Disabling rotates the security stamp, revokes every refresh token in the same save, and drops the cached stamp so the access tokens die at once on that instance.
- An account outside the roles being managed (a customer, from the staff screen) is reported as not found.

## Public contracts

| Contract | Path | Who calls it |
|---|---|---|
| `ICurrentUser` (+ `RequireUserId`, `RequireCustomerId`, `CanAccessOwnedBy`) | `src/Souq.Application/Common/Security/ICurrentUser.cs` | Every module's use cases; implemented once, in `src/Souq.API/Security/HttpCurrentUser.cs` |
| `Permissions`, `RolePermissions` | `src/Souq.Application/Common/Security/Permissions.cs` | Controllers (`[HasPermission]`), use cases, Notifications (`RolePermissions.RolesGranting` to find who should be notified) |
| `ISessionValidator` | `src/Souq.Application/Common/Security/ISessionValidator.cs` | `AccessTokenValidation`, and every use case that invalidates sessions |
| `IJwtTokenGenerator`, `IPasswordHasher` | `src/Souq.Application/Common/Interfaces` | `AuthSessionIssuer`, the auth handlers, Customers' erase-my-account flow, `DbSeeder` |
| `SouqClaimTypes` | `src/Souq.Application/Common/Security/SouqClaimTypes.cs` | The claims contract between `JwtTokenGenerator`, `AccessTokenValidation` and `HttpCurrentUser` |
| `IUserRepository`, `IRefreshTokenRepository` | `src/Souq.Domain/Interfaces/IUserRepository.cs` | This module; also Customers and Notifications (see Dependencies) |
| `AccountInvitations`, `AccountStatusChanger`, `IAccountQueries` | `src/Souq.Application/Common/Accounts/Accounts.cs` | This module's staff use cases and the platform area |

[Modules.md](../Modules.md) lists a planned *IUserDirectory* contract. It does not exist; callers use `IUserRepository` directly.

## Dependencies

- **Uses:**
  - [Platform](../Platform/README.md): `ITenantContext` to refuse self-registration outside a store and to name the inviting store.
  - [Notifications](../Notifications/README.md): `INotificationOutbox` and the messages `PasswordResetRequested`, `EmailVerificationRequested`, `AccountInvited`. No handler ever calls an email provider — an architecture test forbids it.
  - `IStorefrontLinks` to capture the request's origin, so a link always returns the recipient to the host they came from.
  - **Boundary leak:** Identity reaches into **Customers'** domain. `RegisterHandler` constructs a `Customer` and adds it through `ICustomerRepository`; `AuthSessionIssuer` and `GetCurrentUserHandler` call `ICustomerRepository.FindIdByUserIdAsync` to put `cid` in the token. Registration genuinely must create both in one transaction, but nothing in the tests prevents this dependency from growing.
- **Used by:**
  - Every module, through `ICurrentUser` and `Permissions`.
  - **Boundary leak:** [Customers](../Customers/README.md) mutates the `User` aggregate — `CustomerErasure` calls `User.Erase` and revokes the refresh tokens, `UpdateMyProfileHandler` calls `User.Rename`, and the erase-my-account handler in `src/Souq.Application/Features/Customers/Account/AccountUseCases.cs` verifies the password through `IPasswordHasher`.
  - **Boundary leak:** [Notifications](../Notifications/README.md) mints this module's tokens. `src/Souq.Application/Features/Notifications/IdentityEmailHandlers.cs` calls `User.GenerateResetToken`, `User.GenerateEmailVerificationToken` and `User.RenewInvitation` at dispatch time; `src/Souq.Application/Features/Notifications/OrderAndStockHandlers.cs` reads `IUserRepository.ListActiveIdsByRolesAsync`. This is deliberate (no raw token may sit in the outbox) but it does put Identity's token lifetimes in another module's folder.
  - [Platform](../Platform/README.md), through the shared account building blocks.
- **Enforced vs convention.**
  - Enforced: `ModuleAndContractRuleTests` maps this module to the `Auth` and `Staff` folders and forbids referencing another module's namespace outside the allowed contracts — note that the leaks above are through `Souq.Domain.Interfaces` and `Common`, which the test does not cover. `DependencyRuleTests` forbids controllers from reading claims or deciding ownership. `AuthorizationBoundaryTests` requires every endpoint to declare `[AllowAnonymous]`, `[Authorize]` or `[HasPermission]`, keeps the public list to a reviewed set, and rejects any permission name not in `Permissions`.
  - Convention only: keeping account rules inside `Common/Accounts` rather than duplicating them per area; auditing account changes (the staff and platform commands implement `IAuditable`, but nothing forces a new one to).

## Data ownership

| Table | Configuration | Tenant-owned? | Notes |
|---|---|---|---|
| `Users` | `UserConfiguration` in `src/Souq.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs` | `ITenantOrPlatformOwned` — `TenantId` is nullable | Two filtered unique indexes encode the two worlds: `(TenantId, NormalizedEmail)` where `TenantId` is not null, and `IX_Users_NormalizedEmail_Platform` on `NormalizedEmail` where it is null. Indexes on both token-hash columns (64-character hex). `rowversion`. `BelongsToPlatform` is derived from the role and not stored |
| `RefreshTokens` | `RefreshTokenConfiguration` in the same file | `ITenantOrPlatformOwned` | Unique index on `TokenHash`; index on `FamilyId`; cascade delete from `Users`; `BelongsToPlatform` **is** stored here, because the write guard uses it to decide where the row may be created |

The named tenant filter does the separation: in a store scope both tables show that store's rows, and in the platform scope only rows with `TenantId IS NULL`. Signing in on the wrong host therefore finds no account at all — it is not a comparison that could be forgotten.

Data this module reads but does not own: `Customers` (the `cid` claim, via `ICustomerRepository`). Migration `Phase3Identity` split the original `Customers` table into `Users` + `Customer`, preserving ids and password hashes.

## API

| Method | Route | Authorization | Module flag | Use case |
|---|---|---|---|---|
| POST | `/api/auth/register` | anonymous, rate-limited | — | Register (403 on platform hosts) |
| POST | `/api/auth/login` | anonymous, rate-limited | — | Sign in |
| POST | `/api/auth/refresh` | the refresh cookie, rate-limited | — | Rotate the session |
| POST | `/api/auth/logout` | anonymous | — | Revoke this device's family |
| GET | `/api/auth/me` | authenticated | — | Current account, permissions, area |
| POST | `/api/auth/change-password` | authenticated, rate-limited | — | Change password, keep this device |
| POST | `/api/auth/forgot-password` | anonymous, rate-limited | — | Always 200 |
| POST | `/api/auth/reset-password` | anonymous, rate-limited | — | Reset, and accept an invitation |
| POST | `/api/auth/verify-email` | anonymous, rate-limited | — | Confirm an email |
| POST | `/api/auth/resend-verification` | authenticated, rate-limited | — | New confirmation link |
| GET/POST | `/api/admin/staff` | `store.staff.manage` | — | List / invite staff |
| POST | `/api/admin/staff/{id}/status` | `store.staff.manage` | — | Enable / disable staff |
| GET/POST | `/api/platform/users` | `platform.users.manage`, platform host | — | List / invite platform accounts |
| POST | `/api/platform/users/{id}/status` | `platform.users.manage`, platform host | — | Enable / disable |

`AuthController` carries `[AvailableOnAllHosts]` (the same routes serve both areas, and the scope decides which accounts exist) and `[AvailableDuringProvisioning]` (a store's staff must be able to sign in while the store is still being prepared). Its class-level attributes do **not** include the closed-store exemption, so sign-in on a suspended store answers `503 StoreUnavailable`.

## Security and permissions

| Role | Scope | Permissions |
|---|---|---|
| PlatformOwner | Platform | Every `platform.*` permission |
| PlatformAdmin | Platform | `platform.tenants.manage`, `platform.reports.view`, `platform.audit.view` |
| TenantAdmin | One store | Every store permission |
| TenantStaff | One store | `catalog.manage`, `inventory.view`, `inventory.manage`, `orders.view`, `orders.manage`, `customers.view`, `reviews.moderate`, `store.reports.view` |
| Customer | One store | None — their access to their own data is ownership, checked with `CanAccessOwnedBy` |

- `[HasPermission("x")]` builds a policy on demand in `PermissionPolicyProvider`; an unknown permission name throws at request time instead of silently denying everyone, and `AuthorizationBoundaryTests` catches it in CI. Anonymous callers get 401, authenticated callers without the permission get 403.
- The token carries the **role**, not the permissions, so a change to `RolePermissions` takes effect on the next request without reissuing tokens.
- `RolePermissionsTests` proves the two worlds never overlap and that every permission is granted to at least one role.
- Sessions: an access token lives 15 minutes (`Jwt:ExpiryMinutes`, validated 5–60 at startup) and is held in JavaScript memory only. The refresh token is a 256-bit random value stored only as a SHA-256 hash, delivered in the cookie `souq_refresh` (`HttpOnly`, `Secure` unless `Auth:RefreshCookie:Secure` is false for local http, `SameSite=Strict`, `Path=/api/auth`), rotated on every use, and living 30 days sliding (`Jwt:RefreshTokenDays`, validated 1–90).
- Every authenticated request re-checks two things after the signature, issuer, audience and lifetime (30-second skew) in `src/Souq.API/Security/AccessTokenValidation.cs`: the `tid` claim against the store resolved from the host (and the **absence** of `tid` on a platform host), and the `sstamp` claim against the account's current security stamp through `ISessionValidator` (cached 30 seconds per instance, dropped instantly on the instance that changed it).
- Rate limits are fixed windows per `host|client IP` (`RateLimiting:*`): 10/min on every endpoint that takes a password or sends an email, 30/min on refresh. Exceeding one returns `429 TooManyRequests` with `Retry-After`. The client IP is trusted from `X-Forwarded-For` only for proxies listed in `ForwardedHeaders:KnownNetworks`.
- Sign-in never reveals which emails exist: an unknown email still runs a BCrypt verification against a decoy hash, and forgot-password always answers 200.
- Bootstrap accounts come from configuration (`Seed:PlatformOwnerEmail`/`Seed:PlatformOwnerPassword`, `Seed:AdminEmail`/`Seed:AdminPassword`). Outside Development a seed password must be at least 12 characters and differ from the documented development one, or the API refuses to start.

## Tenant behaviour

- Accounts and refresh tokens are `ITenantOrPlatformOwned`: the named filter scopes every lookup, and `TenantWriteGuardInterceptor` refuses to create a platform account inside a store or a store account in the platform scope.
- `JwtTokenGenerator` refuses to issue a token for an unsaved account or for a store account with no `TenantId`, because `tid` is what binds the token to a host.
- A token minted for store A presented on store B's host fails authentication (`TenantIsolationTests`), as does a store token on a platform host (`AuthorizationBoundaryTests`).
- Email links are built from the request's own scheme and host (`RequestStorefrontLinks`), except when the platform invites a store's administrator: then the link is built on that store's **primary domain**, so the administrator lands on their store, not on the platform.
- The session-stamp cache key includes the scope, so the same user id in two scopes cannot share a cached stamp.

## Events and background work

- No domain events.
- Three outbox messages are enqueued inside the caller's unit of work: `PasswordResetRequested`, `EmailVerificationRequested` and `AccountInvited`. They carry only an account id and the origin — **no raw token ever reaches a table**. The token is minted when the message is dispatched, saved, and only then emailed; a retry mints a new token that invalidates the previous one, so the last message to arrive is the valid one.
- The dispatcher runs each message inside its store's scope (or the platform scope) through `TenantScopes`, which is why an invitation written into store B is emailed with store B's branding.
- No hosted service in this module. Expired refresh tokens are never purged (see Known limitations).

## External integrations

- BCrypt (`BcryptPasswordHasher`) with the library's default work factor, including a lazily-created decoy hash so an unknown email costs the same time as a wrong password.
- HMAC-SHA256 JWTs (`JwtTokenGenerator`) with `Jwt:Key` from user-secrets or environment variables; `JwtSettingsValidator` rejects a key shorter than 256 bits at startup rather than at the first sign-in.
- Email providers are reached only by the Notifications dispatch handlers.

## Tests

| Level | Class | What it covers |
|---|---|---|
| Domain | `tests/Souq.Domain.Tests/UserTests.cs` | Email normalization, invalid data, the two worlds, lockout and its release, reset-token randomness and hashing, reset semantics inside and outside the window, stamp rotation, hash upgrade, email confirmation |
| Domain | `tests/Souq.Domain.Tests/RefreshTokenTests.cs` | Issue returns the raw value once, consumed tokens are inactive, the grace window, revocation keeps the first reason |
| Domain | `tests/Souq.Domain.Tests/InvitationAndAuditTests.cs` | Invitation without a password, no invitations for customers, acceptance sets the password and confirms the email, renewal only while pending |
| Application | `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` | Registration rules and the outbox message, the uniform sign-in failure and its timing, lockout, disabled accounts, rotation, the grace window, reuse detection, logout, change password, forgot/reset/verify |
| Application | `tests/Souq.Application.Tests/Common/AccountsTests.cs` | Invitation, renewal, `EmailTaken`, self-disable, last administrator, unmanaged roles, and that disabling rotates the stamp, revokes tokens and drops the cache |
| Application | `tests/Souq.Application.Tests/Security/RolePermissionsTests.cs` | The role table per role, the store/platform split, ownership access, customer-profile requirement |
| Integration | `tests/Souq.IntegrationTests/AuthSessionTests.cs` | Cookie flags, rotation and `RefreshTokenReused`, logout, a password change ending other devices, lockout, platform sign-in only on the platform host, `RegistrationNotAllowed`, single-use email confirmation, email links on the request's host, 429 with `Retry-After` |
| Integration | `tests/Souq.IntegrationTests/AuthorizationMatrixTests.cs` | Role × endpoint, and that staff without a customer profile cannot shop |
| Integration | `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs` | Every endpoint declares a decision, the reviewed public list, permissions exist, platform endpoints reject store tokens, 404 for another customer's resource |
| Integration | `tests/Souq.IntegrationTests/StoreAdministrationTests.cs` | The staff lifecycle: invite on the store's host, renewal invalidating the first link, acceptance, sign-in, self-disable refusal, disabling ending the session immediately, and a customer's email refusing a staff invitation |
| Integration | `tests/Souq.IntegrationTests/StartupAndSecurityTests.cs` | No default administrator outside Development, weak seed passwords failing startup, reset tokens stored hashed and never logged, single use |
| Integration | `tests/Souq.IntegrationTests/ConfigurationTests.cs` | `JwtSettings` validation (missing, weak, out-of-range values) rejecting startup by name |
| Integration | `tests/Souq.IntegrationTests/TenantIsolationTests.cs` | Store A's token on store B's host, and per-store email uniqueness |
| Architecture | `tests/Souq.ArchitectureTests/DependencyRuleTests.cs` | Controllers never read claims or decide ownership |
| Frontend | `frontend/src/api/client.test.js` | The token stays in memory, one shared refresh for concurrent requests, a single retry, the session-expired event, no refresh on a failed sign-in, session restoration from the cookie |

## Failure modes

| Situation | Error code | HTTP | Where it comes from |
|---|---|---|---|
| Unknown email or wrong password | `InvalidCredentials` | 401 | `LoginHandler` (same message and timing in both cases) |
| Five consecutive failures | `AccountLocked` | 401 | `User.IsLockedOut`, checked before verification |
| Disabled account, correct password | `AccountDisabled` | 401 | `LoginHandler`, revealed only after the password is right |
| Missing, unknown, expired or revoked refresh token; disabled account | `InvalidRefreshToken` | 401 (cookie cleared) | `RefreshSessionHandler` |
| A consumed refresh token replayed outside the grace window | `RefreshTokenReused` | 401 | `RefreshSessionHandler` — revokes the family, rotates the stamp, logs a warning |
| Self-registration on a platform host | `RegistrationNotAllowed` | 403 | `RegisterHandler` |
| Registering an email already used in this store | `EmailTaken` | 409 | `RegisterHandler`; a race is caught by the unique index as `DuplicateValue` |
| Password policy, or a malformed request | `ValidationFailed` | 400 | `ValidationBehavior` with `PasswordRules.StrongPassword` |
| Wrong current password when changing it | `CurrentPasswordIncorrect` | 400 | `ChangePasswordHandler` — deliberately not 401, which the frontend reads as "session over" |
| Unknown reset or verification token | `InvalidResetToken`, `InvalidVerificationToken` | 422 | The handlers |
| Expired reset or verification token | `ResetTokenExpired`, `VerificationTokenExpired` | 422 | `InvalidPasswordResetException`, `InvalidEmailVerificationException` |
| Invalid email, name or role; moving an account between the worlds | `InvalidIdentityOperation` | 422 | `InvalidIdentityOperationException` |
| Inviting an email that holds an active account | `EmailTaken` | 409 | `AccountInvitations` |
| Disabling yourself, or the last administrator | `CannotDisableSelf`, `LastAdministrator` | 422 | `AccountStatusChanger` |
| Token for another store, or a store token on a platform host, or a stale security stamp | `Unauthenticated` | 401 | `AccessTokenValidation` |
| Authenticated but without the permission | `Forbidden` | 403 | `PermissionAuthorizationHandler` |
| A staff account calling a shopper use case | `CustomerAccountRequired` | 403 | `CurrentUserExtensions.RequireCustomerId` |
| Too many attempts | `TooManyRequests` | 429 + `Retry-After` | `RateLimitingSetup` |
| Two sign-ins to one account racing (both write the counter) | `ConcurrencyConflict` | 409 | The `Users` `rowversion`; no handler catches it, and no test covers it |
| Missing or weak `Jwt:*`, or a weak seed password outside Development | — | startup failure | `JwtSettingsValidator`, `DbSeeder` |

## Common change scenarios

Details in [ChangeGuide.md](ChangeGuide.md): adding a permission or a role; protecting a new endpoint; changing token lifetimes; changing the password or lockout policy; adding an account-state rule; auditing authentication; adding an external identity provider.

## Known limitations

- **No role change for an active account.** `User.ChangeRole` is reachable only while renewing a pending invitation, so promoting a `TenantStaff` to `TenantAdmin` after they have signed in is impossible through the API.
- **No account removal.** Staff can only be disabled; only Customers' erasure path anonymizes a `User`.
- **Authentication is not audited.** Sign-in, sign-out, lockout, password changes and resets leave no row in `AuditEntries`; reuse detection produces only a log warning. The audit log therefore answers "who changed this store" but not "who signed in".
- **Lockout leaks existence and enables a nuisance.** `AccountLocked` is returned only for accounts that exist, and anyone who knows an email can keep it locked in 15-minute blocks within the rate limit.
- **Revocation is not instant across instances.** The stamp cache is per process with a 30-second lifetime, so with several API instances a disabled account can keep using an access token for up to 30 seconds elsewhere.
- **No absolute session lifetime and no "sign out everywhere".** A refresh token used at least once every 30 days lives forever (PLANNED for the Phase 20 review and the Phase 16 account page).
- **Expired and revoked refresh tokens are never purged.** `RefreshTokens` grows without bound.
- **Email confirmation is recorded but never required**, for sign-in or for checkout. [ADR-0023](../../11-ADR/0023-sessions-and-credentials.md), [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) and the roadmap all say the requirement is "a store setting (Phase 4), enforced at checkout (Phase 9)" — both phases shipped and no such setting exists.
- **One role per account, no custom roles** (DEFERRED as YAGNI), and no group or delegation model.
- **Reset and invitation share one token field.** Requesting a password reset invalidates a pending invitation link and vice versa.
- **Password policy is length plus one letter and one digit.** No breach-list check, no MFA, and the BCrypt work factor is the library default rather than a configured one.
- **No session list for the account holder**, so a user cannot see or end individual devices.

## Future evolution

- **PLANNED (Phase 16):** an account page with "sign out everywhere".
- **PLANNED (Phase 17):** store staff and permission screens in the tenant dashboard (today the staff API has no UI).
- **PLANNED (Phase 18):** platform account screens.
- **PLANNED (Phase 20 review):** an absolute refresh-family lifetime, an authorization-matrix review, secret and key rotation, and cross-tenant attack tests.
- **DEFERRED:** custom per-store roles — `RolePermissions` is deliberately shaped so they could move into tenant data behind the same `HasPermission` API ([ADR-0019](../../11-ADR/0019-authorization-foundation.md)).
- **FUTURE:** an external identity provider (SSO, SAML, MFA). [ADR-0010](../../11-ADR/0010-authentication-authorization.md) keeps this open by depending on nothing but the claims contract `sub`, `tid`, `cid`, role and `sstamp`.
