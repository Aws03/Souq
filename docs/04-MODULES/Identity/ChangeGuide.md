# Identity: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change:**

1. A secret is never stored in a form that can be read back: passwords are BCrypt hashes, and reset, verification, invitation and refresh tokens are stored only as SHA-256 hashes of 256-bit random values.
2. Anything that must end existing sessions rotates the security stamp **and** revokes the refresh tokens, then drops the cached stamp (`ISessionValidator.Forget`).
3. Platform accounts and store accounts never cross: not by role change, not by token, not by host.
4. Responses must not reveal which emails exist — one message and one timing for a bad email and a bad password, and forgot-password always succeeds.
5. Every endpoint declares `[AllowAnonymous]`, `[Authorize]` or `[HasPermission]`; making one public means editing the reviewed list in `AuthorizationBoundaryTests`, which is a visible decision.
6. Use cases learn the caller from `ICurrentUser` only. No command carries a caller id, and no controller reads claims (`DependencyRuleTests`).
7. Rules belong in the `User` aggregate; handlers coordinate.

**Files to read first:** `src/Souq.Domain/Identity/User.cs`, `src/Souq.Domain/Identity/RefreshToken.cs`, `src/Souq.Application/Common/Security/Permissions.cs`, `src/Souq.Application/Features/Auth/AuthSessionIssuer.cs`, `src/Souq.Application/Common/Accounts/Accounts.cs`, `src/Souq.API/Security/AccessTokenValidation.cs`, `src/Souq.API/Security/PermissionAuthorization.cs`.

**Tests that guard the module:** `tests/Souq.Domain.Tests/UserTests.cs`, `tests/Souq.Domain.Tests/RefreshTokenTests.cs`, `tests/Souq.Domain.Tests/InvitationAndAuditTests.cs`, `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs`, `tests/Souq.Application.Tests/Common/AccountsTests.cs`, `tests/Souq.Application.Tests/Security/RolePermissionsTests.cs`, `tests/Souq.IntegrationTests/AuthSessionTests.cs`, `tests/Souq.IntegrationTests/AuthorizationMatrixTests.cs`, `tests/Souq.IntegrationTests/AuthorizationBoundaryTests.cs`.

---

## I need to add a permission

- **Inspect:** `src/Souq.Application/Common/Security/Permissions.cs` (the grouped constants, `StoreAll`, `PlatformAll`, and `RolePermissions`).
- **Rules to respect:** a permission belongs to exactly one world — a store permission may never be granted to a platform role or the reverse, and `RolePermissionsTests` proves it; every declared permission must be granted to at least one role; the name is a stable contract because `/api/auth/me` ships it to the frontend.
- **Steps:**
  1. Add the constant to the right nested class, using the `area.subject.verb` shape.
  2. Add it to `Permissions.StoreAll` or `Permissions.PlatformAll` — forgetting this makes `PermissionPolicyProvider` throw when an endpoint declares it.
  3. Grant it in `RolePermissions.ByRole` to every role that should have it.
  4. Use it on the endpoint (`[HasPermission]`) and, if the rule also applies inside a use case, through `ICurrentUser.HasPermission`.
- **Tests:** extend `RolePermissionsTests` with the new expectation per role; `AuthorizationBoundaryTests` then automatically checks that the endpoint refuses anonymous callers with 401 and customers with 403.
- **API:** the permission list in `/api/auth/me` grows; frontend guards (`can(...)`, `RequirePermission`) can use it immediately.
- **Database:** none. Permissions live in code by design.
- **Security:** granting an existing role a new permission takes effect on the next request for every signed-in holder, without reissuing tokens. That is the intended behaviour — verify it is what you want.
- **Docs and ADR:** [README.md](README.md) and [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) §2. No ADR.

## I need to add a role

- **Inspect:** `src/Souq.Domain/Common/Roles.cs`, `RolePermissions`, and the two scope helpers `Roles.IsPlatform` and `Roles.IsStoreStaff`.
- **Rules to respect:** the role name travels in the token and is stored in `Users.Role` (30 characters); `Roles.All` is what `User` validates against; `IsPlatform` decides where the account may exist, so a new role must be classified explicitly; the "last administrator" rule names a single top role per scope (`Roles.TenantAdmin`, `Roles.PlatformOwner`).
- **Steps:**
  1. Add the constant, add it to `Roles.All`, and extend `IsPlatform` or `IsStoreStaff`.
  2. Add its row to `RolePermissions.ByRole` — a role missing from that table silently grants nothing.
  3. Decide whether it may be invited: `StaffRoles.All` and `PlatformRoles.All` control the listings, and the invite validators (`InviteStaffValidator`, `InvitePlatformUserValidator`) control what may be created.
  4. Check `AccountStatusChanger` calls: `managedRole` and `lastStandingRole` decide who this screen may touch and who may not be disabled.
- **Tests:** `RolePermissionsTests` (its "every permission has a role, and the worlds do not overlap" case will fail until the new role is classified); `AuthorizationMatrixTests` gains a row; `UserTests` covers role validation and the world guard.
- **API:** `role` appears in account listings and in `/api/auth/me`; invite endpoints accept a new value.
- **Database:** none, unless existing accounts must be migrated to it.
- **Security:** a new store role must not receive a `platform.*` permission, and vice versa; the unit test enforces it.
- **Docs and ADR:** [README.md](README.md), [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md). An ADR only if you move away from one role per account.

## I need to add a permission-protected endpoint

- **Inspect:** an existing controller such as `src/Souq.API/Controllers/StoreAdministrationControllers.cs`, plus `src/Souq.API/Security/PermissionAuthorization.cs`.
- **Rules to respect:** the endpoint declares the permission, the use case re-checks anything that depends on data (ownership, "is this row mine?"); a resource belonging to someone else is 404, never 403; controllers stay thin and never read claims; a route with a resource id joins the isolation table in `TenantIsolationTests`.
- **Steps:**
  1. Add `[HasPermission(Permissions.Area.Thing)]` on the action or the controller (attributes combine: `ReviewModerationController` requires both `reviews.moderate` and `store.settings.manage` on its `PUT`).
  2. Send a command or query; let the handler use `ICurrentUser` for identity and ownership.
  3. If the endpoint belongs to an optional module, add `[RequiresModule(...)]`; if it is a platform endpoint, add `[PlatformEndpoint]` and a `platform.*` permission.
- **Tests:** `AuthorizationBoundaryTests` discovers the endpoint from routing metadata and asserts 401/403 automatically; add the route to the isolation table in `TenantIsolationTests` if it takes a resource id; add the behavioural test where the feature lives.
- **API:** document the route and its permission in [Endpoints.md](../../05-API/Endpoints.md).
- **Database:** none.
- **Security:** never make an endpoint anonymous without editing the reviewed public list — that edit is the review.
- **Docs and ADR:** the owning module's README. No ADR.

## I need to change token lifetimes or the cookie

- **Inspect:** `src/Souq.Infrastructure/Services/JwtSettings.cs` (and `JwtSettingsValidator`), `AuthSessionIssuer`, `AuthController` (`RefreshCookieName` and the cookie options), `src/Souq.API/Security/RateLimiting.cs` (`RefreshCookieOptions`).
- **Rules to respect:** the access token must stay short — revocation is a stamp check with a 30-second cache, so its lifetime is the worst-case window for a role change; the validator's bounds (5–60 minutes, 1–90 days) exist because a 120-minute token once shipped; the refresh cookie must stay `HttpOnly`, `SameSite=Strict` and scoped to `Path=/api/auth`; `Secure` may be relaxed only through configuration for local http.
- **Steps:** change `appsettings.json` or the environment for a deployment, and the defaults plus `JwtSettingsValidator` bounds only if the acceptable range itself changes. Refresh lifetime is read through `IJwtTokenGenerator.RefreshTokenLifetime`, so nothing else needs touching.
- **Tests:** `ConfigurationTests` covers the validator's messages; `AuthSessionTests` covers the cookie's flags — update it if you change them.
- **API:** `expiresAt` in the sign-in response changes; the frontend does not depend on the value (it refreshes on 401).
- **Database:** none. Tokens already issued keep their own expiry.
- **Security:** lengthening the access token widens the revocation gap; shortening the refresh lifetime signs people out sooner. Never put the refresh token in the response body or in `localStorage`.
- **Docs and ADR:** [README.md](README.md), [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) §3, [Configuration.md](../../09-OPERATIONS/Configuration.md). Changing the model (an absolute family lifetime, for instance) needs an ADR — it is [ADR-0023](../../11-ADR/0023-sessions-and-credentials.md)'s explicit revisit item.

## I need to change the password policy

- **Inspect:** `src/Souq.Application/Features/Auth/PasswordRules.cs`, its three callers (`RegisterValidator`, `ChangePasswordValidator`, `ResetPasswordValidator`), and `DbSeeder.MinimumAdminPasswordLength`.
- **Rules to respect:** one policy for every entry point — never inline a rule in a single validator; the maximum length is a denial-of-service guard for BCrypt, not a usability choice; existing hashes must keep working, so a stricter rule applies at the next password change and never invalidates a stored hash.
- **Steps:** change `StrongPassword`; if you add an expensive check (a breach list, for example), put it behind a port in Application implemented in Infrastructure, and keep it out of the request path if it calls a network service.
- **Tests:** add cases to `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` (registration and change); `StartupAndSecurityTests` covers the seed-password rule.
- **API:** rejected passwords return `400 ValidationFailed` with per-field messages; the frontend shows them. `frontend/src/pages/auth/ResetPassword.jsx` also checks a minimum length client-side — keep the two consistent or the user sees a server error the form said was fine.
- **Database:** none.
- **Security:** never log or echo a password, and keep the maximum bounded.
- **Docs and ADR:** [README.md](README.md), [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) §4.

## I need to change the lockout policy

- **Inspect:** `User.MaxFailedLogins`, `User.LockoutDuration`, `User.RecordFailedLogin`, `User.RecordSuccessfulLogin`, `User.ResetPassword` (which clears the lockout), and `LoginHandler`.
- **Rules to respect:** the rule lives in the aggregate, not in the handler; the lockout is checked **before** password verification, so a locked account cannot be probed; a successful reset must keep clearing it, because that is the documented way back in; the counter resets when the lock is applied, so the next lock needs a fresh run of failures.
- **Steps:** change the constants, or the shape of the rule inside `User` (exponential backoff, for instance, would add state to the aggregate and therefore a migration).
- **Tests:** `UserTests` covers the counter, the lock and its release; `tests/Souq.Application.Tests/Auth/AuthHandlersTests.cs` covers the handler's behaviour; `AuthSessionTests` covers it over real HTTP.
- **API:** `401 AccountLocked` is a stable code the frontend translates.
- **Database:** a migration if you add fields to `Users`.
- **Security:** weigh two facts recorded in [README.md](README.md#known-limitations) — `AccountLocked` is returned only for accounts that exist, and a lockout is a cheap way to annoy a known user. Per-IP rate limiting already blunts both; a stricter policy makes the second worse.
- **Docs and ADR:** [README.md](README.md), [AuthenticationAndAuthorization.md](../../07-SECURITY/AuthenticationAndAuthorization.md) §4.

## I need to add a rule about who may be enabled, disabled or invited

- **Inspect:** `src/Souq.Application/Common/Accounts/Accounts.cs` (`AccountInvitations`, `AccountStatusChanger`) and its two callers, `Features/Staff` and `Features/Platform`.
- **Rules to respect:** both areas share these classes on purpose — a rule added in one place must hold in the other; an account outside the managed roles must look non-existent, not forbidden; disabling must keep rotating the stamp and revoking tokens in the same save.
- **Steps:** add the rule to the shared class, parameterised if the two areas genuinely differ (as `managedRole` and `lastStandingRole` already are).
- **Tests:** `AccountsTests` is the home for the rule; `StoreAdministrationTests` and `PlatformAdministrationTests` prove it over HTTP for both areas.
- **API:** new failure codes must be stable and translatable; follow `CannotDisableSelf` and `LastAdministrator`.
- **Database:** none.
- **Security:** never allow a path that leaves a store or the platform with no one who can administer it.
- **Docs and ADR:** [README.md](README.md); [ADR-0024](../../11-ADR/0024-platform-administration.md) §6 records these shared rules.

## I need to audit authentication events

Today no `Features/Auth` request implements `IAuditable`, so sign-ins, lockouts and password changes leave no audit row.

- **Inspect:** `src/Souq.Application/Common/Auditing/Auditing.cs`, `src/Souq.Application/Common/Behaviors/AuditBehavior.cs`, `src/Souq.Domain/Auditing/AuditEntry.cs`.
- **Rules to respect:** an audit record must never contain a password, a token or a token hash; the behaviour discards the row when the result fails — so a **failed** sign-in would not be recorded by simply implementing `IAuditable`, which is precisely the event most worth recording; the actor is taken from `ICurrentUser`, which is empty on an anonymous sign-in attempt, so the subject must be carried in the record (an email address is personal data — decide deliberately).
- **Steps:** either implement `IAuditable` on the commands whose *success* matters (change password, sign out), or stage the row in the handler for failures. Keep the action names in the `area.subject.verb` shape so prefix filtering keeps working.
- **Tests:** `AuditBehaviorTests` for the staging rules; assert the new actions in `AuthSessionTests`.
- **API:** the new actions show up in `GET /api/platform/audit`.
- **Database:** none.
- **Security:** audit rows are readable by platform administrators; do not put anything there you would not show them.
- **Docs and ADR:** [README.md](README.md), [Platform/README.md](../Platform/README.md), and [ADR-0024](../../11-ADR/0024-platform-administration.md) if the write rules change.

## I need to add an external identity provider (FUTURE)

Not scheduled. [ADR-0010](../../11-ADR/0010-authentication-authorization.md) keeps it possible and names the trigger: a client requiring SSO, SAML, or MFA beyond TOTP.

- **Inspect:** `src/Souq.Application/Common/Security/SouqClaimTypes.cs`, `src/Souq.API/Security/AccessTokenValidation.cs`, `src/Souq.API/Security/HttpCurrentUser.cs`, `src/Souq.Infrastructure/Services/JwtTokenGenerator.cs`.
- **Rules to respect:** the rest of the system depends on exactly one thing — the claims contract `sub`, role, `tid`, `cid`, `sstamp`. Whatever mints the token must keep producing it, keep binding `tid` to the host, and keep a revocation signal equivalent to the stamp. A store account must still be unable to authenticate on another store's host.
- **Steps (sketch):**
  1. Decide where accounts live. If the provider becomes the source of truth, `Users` becomes a projection and the local password fields become dead weight; if it is only a sign-in method, add a link table and keep `User` as is.
  2. Map the provider's tenant concept to `TenantId`, per store. One provider configuration per store is the hard part, not the protocol.
  3. Keep `ICurrentUser`, `RolePermissions` and the permission policies untouched — that is the payoff of the contract.
  4. Decide what replaces the security stamp for revocation, and keep `ISessionValidator` as the seam.
- **Tests:** the existing suites are the acceptance criteria — `AuthorizationMatrixTests`, `TenantIsolationTests` and `AuthSessionTests` must stay green with the new provider behind the same contract.
- **API:** `/api/auth/*` shapes change or disappear; the frontend's `api/client.js` session handling is the other side of that contract.
- **Database:** a migration for account links, and a decision about existing password hashes.
- **Security:** this is the highest-risk change in the system. It needs its own ADR, a threat model, and the Phase 20 review's checklist.
- **Docs and ADR:** a new ADR superseding parts of [ADR-0010](../../11-ADR/0010-authentication-authorization.md) and [ADR-0023](../../11-ADR/0023-sessions-and-credentials.md).
