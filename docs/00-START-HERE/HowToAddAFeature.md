# Build your first feature

> **Who this is for:** anyone adding new behaviour to Souq. Changing behaviour that already exists? Use [HowToChangeExistingCode.md](HowToChangeExistingCode.md).
> **Level:** L2. **Read first:** [RequestLifecycle.md](RequestLifecycle.md) (how a request moves), [EngineeringMentalModel.md](EngineeringMentalModel.md) (where each kind of logic belongs), [CriticalInvariants.md](CriticalInvariants.md) (what you must not break).
> **Last verified against the code:** 2026-09-17, branch `phase/17-production-hardening`.

## The reference feature: store shipping methods

Every step below shows how an existing feature did it, so you can open real files instead of imagining them.

**Store shipping methods** (Phase 12, [ADR-0032](../11-ADR/0032-shipping-methods.md)) lets a store's staff define ways to ship:
- a price in the store's currency;
- an optional free-shipping threshold;
- the countries served;
- a delivery estimate;
- a carrier with a tracking-link template.

Checkout then offers the methods that serve the customer's country, and the order keeps a snapshot of the one chosen. It is small enough to read in an hour, and it touches every layer: a domain rule, a permission, a tenant-owned table, a migration, an admin screen, a contract another module uses, and tests at four levels.

| Layer | Shipping methods |
|---|---|
| Domain | `src/Souq.Domain/Entities/ShippingMethod.cs`, `src/Souq.Domain/Exceptions/InvalidShippingMethodException.cs`, `src/Souq.Domain/Interfaces/IShippingMethodRepository.cs` |
| Application | `src/Souq.Application/Features/Shipping/ShippingMethodUseCases.cs`, `src/Souq.Application/Features/Shipping/Contracts/ShippingContracts.cs`, `src/Souq.Application/Features/Shipping/StoreShippingRates.cs` |
| Infrastructure | `src/Souq.Infrastructure/Persistence/Configurations/ShippingMethodConfiguration.cs`, `src/Souq.Infrastructure/Persistence/Repositories/ShippingMethodRepository.cs`, `src/Souq.Infrastructure/Migrations/20260911183736_Phase12Shipping.cs` |
| API | `src/Souq.API/Controllers/ShippingMethodsController.cs` |
| Frontend | `frontend/src/pages/admin/ShippingMethods.jsx`, `frontend/src/pages/admin/ShippingMethodFormDrawer.jsx`, `frontend/src/features/admin/shipping/shippingForm.js`, `frontend/src/features/checkout/shippingOptions.js` |
| Tests | `tests/Souq.Domain.Tests/ShippingMethodTests.cs`, `tests/Souq.Application.Tests/Shipping/ShippingMethodHandlersTests.cs`, `tests/Souq.IntegrationTests/ShippingTests.cs`, `frontend/src/features/admin/shipping/shippingForm.test.js` |
| Documents | [Shipping module](../04-MODULES/Shipping/README.md), BR-SHP rules in [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md), [ADR-0032](../11-ADR/0032-shipping-methods.md) |

---

## 1. Identify the module that owns it

Write one sentence naming the **actor** and what they can do: *"A store's staff defines how its orders ship, and a customer chooses one at checkout."* If you cannot name the actor, the requirement isn't ready.

Then find the owner in [Modules.md](../04-MODULES/Modules.md) and [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md). The concept belongs to **one** module. Other modules may *read* it through a contract.

> **In Souq:** shipping methods belong to **Shipping**. Checkout (Ordering and Shopping) needs the rates but doesn't own them, so Shipping publishes `IShippingRateProvider` in its `Contracts` folder, implemented by `StoreShippingRates`. The pricing pipeline calls that contract; it never touches the table.

Read the module's `README.md` and `ChangeGuide.md` before designing. If the feature seems to need two owners, see [ModuleBoundaries.md](../02-ARCHITECTURE/ModuleBoundaries.md) §6 before inventing a new module.

## 2. Put the business rules in the Domain

List the rules as things that must always be true. Put each one in the aggregate that owns the state, and write the domain test **first**: it is the cheapest place to discover the rule is ambiguous.

- A rule true regardless of screen, database or provider → Domain.
- A rule that needs a lookup ("this name is already used") → the handler, because the aggregate can't query.
- Never add a field to an entity "for the UI".

> **In Souq:** `ShippingMethod.Update` validates everything **before** assigning anything, so a rejected value changes nothing:
> - a name up to `NameMaxLength`;
> - a free-over threshold above zero, in the price's currency;
> - an estimate within `MaxEstimateDays`;
> - a tracking template that is an absolute https URL containing `{number}`.
>
> Violations throw `InvalidShippingMethodException`, which the API turns into `422` with a stable code. Rules BR-SHP-01…03 in [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md) name the code and the test.

**Don't:** invent a rule that changes prices, limits, refunds or eligibility because it seems reasonable. If it isn't in [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md), a test or an ADR, it's a business decision: stop and ask (`AGENTS.md` §9).

## 3. Write the use cases in the Application layer

One command per state change, one query per read, named as intentions, each with a validator.

- **Input** is a DTO: never an entity, never a store id.
- **Output** is a `Result`, an id or a DTO: never an entity (an architecture test enforces this).
- **Failures** carry stable error codes ([ApiDocumentation.md](../05-API/ApiDocumentation.md) §4).
- The handler orchestrates: load, call one domain method, save. It doesn't decide business rules.

> **In Souq:** `ShippingMethodUseCases.cs` holds `ListShippingMethodsQuery`, `CreateShippingMethodCommand`, `UpdateShippingMethodCommand` and `DeleteShippingMethodCommand`, their handlers, and `ShippingMethodInputValidator` (shape and ranges before the domain runs).
>
> `CreateShippingMethodHandler` takes the currency from the **store** (`ITenantContext`), not from the request. A client can't price a method in another currency.

**If another module needs your data**, publish a small interface in your module's `Contracts` folder, as Shipping did. If you need another module's data, use *its* contract. A new arrow between modules is a boundary decision (step 11).

## 4. Define authorization

Every endpoint declares its access explicitly. `tests/Souq.ArchitectureTests/EndpointRuleTests.cs` fails the build otherwise.

- A back-office capability gets a **permission** (`[HasPermission(...)]`). Reuse an existing one when the audience is the same, and add one to `src/Souq.Application/Common/Security/Permissions.cs` only when a role should be able to lack it. Grants to roles live in the same file (`RolePermissions`).
- A customer's own resource is protected by **ownership inside the use case**: another customer's id answers `404`, not `403`.
- Public endpoints are marked `[AllowAnonymous]` on purpose, never by omission.
- A platform capability uses a `platform.*` permission and `[PlatformEndpoint]`.

> **In Souq:** `ShippingMethodsController` carries `[HasPermission(Permissions.Store.Shipping)]` (`store.shipping.manage`), granted to store administrators only. The admin route in `frontend/src/App.jsx` is guarded with the same string, but only for the user experience. The customer-facing rates arrive through the basket quote, not through this controller.

## 5. Keep it inside the store: tenant isolation

You rarely write isolation code; you make sure you're covered by it.

- A new table owned by a store implements `ITenantOwned` in the Domain. `AppDbContext` then applies the tenant query filter, the write guard refuses cross-store writes, and `tests/Souq.ArchitectureTests/TenancyRuleTests.cs` requires the foreign key to `Tenants`.
- No request type carries a `TenantId`; handlers read the store from `ITenantContext`.
- No `IgnoreQueryFilters`, raw SQL or bulk `ExecuteUpdate` in feature code. They are restricted to reviewed places by `TenancyRuleTests`.
- **Every new endpoint with a resource id must be added to the isolation table** in `tests/Souq.IntegrationTests/TenantIsolationTests.cs`. The test lists every such route and fails if yours is missing. It then proves that another store's id answers `404` for reads, updates and deletes.

> **In Souq:** `ShippingMethod : Entity, ITenantOwned`. The `PUT` and `DELETE /api/admin/shipping-methods/{id:int}` routes appear in the isolation table beside products, coupons and orders.

## 6. Add persistence and the API endpoint

**Persistence** (`src/Souq.Infrastructure`):
- an EF configuration (lengths from the entity's constants, indexes that lead with `TenantId`);
- a repository behind the Domain interface;
- for reads that shape data for a screen, a query service behind an Application port.

A schema change needs a **migration**. Additive first; a data move is written and reviewed by hand; a destructive change needs the owner's approval ([Migrations.md](../06-DATABASE/Migrations.md)). Read the generated migration before keeping it.

```bash
dotnet ef migrations add <Name> --project src/Souq.Infrastructure --startup-project src/Souq.API
```

**The endpoint** is a thin controller action: bind, authorize, send one request, map the result with `ToHttp` or `Failure` (`src/Souq.API/Http/ResultHttpExtensions.cs`). Follow the conventions: plural resources, sub-resource actions instead of invented verbs, paging on lists that grow, `[RequiresModule(...)]` if the capability is optional per store.

> **In Souq:** `ShippingMethodConfiguration` indexes `(TenantId, IsActive, SortOrder)`, the order checkout reads them in. `Phase12Shipping` added the table and the order's shipping snapshot. The controller is about 40 lines: `GET`, `POST`, `PUT {id}`, `DELETE {id}` under `api/admin/shipping-methods`, each sending one request.

## 7. Add the frontend API call

Add one named function per endpoint to `frontend/src/api/client.js`, next to its neighbours. It speaks the domain's language and hides HTTP from pages. Errors arrive as exceptions carrying the server's stable `code`; never parse messages.

> **In Souq:** `getShippingMethods`, `createShippingMethod`, `updateShippingMethod` and `deleteShippingMethod` call `/admin/shipping-methods`.

## 8. Build the screen

The UI reflects server decisions and never makes them.

- **Route and guard:** add the page to `frontend/src/App.jsx` with its permission (`guarded(...)` for store admin, `platformGuarded(...)` for the platform) and to the navigation with the same permission.
- **Server state:** new screens use TanStack Query. Add a key in `frontend/src/app/queryKeys.js`, read with `useQuery` (`keepPreviousData` for paged lists), and invalidate the key after a write. The step-by-step recipe is the "Build a server-state screen" section of [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md) §16; `frontend/src/pages/platform/Accounts.jsx` is the reference to copy.
- **Pure logic** (form ↔ payload, validation that mirrors server limits) goes in a typed `.js` file under `frontend/src/features` with a Vitest test. `npm run typecheck` checks `.js` files, not `.jsx`.
- **Every state:** loading, error with retry, empty, and success.
- **Destructive actions** confirm through `useConfirmAction` (`frontend/src/components/common/useConfirmAction.jsx`). The server's refusal is shown inside the dialog.
- **Text:** every string in both `frontend/src/i18n/locales/en.json` and `frontend/src/i18n/locales/ar.json`. Wrap names inside sentences with the `bidi` formatter. No brand or currency literals (`whiteLabel.test.js` and `WhiteLabelSourceTests` fail otherwise).
- **Layout and accessibility:** design-system components (`DataTable`, `Drawer`, `FormField`, `Button`), logical CSS properties so right-to-left works, labels linked to inputs.

> **In Souq:** `ShippingMethods.jsx` lists the methods in a `DataTable`, edits them in `ShippingMethodFormDrawer.jsx` and confirms deletion with `useConfirmAction`. `shippingForm.js` converts between the form and the payload and is unit-tested.
>
> This screen predates the query layer and still loads with `useEffect`. Write new screens the Accounts way, not this way.

## 9. Add the tests

| Level | What to prove | Shipping methods |
|---|---|---|
| Domain | Every invariant, including the refusal | `tests/Souq.Domain.Tests/ShippingMethodTests.cs` |
| Application | Orchestration, the store's currency, error codes | `tests/Souq.Application.Tests/Shipping/ShippingMethodHandlersTests.cs` |
| Integration (real API, real SQL Server) | The endpoint end to end; rules refused with their code; a customer can't reach admin routes; another store's id answers `404` | `tests/Souq.IntegrationTests/ShippingTests.cs`, `tests/Souq.IntegrationTests/TenantIsolationTests.cs` |
| Frontend unit (Vitest) | Payload builders, view logic, and the page's behaviour with a mocked API | `frontend/src/features/admin/shipping/shippingForm.test.js`, `frontend/src/features/checkout/shippingOptions.test.js` |

A feature that touches **money, stock or personal data** without an integration test isn't finished. Neither is one that adds an endpoint with an id without an isolation row.

## 10. Add a browser journey when the flow needs one

Vitest can't prove what only a browser shows: real hosts, contrast in dark mode, focus, a multi-step flow across pages. Add or extend a Playwright journey in `frontend/e2e` when the feature:
- is a user flow that crosses pages or hosts;
- adds a destructive action;
- changes something visual in both languages or modes.

Run it against a live stack ([DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md) has the runbook). Keep one sign-in per file: the server allows ten sign-ins a minute. Clean up any data the journey creates.

> **In Souq:** the store-admin confirmations, including deleting a category and removing a product image, are proven in `frontend/e2e/back-office.spec.js`. The screens' phone layout is checked in `frontend/e2e/responsive.spec.js`.

## 11. Record the documentation and decisions

In the same commit as the code:
- the module's `README.md`: concepts, use cases, data ownership, failure modes, screens;
- new rules in [BusinessRules.md](../01-REQUIREMENTS/BusinessRules.md), a new capability in [Traceability.md](../10-TESTING/Traceability.md);
- regenerate the inventories after changing a controller, a use case or a test file:
  `SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"`
- **an ADR** if the decision is architectural: a new dependency, a new module boundary or contract arrow, a different transaction or consistency strategy, anything touching tenant isolation, authentication or payment flow, or a deliberate non-choice ([ADR index §1](../11-ADR/README.md#1-how-to-use-this-index)). Shipping got one ([ADR-0032](../11-ADR/0032-shipping-methods.md)) because it defined how rates enter the pricing pipeline and what an order freezes.

Backticks in documents mean "exists in the repository"; write planned names in *italics*. `DocumentationTests` enforces this.

## 12. Verify before committing

Run the gate in [DeveloperQualityGates.md](../09-OPERATIONS/DeveloperQualityGates.md), the canonical list that CI mirrors:
- backend build with warnings as errors;
- the Domain, Application and Architecture suites;
- the Integration suite (needs Docker);
- frontend lint, typecheck, Vitest and build;
- the browser journeys your change affects.

Then review your own diff with [CodeReviewGuide.md](CodeReviewGuide.md) and ask:
- Did I add an arrow between modules?
- Did I put a rule outside the Domain?
- Did I make the frontend authoritative for anything?
- Is every new id-bearing endpoint in the isolation table?

Commit with a Conventional Commit message that says what changed and why (`feat(shipping): …`).

---

## When the sequence is shorter

| Kind of feature | What changes |
|---|---|
| **Read-only screen** | No domain change: a query, a projection in the module's query service, an endpoint, a page. Still paged, authorized and tested. |
| **A new setting for stores** | Platform module: the settings document and its validation in `src/Souq.Domain/Platform/StoreSettings.cs`, the options the editor may offer, the storefront configuration if visitors need it. The settings editor is shared by the store admin and the platform, so change it once. |
| **Frontend-only change** | No backend work, but check that the server already enforces whatever the UI now implies. |
| **An optional capability** | A module flag stores can turn off, enforced on the server (`[RequiresModule]` and in the use case) as well as hidden in the UI (`useModule`). |
| **An internal job** | A command plus a hosted service; make it idempotent and run it inside an explicit store scope (`StoreSweepService` is the base for per-store sweeps). |
| **A new module** | Name it after a capability; add it to [Modules.md](../04-MODULES/Modules.md), `tests/Souq.ArchitectureTests/ModuleMap.cs` and the allowed contracts in the architecture tests; refuse cycles; create its `docs/04-MODULES/<Module>/README.md`; write an ADR. |

## When to stop and ask

Before building, stop for anything that:
- changes commercial behaviour by guessing (prices, refunds, limits, tax);
- stores new personal data;
- opens a new public endpoint to sensitive data;
- needs a new dependency;
- contradicts an ADR.

Do the safe part, then ask (`AGENTS.md` §9). The storefront preview is a worked example of stopping: [StorefrontPreview.md](../04-MODULES/Platform/StorefrontPreview.md).
