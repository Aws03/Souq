# Marka 🛍️

<p>
  <img alt="Build" src="https://img.shields.io/badge/build-passing-brightgreen?style=flat-square" />
  <img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" />
  <img alt="Tests" src="https://img.shields.io/badge/tests-233%20passing-brightgreen?style=flat-square" />
  <img alt="PRs Welcome" src="https://img.shields.io/badge/PRs-welcome-orange?style=flat-square" />
</p>

**Marka** is a full-stack e-commerce platform — a real, deployable storefront and
admin backend, not a toy demo. It pairs a **.NET 10 REST API** built on strict
Clean Architecture with a **React 18** RTL storefront, covering the full
commerce loop: catalog, cart, coupons, real Stripe payments, reviews, and order
management.

<p>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white" />
  <img alt="React" src="https://img.shields.io/badge/React_18-61DAFB?style=for-the-badge&logo=react&logoColor=black" />
  <img alt="Vite" src="https://img.shields.io/badge/Vite-646CFF?style=for-the-badge&logo=vite&logoColor=white" />
  <img alt="SQL Server" src="https://img.shields.io/badge/SQL_Server_2022-CC2927?style=for-the-badge&logo=microsoftsqlserver&logoColor=white" />
  <img alt="Docker" src="https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white" />
  <img alt="Stripe" src="https://img.shields.io/badge/Stripe-635BFF?style=for-the-badge&logo=stripe&logoColor=white" />
</p>

---

## Table of Contents

- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Features](#features)
- [Quick Start](#quick-start)
- [Project Structure](#project-structure)
- [API Reference](#api-reference)
- [Environment Variables](#environment-variables)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)

---

## Tech Stack

| Layer          | Technology                                                                 |
| -------------- | --------------------------------------------------------------------------- |
| **Backend**    | C# · .NET 10 · ASP.NET Core Web API · MediatR (CQRS) · FluentValidation     |
| **Persistence**| Entity Framework Core 10 · SQL Server 2022                                  |
| **Auth**       | JWT Bearer tokens · BCrypt password hashing                                 |
| **Payments**   | Stripe.js + Stripe.NET (real Elements-based checkout, with a fake gateway fallback for local dev) |
| **Frontend**   | React 18 · Vite 5 · React Router 6 · RTL, Arabic-first UI                   |
| **Testing**    | xUnit · AwesomeAssertions · NSubstitute · Testcontainers (SQL Server) · NetArchTest · Vitest — 233 tests |
| **DevOps**     | Docker · Docker Compose (fully isolated 3-container stack)                  |
| **API Docs**   | Swagger / OpenAPI                                                            |

---

## Architecture

Marka's backend follows **Clean Architecture** with a strict, one-directional
dependency rule: outer layers depend on inner layers, never the reverse. The
Domain layer has zero dependencies — it is pure business logic.

```
┌──────────────────────────────────────────────────────────────────┐
│                             Souq.API                             │
│                                                                  │
│          Controllers · Middleware · Swagger · JWT Auth           │
└──────────────────────────────────────────────────────────────────┘

                     │ depends on ▼

┌──────────────────────────────────────────────────────────────────┐
│                       Souq.Infrastructure                        │
│                                                                  │
│   EF Core · SQL Server · Stripe · Repositories · Unit of Work    │
└──────────────────────────────────────────────────────────────────┘

                     │ depends on ▼

┌──────────────────────────────────────────────────────────────────┐
│                         Souq.Application                         │
│                                                                  │
│        CQRS (MediatR) · FluentValidation · Result Pattern        │
└──────────────────────────────────────────────────────────────────┘

                     │ depends on ▼

┌──────────────────────────────────────────────────────────────────┐
│                           Souq.Domain                            │
│                                                                  │
│      Entities · Value Objects · Business Rules · Interfaces      │
│                                                                  │
│      (depends on NOTHING — the center of the architecture)       │
└──────────────────────────────────────────────────────────────────┘
```

**Why it matters:** business rules (Domain) change slowly and hold the highest
value; technology choices (database, payment gateway, even REST itself) change
fast. Isolating the slow-changing core from the fast-changing shell means
swapping SQL Server for Postgres, or Stripe for another provider, touches
**zero** business logic.

Key patterns applied throughout:

- **Domain entities guard their own invariants** — no anemic models, no business rules leaking into handlers or controllers.
- **Value Objects** (`Money`) — immutable, currency-safe, equality by value.
- **CQRS via MediatR** — commands mutate, queries read; each has a single handler.
- **Result pattern** — expected failures are explicit return values, not exceptions.
- **Repository + Unit of Work** — persistence is abstracted behind Domain-defined interfaces (Dependency Inversion).
- **Centralized validation & error handling** — a MediatR pipeline behavior runs FluentValidation before every command; a single middleware shapes every error response.

---

## Features

**Storefront**
- Product catalog with search, category filters, and pagination
- Shopping cart and checkout flow
- Coupon codes (percentage or fixed-amount discounts, start and end dates, and total and per-customer usage limits that hold under concurrent checkouts)
- Real payments via Stripe Elements, with idempotent webhook-based order confirmation
- Product reviews and ratings
- Fully RTL, Arabic-first UI (Marka brand identity)

**Admin**
- Product, category, and coupon management (CRUD)
- Order status management
- Role-based access control (JWT + `Admin` role claim)

**Platform**
- Clean Architecture with strict layer boundaries
- Centralized validation, error handling, and Result-based error contracts
- Auto-applied EF Core migrations and idempotent seed data on startup
- Fully containerized, isolated Docker stack
- 233 automated tests: domain rules, use cases, architecture rules, real SQL Server integration, frontend logic

---

## Quick Start

### Option A — Docker (recommended, one command)

Spins up an isolated SQL Server, the API, and the frontend — nothing touches
your host's existing services.

```bash
cp .env.example .env   # fill in DB_SA_PASSWORD, JWT_KEY, SEED_ADMIN_EMAIL, SEED_ADMIN_PASSWORD
docker compose up --build
```

| Service   | URL                              |
| --------- | --------------------------------- |
| Frontend  | http://localhost:8081             |
| API       | http://localhost:5201/swagger     |

Migrations and seed data run automatically on API startup. **Payments:** set
Stripe test keys, or `PAYMENTS_PROVIDER=Fake` for a demo in which every payment
succeeds without money (never with real customers). With neither, the API
refuses to start — the Docker stack runs in Production mode. Local
`dotnet run` (Development) uses the fake gateway automatically.

### Option B — Local development

```bash
# 1) Backend secrets (never commit connection strings or keys)
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;" \
  --project src/Souq.API

# 2) Run the API — applies migrations & seeds data automatically
#    (Development admin: admin@souq.com / Admin@123, or set Seed:AdminEmail / Seed:AdminPassword)
dotnet run --project src/Souq.API
# → http://localhost:5200/swagger

# 3) Run the frontend
cd frontend && npm install && npm run dev
# → http://localhost:5173 (proxies /api to the backend)
```

---

## Project Structure

```
Marka/
├── src/
│   ├── Souq.Domain/          # Entities, Value Objects, business rules. Zero dependencies.
│   ├── Souq.Application/     # Use cases (CQRS): Commands, Queries, validators, DTOs.
│   ├── Souq.Infrastructure/  # EF Core, repositories, Stripe integration, file storage.
│   └── Souq.API/             # ASP.NET Core entry point: controllers, middleware, JWT config.
├── frontend/                 # React + Vite storefront and admin UI (Marka brand).
├── tests/
│   ├── Souq.Domain.Tests/        # Entity and Value Object unit tests.
│   ├── Souq.Application.Tests/   # Command/query handler unit tests.
│   ├── Souq.ArchitectureTests/   # Dependency rules enforced as tests (NetArchTest).
│   └── Souq.IntegrationTests/    # Real API + SQL Server in Docker (Testcontainers).
├── docs/                      # Architecture notes and the project's original design log.
├── docker-compose.yml         # Isolated 3-container stack (db, api, web).
└── .env.example                # Template for Docker Compose secrets.
```

---

## API Reference

Full interactive documentation is served by Swagger at `/swagger` once the API
is running. Summary of the main endpoints:

### Auth
| Method | Endpoint             | Auth | Description            |
| ------ | --------------------- | ---- | ----------------------- |
| POST   | `/api/auth/register`  | —    | Create a customer account |
| POST   | `/api/auth/login`     | —    | Exchange credentials for a JWT |

### Products
| Method | Endpoint                                                    | Auth  | Description                     |
| ------ | ------------------------------------------------------------ | ----- | -------------------------------- |
| GET    | `/api/products?keyword=&categoryIds=&minPrice=&maxPrice=&sortBy=&onSale=&page=1&pageSize=12` | — | Search / filter / sort / paginate published products |
| GET    | `/api/products/{id}`                                          | —     | Get a single product (every translation, gallery) |
| GET    | `/api/products/by-slug/{slug}`                                | —     | Get a product by its store-unique slug |
| POST   | `/api/products`                                                | Admin | Create a product (per-language texts, SKU, price, compare-at price, status) |
| PUT    | `/api/products/{id}`                                           | Admin | Update a product                 |
| DELETE | `/api/products/{id}`                                           | Admin | Archive a product (never hard-deleted) |
| POST   | `/api/products/{id}/image`                                     | Admin | Add an image to the gallery (up to 10) |
| GET    | `/api/admin/products`                                          | Admin | Every status; search by name, SKU or slug |
| GET    | `/api/admin/products/{id}`                                     | Admin | Full product for editing         |
| PUT    | `/api/admin/products/{id}/status`                              | Admin | Publish, move to draft, archive, or restore |
| PUT    | `/api/admin/products/{id}/images/order`                        | Admin | Reorder the gallery (the first image is the main one) |
| DELETE | `/api/admin/products/{id}/images/{imageId}`                    | Admin | Remove a gallery image           |

### Categories
| Method | Endpoint               | Auth  | Description       |
| ------ | ------------------------ | ----- | ------------------- |
| GET    | `/api/categories`        | —     | List visible categories |
| GET    | `/api/admin/categories`  | Admin | List every category, including hidden ones |
| POST   | `/api/categories`        | Admin | Create a category (per-language names, parent, order, visibility) |
| PUT    | `/api/categories/{id}`   | Admin | Update or move a category (no cycles, at most 5 levels) |
| DELETE | `/api/categories/{id}`   | Admin | Delete an empty category (no products, no subcategories) |

### Inventory
| Method | Endpoint                                         | Auth  | Description                                   |
| ------ | ------------------------------------------------- | ----- | ---------------------------------------------- |
| GET    | `/api/admin/inventory`                            | Admin | On hand, reserved and available per product, lowest first |
| GET    | `/api/admin/inventory/low-stock`                  | Admin | Products at or below their alert threshold    |
| GET    | `/api/admin/inventory/{productId}/movements`      | Admin | The stock ledger, newest first                |
| POST   | `/api/admin/inventory/{productId}/adjustments`    | Admin | Correct stock by a delta with a reason        |
| PUT    | `/api/admin/inventory/{productId}/threshold`      | Admin | Set the low-stock alert threshold             |

Placing an order reserves stock; paying commits it; cancelling releases it (or restocks a paid order).
Unpaid checkouts expire after `Inventory:ReservationMinutes` (default 30).

### Customer account
| Method | Endpoint                                           | Auth     | Description                                         |
| ------ | --------------------------------------------------- | -------- | ---------------------------------------------------- |
| GET    | `/api/account/profile`                              | Customer | The caller's profile and addresses                  |
| PUT    | `/api/account/profile`                              | Customer | Update name and phone                               |
| GET    | `/api/account/addresses`                            | Customer | The address book (default shipping first)           |
| POST   | `/api/account/addresses`                            | Customer | Add an address, optionally as a default             |
| PUT    | `/api/account/addresses/{id}`                       | Customer | Update an address (own addresses only; others are 404) |
| DELETE | `/api/account/addresses/{id}`                       | Customer | Remove an address                                   |
| PUT    | `/api/account/addresses/{id}/default-shipping`      | Customer | Make it the default shipping address                |
| PUT    | `/api/account/addresses/{id}/default-billing`       | Customer | Make it the default billing address                 |
| GET    | `/api/account/export`                               | Customer | Download all of the caller's data (JSON)            |
| POST   | `/api/account/erase`                                | Customer | Delete the account (password required; orders are kept) |

### Customers (admin)
| Method | Endpoint                                                      | Auth  | Description                                        |
| ------ | -------------------------------------------------------------- | ----- | --------------------------------------------------- |
| GET    | `/api/admin/customers?keyword=&status=&page=1&pageSize=20`     | Admin | Customers with order count, spend and last order   |
| GET    | `/api/admin/customers/{id}`                                    | Admin | Detail with addresses (orders: `/api/orders?customerId=`) |
| PUT    | `/api/admin/customers/{id}/status`                             | Admin | Block or unblock                                   |
| GET    | `/api/admin/customers/{id}/export`                             | Admin | Export a customer's data                           |
| POST   | `/api/admin/customers/{id}/erase`                              | Admin | Erase a customer's personal data                   |

### Basket
| Method | Endpoint                               | Auth            | Description                                               |
| ------ | --------------------------------------- | --------------- | ---------------------------------------------------------- |
| GET    | `/api/basket`                           | Guest/Customer  | The caller's basket, priced from the live catalog         |
| GET    | `/api/basket/quote?couponCode=`         | Guest/Customer  | The basket priced with a coupon (same pipeline as checkout) |
| POST   | `/api/basket/items`                     | Guest/Customer  | Add a product (up to the available quantity)              |
| PUT    | `/api/basket/items/{productId}`         | Guest/Customer  | Set a line's quantity (0 removes it)                      |
| DELETE | `/api/basket/items/{productId}`         | Guest/Customer  | Remove a line                                             |
| DELETE | `/api/basket`                           | Guest/Customer  | Empty the basket                                          |

Guests are identified by an HttpOnly cookie that the server sets; signing in merges the guest basket into the customer's.
Baskets never reserve stock; checkout does.

### Coupons
| Method | Endpoint                                          | Auth  | Description                              |
| ------ | --------------------------------------------------- | ----- | ------------------------------------------ |
| GET    | `/api/coupons/apply?code=&subtotal=&currency=JOD`   | —     | Preview a coupon's discount before checkout |
| GET    | `/api/coupons?page=1&pageSize=20`                    | Admin | List all coupons                          |
| POST   | `/api/coupons`                                        | Admin | Create a coupon                           |
| PUT    | `/api/coupons/{id}`                                   | Admin | Update a coupon                           |
| GET    | `/api/coupons/{id}/redemptions?page=1&pageSize=20`    | Admin | Orders that used a coupon, with their status |
| DELETE | `/api/coupons/{id}`                                   | Admin | Delete an unused coupon (a used one answers `409 CouponInUse`) |

### Orders & Payments
| Method | Endpoint                                | Auth      | Description                                 |
| ------ | ------------------------------------------ | --------- | --------------------------------------------- |
| POST   | `/api/orders`                                | Customer  | Place an order from the basket (or explicit lines) and start payment |
| POST   | `/api/orders/{id}/cancel`                    | Customer  | Cancel an unpaid order (own orders only)      |
| GET    | `/api/orders/track/{token}`                  | —         | Public tracking by random token (status and shipment only) |
| POST   | `/api/orders/{id}/confirm-payment`           | Customer  | Confirm payment client-side after Stripe Elements |
| GET    | `/api/orders/{id}`                            | Customer  | Get order details (own orders only; others are 404) |
| GET    | `/api/orders/mine?page=1&pageSize=20`          | Customer  | The current customer's orders (paged)         |
| GET    | `/api/orders?status=&search=&from=&to=&customerId=` | Admin | List orders, filtered by status, number, customer or date |
| PUT    | `/api/orders/{id}/status`                      | Admin     | Update order status (cancelling a paid order refunds it) |
| POST   | `/api/orders/{id}/refunds`                     | Admin     | Refund part or all of an order's payment (idempotent, can't exceed it) |
| POST   | `/api/orders/{id}/refunds/{refundId}/retry`    | Admin     | Retry a refund the gateway didn't answer      |
| GET/PUT/DELETE | `/api/admin/store/payments`            | Admin     | The store's own Stripe account (keys encrypted, never returned) |
| GET    | `/api/payments/config`                         | —         | The publishable key of the store's account, or the platform's |
| POST   | `/api/payments/webhook`                        | —         | Stripe webhook (signature-verified, idempotent, routed to the order's store) |

### Reviews
| Method | Endpoint                              | Auth     | Description               |
| ------ | ---------------------------------------- | -------- | --------------------------- |
| GET    | `/api/products/{productId}/reviews`      | —        | List reviews for a product |
| POST   | `/api/products/{productId}/reviews`      | Customer | Submit a review             |

All error responses are RFC 7807 `application/problem+json` with a stable
`code` (the contract clients branch on) and a `traceId`; every response carries
an `X-Correlation-Id` header with the same id. Details: [docs/ApiDocumentation.md](docs/ApiDocumentation.md).

---

## Environment Variables

Used by `docker-compose.yml` (copy `.env.example` to `.env` and fill in real values):

| Variable                 | Required | Description                                                                 |
| ------------------------- | -------- | ----------------------------------------------------------------------------- |
| `DB_SA_PASSWORD`          | Yes      | SQL Server `sa` password for the isolated Docker DB container (must meet SQL Server's complexity policy). |
| `JWT_KEY`                 | Yes      | Signing secret for JWT access tokens: at least 32 bytes of random text (the API refuses to start with a shorter key). |
| `STRIPE_SECRET_KEY`       | Yes, unless `PAYMENTS_PROVIDER=Fake` | Stripe secret key (test keys work). |
| `STRIPE_PUBLISHABLE_KEY`  | With Stripe | Stripe publishable key, exposed to the frontend via `/api/payments/config`. |
| `PAYMENTS_PROVIDER`       | No       | Empty = Stripe. `Fake` = a demo gateway where every payment succeeds without money; logged as a warning at every start. Never use it with real customers. |
| `STRIPE_WEBHOOK_SECRET`   | No       | Stripe webhook signing secret, used to verify incoming webhook events.       |
| `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` | First start | Creates the first admin outside Development (password ≥ 12 chars). **No default admin exists outside Development.** Remove after the first start. |
| `RESEND_API_KEY` / `BREVO_API_KEY` (+ `BREVO_SENDER_EMAIL`) / `GMAIL_APP_PASSWORD` (+ `GMAIL_USERNAME`) | No | Email provider, in priority order. Without one, emails are not sent: a warning is logged, and reset links are never logged outside Development. |
| `FRONTEND_URL`            | No       | Base URL used in email links (default `http://localhost:8081`). |

For local (non-Docker) development, the connection string and JWT key are
configured via [.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
instead of environment variables — **never commit secrets to `appsettings.json`.**

---

## Testing

```bash
dotnet test
```

| Suite | What it proves | Needs |
| --- | --- | --- |
| `Souq.Domain.Tests` (95) | Entity invariants, money precision, state machines, stable error codes | — |
| `Souq.Application.Tests` (118) | Use-case orchestration, ownership and permissions, validation, paging, upload sniffing, use-case logging | — |
| `Souq.ArchitectureTests` (24) | Layer and module rules, no entities in contracts, no `IQueryable` leaks, no direct clock reads, `TenantId` tripwire | — |
| `Souq.IntegrationTests` (95) | Migrations, error contract, authorization matrix, concurrency, JOD precision, paging/sorting/N+1, logging and correlation, startup configuration, uploads, password reset — on real SQL Server | **Docker** |
| `frontend` — `npm test` (20) | Error parsing and code translations, query strings, admin payloads | — |

The integration suite starts `mcr.microsoft.com/mssql/server:2022-latest` through Testcontainers;
the first run pulls the image. See [docs/DevelopmentGuide.md](docs/DevelopmentGuide.md).

---

## Contributing

Contributions are welcome. Please keep changes aligned with the project's
architecture:

1. **Respect the dependency rule.** `API → Infrastructure → Application → Domain`.
   Domain must never depend on an outer layer — check the `.csproj` references
   before adding a `using`.
2. **Business rules live in the Domain layer**, not in controllers or handlers.
   Entities guard their own invariants.
3. **External integrations (payment, storage, email) are abstracted behind an
   interface** defined in Application and implemented in Infrastructure.
4. **No secrets in source control.** Connection strings and keys go through
   user-secrets locally or environment variables in Docker.
5. **Add tests** for new Domain rules and Application handlers.

Workflow:

```bash
git checkout -b feat/your-feature
# make your changes
dotnet build && dotnet test
git commit -m "feat(scope): concise description"
git push origin feat/your-feature
```

Open a pull request describing the change and why it's needed. Please follow
[Conventional Commits](https://www.conventionalcommits.org/) for commit
messages (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, …).

---

## License

Distributed under the [MIT License](LICENSE).
