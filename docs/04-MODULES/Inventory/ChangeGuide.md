# Inventory: change guide

> Read [README.md](README.md) first. This page lists the changes engineers actually make in this module and how to make them safely. It references stable paths and symbols, never line numbers.

## Before any change in this module

**Invariants that must survive any change**

- `Available = OnHand − Reserved` and it never goes negative. The database backs this with `CK_InventoryItems_Quantities`.
- Every change of `OnHand` produces exactly one `StockMovement`, and a movement can only be created by `InventoryItem` (its constructor is `internal`). Σ movements = `OnHand`, asserted end to end by `InventoryAndOrderTests`.
- A hold is an explicit `StockReservation` row, never a side effect. Σ active reservations = `Reserved`.
- A `Sale` line is written in exactly one place: `InventoryItem.Commit`, when payment succeeded.
- Every reservation transition is guarded by the current status, so every contract operation is idempotent.
- All rules live in `InventoryItem`; handlers and `InventoryReservations` only coordinate load → apply → save.
- No network call inside a transaction ([ADR-0021](../../11-ADR/0021-transaction-boundaries.md)); no raw SQL outside migrations.
- Inventory does not know about orders. It stores an opaque reference string.

**Files to read first**

`src/Souq.Domain/Entities/InventoryItem.cs`, `src/Souq.Domain/Entities/StockMovement.cs`, `src/Souq.Application/Features/Inventory/Reservations/InventoryReservations.cs` (which also holds `InventorySettings`, `InventoryWriter` and `VariantStockInitializer`), `src/Souq.Application/Features/Inventory/Contracts/InventoryContracts.cs`, `src/Souq.Application/Features/Inventory/Commands/InventoryCommands.cs`, `src/Souq.Infrastructure/Persistence/Repositories/InventoryRepository.cs`, `src/Souq.Infrastructure/Persistence/Configurations/InventoryConfiguration.cs`, `src/Souq.Infrastructure/BackgroundJobs/ReservationExpiryService.cs`, and — because they own the decisions Inventory only executes — `src/Souq.Application/Features/Orders/Commands/ExpireStaleCheckoutsCommand.cs` and `src/Souq.Application/Features/Orders/OrderPaymentConfirmation.cs`.

**Tests that guard the module**

`tests/Souq.Domain.Tests/InventoryItemTests.cs`, `tests/Souq.Domain.Tests/DomainEventTests.cs`, `tests/Souq.Application.Tests/Inventory/InventoryReservationsTests.cs`, `tests/Souq.Application.Tests/Inventory/InventoryCommandsTests.cs`, `tests/Souq.IntegrationTests/InventoryAndOrderTests.cs` (parallel checkouts and ledger reconciliation), `tests/Souq.IntegrationTests/QueryServiceTests.cs`, `tests/Souq.IntegrationTests/TenantIsolationTests.cs`, `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.

Set `Inventory:SweepIntervalSeconds` to `0` in any test that drives expiry itself, so the background sweep cannot race it.

---

## I need to change the reservation duration or policy

- **Inspect:** `InventorySettings` (`ReservationMinutes`, `ReservationLifetime`), `AddInventory` in `src/Souq.Infrastructure/DependencyInjection.cs` (the 5–1440 validation and `ValidateOnStart`), `InventoryReservations.ReserveAsync` (it computes `expiresAt` from `TimeProvider` inside the retry, so each attempt gets a fresh window), `src/Souq.API/appsettings.json`, and the sweep that acts on the window (`ExpireStaleCheckoutsHandler`).
- **Rules to respect:** the window is a promise to the customer and a cost to the store — shortening it frees stock sooner but can cancel a payment in flight, which is why the expiry path asks the gateway before cancelling. Never extend an existing hold silently by writing `ExpiresAt` from outside the aggregate; add a method on `InventoryItem`/`StockReservation` if extension becomes a real use case.
- **Steps (simple change):** change the default in `appsettings.json` or the environment; the validation range stays the guard.
- **Steps (per-store window):** move the value onto the tenant (`StoreSettings`) and pass it into `ReserveAsync` — either as a parameter on `ReservationLine`'s caller side or by having `InventoryReservations` read `ITenantContext`. Prefer passing the computed `expiresAt` in, so the module stays free of settings lookups, and keep the platform-wide bounds as a sanity check.
- **Steps (reserve earlier, e.g. from the basket):** this reverses a deliberate decision ([ADR-0028](../../11-ADR/0028-basket-and-pricing-pipeline.md), Phase 8). It needs a reference format for baskets, a much shorter window, a policy for merging guest and customer baskets, and a new source of expiry sweeps. Write an ADR first.
- **Tests:** `InventoryReservationsTests` already asserts the lifetime comes from settings with a fixed clock; add cases for the new source. `ExpireStaleCheckoutsHandlerTests` and `InventoryAndOrderTests` cover what happens when the window closes.
- **API:** none directly, but the checkout UI's copy ("complete payment within 30 minutes") must match.
- **Database:** none for the duration. A per-store window is a Platform settings change, not a schema change, if it lives in the settings document.
- **Security:** none.
- **Docs and ADR:** README (Tenant behaviour, Known limitations 10), [Configuration.md](../../09-OPERATIONS/Configuration.md), and a revisit note on [ADR-0026](../../11-ADR/0026-inventory-reservations.md).

## I need to change low-stock alerting

- **Inspect:** `InventoryItem.IsLowStock` and `RaiseIfBecameLow`, `StockBecameLow`, `NotificationMessageTypes` (the outbox allowlist), `StockBecameLowHandler`, `NotificationKinds`, `GetLowStockQuery`/`InventoryQueries.ListLowStockAsync`, `frontend/src/pages/admin/Dashboard.jsx` and `frontend/src/pages/admin/Inventory.jsx` (both read `low-stock` with `pageSize=1` for the badge).
- **Rules to respect:** the event is a *crossing*, not a state — one alert per fall, so a busy SKU does not notify on every sale. Keep it that way or the alert becomes noise. Events carry ids and numbers only; the handler reads live data when it runs ([ADR-0034](../../11-ADR/0034-notifications-outbox.md)). Events are raised by the entity and written to the outbox by the same `SaveChanges`, so never publish from a handler.
- **Steps (alert in more situations, e.g. at creation or when the threshold is raised):** add the `RaiseIfBecameLow` call to the relevant entity method — `SetLowStockThreshold`, or `VariantStockInitializer` after the opening quantity — and decide what "crossing" means there (for a new item, "already at or below the threshold" is a state, not a crossing, so guard against a notification storm on bulk import).
- **Steps (send an email):** add an outbox message type (or reuse `StockBecameLow`) and a handler in `Features/Notifications` that composes through `NotificationEmails`; register it in `src/Souq.Application/DependencyInjection.cs` and, for a new message type, in `NotificationMessageTypes`. The request path must never call `IEmailSender` — an architecture test enforces it.
- **Steps (per-store or per-product policy):** the threshold already lives per item; a store-wide default would belong to Platform settings and be applied by `VariantStockInitializer`.
- **Tests:** `DomainEventTests` pins "one event per downward crossing" and "no event from an unsaved item" — extend it for the new trigger; `OrderNotificationHandlersTests` covers the handler; `NotificationTests` covers the staff notification end to end.
- **API:** `StockLevelDto.IsLowStock` and the low-stock list are the current contract; adding an email changes no endpoint.
- **Database:** none, unless per-store defaults are stored.
- **Security:** the alert goes to everyone whose role grants `inventory.view` (`RolePermissions.RolesGranting`); keep it that way rather than hard-coding roles, and keep product names out of anything that could leak to customers.
- **Docs and ADR:** README (Events and background work, Known limitations 2), [Events.md](../../02-ARCHITECTURE/Events.md).

## I need to record customer returns (or add a movement type)

- **Inspect:** `StockMovementType`, `InventoryItem.Receive` (it already accepts `Return` but nothing calls it that way), `StockMovement`, `InventoryQueries.ListMovementsAsync` (it serialises the type name), `frontend/src/pages/admin/Inventory.jsx` (the movements drawer renders the type).
- **Rules to respect:** a movement exists only because on hand changed — never write a ledger line for a hold or for a "note". The reason belongs in the type, the direction in the sign of `QuantityChange`. A return is triggered by another module (Ordering or Payments deciding a refund was accepted), so Inventory must expose it through a contract, not through a new endpoint that staff call by hand — otherwise the ledger and the order history drift apart.
- **Steps:**
  1. Decide the trigger and its owner. If a refund causes the return, Ordering or Payments calls Inventory.
  2. Add a method to `IInventoryReservations` (or a new small contract, for example a *stock returns* port) that takes the reference or the variant plus quantity, and implement it on `InventoryReservations` through `InventoryWriter.SaveAsync` so it retries like everything else.
  3. Call `item.Receive(quantity, StockMovementType.Return, note)` and add the movement through `IStockMovementRepository`.
  4. Update `ModuleAndContractRuleTests`' allowed-contract map if a new module becomes a caller.
- **Tests:** `InventoryItemTests` already covers `Receive` for both allowed types; add an application test for the new contract method and an integration case asserting Σ ledger = on hand after a return.
- **API:** an admin endpoint is only needed if staff record returns manually; a delta adjustment with a reason already covers the ad-hoc case, which is why this has not been built.
- **Database:** none — the enum is stored as an int and the values already exist.
- **Security:** returns move money-adjacent quantities; keep the operation behind `inventory.manage` or behind the refund permission of the calling module, and keep it audited.
- **Docs and ADR:** README (Known limitations 5), [BusinessRules.md](../../01-REQUIREMENTS/BusinessRules.md).

## I need to add multi-warehouse stock — FUTURE

Not scheduled. [ADR-0026](../../11-ADR/0026-inventory-reservations.md) names "several warehouses" as one of its revisit triggers. This is what would have to change.

- **Inspect before deciding:** the unique index `(TenantId, VariantId)` on `InventoryItems`; `IInventoryRepository.GetByVariantsAsync` and `IStockAvailability.AvailableAsync`, both of which build a dictionary **keyed by variant** and would throw on a second row per variant; `InventoryReservations.ReserveAsync` (one hold per variant); `InventoryRepository.GetForVariantAsync` (a single row per variant) and `StockTarget.Product` (which refuses a product with more than one row); `CatalogQueries` and `WishlistQueries`, which already `Sum` over a product's items and would keep working; `InventoryQueries.StockedItems`/`PageAsync`, which list one row per variant.
- **Rules to respect:** `Available` stays non-negative per row; the ledger stays per item, so Σ movements = that item's on hand; a single order's reservation may now span several items for one line, so "reserve all or nothing" must hold across an allocation, not just across lines.
- **Steps (sketch):**
  1. Domain: a warehouse/location entity (Platform or Inventory owned), `InventoryItem` gains a location, and the unique index becomes *(TenantId, VariantId, LocationId)*.
  2. Introduce an allocation rule — which location serves a line — as a domain service, and make `ReserveAsync` allocate a requested quantity across items instead of assuming one.
  3. Replace every variant-keyed dictionary (`AvailableAsync`, `GetByVariantsAsync` callers) with an aggregate-then-allocate shape; `IStockAvailability` should keep returning a single number per variant (the sum) so Shopping and Ordering do not change.
  4. Admin screens and endpoints: stock per location, transfers between locations (a pair of movements), and a location filter on the ledger.
  5. Shipping and Ordering may want the chosen location on the order; that is a separate decision.
- **Tests:** the parallel-checkout tests must be repeated per location and across locations; add reconciliation tests per item; `InventoryReservationsTests` needs allocation cases (partial availability in two locations).
- **API:** an inventory row is identified by (`InventoryItemDto.Id` product, `InventoryItemDto.VariantId`) today; a location dimension adds a third part — a breaking change for the admin UI.
- **Database:** migrations for the location table, the wider unique index, and a data migration assigning existing rows to a default location. Reversible only while every variant has exactly one location.
- **Security:** per-location permissions are a plausible follow-on (a branch manager sees only their stock); design the permission before shipping the data model.
- **Docs and ADR:** a new ADR superseding the "where stock lives" row of [ADR-0026](../../11-ADR/0026-inventory-reservations.md), plus [Modules.md](../Modules.md), this README and [DatabaseDesign.md](../../06-DATABASE/DatabaseDesign.md).

## I need to change how variants appear in stock administration

Done in V2 ([ADR-0040](../../11-ADR/0040-product-option-model.md)): the screen keys rows by `variantId`, labels them with `InventoryItemDto.VariantLabel`, marks inactive variants, and calls `api/admin/inventory/variants/{variantId}/…` through `frontend/src/pages/admin/StockDrawers.jsx`. The product-keyed routes stay for older clients.

- **Inspect:** `InventoryQueries.PageAsync` (the label projection reads Catalog's option tables and composes with `VariantLabels.Compose` — never compose a label here by hand), `frontend/src/pages/admin/Inventory.jsx`, `frontend/src/pages/admin/StockDrawers.jsx`.
- **Rules to respect:** the stock rules live in `InventoryItem` and both route families share `StockTarget.ChangeAsync`. Inventory must not own or change option data; it only reads the label for display.
- **Removing the product-keyed routes** would break older clients: version it and record it in [ApiDocumentation.md](../../05-API/ApiDocumentation.md).
- **Tests:** `ProductOptionAdminTests` pins labelled rows; the browser journey `frontend/e2e/product-variants.spec.js` adjusts a variant's stock and reads its row.
