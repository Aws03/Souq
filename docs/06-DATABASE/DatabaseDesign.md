# Souq: Database Design

> **Status:** principles adopted 2026-09-11 ([ADR-0007](../11-ADR/0007-database-strategy.md)). This page describes the schema as it stands after `Phase14Notifications`, the latest migration.
> **Engine:** SQL Server 2022 · **Access:** EF Core 10, code-first migrations (the **only** source of truth for the schema).
> **Companions:** [OwnershipMap.md](OwnershipMap.md) — who owns, writes and reads each table · [Migrations.md](Migrations.md) — how the schema changes and how data is protected.

## 1. Principles

1. **Migrations are the schema.** No hand-written schema scripts live in the repository. For a DBA review, generate SQL with `dotnet ef migrations script --idempotent`.
2. **One `DbContext`, one owning module per table.** A single unit of work is what makes checkout atomic. Every table has exactly one module whose use cases write it ([OwnershipMap.md](OwnershipMap.md)); other modules go through a contract or a read projection. Two caveats, true today: the EF configurations all live in one flat folder, `src/Souq.Infrastructure/Persistence/Configurations/`, not under module folders; and cross-module reads do happen — in query services by design, and in a handful of handlers through another module's domain repository, which no test catches (each one is listed in the ownership map).
3. **Every constraint the business relies on is also a database constraint** — uniqueness, required relationships, precision, and the check constraints on inventory quantities and basket lines. Code checks give friendly messages; the database is the last line of defence against races.
4. **Explicit over clever.** Fluent configuration only; there are no data annotations anywhere in `src/Souq.Domain`.

## 2. Naming

| Item | Convention | Example in the schema |
|---|---|---|
| Table | Plural PascalCase, in `dbo` | `Products`, `OrderItems`, `OutboxMessages` |
| Column | PascalCase | `LowStockThreshold` |
| Primary key | `Id` (`int identity`; `bigint` for the two building-block tables) | |
| Foreign key | `<Entity>Id`, composite with `TenantId` between tenant-owned rows | `(TenantId, CategoryId)` |
| Index | EF default `IX_<Table>_<Cols>`, `TenantId` first | `IX_Products_TenantId_Status_CategoryId` |
| Unique index | the same, with `IsUnique()` | `IX_Categories_TenantId_Slug` |
| Filtered index | the same, with `HasFilter(...)` | `IX_Users_NormalizedEmail_Platform`, `IX_ProductVariants_ProductId_Default` |
| Alternate key | `AK_<Table>_TenantId_Id`, the principal of composite FKs | `AK_Orders_TenantId_Id` |
| Check constraint | `CK_<Table>_<Rule>` | `CK_Baskets_Owner`, `CK_InventoryItems_Quantities` |
| Concurrency column | `RowVersion` (`rowversion`), a shadow property | `PersistenceConventions.HasRowVersion` |
| Money | `<Name>` amount + `<Name>Currency`, or a bare `Currency` when the row has one | `MinOrderAmount` + `MinOrderCurrency`; `Price` + `Currency` |
| Money without its own currency column | a second amount in the row's currency | `CompareAtPrice`, `FreeOverAmount`, `PlacedTotal`, `RefundedAmount` |
| Enum | stored as `int` (`HasConversion<int>()`), exposed as a string by the API | `Orders.Status`, `Reviews.Status` |
| ASCII-only text | `varchar`/`char`, fixed length where the value has one | `TrackingToken char(32)`, `GuestTokenHash char(64)`, `ProviderPaymentId varchar(100)` |

Module schemas (`catalog.Products`) were considered and **DEFERRED** ([ADR-0007](../11-ADR/0007-database-strategy.md)): the ownership map gives the same clarity without migrating every table. Revisit when a module is extracted or per-module database permissions are needed.

## 3. Keys and identifiers

- **Primary keys:** `int IDENTITY`, the clustered index — no churn, compact indexes. `AuditEntries` and `OutboxMessages` use `bigint` because they are append-only and high-volume.
- **Tenant key:** `TenantId int NOT NULL` on tenant-owned rows, `int NULL` on accounts and sessions (NULL = the platform). See §5.
- **Public identifiers** — never expose a guessable id where access is anonymous:
  - orders: `OrderNumber` per store (from 1001) for humans, plus `TrackingToken char(32)` — 128 random bits from `RandomNumberGenerator` — for the anonymous tracking page, which is the only route that accepts it;
  - catalog: slugs, unique per store, in storefront URLs.
- **Secrets are stored hashed or encrypted, never raw:** password hashes (BCrypt), `RefreshTokens.TokenHash` and the reset/verification token hashes (SHA-256 hex, 64 characters), `Baskets.GuestTokenHash` (SHA-256 of the guest cookie), and `StorePaymentAccounts.SecretKeyCipher` / `WebhookSecretCipher` (AES-GCM ciphertext, with no column for the plaintext).
- **Merging tenants' data is not supported,** which is why `int` identity keys are acceptable.
- The only `uniqueidentifier` column is `RefreshTokens.FamilyId`, which groups a rotated token family.

## 4. Data ownership

**Ownership kinds:** *platform* (global), *tenant* (one store), *user* (a person inside a store), *building block* (infrastructure rows that cross stores). **Delete strategies:** *hard* (physically deleted), *soft* (a status that carries business meaning), *never* (append-only history), *anonymize* (the row stays, the person disappears).

The per-table inventory — owner, tenant scope, key constraints, concurrency token, writers, cross-module readers and lifecycle — lives in **[OwnershipMap.md](OwnershipMap.md)**. It is the page to read before changing a table.

One correction to the original plan is worth stating here, because it contradicts [ADR-0007](../11-ADR/0007-database-strategy.md)'s "across modules: an id plus a snapshot, no FK, except `TenantId`": **cross-module foreign keys are the norm in the current schema.** `Orders` → `Customers`, `OrderItems` → `Products` and `ProductVariants`, `Customers` → `Users`, `InventoryItems` → `ProductVariants`, `BasketLines` → `Products`, `CouponRedemptions` → `Orders`, `Payments` → `Orders`, `Reviews` → `Products`, `Customers` and `Orders`, `WishlistItems` → `Customers` and `Products` are all real, tenant-scoped foreign keys. The rule that actually held is narrower and worth keeping: **a reference gets a foreign key when the target row is never hard-deleted; otherwise it is an id plus a snapshot.** That is why the order's shipping method (`ShippingMethods` can be deleted), its coupon code, its payment intent id, every actor column (`ChangedByUserId`, `ModeratedByUserId`, `RequestedByUserId`, `UpdatedByUserId`), `Notifications.RecipientUserId` and both building-block tables carry no foreign key at all.

## 5. Tenancy in the schema

Implementation decisions: [ADR-0005](../11-ADR/0005-multi-tenancy-model.md) and [ADR-0022](../11-ADR/0022-tenancy-enforcement.md); the runtime picture is in [MultiTenancy.md](../02-ARCHITECTURE/MultiTenancy.md).

- **Marker interfaces, applied by reflection.** `AppDbContext.OnModelCreating` walks the model and, for every `ITenantOwned` type, adds the `TenantId` property as `ValueGeneratedNever`, a foreign key to `Tenants` with `Restrict`, and the **named query filter** `AppDbContext.TenantFilter` (`"Tenant"`). `ITenantOrPlatformOwned` types (only `Souq.Domain.Identity`) get the nullable variant, where the platform scope means `TenantId IS NULL`. Nothing is copied per entity, so a new entity cannot be forgotten — and `TenancyRuleTests` fails the build if a business entity implements neither interface, or if a tenant-owned entity ends up without the filter or the FK.
- **Reads throw rather than leak.** The filter reads the current store on every query execution. With no store in scope it throws `TenantContextMissingException` — including in platform scope. There is no code path that returns "all tenants' rows".
- **Writes are guarded, not trusted.** `TenantWriteGuardInterceptor` runs inside every `SaveChanges`: it stamps `TenantId` on insert (entities have no setter, so neither a handler nor a client can choose it), and rejects an insert for another store, an update or delete of another store's row, or any change to `TenantId`, with `CrossTenantWriteException` and a Critical log — before any SQL is sent.
- **References carry the tenant.** Between tenant-owned rows the foreign key is composite, `(TenantId, XId) → (TenantId, Id)`, through alternate keys on `Categories`, `Products`, `Customers`, `Orders`, `ProductVariants`, `InventoryItems`, `Coupons` and `Payments`. The database itself rejects a row pointing at another store's row, whatever a handler forgot. Two deliberate exceptions: aggregate children with a shadow key to their root (created together in one scope), and `Categories.ParentId`, checked in the handler. A violation surfaces as `409 ReferenceConflict`.
- **The only sanctioned bypass** is `IgnoreQueryFilters` inside `PlatformQueries`, allow-listed by name in `TenancyRuleTests`, and used only with an explicit `TenantId` predicate or for aggregate counts. Raw SQL is banned outside migrations by the same test, because it never passes the filter.
- **Bulk writes skip the interceptors, so they are allow-listed.** `ExecuteUpdateAsync` / `ExecuteDeleteAsync` become one UPDATE or DELETE without going through `SaveChanges`, so they neither stamp `UpdatedAt` nor pass the write guard, and the query filter alone keeps them inside one store. Three types issue them today — `OrderNumbers`, `NotificationRepository` (`MarkAllReadAsync`) and `OutboxProcessor` — and are listed in `TenancyRuleTests`, which fails on any other caller. The sharpest is `OrderNumbers`, whose counter increment carries **no `Where` clause at all** and depends entirely on the filter selecting exactly one row; the outbox is exempt because it has no filter by design.
- **Evidence:** `TenantIsolationTests` drives store B against store A's real ids over HTTP for every endpoint that takes a resource id (with a completeness check that fails when a new endpoint is added), and asserts that nothing in store A changed.

## 6. Money and precision

- **Storage:** `decimal(19,4)` for every monetary column (`PersistenceConventions.MoneyColumnType`).
  - 19 digits cover any realistic order.
  - 4 decimal places hold every ISO-4217 currency: most have 2; **BHD, IQD, JOD, KWD, LYD, OMR, TND have 3**; JPY, KRW, VND, CLP, ISK and others have 0 (`CurrencyInfo`).
- **Domain rule:** a `Money` value is *always* representable in its currency's minor units. The constructor rejects `12.3456 JOD`. Calculations that produce fractions (percentage discounts, and tax when it exists) must call `Money.FromCalculation(...)`, which rounds to the currency's minor unit with `MidpointRounding.AwayFromZero` — commercial rounding: 0.0005 → 0.001.
- **Why** ([ADR-0014](../11-ADR/0014-money-precision.md)): before Phase 1A, `decimal(18,2)` silently rounded `12.345 JOD` to `12.35`, and a 15% coupon produced `1.85175` in memory (sent to the payment provider) but `1.85` in the database (shown on the order). Rounding once, in the Domain, makes the in-memory total, the stored total and the charged amount agree.
- **Currency is never implicit.** `Money` has no default currency; prices, coupon thresholds and orders take the store's currency, and `Orders.Currency` is a snapshot of it.
- **Payment providers:** conversion to the provider's minor units happens in the adapter (`StripeAmountConverter`); see [Security.md](../07-SECURITY/Security.md). JOD is currently sent ×100, which is decision **P-05**, still to be confirmed against a real Stripe account.
- **Orders freeze their money at placement** (`Order.Place`): `PlacedSubtotal` and `PlacedTotal` are stored, and lines and discount can no longer change. Lists read the stored totals instead of summing. There is **no tax column**: tax is an explicit zero stage in the pricing pipeline and an open product decision (**P-06**).

## 7. Concurrency

**Optimistic concurrency** with a `rowversion` shadow column on the aggregates that are written concurrently. EF appends `WHERE RowVersion = @original` to every update and delete; a conflict becomes `ConcurrencyConflictException` in `AppDbContext.SaveChangesAsync`, and the API answers **409 `ConcurrencyConflict`** ([ADR-0013](../11-ADR/0013-optimistic-concurrency.md)).

| Row | Who races | Outcome without protection | With `RowVersion` |
|---|---|---|---|
| `InventoryItems` | Checkouts for the last unit; checkout vs payment vs an admin correction | Oversell; lost update | The second save fails; `InventoryWriter` re-reads the committed values and retries (`MaxAttempts` = 5), so the loser gets `422 InsufficientStock`, not a 409 ([ADR-0026](../11-ADR/0026-inventory-reservations.md)) |
| `Coupons` (`UsedCount`) | Checkouts taking the last use; a cancellation racing a checkout | Limit exceeded | The use is taken at checkout inside the order transaction; the loser re-reads (5 attempts) and gets `422 InvalidCoupon` if nothing is left ([ADR-0030](../11-ADR/0030-coupon-redemptions.md)) |
| `Payments` | Two refunds of one payment; a refund result racing another request | Refunds exceeding the payment | A refund reserves its amount on the payment row; the loser re-reads (5 attempts) and gets `422 RefundExceedsPayment` ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)) |
| `Orders` | Client confirmation vs Stripe webhook; admin vs payment | Double side effects | The loser re-reads: already Paid ⇒ idempotent success |
| `Products` | Two admins editing one product | Lost update | The second save fails with 409 |
| `Users` | Two sign-ins; a password change during a session | Lost lockout counter or stamp | The second save fails with 409 |
| `Tenants` | The platform owner and a store admin editing one store | Lost settings | The second save fails with 409 |

Rows **without** a token, by decision: `Baskets` and `BasketLines` (last write wins; checkout re-validates everything — [ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)), `Customers`, `Categories`, `ShippingMethods`, `Reviews`, `Notifications` and every aggregate child. A child's change is guarded only when the same save also writes its root — true for `Refunds` (they move `Payment.RefundedAmount`) and reservations (they move `InventoryItem.Reserved`), and for product options and variants, whose handlers force the `Products` UPDATE through `IProductRepository.GuardConcurrentEdit` ([ADR-0040](../11-ADR/0040-product-option-model.md)) — not for `OrderItems` or `CouponRedemptions.Confirm`.

Other serialization points that are not `rowversion`: `OrderNumberSequences` (the `ExecuteUpdate` row lock serializes one store's checkouts until commit) and `OutboxMessages` (a two-minute `LockedUntil` lease claimed with a conditional update).

**Rejected alternatives:** an atomic conditional `UPDATE … WHERE OnHand - Reserved >= @q` (best under heavy contention, but the rule leaves the aggregate and the unit of work); pessimistic locks (`UPDLOCK`: deadlock risk, and it needs raw SQL, which the architecture tests forbid outside migrations); serializable transactions (throughput cost on every request). Revisit if one SKU's contention shows up in latency.

## 8. Deletion, archiving and retention

| Use soft delete (a status) when… | Use hard delete when… | Never delete when… |
|---|---|---|
| Other records reference the row historically **and** the business may restore it: products → `Archived`, coupons after a redemption → inactive, users → `Disabled`, stores → `Archived`, reviews → `Rejected` | The row has no history value and nothing points at it: an empty category, an address, a basket or its lines, a wishlist item, a shipping method (orders keep a snapshot), a store's payment account, a tenant domain | The row **is** history: orders, order lines, status history, the stock ledger, reservations, payments, refunds, coupon redemptions, notifications and the audit log |

There is no global *IsDeleted* flag and no global filter: each aggregate has an explicit status that carries business meaning (Archived ≠ Deleted), and "delete" in the API means the aggregate's own end-of-life rule — `DELETE /api/products/{id}` archives, a delete on a used coupon answers `409 CouponInUse`, and on a non-empty category `409 CategoryInUse` / `409 CategoryHasChildren`.

**Personal data is removed by anonymizing in place.** `Customer.Erase` and `User.Erase` replace the name, email (`erased-{id}@erased.invalid`, which also frees the original address to register again) and phone, clear the address book, disable the account with an empty password hash, and rotate the security stamp; `CustomerErasure` does it in one save, revokes the refresh tokens, deletes the basket and the wishlist, and forgets the cached stamp so live access tokens die at once. Orders and reviews stay, pointing at a profile that identifies nobody ([ADR-0027](../11-ADR/0027-customer-profile-and-erasure.md)).

**Retention, honestly:**

- Only one thing is purged automatically: processed `OutboxMessages`, once an hour, older than `Notifications:RetentionDays` (14 days).
- Expired `Baskets` are swept per store (at most 500 per run, every `Basket:CleanupIntervalMinutes`).
- Nothing purges `RefreshTokens`, `StockReservations`, `Notifications`, `AuditEntries` or dead outbox rows; they grow for ever.
- The order's shipping snapshot keeps a recipient name, phone and address after erasure — lawful accounting retention, with purging of old snapshots **PLANNED** (Phase 20) and a general data-retention policy **PLANNED** (Phase 23).
- A removed product image deletes the row but leaves the file on disk; an orphaned-file cleanup job is **DEFERRED** — Phase 5 deferred it to "Phase 6 or later" and no phase schedules it (SEC-UP-06).

## 9. Audit fields, time, relationships, transactions

- **Timestamps.** `CreatedAt` / `UpdatedAt` are stamped by `AuditTimestampsInterceptor` from `TimeProvider` (never `DateTime.UtcNow`) and stored as `datetime2` in UTC. A `datetime2` read back has no kind, so the API writes every instant with a `Z` suffix (`UtcDateTimeJsonConverter`, [ApiDocumentation.md §1](../05-API/ApiDocumentation.md#1-style)); the browser converts for display, into the store's time zone when one is set (`frontend/src/app/dateLocale.js`). The setters are `internal`, visible only to Infrastructure (`InternalsVisibleTo` in `src/Souq.Domain/Souq.Domain.csproj`), so no handler can forge them. `AuditTimestampsTests` proves it on real SQL Server with a fixed clock. Two limits: only entities deriving from `Entity` get the columns (`AuditEntry` and `OutboxMessage` carry their own `OccurredAt` instead), and a bulk `ExecuteUpdateAsync` stamps nothing.
- **Actor columns** are added only where the business asks "who?": `OrderStatusHistories.ChangedBy` + `ChangedByUserId`, `Reviews.ModeratedByUserId`, `Refunds.RequestedByUserId`, `StorePaymentAccounts.UpdatedByUserId`. None of them is a foreign key, so disabling an account never breaks a record. Everything else goes to `AuditEntries`, written by `AuditBehavior` for every `IAuditable` request — staged before the handler so it commits in the handler's own transaction, discarded when the request fails, and append-only (the write guard throws on any update or delete).
- **Relationships.**
  - Delete behaviour is `Restrict` by default and `Cascade` only for aggregate children (order → lines, basket → lines, product → translations/images/variants, customer → addresses, tenant → domains).
  - Aggregate-child foreign keys are `NOT NULL` — `OrderItems.OrderId` and `OrderStatusHistories.OrderId` were nullable until Phase 1A, which allowed orphans.
  - References between tenant-owned rows are composite and tenant-scoped (§5); references to rows that may be hard-deleted are an id plus a snapshot (§4).
- **Transactions** ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)).
  - The command handler owns the boundary; each `SaveChangesAsync` is one atomic transaction. Work spanning modules in one step shares the same unit of work, so it is atomic.
  - Several saves that must land together use `IUnitOfWork.InTransactionAsync` (an explicit transaction that joins an existing one): `CreateOrderHandler`, `OrderPaymentConfirmation`, `UpdateOrderStatusHandler`, `CreateProductHandler`, `RegisterHandler` and `AccountInvitations`. ADR-0021 wrote that handlers would open no explicit transaction; this is what the code does instead, and the rule that matters survived: **no transaction is open during a network call** — save, call the provider, save, compensate in a new step.
  - Tracked aggregates are saved without `Update()`, so only changed columns are written; the method no longer exists on the repository contract.
  - Side effects happen after the commit, through the outbox: `AppDbContext.SaveChangesAsync` writes domain-event rows in the same transaction as the change and detaches them if the save fails.
  - Transient-fault retries are not enabled (no execution strategy is configured), which is why the explicit transactions are plain `BeginTransaction` — **DEFERRED** to the Phase 23 review.
- **Reads** ([ADR-0008](../11-ADR/0008-cqrs-strategy.md)): query services project with `AsNoTracking` straight into DTOs, and `QueryableExtensions.ToPageAsync` forces a page to have both an explicit ordering (with an `Id` tiebreaker, proven by `QueryServiceTests`) and an explicit projection. They are where a listing's SQL is written, and therefore where the `TenantId`-leading indexes have to pay off.

## 10. Migration workflow

1. Change the entity or the configuration.
2. `dotnet ef migrations add <PascalCaseIntent> --project src/Souq.Infrastructure --startup-project src/Souq.API`.
3. **Read the generated migration.** EF orders drops before copies; data-preserving steps (backfills, orphan clean-up, temporary defaults that are then removed) are written by hand in `Up()`, and `Down()` is kept honest.
4. Rehearse anything that moves data in `MigrationRehearsalTests`, and let the integration suite apply every migration to a fresh SQL Server container — a migration that fails there fails the build.
5. Migrations currently run at startup, in every environment (`Program.cs` → `DbSeeder.SeedAsync` → `MigrateAsync`). Production moves them to a deployment step (a migration bundle) in Phase 23 (**PLANNED**), so that several replicas cannot race.

The full workflow — commands and their prerequisites, every migration with its purpose and its data-protection notes, the rehearsal test, rollback and the seed data — is in **[Migrations.md](Migrations.md)**.

## 11. Indexes for hot queries

Every listing is filtered by store first, so with two exceptions (noted below) every index leads with `TenantId`.

| Query | Index |
|---|---|
| Host → store, on every request | `IX_TenantDomains_Host` (unique, platform-wide), plus the in-process `TenantDirectory` cache |
| Storefront catalog: active products in a category | `IX_Products_TenantId_Status_CategoryId`; texts, price and media from `IX_ProductTranslations_ProductId_Culture`, `IX_ProductVariants_ProductId_IsDefault`, `IX_ProductImages_ProductId_SortOrder` |
| Product page by slug | `IX_Products_TenantId_Slug` (unique) |
| Availability shown in listings (`OnHand − Reserved`) | `IX_InventoryItems_TenantId_ProductId` |
| Category tree | `IX_Categories_TenantId_ParentId_SortOrder` |
| Admin order list, newest first / filtered by status | `IX_Orders_TenantId_CreatedAt`, `IX_Orders_TenantId_Status_CreatedAt` |
| "My orders", and orders of one customer | `IX_Orders_TenantId_CustomerId` |
| Public tracking page, and lookup by number | `IX_Orders_TenantId_TrackingToken`, `IX_Orders_TenantId_OrderNumber` (both unique) |
| Order detail lines (and one line per variant); best-selling sort; sales per variant | `IX_OrderItems_OrderId_VariantId` (unique); `IX_OrderItems_TenantId_ProductId`; `IX_OrderItems_TenantId_VariantId` |
| Checkout expiry sweep | `IX_StockReservations_TenantId_ExpiresAt`, filtered to `[Status] = 0` so it stays small |
| An order's reservations | `IX_StockReservations_TenantId_Reference` |
| Stock ledger page; reconciliation with on hand | `IX_StockMovements_ProductId_CreatedAt`, `IX_StockMovements_InventoryItemId_CreatedAt` |
| Basket by owner; expiry sweep | `IX_Baskets_TenantId_CustomerId`, `IX_Baskets_TenantId_GuestTokenHash` (both filtered unique), `IX_Baskets_TenantId_ExpiresAt` |
| Coupon by code; per-customer use count | `IX_Coupons_TenantId_Code` (unique), `IX_CouponRedemptions_TenantId_CouponId_CustomerId_Status` |
| Payment by order, and webhook by intent id | `IX_Payments_TenantId_OrderId`, `IX_Payments_TenantId_ProviderPaymentId` (both unique) |
| Product reviews; moderation queue | `IX_Reviews_TenantId_ProductId_Status`, `IX_Reviews_TenantId_Status_CreatedAt` |
| Notification badge and inbox | `IX_Notifications_TenantId_RecipientUserId_ReadAt`, `IX_Notifications_TenantId_RecipientUserId_CreatedAt` |
| Outbox dispatch and purge | `IX_OutboxMessages_NextAttemptAt` and `IX_OutboxMessages_ProcessedAt`, both filtered so the index holds only what each job reads |
| Sign-in; password reset; email verification | `IX_Users_TenantId_NormalizedEmail`, `IX_Users_NormalizedEmail_Platform`, `IX_Users_PasswordResetTokenHash`, `IX_Users_EmailVerificationTokenHash` |
| Refresh a session; revoke a family | `IX_RefreshTokens_TokenHash` (unique), `IX_RefreshTokens_FamilyId` |
| Audit viewer | `IX_AuditEntries_OccurredAt`, `IX_AuditEntries_TenantId_OccurredAt`, `IX_AuditEntries_ActorUserId_OccurredAt` |
| Admin customer list, filtered by status | `IX_Customers_TenantId_Status` |

Known index gaps, all small today and left for the Phase 21 performance review (**PLANNED**):

- The admin customer list sorts by `CreatedAt` with no `(TenantId, CreatedAt)` index.
- Keyword search (products, customers, orders, coupons) uses `Contains`, which SQL Server translates to `CHARINDEX` — no index seek; it scans within the store.
- `IX_Reviews_CustomerId_ProductId` is the one unique index that does not lead with `TenantId`; it is still correct per store only because customer ids are unique platform-wide.
- Sorting the admin product list by price or stock runs correlated subqueries over `ProductVariants` / `InventoryItems`.

## 12. Backups and recovery

Full contract and runbook: [BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md). Stated plainly, so that nobody assumes more than is true:

- **A rehearsed procedure exists** (`scripts/backup.sh` / `restore.sh` / `rehearse-restore.sh`), and a restore has been performed onto a clean server and verified by running the application against it.
- **No backup job, no retention schedule and no off-site copy.** `docker-compose.yml` keeps the database files in the named Docker volume `souq_db_data`; deleting the volume still deletes the database, and nothing runs the backup for you.
- **No point-in-time recovery.** Full backups only, so the exposure is one backup interval. Log backups would change that and are a deliberate future step.
- **A migration's `Down()` is not a recovery mechanism**, and most of them lose data ([Migrations.md](Migrations.md) §7).
- **Manual discipline until the job is scheduled:** take a backup before applying a phase migration (the roadmap's own R4 mitigation), and rehearse the migration on a copy.

## 13. Known gaps

- **Cross-module boundary leaks in the Application layer** (handlers using another module's repository) are documented in [OwnershipMap.md](OwnershipMap.md) but not enforced by any test.
- **Tables that only grow:** `RefreshTokens`, `StockReservations`, `Notifications`, `AuditEntries`, dead `OutboxMessages`.
- **No tax model** (P-06) and therefore no tax column on orders.
- **No transient-fault retry policy** on the SQL Server provider.
- **Migrations run at application startup** in every environment, including production.
- **SQL Server Row-Level Security** as defence in depth is an optional item of the Phase 20 security review (**PLANNED**); today isolation rests on the query filter, the write guard, the composite keys and the tests.
