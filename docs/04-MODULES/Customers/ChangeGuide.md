# Customers: change guide

> Read [README.md](README.md) first. This page lists common changes and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

1. One profile per login account per store — unique `(TenantId, UserId)`.
2. At most `Customer.MaxAddresses` addresses; exactly one default shipping and one default billing while any address exists; deleting a default moves it to the oldest remaining address.
3. Nothing about an erased profile may change afterwards, and `Customer.Erase` stays idempotent.
4. Erasure never deletes the `Customers` or `Users` row: `Orders`, `Reviews` and `CouponRedemptions` hold `Restrict` foreign keys to the customer.
5. An order's address snapshot never changes when the address book changes.
6. The caller is always the `cid` claim. No request may carry a customer id on `/api/account/*`.
7. Admin routes stay behind `customers.view`, with `customers.manage` on top for blocking, export and erasure; those three plus both exports stay audited.
8. Personal data never reaches a log.

**Files to read first**

`src/Souq.Domain/Entities/Customer.cs`, `src/Souq.Domain/ValueObjects/PostalAddress.cs`, `src/Souq.Application/Features/Customers/CustomerErasure.cs`, `src/Souq.Application/Features/Customers/Account/AccountUseCases.cs`, `src/Souq.Application/Features/Customers/Admin/AdminCustomerUseCases.cs`, `src/Souq.Application/Features/Customers/CustomerModels.cs`, `src/Souq.Infrastructure/Persistence/Queries/CustomerQueries.cs`, `src/Souq.Infrastructure/Persistence/Configurations/CustomerConfiguration.cs`, and [ADR-0027](../../11-ADR/0027-customer-profile-and-erasure.md).

**Tests that guard the module**

`CustomerProfileTests`, `CustomerAccountHandlersTests` and `CreateOrderCustomerRulesTests` (both in `tests/Souq.Application.Tests/Customers/CustomerHandlersTests.cs`), `CustomerAccountTests`, `TenantIsolationTests`, `WishlistTests`, `AuthorizationMatrixTests`, `ModuleAndContractRuleTests`, and the frontend tests `frontend/src/features/admin/customers/customerActions.test.js` and `frontend/src/features/account/addressForm.test.js`.

---

## I need to… add a field to the customer profile

*Example: a date of birth, a company name, a tax number.*

- **Inspect:** `Customer` (property, `Customer.UpdateProfile`, `Customer.Erase`), `CustomerConfiguration`, `UpdateMyProfileCommand` and `UpdateMyProfileValidator`, `CustomerProfileDto`, `CustomerDetailDto`, `CustomerExportProfile`, `CustomerQueries`, `frontend/src/pages/account/ProfileForm.jsx`, `updateMyProfile` in `frontend/src/api/client.js`.
- **Rules to respect:** the rule lives in the entity, not the handler or the validator — an invalid value throws `InvalidCustomerDataException`. Any mutator must keep calling the erasure guard. If the field is personal data it must be cleared by `Customer.Erase` **and** appear in the export; those two go together.
- **Steps:** add the property with a private setter → validate it inside `Customer.UpdateProfile` (or a new named method if it is a separate decision) → clear it in `Customer.Erase` → column and length in `CustomerConfiguration` → migration → extend the command, validator and DTOs → add it to `CustomerExportProfile` and to `CustomerQueries.ExportAsync` and `FindDetailAsync` → frontend form field and i18n keys.
- **Tests:** `CustomerProfileTests` for the guard and for erasure clearing it; `CustomerAccountHandlersTests` for the round trip; extend the export assertion in `CustomerAccountTests`.
- **API:** additive — a new optional property in the PUT body and a new field in the responses; existing clients keep working.
- **Database:** `dotnet ef migrations add <Name> --project src/Souq.Infrastructure --startup-project src/Souq.API`. Add it nullable (or with a default) so the migration is additive and reversible; no backfill needed.
- **Security:** treat it as personal data — never log it, keep it out of audit metadata, include it in erasure.
- **Docs and ADR:** update this module's README and `docs/06-DATABASE/DatabaseDesign.md`. No ADR unless the field changes what erasure or export means.

## I need to… change the erasure rules (legal review first)

- **Inspect:** `CustomerErasure`, `Customer.Erase`, `User.Erase`, `EraseMyAccountHandler`, `EraseCustomerHandler`, and the list of what is deliberately kept in [README.md](README.md) — the order snapshot, review comments, in-app notifications, coupon redemptions, payments and audit rows.
- **Rules to respect:**
  - **This is a legal decision before it is a technical one.** Erasure balances a data-subject right against accounting retention; get the change signed off, then write it down in an ADR.
  - Keep it one `SaveChangesAsync`: a partially erased person is worse than either outcome.
  - Keep it idempotent, and keep sessions ending (stamp rotation, token revocation, `ISessionValidator.Forget` **after** the save).
  - Never switch to a hard delete. The `Restrict` foreign keys would throw `ReferenceConstraintViolationException` (409 `ReferenceConflict`), and the store would lose its financial record.
  - If you start erasing another module's data, prefer a narrow port on that module over reaching for its domain repository — the current basket and wishlist deletions are already a leak, not a pattern to copy.
- **Steps:** decide what changes → adjust `Customer.Erase` for data this module owns → adjust `CustomerErasure` for other modules' rows, all before the single save → if review comments must go, that is a Reviews change (and an ADR, because approved reviews are public content) → if order snapshots must be purged, that is an Ordering/retention change, currently PLANNED for Phase 20.
- **Tests:** `CustomerProfileTests` (what the entity clears), `CustomerAccountHandlersTests` (the orchestration), `CustomerAccountTests` (the end-state assertions, which currently check the profile columns and that the order survives), `WishlistTests`. Add an assertion for anything newly deleted — the current suite does not cover notifications or audit rows.
- **API:** unchanged, but the wording in `frontend/src/pages/account/PrivacyPanel.jsx` and its i18n keys must describe what really happens.
- **Database:** a rule change that must apply to already-erased customers needs a hand-written data migration; it is **not reversible**. Say so in the migration and in the ADR.
- **Security:** the audit row keeps the actor; the erased values themselves must never be logged or copied into audit metadata.
- **Docs and ADR:** amend or supersede ADR-0027, and update `docs/07-SECURITY/Security.md` and this module's README.

## I need to… add marketing preferences (PLANNED)

- **Inspect:** `Customer`, `CustomerExportProfile`, `AccountController`, and the Notifications module's handlers, which are where a preference would have to be honoured.
- **Rules to respect:** consent belongs to Customers, not Identity (Identity must not own marketing preferences). Store the decision **and** when it was given. Transactional email (reset, verification, order status) is not marketing and must not be gated by it. Erasure clears it; the export includes it.
- **Steps:** a domain method that records consent with a timestamp → a command and endpoint under `Features/Customers/Account` → export and erasure → when a marketing message type is added, its outbox handler reads the live preference at dispatch (never a copy in the payload), ideally through a Customers read port rather than `ICustomerRepository`.
- **Tests:** domain test for consent and withdrawal; handler test; an integration test that a marketing message is skipped for a customer who opted out.
- **API:** new endpoint plus new fields in the profile response; additive.
- **Database:** additive migration, nullable, default "no consent".
- **Security:** consider making the command `IAuditable` — proving when consent was given is the point of storing it. Never infer consent from silence.
- **Docs and ADR:** an ADR is warranted: consent model, what counts as marketing, and retention of consent history.

## I need to… add a column to the admin customer list or detail

- **Inspect:** `CustomerQueries.ListAsync` and `StatsAsync`, `CustomerListItemDto`, `CustomerDetailDto`, `frontend/src/pages/admin/Customers.jsx`, `frontend/src/pages/admin/CustomerDetailDrawer.jsx`.
- **Rules to respect:** the projection stays in Infrastructure. Do **not** add a Customers → Ordering reference in the Application layer — `ModuleAndContractRuleTests` will fail, and ADR-0027 chose the read projection precisely to avoid it. The tenant filter already scopes the tables you read.
- **Steps:** extend the projection and the DTO → column in the admin table or drawer → i18n keys.
- **Tests:** extend the admin list assertions in `CustomerAccountTests`; keep `TenantIsolationTests` green (listings must not leak the other store).
- **API:** additive.
- **Database:** each added aggregate is another correlated subquery per row — measure on a store with many orders, and add an index instead of shipping a slow list.
- **Security:** `customers.view` already gates it; do not surface credential or session data here beyond the existing `EmailConfirmed` and `LastLoginAt`.
- **Docs:** README's data-ownership table.

## I need to… change address validation or add an address field

- **Inspect:** `PostalAddress`, `AddressInput` and `AddressInputValidator`, `CustomerAddress`, `CustomerAddressConfiguration`, `frontend/src/features/account/addressForm.js` (+ its test), `frontend/src/pages/account/AddressFormDrawer.jsx`, `frontend/src/pages/checkout/AddressStep.jsx`.
- **Rules to respect:** `PostalAddress` is shared — Ordering turns it into the order's single-line snapshot through `ToSingleLine`, and `CreateOrderHandler` reads `Country` from the book to price shipping. Normalization and validation stay in the value object; the FluentValidation rules exist for good field-level messages, not as the real guard. Existing rows are **not** re-validated when loaded, but the next edit of an old address will throw if it cannot satisfy a new required rule — plan a backfill or make the rule apply only on write of that field.
- **Steps:** value object first (property, normalization, limits) → `CustomerAddress.Update` → column in the configuration → migration → `AddressInput` + validator → DTO → frontend form and summary → checkout if the field affects shipping.
- **Tests:** `CustomerProfileTests` for normalization and the snapshot; `addressForm.test.js`; `CustomerAccountTests` for the round trip; `CreateOrderCustomerRulesTests` if the order snapshot changes.
- **API:** adding a required field to `AddressInput` is a **breaking** change for existing clients — prefer optional-with-default, or version the behaviour.
- **Database:** additive column; a required field needs a backfill decision for existing rows.
- **Security:** nothing extra, but remember the snapshot copies this data into `Orders` permanently.
- **Docs:** README (domain model) and `docs/06-DATABASE/DatabaseDesign.md`.

## I need to… change what "blocked" prevents

- **Inspect:** `Customer.Block` / `Customer.Unblock` / `Customer.IsBlocked`, and the two places that check it today: `CreateOrderHandler` and `CreateReviewHandler` (both return `CustomerBlocked`).
- **Rules to respect:** blocking is a **commercial** lever; disabling the login is Identity's `User.Disable`. Do not merge them — a blocked customer must keep the ability to read and export their data (ADR-0027). If you extend the rule to more actions (basket, wishlist), add the check where the profile is already loaded, and consider moving the decision into the entity so every consumer asks the same question instead of repeating `IsBlocked`.
- **Steps:** decide the surface → add the check in each consumer (or a domain method they all call) → use the existing `CustomerBlocked` code so the frontend keeps one message.
- **Tests:** `CreateOrderCustomerRulesTests`, `CreateReviewHandlerTests`, and the admin blocking scenario in `CustomerAccountTests`.
- **API:** a new 403 on endpoints that previously succeeded — tell the frontend, and update the i18n message.
- **Database:** none.
- **Security:** a block must never turn into an information leak (do not reveal other customers' state).
- **Docs:** README's "Known limitations" list is where today's narrow scope is recorded.

## I need to… change who may block, export or erase

- **Inspect:** `Permissions.Customers`, `RolePermissions`, the `[HasPermission]` attributes on `AdminCustomersController`, `frontend/src/features/admin/customers/customerActions.js`.
- **Rules to respect:** the controller declares the permission, never a role name. The frontend only hides buttons; the server is the guard.
- **Steps:** change the permission set in `RolePermissions` (or introduce a new permission constant, adding it to `Permissions.StoreAll`) → update the attributes → update the frontend's `canManage` check.
- **Tests:** `RolePermissionsTests`, `AuthorizationMatrixTests`, `customerActions.test.js`.
- **API:** a role that loses access starts getting 403 `Forbidden`.
- **Security:** widening `customers.manage` means widening who can erase a person and read their full record — treat it as a security review.
- **Docs:** `docs/07-SECURITY/AuthenticationAndAuthorization.md` and README.

## I need to… replace the repository leak with a real contract

*Introducing the deferred* ICustomerDirectory.

- **Inspect:** every caller of `ICustomerRepository` outside this module — `RegisterHandler`, `AuthSessionIssuer`, `GetCurrentUserHandler`, `CreateOrderHandler`, `CreateReviewHandler`, `OrderStatusChangedHandler`, `OrderEmailHandler` — and the allowed-contract map in `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.
- **Rules to respect:** the contract must expose the *questions* other modules ask (is this customer blocked? what is their contact email and account id? which address did they choose?), not the aggregate. No domain entity may appear in it, and Modules.md forbids cycles: Ordering → Customers and Reviews → Customers are fine, Customers → Ordering is not.
- **Steps:** create `Features/Customers/Contracts/` with the interface and small DTOs → implement it inside Customers over `ICustomerRepository` → migrate consumers one at a time, starting with the read-only ones (Notifications) → add the new edges to the allowed-contract map and confirm the no-cycle test still passes → leave `ICustomerRepository` for this module only.
- **Tests:** the architecture test is the point of the change; add the new edges deliberately rather than loosening the rule. Existing handler tests substitute the new interface instead of the repository.
- **API:** none.
- **Database:** none.
- **Security:** the contract must not widen what a consumer can do — read-only questions where the consumer only reads.
- **Docs and ADR:** amend ADR-0027 (its "Negative / limits" section records this debt), and update [Modules.md](../Modules.md), this README and the Ordering, Reviews and Notifications module pages.
