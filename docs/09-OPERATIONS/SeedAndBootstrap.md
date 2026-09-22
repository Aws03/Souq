# Seeding and first-run bootstrap

> **The question this page answers:** what exactly ends up in a database the first time the application starts
> against it, and what a real deployment must do deliberately before it takes customers.
>
> Migrations and seeding both run at startup, in every environment ([Deployment.md](Deployment.md) §2). So
> "deploying" and "writing to the database" are the same act, and what gets written depends on the environment
> and on three settings.

## 1. What each environment gets

| | Development | Testing | Staging / Production |
|---|---|---|---|
| Schema migrations | yes | yes | yes |
| Default store row (id 1) | **yes — written by the migration itself** | yes | **yes** (§3) |
| Demo catalog and branding | yes | yes | **no**, unless `Seed:DemoData=true` |
| Store admin | published demo credentials | from `Seed:Admin*` only | from `Seed:Admin*` only |
| Platform owner | published demo credentials | from `Seed:PlatformOwner*` only | from `Seed:PlatformOwner*` only |
| Host → store binding | `Seed:DefaultTenantHosts` | same | same |

`Seed:DemoData` overrides the environment **in both directions**: `true` puts the demo catalog on a server for a
sales demo, `false` gives a developer a clean local database. The environment only decides the default.

`SeedSafetyTests` proves this by booting a real server against a genuinely empty database with production-shaped
settings and reading the tables back: no products, no categories, no accounts at all, and a default store whose
settings were never touched.

## 2. The published demo credentials exist in Development only

`admin@souq.com` / `Admin@123` and `owner@souq.com` / `Owner@12345` are written **only** when the environment is
`Development`. They are in the source, so treat them as public. Outside Development the first accounts come from
configuration and nowhere else:

```bash
Seed__AdminEmail=owner@yourstore.example
Seed__AdminPassword=<at least 12 characters>
Seed__PlatformOwnerEmail=you@yourcompany.example
Seed__PlatformOwnerPassword=<at least 12 characters>
```

### 2a. The local demo stack, and why it needs a script

That rule has an awkward consequence, and it is worth stating plainly because it cost real time before it was
written down. The `docker compose` stack runs as **`Production`**, so the fallback above does not apply to it:
with `.env.example`'s `SEED_ADMIN_*` left empty — which is correct, because a published password in a repository
is a published password — `docker compose up` produces a stack that boots, serves, and **that nobody can sign in
to**. The seeder says so in the log and carries on. Every person who wanted a demo therefore invented an env file
of their own, outside the repository, which means the working stack was never reproducible from a clean clone.

[`scripts/demo-up.sh`](../../scripts/demo-up.sh) resolves both halves without trading one for the other:

- **The secrets are generated on the machine and never committed** — a fresh SQL password and JWT key are written
  to `.env.demo`, which is in `.gitignore`. They are regenerated only if that file is deleted, because rotating
  the SQL password against an existing data volume gives you a database that will not open.
- **The demo accounts are documented in the repository on purpose**, exactly as the Development pair above is:

  | | |
  |---|---|
  | Store admin (`localhost`) | `admin@souq.com` / `Admin@123456` |
  | Platform owner (`admin.localhost`) | `owner@souq.com` / `Owner@123456` |

  These are not secrets. They open a stack whose payment gateway is the fake one (every payment "succeeds" and no
  money moves), whose email provider is `Log` (nothing is ever delivered), and whose catalogue is seeded demo
  data. Both are twelve characters and differ from the Development password, which is what `DbSeeder`'s
  out-of-Development guard requires — the script does not weaken that guard, it satisfies it.

**Never reuse either pair anywhere a real customer can reach.** A deployment gets its `Seed:*` values from its own
environment, as §2 says, and nothing in this repository should ever be the source of them.

A password shorter than 12 characters **fails the boot** rather than creating a weak account. Remove both pairs
from the environment after the first successful start — they are only read when the account does not yet exist.

## 3. The default store, and why it is in your production database

The `Phase2MultiTenancy` migration inserts store id 1 — slug `marka`, name **"Marka Demo"**, currency JOD,
Arabic, Asia/Amman — into **every** database it runs against. That is not a seeding bug that can simply be
deleted: it is the container every pre-multi-tenancy row was migrated into, and a deployment that has already
adopted and filled it would lose its data if a later migration removed it.

So a fresh production database legitimately starts with an **Active store carrying the demo's name**. Its
branding, contact details, SEO text and catalog are *not* applied (those need `Seed:DemoData`), but the store
exists, it is open, and `Seed:DefaultTenantHosts` may point a real domain at it.

**The startup log says so, every time, until it is resolved:**

```
المتجر الافتراضي ما زال باسم البذر 'Marka Demo' وحالته Active …
المتجر الافتراضي فعّال وبلا أي مدير: لا أحد يستطيع إدارته …
```

Nothing renames or closes it automatically. Both are the owner's decision — this may genuinely be their store —
and silently changing a tenant row is exactly the kind of thing that must never happen on its own.

### Choose one, deliberately

**A — Adopt it** (the common case: this *is* your first store). Sign in as the platform owner on the platform
host and set its real name, currency, culture and domain; or set them directly before opening the store. Then
bind the real domain instead of `localhost`:

```bash
Seed__DefaultTenantHosts=yourstore.example
```

Note that **currency cannot be changed once orders exist** — set it before taking a single order.

**B — Archive it** and provision a real store from the platform area:

```
POST /api/platform/tenants/{id}/status   {"action":"Archive"}
```

An archived store answers `503 StoreUnavailable` for everything except the storefront-config endpoint, so no
customer can reach it.

Either way the warnings stop once the name is no longer the seeded one and the store has an administrator.

## 4. The deterministic first-production bootstrap

In order, for a brand-new production database:

1. **Create the database** with an administrative login — neither the runtime nor the migration identity can
   (`CREATE DATABASE` is not granted to either: [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md)).
2. **Create the two logins** — `scripts/sql/least-privilege-logins.sql`.
3. **Set the configuration**: both connection strings, a real `Jwt:Key` (the `.env.example` placeholder is
   rejected at startup), the payment and email providers, `Seed:Admin*` and `Seed:PlatformOwner*`, and
   `Seed:DefaultTenantHosts`.
4. **Start the API.** Migrations run, the default store row appears, the two accounts are created.
5. **Read the startup log.** It names the payment and email adapters actually chosen, and every configuration
   warning — including the unadopted-store ones above.
6. **Adopt or archive the default store** (§3).
7. **Remove the seed credentials** from the environment.
8. **Take a backup before opening**, and rehearse restoring it
   ([BackupAndRestore.md](BackupAndRestore.md)).

## 5. What cannot happen by accident

Each of these is enforced in code and covered by a test, not merely intended:

| | Why it cannot |
|---|---|
| Demo catalog in production | `ShouldSeedDemoData` returns false outside Development/Testing unless `Seed:DemoData=true` is set explicitly |
| Published demo admin in production | gated on `IsDevelopment`; `StartupAndSecurityTests` proves those credentials cannot sign in |
| A weak first admin | a password under 12 characters throws at startup instead of creating the account |
| Fake payments in production | `PaymentProviderSelector` refuses to boot without an explicit `Payments:Provider=Demo` |
| Email silently going nowhere | no provider outside Development/Testing throws unless `Email:Provider=Log` is explicit |
| A published signing key | `Jwt:Key` containing placeholder text is rejected ([Configuration.md](Configuration.md) §2) |
| A store domain silently stolen | `BindDefaultTenantHosts` skips any host already bound to another store |

## 6. Related

[Configuration.md](Configuration.md) §7 (every seed key) · [Deployment.md](Deployment.md) §2 (the startup
sequence) · [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md) · [BackupAndRestore.md](BackupAndRestore.md)
