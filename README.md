# Marka 🛍️

<p>
  <img alt="Build" src="https://img.shields.io/badge/build-passing-brightgreen?style=flat-square" />
  <img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" />
  <img alt="Tests" src="https://img.shields.io/badge/tests-111%20passing-brightgreen?style=flat-square" />
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
| **Testing**    | xUnit · FluentAssertions · NSubstitute (111 unit tests across Domain/Application) |
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
- Coupon codes (percentage or fixed-amount discounts, with usage limits and expiry)
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
- 111 unit tests covering Domain and Application layers

---

## Quick Start

### Option A — Docker (recommended, one command)

Spins up an isolated SQL Server, the API, and the frontend — nothing touches
your host's existing services.

```bash
cp .env.example .env   # fill in DB_SA_PASSWORD and JWT_KEY
docker compose up --build
```

| Service   | URL                              |
| --------- | --------------------------------- |
| Frontend  | http://localhost:8081             |
| API       | http://localhost:5201/swagger     |

Migrations and seed data run automatically on API startup. Without a Stripe
key configured, payments run through a built-in fake gateway that always
succeeds — the app works out of the box with zero external accounts.

### Option B — Local development

```bash
# 1) Backend secrets (never commit connection strings or keys)
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<your-password>;Encrypt=True;TrustServerCertificate=True;" \
  --project src/Souq.API

# 2) Run the API — applies migrations & seeds data automatically
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
├── database/                 # Raw SQL schema/seed scripts (for manual DB setup).
├── tests/
│   ├── Souq.Domain.Tests/       # Entity and Value Object unit tests.
│   └── Souq.Application.Tests/  # Command/query handler unit tests.
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
| GET    | `/api/products?keyword=&categoryId=&page=1&pageSize=12`      | —     | Search / filter / paginate products |
| GET    | `/api/products/{id}`                                          | —     | Get a single product             |
| POST   | `/api/products`                                                | Admin | Create a product                 |
| PUT    | `/api/products/{id}`                                           | Admin | Update a product                 |
| DELETE | `/api/products/{id}`                                           | Admin | Deactivate a product (soft delete) |
| POST   | `/api/products/{id}/image`                                     | Admin | Upload a product image           |

### Categories
| Method | Endpoint               | Auth  | Description       |
| ------ | ------------------------ | ----- | ------------------- |
| GET    | `/api/categories`        | —     | List all categories |
| POST   | `/api/categories`        | Admin | Create a category   |
| PUT    | `/api/categories/{id}`   | Admin | Update a category   |
| DELETE | `/api/categories/{id}`   | Admin | Delete a category   |

### Coupons
| Method | Endpoint                                          | Auth  | Description                              |
| ------ | --------------------------------------------------- | ----- | ------------------------------------------ |
| GET    | `/api/coupons/apply?code=&subtotal=&currency=JOD`   | —     | Preview a coupon's discount before checkout |
| GET    | `/api/coupons?page=1&pageSize=20`                    | Admin | List all coupons                          |
| POST   | `/api/coupons`                                        | Admin | Create a coupon                           |
| PUT    | `/api/coupons/{id}`                                   | Admin | Update a coupon                           |
| DELETE | `/api/coupons/{id}`                                   | Admin | Delete a coupon                           |

### Orders & Payments
| Method | Endpoint                                | Auth      | Description                                 |
| ------ | ------------------------------------------ | --------- | --------------------------------------------- |
| POST   | `/api/orders`                                | Customer  | Place an order and start payment              |
| POST   | `/api/orders/{id}/confirm-payment`           | Customer  | Confirm payment client-side after Stripe Elements |
| GET    | `/api/orders/{id}`                            | Customer  | Get order details                             |
| GET    | `/api/orders?page=1&pageSize=20`               | Admin     | List all orders                               |
| PUT    | `/api/orders/{id}/status`                      | Admin     | Update order status                           |
| GET    | `/api/payments/config`                         | —         | Get the Stripe publishable key                |
| POST   | `/api/payments/webhook`                        | —         | Stripe webhook (signature-verified, idempotent) |

### Reviews
| Method | Endpoint                              | Auth     | Description               |
| ------ | ---------------------------------------- | -------- | --------------------------- |
| GET    | `/api/products/{productId}/reviews`      | —        | List reviews for a product |
| POST   | `/api/products/{productId}/reviews`      | Customer | Submit a review             |

All error responses share a consistent shape: `{ "error": "message", "code": "ErrorCode" }`.

---

## Environment Variables

Used by `docker-compose.yml` (copy `.env.example` to `.env` and fill in real values):

| Variable                 | Required | Description                                                                 |
| ------------------------- | -------- | ----------------------------------------------------------------------------- |
| `DB_SA_PASSWORD`          | Yes      | SQL Server `sa` password for the isolated Docker DB container (must meet SQL Server's complexity policy). |
| `JWT_KEY`                 | Yes      | Signing secret for JWT access tokens. Use a long, random string (32+ chars). |
| `STRIPE_SECRET_KEY`       | No       | Stripe secret key. Omit to fall back to a built-in fake payment gateway that always succeeds. |
| `STRIPE_PUBLISHABLE_KEY`  | No       | Stripe publishable key, exposed to the frontend via `/api/payments/config`.  |
| `STRIPE_WEBHOOK_SECRET`   | No       | Stripe webhook signing secret, used to verify incoming webhook events.       |

For local (non-Docker) development, the connection string and JWT key are
configured via [.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
instead of environment variables — **never commit secrets to `appsettings.json`.**

---

## Testing

```bash
dotnet test
```

111 unit tests cover the Domain layer (entity invariants, value objects) and
the Application layer (command/query handlers), using xUnit, FluentAssertions,
and NSubstitute for mocking.

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
