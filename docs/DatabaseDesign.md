# Souq: Database Design

> **Status:** Principles adopted 2026-09-11 ([ADR-0007](adr/0007-database-strategy.md)). The §9 changes are applied in Phase 1A; everything else is applied per phase.
> **Engine:** SQL Server 2022 · **Access:** EF Core 10, code-first migrations (the **only** source of truth for the schema).

## 1. Principles

1. **Migrations are the schema.** No hand-written schema scripts live in the repository. For a DBA review, generate SQL with `dotnet ef migrations script --idempotent`.
2. **One `DbContext`, module-owned configurations.** Each module owns its tables. Configurations live under the module's folder, and a module's code reads and writes only its own tables (Reporting is the documented read-only exception).
3. **Every constraint the business relies on is also a database constraint.** This covers uniqueness, required relationships, and precision. Code checks give friendly messages; the database is the last line of defence against races.
4. **Explicit over clever.** Fluent configuration only, no data annotations on Domain entities.

## 2. Naming

| Item | Convention | Example |
|---|---|---|
| Table | Plural PascalCase, in `dbo` for now | `Products`, `OrderItems` |
| Column | PascalCase | `StockQuantity` |
| Primary key | `Id` | |
| Foreign key | `<Entity>Id` | `CategoryId` |
| Index | EF default `IX_<Table>_<Cols>` | `IX_Products_CategoryId` |
| Unique index | `IX_…` with `IsUnique()` | `IX_Categories_Slug` |
| Concurrency column | `RowVersion` (`rowversion`) | |
| Money | `<Name>` amount + `<Name>Currency` (or `Currency` for one-currency rows) | `Price`, `Currency` |

Module schemas (`catalog.Products`) were considered and **postponed**. The ownership table in §4 gives the same clarity without a migration of every table. Revisit when a module is extracted or per-module database permissions are needed.

## 3. Keys and identifiers

- **Primary keys:** `int IDENTITY`, the clustered index ([ADR-0007](adr/0007-database-strategy.md)). This means no churn and compact indexes.
- **Tenant key:** `TenantId int NOT NULL` + FK to `Tenants` (Phase 2). This is the only cross-module FK, and it is part of the shared kernel.
- **Public identifiers** (never expose a guessable id where access is anonymous):
  - orders: a per-tenant `OrderNumber` for humans, plus a random `PublicTrackingToken` for anonymous tracking (Phase 9);
  - catalog: slugs in URLs (Phase 5).
- **Merging tenants' data is not supported,** which is why `int` identity keys are acceptable.

## 4. Data ownership

**Ownership kinds:**
- **Platform:** global, no tenant.
- **Tenant:** owned by one store.
- **User:** owned by a person *inside* a tenant.
- **Reference:** global static lists such as currencies and countries.

**Delete strategies:**
- **Hard:** physically deleted.
- **Soft:** a status or archived flag.
- **Never:** append-only.

| Entity | Module | Ownership | `TenantId` | Uniqueness | Key indexes | Delete | Audit | Concurrency |
|---|---|---|---|---|---|---|---|---|
| Tenant | Platform | Platform | — (it *is* the tenant) | `Slug` | — | Soft (Archived) | Created/Updated + AuditLog | `rowversion` |
| TenantDomain | Platform | Platform (per tenant) | FK column | `Host` globally | `(TenantId)` | Hard | AuditLog | — |
| Tenant settings / branding | Platform | Tenant | on the tenant row (JSON document `Tenants.Settings`, Phase 4) | — | — | With tenant | AuditEntries | the tenant's `rowversion` |
| Module flags | Platform | Tenant | on the tenant row (`Tenants.EnabledModules`, Phase 4) | — | — | With tenant | AuditEntries | the tenant's `rowversion` |
| User | Identity | Tenant **or** Platform (`TenantId NULL`) | nullable | filtered: `(TenantId, NormalizedEmail) WHERE TenantId IS NOT NULL`; `(NormalizedEmail) WHERE TenantId IS NULL` | reset and verification token hashes | Soft (Disabled) | Created/Updated/LastLogin | `rowversion` |
| Role / Permission | Identity | Reference (in code; one `Role` column per user) | — | — | — | — | — | — |
| RefreshToken | Identity | User (store or platform) | nullable (copied from the user) | `TokenHash` | `(UserId)`, `(FamilyId)` | Hard (an expired-token purge job is planned) | Created/Used/Revoked + reason | — |
| Customer | Customers | User (in tenant) | ✓ | `(TenantId, UserId)` | `(TenantId, CreatedAt)`, `(TenantId, Status)` (Phase 7) | Soft (Blocked); anonymized on erasure (Phase 7) | Created/Updated | `rowversion` |
| CustomerAddress (Phase 7) | Customers | User | ✓ | — | `(CustomerId)`, `(TenantId)` | Hard; part of the customer aggregate (orders keep single-line snapshots) | Created/Updated | — (changed through its customer) |
| Category | Catalog | Tenant | ✓ | `(TenantId, Slug)` | `(TenantId, ParentId, SortOrder)` | Hard, only when empty (no products or children) | Created/Updated | — |
| Product | Catalog | Tenant | ✓ | `(TenantId, Slug)` | `(TenantId, Status, CategoryId)` | **Soft (Archived)**, since orders reference it | Created/Updated | `rowversion` |
| ProductVariant (Phase 5: exactly one default per product) | Catalog | Tenant | ✓ | `(TenantId, Sku) WHERE Sku IS NOT NULL`; `(ProductId) WHERE IsDefault = 1` | `(ProductId, IsDefault)`; AK `(TenantId, Id)` for later cart/order references | With the product (which is archived, never deleted) | Created/Updated | — (admin edits, last write wins) |
| ProductImage | Catalog | Tenant | ✓ | — (at most 10 per product, Domain) | `(ProductId, SortOrder)` | Hard; the file stays until a cleanup job exists | Created | — |
| ProductTranslation / CategoryTranslation (Phase 5) | Catalog | Tenant | ✓ | `(ProductId, Culture)` / `(CategoryId, Culture)` | `(TenantId)` | With the owner (replaced as a set) | Created/Updated | — |
| InventoryItem (Phase 6) | Inventory | Tenant | ✓ | `(TenantId, VariantId)` | `(TenantId, ProductId)`; AK `(TenantId, Id)` | Never (products are archived) | Created/Updated | **`rowversion` (hot)** + checks `0 ≤ Reserved ≤ OnHand` |
| StockReservation (Phase 6) | Inventory | Tenant | ✓ | — | `(TenantId, Reference)`; `(TenantId, ExpiresAt) WHERE Status = Active` | **Never** (closed with a status: Committed, Released, Expired, Restocked) | Created/ClosedAt | via its item |
| StockMovement | Inventory | Tenant | ✓ | — | `(ProductId, CreatedAt)`, `(InventoryItemId, CreatedAt)` | **Never** (ledger; Σ = on hand) | Created (+ user later) | — |
| Basket / BasketLine (Phase 8) | Shopping | Customer or guest token | ✓ | `(TenantId, CustomerId)` and `(TenantId, GuestTokenHash)`, both filtered; `(BasketId, VariantId)` | `(TenantId, ExpiresAt)` | Hard (sliding expiry; erasure) | Created/Updated | last-write-wins (no `rowversion`) + checks: exactly one owner, quantity 1–99 |
| WishlistItem | Shopping | User | ✓ | `(TenantId, CustomerId, ProductId)` | — | Hard | Created | — |
| Order | Ordering | User (in tenant) | ✓ | `(TenantId, OrderNumber)`; `PublicTrackingToken` | `(TenantId, CreatedAt DESC)`, `(TenantId, CustomerId)`, `(TenantId, Status)` | **Never** (financial record; cancelled ≠ deleted) | Created/Updated + status history | **`rowversion`** |
| OrderItem | Ordering | User | ✓ | — | `(OrderId)` | Cascade with order (never deleted in practice) | Created | via Order |
| OrderStatusHistory | Ordering | User | ✓ | — | `(OrderId)` | **Never** | Created (+ actor) | — |
| Payment / Refund | Payments | Tenant | ✓ | `ProviderPaymentId` | `(TenantId, OrderId)` | **Never** | Created/Updated | `rowversion` |
| Coupon | Promotions | Tenant | ✓ | `(TenantId, Code)` | `(TenantId, IsActive)` | Soft (Inactive) once redeemed; hard before | Created/Updated | **`rowversion`** |
| CouponRedemption | Promotions | Tenant | ✓ | `(CouponId, OrderId)` | `(TenantId, CustomerId)` | Never | Created | — |
| ShippingMethod | Shipping | Tenant | ✓ | `(TenantId, Code)` | — | Soft (Inactive) | Created/Updated | — |
| Review | Reviews | User | ✓ | `(TenantId, CustomerId, ProductId)` | `(TenantId, ProductId, Status)` | Soft (Rejected/Hidden) | Created/Moderated | — |
| AuditEntries (Phase 4) | building block | Tenant or Platform | nullable, no FK, no filter | — | `(OccurredAt)`, `(TenantId, OccurredAt)`, `(ActorUserId, OccurredAt)` | **Never** (append-only guard; a retention policy later) | — | — |
| OutboxMessage | building block | Tenant or Platform | nullable | — | `(ProcessedAt, OccurredAt)` | Hard after processing + retention | — | — |
| Currency / Country | shared kernel | Reference | — | ISO code | — | — | — | — |

## 5. Money and precision

- **Storage:** `decimal(19,4)` for every monetary column.
  - 19 digits of precision covers any realistic order.
  - 4 decimal places holds every ISO-4217 currency. Most use 2; **JOD, KWD, BHD, OMR, TND use 3**; a few use 4.
- **Domain rule:** a `Money` value is *always* representable in its currency's minor units.
  - The constructor rejects `12.3456 JOD`.
  - Calculations that produce fractions (percentage discounts, tax) must call `Money.FromCalculation(...)`. It rounds to the currency's minor unit with `MidpointRounding.AwayFromZero`, i.e. commercial rounding: 0.0005 → 0.001.
- **Why:**
  - Before Phase 1A, `decimal(18,2)` silently rounded `12.345 JOD` to `12.35` when saving.
  - Worse, a 15% coupon produced `1.85175` in memory (sent to the payment provider) and `1.85` in the database (shown on the order).
  - Rounding in one place, in the Domain, makes the in-memory total, the stored total, and the charged amount agree.
- **Payment providers:** conversion to the provider's minor units happens in the adapter (see [Security.md §8](Security.md#8-payments)).
- **Orders snapshot their totals** (subtotal, discount, shipping, tax, grand total) at placement (Phase 9). Reports must never recompute history from current prices.

## 6. Concurrency

**Optimistic concurrency** with a `rowversion` column on aggregates that are updated concurrently. A conflict raises `ConcurrencyConflictException` (Application), and the API returns **409 Conflict**.

| Row | Who races | Outcome without protection | With `rowversion` |
|---|---|---|---|
| `InventoryItems` (Phase 6) | Checkouts for the last unit; checkout vs payment vs admin correction | Oversell; lost update | Second save fails; the inventory writer re-reads the committed values and retries (up to 5 attempts), so the loser gets `422 InsufficientStock`, not a 409 ([ADR-0026](adr/0026-inventory-reservations.md)) |
| `Coupons.UsedCount` | Two payments confirmed at once | Lost increment | Second save fails → retried by the client or webhook |
| `Orders.Status` | Client confirmation vs Stripe webhook; admin vs payment | Double side effects | Loser re-reads: already Paid → idempotent success |

**Rejected alternatives:**
- **Atomic conditional `UPDATE … WHERE OnHand - Reserved >= @q`.** Best under heavy contention, but it moves the rule out of the aggregate and outside the unit of work. Re-evaluated in Phase 6 and still rejected; revisit if one SKU's contention shows in latency (flash sales).
- **Pessimistic locks** (`UPDLOCK`). They add deadlock risk and hold locks during payment-provider calls.
- **Serializable transactions.** Throughput cost for every request.

## 7. Soft delete policy (not everywhere)

| Use soft delete when… | Use hard delete when… | Never delete when… |
|---|---|---|
| Other records reference the row historically **and** the business may restore it (products → *Archived*, coupons after redemption, users → *Disabled*, reviews → *Hidden*) | The row has no history value and nothing references it (unused categories, addresses, basket lines, expired tokens) | The row **is** history: orders, order lines, status history, stock ledger, payments, audit log |

There is no global `IsDeleted` flag with a global filter. Each aggregate has an explicit status that carries business meaning (Archived ≠ Deleted).

## 8. Audit fields, time, relationships, transactions

- **Timestamps:**
  - `CreatedAt` / `UpdatedAt` are stamped by `AuditTimestampsInterceptor` (a SaveChanges interceptor) from `TimeProvider`, and stored as `datetime2` in UTC. `TenantWriteGuardInterceptor` runs next to it (Phase 2): it stamps `TenantId` on insert and rejects any write to another tenant's row ([ADR-0022](adr/0022-tenancy-enforcement.md)).
  - Actor columns (`CreatedBy`) are added only where the business asks "who?" (status history, ledger, audit log). Everything else goes to the `AuditLog`.
- **Relationships:**
  - FKs are declared for every relationship **inside** a module.
  - Delete behaviour is `Restrict` by default, and `Cascade` only for aggregate children (order → lines).
  - Across modules: an id plus a snapshot, no FK (the only exception is `TenantId`).
  - Aggregate-child FKs are `NOT NULL`.
  - **References between tenant-owned rows carry the tenant (Phase 2).**
    - The FK is `(TenantId, XId) → (TenantId, Id)` through an alternate key on the principal (`AK_Categories_TenantId_Id`, `AK_Products_…`, `AK_Customers_…`, `AK_Orders_…`).
    - The database itself therefore rejects a row that points at another tenant's row, whatever a handler forgot.
    - The exceptions are aggregate children (shadow key to their root, always created together) and the optional category parent (checked in the handler).
    - An FK violation surfaces as `409 ReferenceConflict`.
- **Transactions ([ADR-0021](adr/0021-transaction-boundaries.md)):**
  - The command handler owns the boundary; each `SaveChangesAsync` is one atomic transaction. Work across modules in one step uses the same unit of work.
  - No transaction is open during a network call: save → call the provider → save. A provider failure is compensated in a new step (checkout); races are resolved by `rowversion` and an idempotent re-read (payment confirmation).
  - Tracked aggregates are saved without `Update()`, so only changed columns are written.
  - Side effects (email) happen after the commit; the outbox (Phase 14) makes them reliable.
- **Reads ([ADR-0008](adr/0008-cqrs-strategy.md)):** query services project with `AsNoTracking` straight into DTOs and page with a mandatory ordering plus an `Id` tiebreaker. They are the only place a listing's SQL is written — the single point where Phase 2's tenant filter and `TenantId`-leading indexes apply.

## 9. Current state and Phase 1A changes

| Finding (Phase 0) | Change in 1A |
|---|---|
| C5: money columns `decimal(18,2)` | All money columns become `decimal(19,4)`; `Money` enforces minor units |
| C1: no concurrency tokens | `RowVersion` on `Products`, `Coupons`, `Orders` |
| DB #4: `Categories.ParentId` without FK | Self-FK (`Restrict`); orphaned parents cleaned in the migration |
| DB #5: nullable `OrderItems.OrderId` / `OrderStatusHistories.OrderId` | Made `NOT NULL` (orphans, which are unreachable garbage, are removed in the migration) |
| G2: stale `database/*.sql` | Deleted; the README documents `dotnet ef migrations script --idempotent` |
| B6: plaintext reset tokens | Only a SHA-256 hash is stored; column widened for the hash |

Deferred to later phases, with the phase noted: `TenantId` (2), `Users` split (3), variants, slugs, and translations (5), inventory items and reservations (6), order snapshot totals and numbers (9).

**Phase 1B:** no schema change and no migration. The existing indexes cover the new read paths (`IX_OrderItems_ProductId` for the best-selling sort, `IX_StockMovements_ProductId_CreatedAt` for the paged ledger, `IX_Orders_CustomerId` for "my orders"). The admin order list sorts by `CreatedAt` without a dedicated index; Phase 2 adds `(TenantId, CreatedAt DESC)`, so no interim index was added.

**Phase 2 (`Phase2MultiTenancy`, additive, rehearsed on Phase 1 data by `MigrationRehearsalTests`):**

| Change | Detail |
|---|---|
| Platform tables | `Tenants` (slug unique, status, culture, currency, time zone, `rowversion`) and `TenantDomains` (`Host` unique across the platform) |
| Default tenant | id 1, "Marka Demo" / `marka`, JOD, `ar`, Asia/Amman. Every pre-existing row belongs to it |
| `TenantId` | `int NOT NULL` on all nine tenant-owned tables. Added with default 1 as an atomic backfill, then the default constraint is dropped, so a later insert without a tenant fails. FK to `Tenants` is `Restrict` |
| Uniqueness per tenant | `(TenantId, Slug)` categories, `(TenantId, Code)` coupons, `(TenantId, Email)` customers. These replace the global indexes, and are looser, so they cannot fail on existing data |
| Tenant-scoped FKs | Products→Categories, OrderItems→Products, Orders→Customers, Reviews→Products/Customers/Orders, StockMovements→Products. Each became composite, with `TenantId`-leading indexes |
| Hot-path indexes | `(TenantId, IsActive)` products; `(TenantId, CreatedAt)` orders; `(TenantId, CustomerId)` orders |
| `Orders.Currency` | Snapshot of the store currency. Backfilled from each order's first line, or `JOD` for orders without lines |

**Phase 3 (`Phase3Identity`, data-preserving, rehearsed by `MigrationRehearsalTests`):**

| Change | Detail |
|---|---|
| `Users` | Credentials, role, status, security stamp, lockout counters, hashed reset and verification tokens, `rowversion`. `TenantId` is nullable (platform accounts), FK `Restrict`. Filtered unique indexes: `(TenantId, NormalizedEmail)` for store accounts, and `NormalizedEmail` for platform accounts |
| `RefreshTokens` | Hash (unique), family, expiry, used and revoked timestamps, revoke reason. FK to `Users` is `Cascade` |
| Account backfill | Every `Customers` row becomes a `Users` row **with the same id** (`IDENTITY_INSERT`), store, email, name and BCrypt hash, so every account keeps its password. Role `Admin` → `TenantAdmin`; anything else → `Customer`. Each account gets a fresh security stamp, so old tokens (which have no `sstamp`) stop working |
| `Customers.UserId` | Added with a temporary default, set to `Id`, then the default is dropped. Unique `(TenantId, UserId)`, FK `Restrict` |
| Dropped from `Customers` | `PasswordHash`, `Role` and the reset-token columns, **only after** the copy. EF generated the drops first, which would have lost every password; the order was rewritten by hand |
| `Down()` | Copies credentials back to the profiles. Accounts without a profile (staff, the platform owner) have no place in the old schema and are lost, so `Down()` is for development only |

**Phase 4 (`Phase4PlatformAdministration`, additive only, [ADR-0024](adr/0024-platform-administration.md)):**

| Change | Detail |
|---|---|
| `Tenants.Settings` | `nvarchar(max) NULL`: the store settings as one JSON document, read and written whole, never queried inside. `NULL` means the defaults, derived from the name and language; the seeder gives the default store the Marka look once. The document is mapped by an Infrastructure type (`StoreSettingsJson`), so the Domain carries no serialization attributes, and a tightened rule never breaks loading older settings |
| `Tenants.EnabledModules` | `nvarchar(200) NOT NULL DEFAULT 'promotions,reviews,wishlist'`, so existing stores keep every module. It is carried in the cached tenant snapshot, so enforcement needs no query |
| `AuditEntries` | `bigint` identity, UTC timestamp, area, action, affected store (nullable), actor and role, target, metadata (≤ 4000), IP, correlation id. No FK and no tenant filter. Append-only (write guard) |

**Phase 5 (`Phase5Catalog`, data-preserving, rehearsed by `MigrationRehearsalTests`, [ADR-0025](adr/0025-catalog-model.md)):**

| Change | Detail |
|---|---|
| `ProductTranslations`, `CategoryTranslations` | One row per (owner, culture): name (200), description (4000), SEO title (70) and SEO description (160). Unique `(OwnerId, Culture)`. `TenantId` on every row. Cascade with the owner (a shadow FK: aggregate children, exempt from the composite-FK rule) |
| `ProductVariants` | SKU (64, unique per store when present), `Price` + `Currency` (`decimal(19,4)`), `CompareAtPrice` (same currency), `IsDefault`. Exactly one default per product, by a filtered unique index. Alternate key `(TenantId, Id)` so cart and order lines can reference a variant with a tenant-scoped FK later |
| `ProductImages` | URL (500) and sort order; index `(ProductId, SortOrder)` |
| `Products` | Added `Slug` (120, unique per store), `Status` (Draft 0 / Active 1 / Archived 2) and `Brand`. Dropped `NameAr`, `NameEn`, `Description`, `Price`, `Currency`, `ImageUrl` and `IsActive`. `(TenantId, Status, CategoryId)` replaces `(TenantId, IsActive)` |
| `Categories` | Added `SortOrder` and `IsActive`. Dropped `Name`. New index `(TenantId, ParentId, SortOrder)` |
| Data copy (runs before any drop) | An Arabic translation from `NameAr` + `Description`; an English one from `NameEn` when present and different; a default variant from `Price`/`Currency`; an image row only for real `/uploads/` or http(s) URLs (the old placeholder values are dropped); `Status` from `IsActive`; slug `p-{Id}`; each category name → a translation in its store's default language. EF generated the column drops first, which would have lost every name and price; the order was rewritten by hand |
| `Down()` | Restores the old columns from the Arabic/English translations, the default variant and the primary image. It is lossy by nature (other languages, extra images, SKUs and compare-at prices have no old column), so it is for development only |

**Phase 6 (`Phase6Inventory`, data-preserving, rehearsed by `MigrationRehearsalTests`, [ADR-0026](adr/0026-inventory-reservations.md)):**

| Change | Detail |
|---|---|
| `InventoryItems` | One row per variant: `OnHand`, `Reserved`, `LowStockThreshold`, `rowversion`, check constraints. Composite FKs to the variant and the product within the store |
| `StockReservations` | Reference (`order:{id}`), quantity, status, `ExpiresAt`, `ClosedAt`. Composite FK to the item. A filtered index on the active rows keeps the expiry sweep cheap |
| `StockMovements.InventoryItemId` | Added (nullable), backfilled from each movement's product, then made `NOT NULL` with a composite FK |
| Data copy (before any drop) | On hand = the old `StockQuantity` + the quantities of Pending orders (the old checkout had already decremented them); reserved = those quantities. Active reservations for Pending orders (a fresh 30-minute window, then the sweeper settles them); Committed reservations for Paid orders not yet shipped (so cancelling them restocks as before). An opening-balance `Adjustment` wherever the history did not add up to on-hand, so Σ ledger = on hand from here on |
| Dropped from `Products` | `StockQuantity`, `LowStockThreshold`, only **after** the copy. EF generated the drops first, which would have lost every stock level; the order was rewritten by hand |
| `Down()` | Restores `StockQuantity` as the available quantity (on hand − reserved) and the threshold. Reservations have no place in the old schema, so it is for development only |

**Phase 7 (`Phase7Customers`, additive only, [ADR-0027](adr/0027-customer-profile-and-erasure.md)):**

| Change | Detail |
|---|---|
| `Customers` | `Phone` (nullable), `Status` (default `0` = Active, so every existing customer stays active), `BlockedAt`, `ErasedAt`. Index `(TenantId, Status)` for the admin status filter |
| `CustomerAddresses` | New table: label, recipient, phone, country code, city, region, lines 1–2, postal code, default-shipping and default-billing flags, `TenantId`. FK to the customer with cascade (the addresses are part of its aggregate), and to the store |
| Data | Nothing moved or rewritten. Existing orders keep their typed addresses; new orders store the same single-line snapshot |
| `Down()` | Drops the table and the columns, so saved addresses and statuses are lost. Development only |

**Phase 8 (`Phase8Basket`, additive only, [ADR-0028](adr/0028-basket-and-pricing-pipeline.md)):**

| Change | Detail |
|---|---|
| `Baskets` | New: `CustomerId` (nullable, composite FK to the customer within the store), `GuestTokenHash` (`char(64)`, the SHA-256 of the guest cookie; the token itself is never stored), `ExpiresAt`. Check `CK_Baskets_Owner`: exactly one of the two owners. Filtered unique indexes: one basket per customer and one per guest token in a store. `(TenantId, ExpiresAt)` for the sweep |
| `BasketLines` | New: `ProductId` and `VariantId` (composite FKs within the store, Restrict, since products are archived, not deleted), `Quantity` (check 1–99). Unique `(BasketId, VariantId)`; cascade from the basket. No price column: prices are read live at every quote |
| Data | None: the cart was client-side before this phase |

## 10. Migration workflow

1. Change the entity or configuration.
2. Run `dotnet ef migrations add <PascalCaseIntent> --project src/Souq.Infrastructure --startup-project src/Souq.API`.
3. **Read the generated migration.** Data-preserving steps (backfills, orphan cleanup) are added by hand in `Up()`; `Down()` is kept honest.
4. The integration tests apply all migrations to a fresh SQL Server container. A migration that fails there fails the build.
5. Today migrations run at startup. Production moves them to a **deployment step** (a migration bundle) in Phase 23, so that multiple replicas don't race.
