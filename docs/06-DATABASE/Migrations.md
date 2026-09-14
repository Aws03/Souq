# Schema migrations

> **How the schema changes in this repository, and how a change avoids destroying data.**
> **Companions:** [DatabaseDesign.md](DatabaseDesign.md) (conventions) · [OwnershipMap.md](OwnershipMap.md) (who owns which table) · [ADR-0007](../11-ADR/0007-database-strategy.md) (why EF migrations are the only source of truth).

## 1. How schema change works here

- **Code-first, EF Core 10 on SQL Server 2022.** The C# model (`src/Souq.Domain` entities + `src/Souq.Infrastructure/Persistence/Configurations/`) is edited first; `dotnet ef migrations add` then generates the migration. There are no hand-written schema scripts in the repository — the old *database/\*.sql* folder was deleted in Phase 1A.
- **Where they live:** `src/Souq.Infrastructure/Migrations/`, namespace `Souq.Infrastructure.Migrations` — beside `src/Souq.Infrastructure/Persistence/`, not inside it. Each migration is three committed files:
  - *timestamp*_*Name*.cs — `Up()` and `Down()`; this is the file you read and edit;
  - *timestamp*_*Name*.Designer.cs — the model snapshot at that point, generated;
  - and the shared `AppDbContextModelSnapshot.cs` — the model after the **latest** migration. EF diffs against it, so it must be committed with every migration and is the file that conflicts when two branches add migrations.
- **Names** are PascalCase statements of intent. Since Phase 1A they carry the phase: `Phase1AIntegrityPrecisionConcurrency`, `Phase2MultiTenancy` … `Phase14Notifications`.
- **The generated code is a draft.** Where data has to move, `Up()` is rewritten by hand — EF orders column drops before copies, which would have destroyed passwords (Phase 3), names and prices (Phase 5) and stock (Phase 6). See §4.
- **Raw SQL is allowed only inside migrations.** `TenancyRuleTests` scans the Infrastructure assembly and fails on `FromSql*`/`ExecuteSql*` outside `Souq.Infrastructure.Migrations`, because raw SQL does not pass through the tenant query filter. Inside a migration, you are responsible for the `TenantId` predicates yourself.

## 2. When migrations are applied

**At application startup, in every environment.** `src/Souq.API/Program.cs` calls `DbSeeder.SeedAsync` before the request pipeline is built, and the first thing that does is `db.Database.MigrateAsync()`. There is no environment check around it:

| Where | What happens |
|---|---|
| Local development (`dotnet run --project src/Souq.API`) | Pending migrations are applied to the developer database, then the seed runs |
| Integration tests | `SouqApiFactory` starts the real API against SQL Server 2022 in Testcontainers with environment `Testing`; the first `CreateClient()` applies **every** migration to a fresh database. A migration that fails there fails the test run |
| `docker-compose.yml` | The `api` service waits for the `db` health check, then migrates on startup, exactly like development (the file says so) |
| Production | The same path. Migrations are applied by whichever instance starts |

Consequences to know:

- A failing migration **fails startup**: the exception propagates out of `SeedAsync` and the API never listens. That is deliberate — a half-migrated schema serving requests is worse.
- EF applies each migration in its own transaction, so a migration either lands completely or not at all; earlier migrations stay applied.
- Startup migration and several replicas do not mix well; that is why moving migrations to a **deployment step (a migration bundle)** is **PLANNED** for Phase 23 ([ADR-0007](../11-ADR/0007-database-strategy.md), roadmap Phase 23). Recent EF versions take a database-wide lock while migrating, which reduces the race, but this has not been verified in this repository.
- Long backfills run while the application is starting; the Phase 9 and Phase 11 backfills rewrite every order row of every store in one statement. On today's data volumes that is instant; on a large database it is a startup outage.
- EF also refuses to migrate when the model has changes that no migration captures (a forgotten `migrations add`), so the integration suite catches that class of mistake — EF behaviour, not verified by a dedicated test here.

## 3. Commands

All commands run from the repository root. The EF Core CLI is not pinned by a local tool manifest (the repository has none), so install it globally. `Microsoft.EntityFrameworkCore.Design` is referenced by both `src/Souq.Infrastructure/Souq.Infrastructure.csproj` and `src/Souq.API/Souq.API.csproj`.

```bash
# add a migration (after editing entities or configurations)
dotnet ef migrations add <PascalCaseIntent> --project src/Souq.Infrastructure --startup-project src/Souq.API

# review what it will do to a database, as SQL a DBA can read; --idempotent guards every step
dotnet ef migrations script --idempotent --project src/Souq.Infrastructure --startup-project src/Souq.API --output schema.sql

# only the delta between two migrations
dotnet ef migrations script Phase13ReviewsWishlist Phase14Notifications --project src/Souq.Infrastructure --startup-project src/Souq.API

# what is applied where
dotnet ef migrations list --project src/Souq.Infrastructure --startup-project src/Souq.API

# apply or roll back to a target by hand (normally unnecessary: the API migrates at startup)
dotnet ef database update Phase13ReviewsWishlist --project src/Souq.Infrastructure --startup-project src/Souq.API

# undo the last migration you just generated
dotnet ef migrations remove --project src/Souq.Infrastructure --startup-project src/Souq.API
```

**Tooling prerequisites.** The tools build the host from `src/Souq.API/Program.cs`; there is no design-time `DbContext` factory in the repository. That means the startup path has to succeed: `AddInfrastructure` throws when `ConnectionStrings:Default` is missing, and outside Development/Testing the payment and email registrations refuse to start without a configured provider. In practice: run the tools with the Development environment and the connection string in user-secrets (inferred from `src/Souq.Infrastructure/DependencyInjection.cs`; the tools are not exercised by any test).

**`migrations remove` caveats.**

- It deletes the last migration's files and rewrites `AppDbContextModelSnapshot.cs`. **Any hand-written SQL in that migration is lost** — copy it out first.
- If the migration has already been applied to the database the tool is pointed at, it reverts it (`Down()`) before removing; with a lossy `Down()` that destroys data in that database.
- Never remove a migration that has reached `main` or any shared database. Fix it forward with a new migration instead.

## 4. The migrations, in order

Twenty migrations. "Data" says what happens to existing rows.

| # | Migration | Purpose | Data |
|---|---|---|---|
| 1 | `InitialCreate` | Categories, Customers (with `PasswordHash` and `Role` on the profile), Products (name, description, `decimal(18,2)` price, stock, image url, `IsActive`), Orders, OrderItems; global unique `Slug` and `Email` | New tables |
| 2 | `AddCouponsReviewsOrderPayment` | Coupons and Reviews; Orders gain `CouponCode`, discount and `PaymentIntentId`; unique review per (customer, product) | Additive |
| 3 | `AddProductBilingualNames` | `Products.Name` → `NameAr`, plus `NameEn` | **Hand-written:** the rename is forced to `NameAr` (EF's alphabetical guess was `NameEn`), and `UPDATE [Products] SET [NameEn] = [NameAr] WHERE [NameEn] = ''` mirrors the entity's fallback so old products do not show an empty English name |
| 4 | `AddProductVideoUrl` | `Products.VideoUrl`, and — despite the name — the customer password-reset token columns; the file says why | Additive, nullable columns only |
| 5 | `AddInventoryTracking` | `Products.LowStockThreshold` and the `StockMovements` ledger | Default `5` matches the Domain default so existing products get a sensible threshold instead of "never warn" |
| 6 | `AddOrderTracking` | Order shipment fields and `OrderStatusHistories` | Additive |
| 7 | `Phase1AIntegrityPrecisionConcurrency` | Money → `decimal(19,4)`; `RowVersion` on Products, Orders, Coupons; `Categories.ParentId` self-FK; aggregate-child `OrderId` made `NOT NULL`; reset token stored hashed | **Hand-written clean-up first:** plaintext reset tokens are nulled (they can never match a hash again), orphaned `OrderItems`/`OrderStatusHistories` with `OrderId IS NULL` are deleted (unreachable rows that would block the `NOT NULL` change), and categories pointing at a missing parent become roots before the FK is added |
| 8 | `Phase2MultiTenancy` | `Tenants` and `TenantDomains`; `TenantId` on nine tables; per-store uniqueness; tenant-scoped composite foreign keys | **Hand-written backfill.** See §4.1 |
| 9 | `Phase3Identity` | `Users` and `RefreshTokens`; credentials move off `Customers` | **Hand-written, order rewritten.** See §4.2 |
| 10 | `Phase4PlatformAdministration` | `Tenants.EnabledModules` and `Tenants.Settings`; `AuditEntries` | Additive. `EnabledModules` defaults to `promotions,reviews,wishlist`, so an existing store keeps every module |
| 11 | `Phase5Catalog` | Translations, variants and image gallery; product status and slug | **Hand-written, order rewritten.** See §4.3 |
| 12 | `Phase6Inventory` | `InventoryItems`, `StockReservations`, `StockMovements.InventoryItemId` | **Hand-written, order rewritten.** See §4.4 |
| 13 | `Phase7Customers` | Customer phone, status, `BlockedAt`/`ErasedAt`, and `CustomerAddresses` | Additive. `Status` defaults to `0` (Active), so every existing customer stays active. Existing orders keep their typed address |
| 14 | `Phase8Basket` | `Baskets` and `BasketLines` with the owner and quantity check constraints and the filtered unique owner indexes | Additive; there was nothing to migrate — the cart lived in the browser |
| 15 | `Phase9Orders` | Order number, public tracking token, billing snapshot, frozen totals, history actor, `OrderNumberSequences` | **Hand-written backfill before the unique indexes.** See §4.5 |
| 16 | `Phase10Coupons` | `StartsAt`, `MaxUsesPerCustomer`, `AK_Coupons_TenantId_Id`, and `CouponRedemptions` | **Hand-written backfill.** See §4.6 |
| 17 | `Phase11Payments` | `Payments`, `Refunds`, `StorePaymentAccounts` | **Hand-written backfill after the unique indexes.** See §4.7 |
| 18 | `Phase12Shipping` | `ShippingMethods` and the order's shipping snapshot columns | Additive. `ShippingAmount` defaults to `0`, the other columns stay null, so existing totals are unchanged |
| 19 | `Phase13ReviewsWishlist` | Review moderation, `Tenant.ReviewsAutoApprove`, `WishlistItems` | **Hand-written backfill:** `UPDATE [Reviews] SET [Status] = 1` (every existing review was public, so it is Approved — with no moderator and no decision time, because it was a policy, not a decision) and `UPDATE [Tenants] SET [ReviewsAutoApprove] = 1` (existing stores published at once and keep doing so; stores created afterwards start moderated). It also swaps `IX_Reviews_TenantId_ProductId` for the status-carrying indexes |
| 20 | `Phase14Notifications` | `Notifications` and `OutboxMessages` with their filtered indexes | Additive, no existing data touched |

### 4.1 `Phase2MultiTenancy` — the tenant backfill

Additive and non-destructive: no row and no data column is dropped.

1. Drops the pre-tenant foreign keys and global unique indexes that the composite ones replace.
2. Creates `Tenants` and `TenantDomains`, then inserts the default store with the fixed id 1 ("Marka Demo" / `marka`, JOD, `ar`, Asia/Amman) using `SET IDENTITY_INSERT`. Every pre-existing row belongs to it (decision P-04).
3. Adds `TenantId` to the nine tenant-owned tables **with `defaultValue: 1`**, which backfills every existing row atomically inside the migration's transaction, and then **drops the default constraint** through the `DropDefaultConstraint` helper (a `sys.default_constraints` lookup, because SQL Server names the constraint). From then on an insert without an explicit store fails loudly instead of landing silently in store 1.
4. `Orders.Currency` is added with a `JOD` default, then backfilled from each order's first line (`CROSS APPLY … TOP 1`), and its default constraint is dropped too.
5. Per-store uniqueness (`(TenantId, Slug)`, `(TenantId, Code)`, `(TenantId, Email)`) replaces the global indexes. These are **looser** than what they replace, so they cannot fail on existing data.
6. Alternate keys `AK_<Table>_TenantId_Id` on Categories, Products, Customers and Orders, then composite `(TenantId, XId) → (TenantId, Id)` foreign keys with `TenantId`-leading indexes. Every existing reference is valid by definition, since all data belongs to one store.

`Down()` restores the global indexes and drops `TenantId`; it is only safe while all data still belongs to one store, because the global unique indexes would collide otherwise.

### 4.2 `Phase3Identity` — credentials move without being lost

1. Creates `Users` and `RefreshTokens`.
2. **Copies before dropping:** every `Customers` row becomes a `Users` row with the **same id** (`SET IDENTITY_INSERT`), the same store, email, name and BCrypt hash, so every password keeps working. Role `Admin` → `TenantAdmin`, anything else → `Customer`. Each account gets a fresh security stamp, so tokens issued before the phase (which carry no `sstamp`) stop working.
3. `Customers.UserId` is added with a temporary default, set to `Id`, and the default constraint is dropped.
4. **Only then** are `PasswordHash`, `Role` and the reset-token columns dropped from `Customers`. EF generated the drops first, which would have lost every password; the order was rewritten by hand.

`Down()` copies credentials back into `Customers`. Accounts with no purchase profile (staff, the platform owner) have nowhere to go and are lost — development only.

### 4.3 `Phase5Catalog` — texts, prices and media move to child tables

Same shape: add the new columns and child tables, copy in SQL, drop the old columns last, create the unique indexes after the values exist.

The copy: an Arabic translation from `NameAr` + `Description`; an English one from `NameEn` when it is present and different; a default variant from `Price`/`Currency`; an image row **only** for real `/uploads/` or `http(s)` URLs, so the old placeholder keys are dropped instead of becoming broken images; `Status` from `IsActive`; a temporary unique slug `p-{Id}`; and each category's name as a translation in its store's default culture. The temporary column defaults are then dropped (`DropDefault`), so a new row must set a slug and a status explicitly.

`Down()` rebuilds the old columns from the Arabic/English translations, the default variant and the first image. It is lossy by nature (other languages, extra images, SKUs, compare-at prices, and Draft ≠ Archived) — development only.

### 4.4 `Phase6Inventory` — stock moves off the product

1. Create `InventoryItems` and `StockReservations`; add `StockMovements.InventoryItemId` as **nullable** first.
2. Copy, in one SQL block:
   - one item per default variant, where `OnHand` = the old `StockQuantity` **plus** the quantities held by Pending orders, because the old checkout had already decremented them;
   - Active reservations for Pending orders (a fresh 30-minute window, after which the sweeper settles them) and Committed reservations for Paid orders that have not shipped, so cancelling them still restocks;
   - every historical movement attached to its item;
   - an `Adjustment` opening balance wherever the ledger did not add up to on hand, so Σ movements = `OnHand` from that point on.
3. Make `InventoryItemId` `NOT NULL`, add the composite FK and the indexes.
4. **Only then** drop `Products.StockQuantity` and `Products.LowStockThreshold`.

`Down()` writes the available quantity (`OnHand − Reserved`) back to `Products`; reservations have no home in the old schema — development only.

### 4.5 `Phase9Orders` — numbers and tokens for existing orders

Additive columns first, then the backfill **before** the unique indexes are created:

- numbers from 1001 per store, in creation order (`ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY CreatedAt, Id)`);
- a random tracking token per order from `NEWID()` (122 random bits — enough for a link that reveals only status and shipment);
- billing address = shipping address (the only snapshot that exists);
- placement at creation time, with `PlacedSubtotal`/`PlacedTotal` computed from the lines and the discount exactly as `Order` computes them;
- each store's counter set to its highest number, so the next real order cannot collide.

The temporary defaults on `OrderNumber`, `TrackingToken` and `BillingAddress` are then dropped with dynamic SQL. Nothing is deleted or rewritten. `Down()` drops the columns and the sequence table: numbers and tokens are lost — development only.

### 4.6 `Phase10Coupons` — redemptions reconstructed from orders

Every existing order that used a coupon **that still exists** gets a redemption row: Pending → Reserved; Paid, Shipped or Delivered → Confirmed; Cancelled → none. The join is on `(TenantId, Code)`, which is unique per store, so no order is duplicated. Coupons deleted earlier (deletion used to be unconditional) leave their orders without a record.

Counters keep their old value and **gain the pending orders**, because the old model counted a use at payment and the new one counts it at checkout; without that, paying a pending order after the upgrade would never be counted. A count that is too high is the safe error. `Down()` subtracts the reserved uses again before dropping the table.

### 4.7 `Phase11Payments` — a payment row per intent

Every order with a `PaymentIntentId` gets a `Payment`: the fake gateway for `pi_fake_%` intents, otherwise the deployment Stripe account (no store accounts existed before this phase); the order's frozen total and currency; status Pending → Pending, Paid/Shipped/Delivered → Succeeded, and Cancelled → Succeeded **if its status history shows it was paid first** (so an admin can refund money that was kept), else Cancelled.

The insert deliberately runs **after** the unique indexes are created, so a duplicated intent across two orders stops the migration instead of being hidden. `Down()` drops the three tables, including store accounts with their encrypted keys — development only.

## 5. The rehearsal test

`tests/Souq.IntegrationTests/MigrationRehearsalTests.cs` is the safety net for every migration that moves data (risk R4 in the roadmap).

**What it does.** On the same SQL Server container the integration suite uses, it creates a separate database, migrates it to `LastPhase1Migration` (`Phase1AIntegrityPrecisionConcurrency`) through `IMigrator`, inserts rows **in the old shape with raw SQL** (a category, two customers including one with the legacy `Admin` role and a real BCrypt hash, a KWD product with a legacy image key, four orders covering Delivered / paid-then-Cancelled / Pending with a fake intent / Paid, a coupon with `UsedCount = 1`, a stock movement and a review), then migrates to the latest migration and reads the result back both in SQL and through EF.

**What it proves.**

- No row is lost: every tenant-owned table keeps its count (`StockMovements` gains exactly one opening-balance row), and every row belongs to store 1.
- `Orders.Currency` came from the lines (KWD) and fell back to JOD for an order without lines.
- No default constraint is left on any `TenantId` or on `Orders.Currency`, and an insert without a store really fails.
- Phase 3: one account per customer with the same id, store and email; the **old password still verifies**; `Admin` became `TenantAdmin`; no credential column remains on `Customers`.
- Phase 5: translations, default variant with price and currency, the placeholder image dropped, status and slug set, category translation written, old columns gone.
- Phase 6: on hand / reserved / threshold are `7/2/5` for the legacy product, an Active reservation for the Pending order and a Committed one for the Paid order, Σ ledger = on hand, no null `InventoryItemId`, old product columns gone.
- Phase 9: numbers 1001…, unique 32-character hex tokens, billing = shipping, frozen totals matching the lines and discount, and each store's counter at its highest number.
- Phase 10: redemption status and discount per order, and the adjusted `UsedCount`.
- Phase 11: one payment per intent with the right gateway, status, amount and currency, and no refunds.
- Phase 12: legacy orders still have zero shipping and no method.
- The tenant filter works on migrated data: store 1 sees its rows through EF (aggregate, price, availability), store 999 sees none.

**What it does not prove.**

- `Down()` is never executed by any test.
- Phase 4, 7, 8, 13 and 14 are only exercised as "the migration runs"; the Phase 13 review/auto-approve backfill is not asserted on legacy rows (the default store's publish-at-once behaviour is covered indirectly by `QueryServiceTests`).
- Nothing about volume: every backfill is tested on a handful of rows, so locking and duration on a real database are unknown.

## 6. Rules for a safe migration

1. **Additive first.** Add columns and tables, backfill, and only then tighten (`NOT NULL`, unique indexes) or drop. Phases 4, 7, 8, 12 and 14 are pure additions and needed no special care.
2. **Read the generated code before anything else.** EF emits drops before inserts. Three migrations here (`Phase3Identity`, `Phase5Catalog`, `Phase6Inventory`) would have destroyed passwords, names and prices, or stock, in the order EF produced.
3. **Copy before you drop**, in the same migration, with `migrationBuilder.Sql`. A separate "clean-up later" migration is a window in which the data is already gone.
4. **Order matters around indexes.** Backfill *before* creating a unique index when you generate the values (Phase 9), and *after* when you want an existing duplicate to abort the migration instead of hiding (Phase 11).
5. **Temporary defaults, then drop them.** A `NOT NULL` column added to a populated table needs a default; leaving it behind turns "the developer forgot" into a silent wrong value. Use the `sys.default_constraints` helper the existing migrations carry.
6. **Loosening uniqueness is safe; tightening is not.** Check the data first, in SQL, on a copy.
7. **Raw SQL inside a migration has no tenant filter.** Join on `TenantId` explicitly (as `Phase10Coupons` does on `(TenantId, Code)`).
8. **Keep the schema's tenancy contract:** a new tenant-owned entity implements `ITenantOwned` (the `TenantId` column, the `Tenants` FK and the query filter appear by reflection), and a reference to another tenant-owned row is a composite `(TenantId, XId) → (TenantId, Id)` key. `TenancyRuleTests` fails otherwise.
9. **Rehearse it.** Extend `MigrationRehearsalTests` with rows in the *previous* shape and assert what the new schema should contain. A data migration with no rehearsal is a guess.
10. **Test against a copy of real data** before a production run, and take a backup first — the roadmap's own mitigation for R4. Nothing automated does either today (§7).
11. **Keep `Down()` honest.** Restore what can be restored, and say in a comment what cannot.
12. **Generate the SQL for review:** `dotnet ef migrations script --idempotent`.
13. **Never edit or remove a migration that has been applied anywhere shared.** Fix forward.

## 7. Rollback

- **Every migration has a `Down()`**, and none of them is exercised by a test.
- **Most `Down()`s lose data**, and the file says so: `Phase3Identity` (accounts without a purchase profile), `Phase5Catalog` (extra languages, extra images, SKUs, compare-at prices, Draft vs Archived), `Phase6Inventory` (reservations), `Phase7Customers` (address book, statuses), `Phase9Orders` (numbers, tokens), `Phase11Payments` (payments, refunds, encrypted store keys), `Phase12Shipping`, `Phase13ReviewsWishlist`, `Phase14Notifications` (outbox and notifications). `Phase1AIntegrityPrecisionConcurrency` narrows money back to `decimal(18,2)`, which cannot keep a three-decimal JOD amount. `Phase2MultiTenancy`'s `Down()` is only safe while all data belongs to one store.
- **Treat `Down()` as a development convenience, not a production rollback plan.** The production answer is: restore a backup, or roll forward with a corrective migration.
- Rolling back the application image does **not** roll back the schema: the previous build starts, finds nothing pending, and runs against the newer schema.
- To step back locally: `dotnet ef database update <PreviousMigration>` runs the `Down()`s between the two points; `dotnet ef migrations script <From> <To>` shows exactly what that would execute.

## 8. Seed data

`src/Souq.Infrastructure/Persistence/DbSeeder.cs`, called from `Program.cs` at every startup, in every environment, immediately after `MigrateAsync`. It is written to be idempotent: each step checks before it writes.

**Two of the five steps are demo content and are environment-gated** (R-17, fixed in Phase 17). `DbSeeder.ShouldSeedDemoData` resolves `Seed:DemoData` if it is set, and otherwise follows the environment — Development and Testing only. When it is off, the seeder logs `Demo data not seeded (Seed:DemoData is off for this environment)` and a production database gets only what was configured deliberately: the platform owner, the first store administrator and the host binding.

| Step | What it seeds | Where it applies | Idempotency |
|---|---|---|---|
| `BindDefaultTenantHostsAsync` | Binds the hosts in `Seed:DefaultTenantHosts` to the default store `marka` (the Docker stack passes `localhost`) | Any environment where the setting is present | Skips a host already bound **to any store** (it never steals another store's domain); an invalid host is logged and ignored |
| `ApplyDefaultStoreLookAsync` | Gives the default store the "Marka" look: enabled cultures, brand colours, typography, storefront texts, contact and SEO — through the Domain's own rules | **Only when `Seed:DemoData` resolves true** (Development and Testing, unless set explicitly) | Runs **only while `Tenant.HasCustomSettings` is false**; once an admin saves settings, the seeder never touches them again |
| `SeedPlatformOwnerAsync` | The platform owner account (`PlatformOwner`, no store), in platform scope | Any environment, from `Seed:PlatformOwnerEmail` / `Seed:PlatformOwnerPassword`; the `DevelopmentPlatformOwnerEmail` / `DevelopmentPlatformOwnerPassword` constants apply **only** in Development | An existing account is never given a new password; only a legacy non-BCrypt hash is replaced (`UpgradePasswordHash`) |
| `SeedCatalogAsync` | Three categories and eight demo products in the store's currency, plus an `InventoryItem` and an opening `Purchase` movement for each | The default store, **only when `Seed:DemoData` resolves true** | Skipped entirely if the default store already has any category |
| `SeedAccountAsync` (store admin) | The first `TenantAdmin` of the default store | Any environment, from `Seed:AdminEmail` / `Seed:AdminPassword`; `DevelopmentAdminEmail` / `DevelopmentAdminPassword` **only** in Development | Same rule as the platform owner |

Two safety rules worth knowing before a production deployment:

- **No account is created without explicit configuration outside Development**, and a configured password shorter than `MinimumAdminPasswordLength` (12) — or equal to the development password — **throws at startup**. A loud failure beats a privileged account with a weak password (the Phase 0 finding B1: an admin with a published password used to be seeded in every environment).
- **The default store row exists in every database, but its contents no longer do.** `Phase2MultiTenancy` creates tenant 1 "Marka Demo" in every database, because a migration cannot be environment-gated and every pre-tenant row had to belong to something (decision P-04). Since Phase 17 the seeder only gives it the Marka look and the demo catalogue when `Seed:DemoData` resolves true, so a production database starts with an **empty** default store rather than a furnished demo one. It is reachable only from a host bound to it. Treat the row as something to adopt, rename or archive deliberately when onboarding a real first client.

## 9. Known gaps

- **Backups are not scheduled.** A rehearsed backup/restore procedure now exists ([BackupAndRestore.md](../09-OPERATIONS/BackupAndRestore.md)), so §6 rule 10 has a command to run — but nothing runs it for you, and nothing copies it off the host.
- **No migration bundle and no deployment step** — migrations still run at startup (Phase 23).
- **No CI check** that a generated migration matches the model, and no test exercises `Down()`.
- **No batching for large backfills.** Every data migration here is a single statement per table inside the migration's transaction.
- **Transient-fault retries are off:** the SQL Server provider is registered without an execution strategy, so a dropped connection during a migration (or during any request) is not retried. Deliberately **DEFERRED** to the Phase 23 review ([ADR-0021](../11-ADR/0021-transaction-boundaries.md)), because the retry strategy depends on the deployment target.
