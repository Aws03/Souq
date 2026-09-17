# Module catalog

> **A module is a business capability that owns its rules and its data.** It is not a folder created for appearance. Souq has thirteen.
> **This page is the index:** what each module owns, where its code lives, and where its documentation is. The rules about who may call whom are in [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md); the physical structure is in [Architecture.md](../02-ARCHITECTURE/Architecture.md) and [ADR-0002](../11-ADR/0002-modular-monolith-structure.md); the decision to split this way is [ADR-0004](../11-ADR/0004-module-boundaries.md).

## 1. The catalog

| Module | Owns | Application code | Document |
|---|---|---|---|
| **Platform** | Stores, their domains, settings and branding, module flags, platform administration, the audit log | `src/Souq.Application/Features/Platform`, `src/Souq.Application/Features/Stores` | [Platform/README.md](Platform/README.md) · [change guide](Platform/ChangeGuide.md) |
| **Identity** | Accounts, credentials, sessions, roles and permissions, staff and invitations | `src/Souq.Application/Features/Auth`, `src/Souq.Application/Features/Staff` | [Identity/README.md](Identity/README.md) · [change guide](Identity/ChangeGuide.md) |
| **Catalog** | Products, variants, images, translations, categories | `src/Souq.Application/Features/Products`, `src/Souq.Application/Features/Categories` | [Catalog/README.md](Catalog/README.md) · [change guide](Catalog/ChangeGuide.md) |
| **Inventory** | Stock levels, reservations, the movement ledger, low-stock thresholds | `src/Souq.Application/Features/Inventory` | [Inventory/README.md](Inventory/README.md) · [change guide](Inventory/ChangeGuide.md) |
| **Customers** | The shopper as a commercial relationship: profile, addresses, status, export and erasure | `src/Souq.Application/Features/Customers` | [Customers/README.md](Customers/README.md) · [change guide](Customers/ChangeGuide.md) |
| **Shopping** | Baskets, the pricing pipeline, wishlists | `src/Souq.Application/Features/Baskets`, `src/Souq.Application/Features/Wishlist` | [Shopping/README.md](Shopping/README.md) · [change guide](Shopping/ChangeGuide.md) |
| **Ordering** | Orders, order numbers, tracking tokens, the status machine | `src/Souq.Application/Features/Orders` | [Ordering/README.md](Ordering/README.md) · [change guide](Ordering/ChangeGuide.md) |
| **Payments** | Payments, refunds, the per-store gateway account (its entity, key rules and table — the use cases that edit it sit in Platform's folders, see §2) | `src/Souq.Application/Features/Payments` | [Payments/README.md](Payments/README.md) · [change guide](Payments/ChangeGuide.md) |
| **Promotions** | Coupons and their redemptions | `src/Souq.Application/Features/Coupons` | [Promotions/README.md](Promotions/README.md) · [change guide](Promotions/ChangeGuide.md) |
| **Shipping** | Shipping methods and their rates | `src/Souq.Application/Features/Shipping` | [Shipping/README.md](Shipping/README.md) · [change guide](Shipping/ChangeGuide.md) |
| **Reviews** | Reviews and their moderation | `src/Souq.Application/Features/Reviews` | [Reviews/README.md](Reviews/README.md) · [change guide](Reviews/ChangeGuide.md) |
| **Notifications** | The outbox, in-app notifications, email delivery | `src/Souq.Application/Features/Notifications` | [Notifications/README.md](Notifications/README.md) · [change guide](Notifications/ChangeGuide.md) |
| **Reporting** | Read-only statistics across modules | `src/Souq.Application/Features/Reporting` | [Reporting/README.md](Reporting/README.md) |

Generated companions: [UseCases.md](UseCases.md) (every command, query, handler, validator and endpoint per module) · [Endpoints.md](../05-API/Endpoints.md) · [ModuleDomainDependencies.md](../02-ARCHITECTURE/ModuleDomainDependencies.md).

**Where each module surfaces in the frontend.** One SPA serves four areas, chosen by host in `frontend/src/App.jsx`: the **storefront** and the customer **account** (`/account`, `/orders`) on a store host, the store **admin** (`/admin`) on the same host, and the **platform** area (`/platform`) on a platform host. Each module README names its screens.

| Module | Storefront | Account | Admin | Platform |
|---|---|---|---|---|
| Platform | the store's identity and theme at boot (`frontend/src/app/TenantProvider.jsx`) | — | `/admin/settings` | `/platform/stores`, the provisioning wizard, a store's page and settings, `/platform/audit` |
| Identity | `/login`, `/register`, `/forgot-password`, `/reset-password`, `/verify-email`, `/accept-invitation` | — | `/admin/staff` | `/login`, `/accept-invitation`, `/platform/accounts` |
| Catalog | home, `/offers`, `/products/:handle` | — | `/admin/products`, `/admin/categories` | — |
| Inventory | — (availability reaches the storefront through Catalog's product data) | — | `/admin/inventory` | — |
| Customers | — | `/account`, `/account/addresses` (profile, address book, export and erasure) | `/admin/customers` | — |
| Shopping | cart drawer, `/cart`, `/wishlist` | — | — | — |
| Ordering | `/checkout`, `/confirmation`, `/track/:token` | `/orders`, `/orders/:id` | `/admin/orders` | — |
| Payments | the card form in `/checkout` | refunds shown on `/orders/:id` | `/admin/payments`; refunds in the order drawer | — |
| Promotions | the coupon field in `/checkout` | — | `/admin/coupons` | — |
| Shipping | the method choice in `/checkout` | — | `/admin/shipping` | — |
| Reviews | ratings and the review form on `/products/:handle` | — | `/admin/reviews` | — |
| Notifications | the bell in the store header | — | the bell in the admin header | — |
| Reporting | — | — | `/admin`, `/admin/business` | `/platform` |

## 2. Module names versus folder names

A module is a capability; the folder is where its use cases happened to be created. They differ in six places, and the mapping lives in exactly one machine-readable spot: the `ModuleFolders` map in `tests/Souq.ArchitectureTests/ModuleMap.cs`.

| Module | Folder(s) | Why the names differ |
|---|---|---|
| Ordering | `Orders` | The capability is the ordering process; the folder is named after the entity |
| Promotions | `Coupons` | Named for the capability, so future discount types have a home |
| Shopping | `Baskets`, `Wishlist` | Both are pre-purchase intent. `Baskets` is plural because a `Basket` namespace would shadow the entity ([ADR-0028](../11-ADR/0028-basket-and-pricing-pipeline.md)) |
| Identity | `Auth`, `Staff` | Sign-in and staff administration are one capability: who exists and what they may do |
| Catalog | `Products`, `Categories` | Categories exist only to organize products |
| Platform | `Platform`, `Stores` | `Platform` is the platform owner's side; `Stores` is a store administering **its own** settings |

**`Stores` carries a known inconsistency:** it also holds the store-side payment-account and review-settings use cases. The payment account's domain is Payments' — `StorePaymentAccount`, `PaymentKeyRules` and `IStorePaymentAccountRepository` are mapped to Payments in `DomainOwners` — but the use cases that read and edit it live in `src/Souq.Application/Features/Stores/StorePaymentAccounts.cs` (and, for the platform side, `src/Souq.Application/Features/Platform/TenantPaymentAccounts.cs`), so `ModuleFolders` and the architecture tests count them as **Platform's**. TD-04 in [TechnicalDebt.md](../12-ROADMAP/TechnicalDebt.md) records moving them (and the review-settings use cases) to Payments and Reviews; until then, a change to them is a change in a Platform folder.

## 3. How these thirteen were chosen

Sixteen candidate areas were evaluated; the ones that were not independent capabilities were merged or reclassified ([ADR-0004](../11-ADR/0004-module-boundaries.md)):

| Candidate | Outcome | Reason |
|---|---|---|
| Categories | Merged into **Catalog** | Categories exist to organize products; same language, same rules |
| Wishlist | Merged into **Shopping** | Same lifecycle as the basket (guest → merge at sign-in); two tiny modules would be ceremony |
| Tenants | Became **Platform** | The store lifecycle, domains and module flags are one capability |
| Platform administration | **An area, not a module** | It owns no data; it is the platform owner's entry point into Platform, Identity and Reporting use cases |
| Storefront | **An area, not a module** | A public view composed of Catalog, Platform configuration and Shopping |
| Store configuration | Into **Platform** | Branding, SEO, contact and locale are store settings |
| Audit, tenancy, errors, paging | **Building blocks** | Cross-cutting mechanisms with no business capability of their own |

## 4. What a module document contains

Every `<Module>/README.md` follows the same shape, so you can jump straight to the section you need: purpose · responsibilities · what it must *not* own · business concepts · domain model · use cases · public contracts · dependencies · data ownership · API · security · tenant behaviour · events · integrations · tests · failure modes · common changes · known limitations · future evolution.

Where a module has a `ChangeGuide.md`, it lists the changes people actually make ("add an order status", "add a payment provider", "change reservation expiry") with the files to inspect, the invariants to respect, the tests to update, and whether the change needs a migration or an ADR.

## 5. Adding a module

1. Name it after a **capability**, not an entity. Write down what it owns and what it must not own.
2. Add it to this catalog and to the graph in [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md). Refuse any cycle.
3. Create the feature folder and register it in `ModuleFolders` (`tests/Souq.ArchitectureTests/ModuleMap.cs`) — an unmapped folder fails the build.
4. Declare which other modules' contracts it may call, in `AllowedContracts` in `tests/Souq.ArchitectureTests/ModuleAndContractRuleTests.cs`.
5. If it owns Domain types, add them to the `DomainOwners` map so cross-module use of them is counted.
6. Tenant-owned tables implement `ITenantOwned`; the tenancy tests then require the filter and the composite keys.
7. Write `docs/04-MODULES/<Module>/README.md` from the shape above, and record the new tables in [OwnershipMap.md](../06-DATABASE/OwnershipMap.md).
