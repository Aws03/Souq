# Configuration

> Every setting the API reads, where its value may live, and what happens when it is missing or wrong.
> Decisions: [ADR-0020](../11-ADR/0020-configuration-and-secrets.md) (typed options, fail-fast, no silent development fallbacks), [ADR-0006](../11-ADR/0006-tenant-resolution.md) (tenancy), [ADR-0034](../11-ADR/0034-notifications-outbox.md) (email and outbox).
> Running the stack: [Deployment.md](Deployment.md) · Local setup: [DevelopmentGuide.md](DevelopmentGuide.md) · When it fails: [Troubleshooting.md](Troubleshooting.md).

## 1. How configuration is loaded

The API is a default ASP.NET Core host (`src/Souq.API/Program.cs`), so sources are read in this order, each overriding the previous one:

1. `src/Souq.API/appsettings.json` — the only committed defaults. It contains **no secrets**: `ConnectionStrings:Default` and `Jwt:Key` are present but empty, on purpose.
2. `src/Souq.API/appsettings.Production.json` — JSON console logging only. There is no *appsettings.Development.json* and no *appsettings.Testing.json*.
3. User secrets — **Development only** (the host adds them for the Development environment; the id is `UserSecretsId` in `src/Souq.API/Souq.API.csproj`). The integration tests deliberately run as `Testing` so a developer's secrets never leak into a test run (`tests/Souq.IntegrationTests/Infrastructure/SouqApiFactory.cs`).
4. Environment variables — how every deployed value arrives. `:` becomes `__`, array items get a numeric segment: `Jwt:Key` → `Jwt__Key`, `Tenancy:PlatformHosts[0]` → `Tenancy__PlatformHosts__0`.
5. Command-line arguments.

**Environment names matter.** `Development` and `Testing` are "local" (`PaymentProviderSelector.IsLocal`); every other name, including `Staging`, is treated as production-like: no implicit fake payment gateway, no implicit log-only email, no development tenant resolution, no default admin. `docker-compose.yml` sets `ASPNETCORE_ENVIRONMENT: Production`; `src/Souq.API/Properties/launchSettings.json` sets `Development`.

**Where secrets live**

| Environment | Secrets come from | Never |
|---|---|---|
| Development | `dotnet user-secrets set "<key>" "<value>" --project src/Souq.API` | committed files |
| Testing (integration tests) | `SouqApiFactory` sets them in code, per run | user secrets |
| Docker / deployed | environment variables, filled from `.env` (git-ignored; `.env.example` holds placeholders) | `appsettings*.json` |

A managed secret store (Key Vault or similar) is **PLANNED** for Phase 23 ([ProductRoadmap.md](../12-ROADMAP/ProductRoadmap.md)); the options layer does not change when it arrives.

## 2. How settings are validated

Three mechanisms, in the order they run at startup:

1. **Registration-time checks** inside `AddInfrastructure` (`src/Souq.Infrastructure/DependencyInjection.cs`), before the host is even built: the connection string, the payment-provider choice, the email-provider choice. They throw `InvalidOperationException` with a message naming the key.
2. **Typed options with `ValidateOnStart`**, forced by the startup validator that `Program.cs` calls right after `Build()` — before migrations, seeding or any request. Failures surface as an options-validation exception listing every broken rule.
3. **Runtime checks** that no startup path covers (for example a sender address that only the email adapter needs). These fail per message, not at startup — see [§14](#14-settings-that-are-not-validated-gaps).

The startup report (`InfrastructureStartupReport`) then logs one line naming the chosen adapters and one warning line per "works, but not safe for real customers" condition:

```text
Adapters selected: payments {PaymentProvider}, email {EmailProvider}
Configuration warning: {ConfigurationWarning}
```

`tests/Souq.IntegrationTests/ConfigurationTests.cs` proves that a missing connection string, a short `Jwt:Key` and a missing payment provider all stop the startup **before the database is touched**, and that failure messages never print a secret's value.

### Placeholder values are rejected

`Jwt:Key` is checked for length **and** for placeholder text. This is not defensive decoration: the placeholder
shipped in `.env.example` is 33 bytes, so it passed the 32-byte minimum. Copying that file to `.env` without
editing it produced a working deployment whose signing key is published in this repository — and anyone holding
it can mint a valid token for any store. The startup validator now refuses any `Jwt:Key` containing `REPLACE`,
`CHANGE_ME`, `PLACEHOLDER` or `YOUR_KEY`, and `.env.example`'s database password was changed to a value SQL
Server's own password policy rejects, so the demo stack fails loudly instead of running on a published password.
`ConfigurationSourceTests` keeps both properties true as the files change.

## 3. Connection string

| Key | Default | Required | Secret | Validated |
|---|---|---|---|---|
| `ConnectionStrings:Default` | empty in `appsettings.json` | every environment | yes | present and non-blank, in `AddInfrastructure`, before the host is built |
| `ConnectionStrings:Migrations` | unset — falls back to `Default` | production, once least-privilege logins exist | no | none; if unset, migrations use the runtime identity exactly as before |
| `Security:HstsMaxAgeDays` | 30 | any deployment behind TLS | no | none; HSTS is sent only on requests the API sees as https, and never for `localhost` |

Missing value:

```text
سلسلة الاتصال 'Default' غير مضبوطة (ConnectionStrings:Default). للتطوير: dotnet user-secrets set "ConnectionStrings:Default" "..." --project src/Souq.API
```

docker-compose builds it from the SQL container's password: `ConnectionStrings__Default: "Server=db,1433;Database=SouqDb;User Id=sa;Password=${DB_SA_PASSWORD};TrustServerCertificate=True"`, with `DB_SA_PASSWORD` in `.env`. There is no separate variable for the host, database name or login — change the line in `docker-compose.yml` to use a least-privilege login (see [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md)).

**`ConnectionStrings:Migrations`** is used by the startup migrator and by nothing else, so the running application can hold an identity that cannot change the schema. Unset, it falls back to `Default` and behaviour is identical to before the split — an existing deployment upgrades without touching its configuration. The startup log records which identity applied the migrations. Full rationale and the measured permissions: [DatabasePrivileges.md](../07-SECURITY/DatabasePrivileges.md).

## 4. Jwt

Bound to `JwtSettings` (`src/Souq.Infrastructure/Services/JwtSettings.cs`), validated by `JwtSettingsValidator` with `ValidateOnStart`. The same settings both issue tokens (`JwtTokenGenerator`) and validate incoming ones (`Program.cs`).

| Key | Default | Required | Secret | Validation |
|---|---|---|---|---|
| `Jwt:Key` | empty | yes | **yes** | non-blank, ≥ 32 UTF-8 bytes (`MinimumKeyBytes`, the HS256 minimum) |
| `Jwt:Issuer` | `Souq` (appsettings) | yes | no | non-blank |
| `Jwt:Audience` | `SouqClient` (appsettings) | yes | no | non-blank |
| `Jwt:ExpiryMinutes` | 15 | no | no | 5–60 |
| `Jwt:RefreshTokenDays` | 30 | no | no | 1–90 |

Messages:

```text
Jwt:Key غير مضبوط — اضبطه في user-secrets أو متغيّرات البيئة (Jwt__Key).
Jwt:Key أقصر من 32 بايت (256 بت) — الحدّ الأدنى لتوقيع HS256.
Jwt:Issuer مطلوب.
Jwt:Audience مطلوب.
Jwt:ExpiryMinutes (عمر توكن الوصول) يجب أن يكون بين 5 و60 دقيقة.
Jwt:RefreshTokenDays يجب أن يكون بين 1 و90 يوماً.
```

docker-compose: `Jwt__Key: ${JWT_KEY}` (`.env`: `JWT_KEY`), plus `Jwt__Issuer` and `Jwt__Audience` pinned to the appsettings values. Token lifetimes are not exposed as `.env` variables.

Token validation also pins `ClockSkew` to 30 seconds and runs `AccessTokenValidation` on every validated token (the token's store must match the host, and the account's security stamp must still be current).

## 5. Auth (cookies and session lifetimes)

| Key | Default | Required | Secret | Validation |
|---|---|---|---|---|
| `Auth:RefreshCookie:Secure` | `true` (class default and `appsettings.json`) | no | no | none |

`RefreshCookieOptions` (`src/Souq.API/Security/RateLimiting.cs`) is used by **two** cookies, both `HttpOnly`, `SameSite=Strict`, path-scoped:

| Cookie | Set by | Path | Lifetime |
|---|---|---|---|
| `souq_refresh` (`AuthController.RefreshCookieName`) | sign-in, register, refresh, change-password | `/api/auth` | `Jwt:RefreshTokenDays` |
| guest basket cookie | `BasketController` | its own basket path | `Basket:GuestLifetimeDays` |

Setting `Secure=false` is meant only for an http deployment on a non-`localhost` address (browsers treat `localhost` and `*.localhost` as secure contexts even on http). It is **not** exposed by `docker-compose.yml`; a stack served over plain http on a LAN address needs `Auth:RefreshCookie:Secure` added there by hand as an environment variable (double-underscore spelling), otherwise sessions cannot be refreshed.

Access-token lifetime, refresh rotation and session invalidation are described in [ADR-0010](../11-ADR/0010-authentication-authorization.md) and [ADR-0023](../11-ADR/0023-sessions-and-credentials.md).

## 6. Tenancy

`TenancyOptions` (`src/Souq.API/Tenancy/TenancyOptions.cs`), bound in `Program.cs` and then post-configured from the environment. No `ValidateOnStart`.

| Key | Default | Required | Secret | Notes |
|---|---|---|---|---|
| `Tenancy:PlatformHosts` | `[]` | in production, yes (otherwise no platform area) | no | array of hosts that serve only `PlatformEndpoint` actions. In Development and Testing, `admin.localhost` (`TenancyOptions.DevelopmentPlatformHost`) is appended automatically |
| `Tenancy:LocalDefaultTenant` | the seeded default store slug (`DbSeeder.DefaultTenantSlug`) | no | no | Development/Testing only: which store `localhost` and `127.0.0.1` serve |
| `Tenancy:AllowDevelopmentResolution` | computed | — | no | **Ignored as configuration.** `Program.cs` overwrites it with "environment is Development or Testing", so the `X-Tenant` header and `{slug}.localhost` can never be switched on in production |

docker-compose maps one platform host only: `Tenancy__PlatformHosts__0: ${PLATFORM_HOST:-admin.localhost}` (`.env`: `PLATFORM_HOST`). Additional platform hosts need extra numbered `Tenancy:PlatformHosts` entries in `docker-compose.yml`.

Store hosts themselves are **data**, not configuration: they live in the `TenantDomains` table and are managed through the platform API. Only the default store can be bound from configuration, through `Seed:DefaultTenantHosts`.

## 7. Seed

Read directly from configuration in `Program.cs` into `SeedOptions` and applied by `DbSeeder` (`src/Souq.Infrastructure/Persistence/DbSeeder.cs`) after migrations.

| Key | Default | Required | Secret | Validation |
|---|---|---|---|---|
| `Seed:AdminEmail` | Development only: `admin@souq.com` | to get a store admin outside Development | personal data | — |
| `Seed:AdminPassword` | Development only: `Admin@123` | with the email | **yes** | outside Development: ≥ 12 characters (`DbSeeder.MinimumAdminPasswordLength`) and not equal to the development password |
| `Seed:PlatformOwnerEmail` | Development only: `owner@souq.com` | to get a platform owner | personal data | — |
| `Seed:PlatformOwnerPassword` | Development only: `Owner@12345` | with the email | **yes** | same rule |
| `Seed:DefaultTenantHosts` | empty | no | no | each value must normalize as a host (`TenantDomain.TryNormalizeHost`); invalid entries are logged and skipped |
| `Seed:DemoData` | unset = Development and Testing only | no | no | the demo catalog and the default store's demo look. Unset follows the environment (`DbSeeder.ShouldSeedDemoData`); `true` or `false` overrides it in either direction |

Behaviour worth knowing before you rely on it:

- **Nothing is created without explicit configuration outside Development.** A missing pair logs `No {Role} account seeded: set {Settings} (environment variables/user-secrets)` — including on every later start after you remove the variables, which is expected, not an error.
- **Demo data is never seeded into a production database by accident.** The demo catalog and the default store's demo look are applied only when `Seed:DemoData` resolves true, which outside Development and Testing means setting it explicitly. When it is off the seeder logs `Demo data not seeded (Seed:DemoData is off for this environment)`. Migrations still create the default store row itself — only its contents and look are gated.
- **Seeding never changes an existing account's password.** It only upgrades a stored hash that is not BCrypt. Changing `SEED_ADMIN_PASSWORD` later does nothing; reset the password through the app.
- A weak password fails the start with `كلمة مرور البذرة (Seed:AdminEmail/Seed:AdminPassword) ضعيفة: يلزم 12 حرفاً على الأقل ولا تساوي كلمة مرور التطوير.` — this happens **after** migrations have been applied.
- `Seed:DefaultTenantHosts` accepts an array (numbered environment entries) or one comma-separated value; a host already bound to any store is left alone, so it never steals another store's domain.

docker-compose: `Seed__AdminEmail`, `Seed__AdminPassword`, `Seed__PlatformOwnerEmail`, `Seed__PlatformOwnerPassword` (`.env`: `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD`, `SEED_PLATFORM_OWNER_EMAIL`, `SEED_PLATFORM_OWNER_PASSWORD`), `Seed__DefaultTenantHosts: ${DEFAULT_TENANT_HOSTS:-localhost}` and `Seed__DemoData: ${SEED_DEMO_DATA:-}`.

## 8. Email and providers

Provider selection happens once, in `AddEmail`, in strict priority order: **Resend → Brevo → Gmail SMTP → log-only**. The first non-empty key wins; the others are ignored entirely.

| Key | Default | Required | Secret | Notes |
|---|---|---|---|---|
| `Email:Provider` | empty | only to allow log-only outside local | no | the single recognised value is `Log` (case-insensitive) |
| `Resend:ApiKey` | empty | to select Resend | **yes** | |
| `Resend:From` | `Souq <onboarding@resend.dev>` (`appsettings.json`) | no | no | the display name is replaced per message by the store's name. The shared `onboarding@resend.dev` sender is Resend's test sender — use a verified domain for real traffic |
| `Brevo:ApiKey` | empty | to select Brevo | **yes** | |
| `Brevo:SenderEmail` | falls back to `Gmail:Username` or `Gmail:SenderEmail` | with Brevo | personal data | empty at send time throws `EmailDeliveryException` |
| `Brevo:SenderName` | `Souq` (`appsettings.json`) | no | no | fallback only; the store's name is used per message |
| `Gmail:AppPassword` | empty | to select Gmail | **yes** | whitespace is stripped defensively |
| `Gmail:Username` (alias `Gmail:SenderEmail`) | empty | with Gmail | personal data | empty at send time throws `EmailDeliveryException` |
| `Gmail:Host` | `smtp.gmail.com` | no | no | |
| `Gmail:Port` | 587 | no | no | 465 switches to implicit TLS; anything else uses STARTTLS when `Gmail:EnableSsl` is true |
| `Gmail:EnableSsl` | `true` | no | no | |

With no provider key at all:

- Development/Testing: the log adapter (`ConsoleEmailService`) is selected, and links appear in the log (`IncludeLinksInLog` is true in Development only — it is derived from the environment, not configurable).
- Anywhere else: the start **fails** unless `Email:Provider=Log` is set explicitly.

```text
لا مزوّد بريد مضبوط في بيئة {Environment}: اضبط Resend:ApiKey أو Brevo:ApiKey أو Gmail:AppPassword، أو Email:Provider=Log صراحةً لعرض توضيحي لا تصل فيه أي رسالة.
```

With `Email:Provider=Log` outside local, the startup report warns `Email:Provider=Log — لا تُرسَل أي رسالة (إعادة تعيين، تأكيد، دعوة، تأكيد طلب): عرض توضيحي فقط.` and every message logs `No email provider configured — {Kind} to {Recipient} was not sent`.

Provider HTTP calls use `IHttpClientFactory` clients with a 15-second timeout; failures raise `EmailDeliveryException` and the outbox retries (see [§12](#12-notifications-inventory-and-basket-schedules)).

docker-compose: `Email__Provider`, `Resend__ApiKey`, `Brevo__ApiKey`, `Brevo__SenderEmail`, `Gmail__AppPassword`, `Gmail__Username`, `Gmail__Port` (`.env`: `EMAIL_PROVIDER`, `RESEND_API_KEY`, `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `GMAIL_APP_PASSWORD`, `GMAIL_USERNAME`, `GMAIL_SMTP_PORT`). `Resend:From`, `Brevo:SenderName`, `Gmail:Host` and `Gmail:EnableSsl` are **not** exposed by compose.

> `.env.example` ships `GMAIL_APP_PASSWORD` **empty**, and it must stay that way unless Gmail is genuinely the provider. Any non-empty value — a placeholder included — selects the Gmail adapter, so the "no email provider" fail-fast never triggers: the API starts and every message then dies in the outbox after eight attempts instead.

## 9. Payments and Stripe

Selection is decided once by `PaymentProviderSelector` (`src/Souq.Infrastructure/Services/PaymentProviderSelector.cs`) and nothing else in the system knows which gateway runs.

| Key | Default | Required | Secret | Notes |
|---|---|---|---|---|
| `Payments:Provider` | empty | outside local, when no Stripe key | no | `Stripe`, `Fake`, or empty (empty = Stripe when a secret key exists, Fake in Development/Testing, otherwise a failed start) |
| `Stripe:SecretKey` | empty | when Stripe runs | **yes** | |
| `Stripe:PublishableKey` | empty | when Stripe runs **outside Development** | no (it is meant for the browser; served by `GET /api/payments/config`) | |
| `Stripe:WebhookSecret` | empty | no (strongly recommended) | **yes** | missing ⇒ startup warning; webhook events are then acknowledged without action |
| `Payments:AllowTestModeStoreAccounts` | `false` | no | no | outside local, allows stores to save Stripe **test** keys; logs a warning |
| `Payments:Fake:WebhookSecret` | empty | no | **yes** in spirit | HMAC secret that lets the fake gateway accept signed test webhooks. Development and tests only |

Messages:

```text
لا بوّابة دفع مضبوطة في بيئة {Environment}: اضبط Stripe:SecretKey، أو Payments:Provider=Fake صراحةً لعرض توضيحي بلا دفع حقيقي.
Payments:Provider=Stripe لكن Stripe:SecretKey غير مضبوط.
Payments:Provider غير معروف: '{Value}' (المسموح: Stripe أو Fake).
Stripe:SecretKey مطلوب حين تعمل بوّابة Stripe.
Stripe:PublishableKey مطلوب خارج Development — بدونه لا تعرض الواجهة نموذج البطاقة.
```

Startup warnings:

```text
بوّابة الدفع التجريبية مفعّلة صراحةً (Payments:Provider=Fake): كل دفع يُعتبر ناجحاً بلا مال — للعرض التوضيحي فقط، لا زبائن حقيقيون.
Stripe:WebhookSecret غير مضبوط — تأكيد الدفع يعتمد على متصفّح العميل وحده؛ طلب يُغلق صاحبه الصفحة قبل التأكيد يبقى معلّقاً.
Payments:AllowTestModeStoreAccounts مفعّل: متجر بمفاتيح Stripe تجريبية يقبل بطاقات الاختبار بلا مال حقيقي.
```

These keys configure the **deployment account**. A store that connects its own Stripe account stores its keys encrypted in `StorePaymentAccounts`, and `PaymentGatewayRouter` picks per call ([ADR-0031](../11-ADR/0031-payments-and-refunds.md)).

**Currency caveat (P-05):** `StripeAmountConverter` sends every currency except a fixed zero-decimal list as ×100. JOD is a three-decimal currency that Stripe's documentation does not list as a special case; confirm the multiplier on the real account before taking live JOD payments, because a wrong multiplier charges a tenth of the price.

docker-compose: `Payments__Provider`, `Stripe__SecretKey`, `Stripe__PublishableKey`, `Stripe__WebhookSecret`, `Payments__AllowTestModeStoreAccounts` (`.env`: `PAYMENTS_PROVIDER`, `STRIPE_SECRET_KEY`, `STRIPE_PUBLISHABLE_KEY`, `STRIPE_WEBHOOK_SECRET`, `PAYMENTS_ALLOW_TEST_MODE_STORE_ACCOUNTS`). `Payments:Fake:WebhookSecret` is not exposed by compose, by design.

## 10. Secrets (the key that encrypts stores' own keys)

`SecretsSettings` and `AesGcmSecretProtector` (`src/Souq.Infrastructure/Security/AesGcmSecretProtector.cs`), validated by `SecretsSettingsValidator` with `ValidateOnStart`. Optional: without it, no store can connect its own payment account and every store is charged through the deployment account.

| Key | Default | Required | Secret | Validation |
|---|---|---|---|---|
| `Secrets:Keys:{id}` | none | only for per-store payment accounts | **yes** | base64 of exactly 32 bytes; `{id}` is ASCII letters and digits only. Empty values are treated as unset (compose passes empty variables) |
| `Secrets:ActiveKeyId` | none | when any key is set | no | must name an existing key |

```text
Secrets:Keys: معرّف المفتاح '{Id}' حروف وأرقام لاتينية فقط.
Secrets:Keys:{Id} يجب أن يكون base64 لـ 32 بايت بالضبط.
Secrets:ActiveKeyId '{Id}' غير موجود في Secrets:Keys.
Secrets:ActiveKeyId مطلوب حين تُضبط Secrets:Keys.
```

Ciphertext is stored as `v1.{keyId}.{base64}`, so the key id travels with the data and several keys can coexist. If `Secrets:ActiveKeyId` is unset outside local, the startup report warns that no store payment accounts can be connected.

**Rotation** (the only supported procedure today):

1. Add a second key with a new id, keeping the old one: `Secrets:Keys:k2`.
2. Point `Secrets:ActiveKeyId` at the new id and restart. Existing ciphertexts still decrypt with the old key.
3. Have every store with its own account **re-enter its Stripe secret key and webhook secret** — a blank field keeps the stored ciphertext (`StorePaymentAccountInput`), so re-saving other fields does not re-encrypt anything.
4. Find who is left:

   ```sql
   SELECT TenantId, SecretKeyHint FROM StorePaymentAccounts WHERE SecretKeyCipher LIKE 'v1.<oldId>.%';
   ```

5. Only when that query is empty, remove the old key. Removing a key that ciphertexts still need makes those stores' payments answer `503 PaymentsUnavailable` (`مفتاح التشفير '{Id}' لم يعد مضبوطاً (Secrets:Keys)`).

docker-compose hard-codes a single key id: `Secrets__ActiveKeyId: ${SECRETS_KEY:+primary}` and `Secrets__Keys__primary: ${SECRETS_KEY:-}` (`.env`: `SECRETS_KEY`). A rotation therefore needs a temporary edit of `docker-compose.yml` to carry the second key.

## 11. Storage

| Key | Default | Required | Secret | Validation |
|---|---|---|---|---|
| `Storage:Local:RootPath` | `{ContentRoot}/wwwroot/uploads` | no | no | must be an absolute path (`ValidateOnStart`): `Storage:Local:RootPath يجب أن يكون مساراً مطلقاً.` |

`FileStorageOptions.PublicBasePath` is fixed at `/uploads` in code. Files are written as `{RootPath}/tenants/{tenantId}/{folder}/{guid}{ext}` by `LocalFileStorage`, and the same folder is served read-only at `/uploads` with an allow-list of media types, `nosniff` and a sandboxing CSP. `Program.cs` creates the directory at startup. The integration tests point it at a temporary directory.

docker-compose does not set it; the container default resolves to `/app/wwwroot/uploads`, which is the mount point of the `souq_uploads` volume.

## 12. Notifications, inventory and basket schedules

All three are typed options with `ValidateOnStart`, and each interval accepts `0` to disable its background service (which is how the integration tests run deterministic dispatches).

| Key | Default | Range | Effect |
|---|---|---|---|
| `Inventory:ReservationMinutes` | 30 | 5–1440 | how long an unpaid checkout holds stock |
| `Inventory:SweepIntervalSeconds` | 60 | 0 (off) or 10–3600 | how often `ReservationExpiryService` settles expired checkouts per active store |
| `Basket:GuestLifetimeDays` | 30 | 1–365 | guest basket idle lifetime (also the guest cookie's lifetime) |
| `Basket:CustomerLifetimeDays` | 180 | 1–730 | customer basket idle lifetime |
| `Basket:CleanupIntervalMinutes` | 60 | 0 (off) or 5–1440 | how often `BasketCleanupService` deletes expired baskets |
| `Notifications:DispatchIntervalSeconds` | 5 | 0 (off) or 1–300 | outbox polling interval (`OutboxDispatcherService`) |
| `Notifications:RetentionDays` | 14 | 1–365 | processed outbox rows are purged after this; the purge runs at most hourly. Dead rows are kept |

Validation messages name the key, for example `Inventory:SweepIntervalSeconds صفر (معطّل) أو بين 10 و3600 ثانية.` None of these are exposed by `docker-compose.yml`; set them as environment variables (`Inventory:SweepIntervalSeconds` and so on, with double underscores) if you need to.

Retry behaviour for failed messages is in code, not configuration: `OutboxRetryPolicy` allows 8 attempts (30 s, 2 min, 10 min, 30 min, 1 h, 3 h, 6 h) before a message is marked dead.

## 13. Rate limiting, CORS, forwarded headers, frontend URL, logging, hosts

### Rate limiting

`RateLimitingOptions` (`src/Souq.API/Security/RateLimiting.cs`), read with a plain bind — **no validation**. Each policy is a fixed window partitioned by `host|client IP`, with no queue; rejection is `429` with `Retry-After` and the code `TooManyRequests`.

| Key | Default | Applies to |
|---|---|---|
| `RateLimiting:Auth:PermitLimit` / `:WindowSeconds` | 10 / 60 | login, register, password reset and change, verification (`RateLimitPolicies.Auth`) |
| `RateLimiting:Refresh:PermitLimit` / `:WindowSeconds` | 30 / 60 | session refresh |
| `RateLimiting:CouponPreview:PermitLimit` / `:WindowSeconds` | 30 / 60 | coupon preview |
| `RateLimiting:Basket:PermitLimit` / `:WindowSeconds` | 120 / 60 | basket writes |

Counters live in the process, so N instances mean roughly N times the configured limit.

### CORS

| Key | Default | Notes |
|---|---|---|
| `Cors:AllowedOrigins` | `["http://localhost:5173"]` in **Development/Testing only**; **empty** everywhere else | bound with `Get<string[]>()`, so it must be an array (numbered environment entries). Unlike `Seed:DefaultTenantHosts`, a single comma-separated value is not split |

The policy allows any header and method for those origins and exposes `X-Correlation-Id`.

**Every deployment of this system is same-origin and needs no CORS at all:** the SPA calls relative `/api` paths, proxied by Vite in development and nginx in production. The localhost fallback used to apply in *every* environment, which meant a production API quietly allowed a development origin — the kind of leak the rest of this page exists to prevent. `CorsOrigins.For` now returns the development origin only in Development/Testing, and nothing otherwise; an explicit `Cors:AllowedOrigins` still wins everywhere. The startup log records how many origins are allowed.

### Forwarded headers

| Key | Default | Notes |
|---|---|---|
| `ForwardedHeaders:KnownNetworks` | none (framework defaults only) | CIDR list, array or comma-separated. `X-Forwarded-For` and `X-Forwarded-Proto` are honoured only from these networks — otherwise a client could forge its IP to escape rate limits, or forge `https` |

docker-compose: `ForwardedHeaders__KnownNetworks: ${TRUSTED_PROXY_NETWORKS:-172.16.0.0/12}`. `.env.example` says, correctly: never use `0.0.0.0/0`.

### Frontend URL

Read as `FRONTEND_URL` first, then `App:FrontendUrl`, then the hard-coded `http://localhost:5173`, by `RequestStorefrontLinks` and `StoreOrigins`.

| Key | Default | Used for |
|---|---|---|
| `FRONTEND_URL` (flat key, not a section) | — | scheme and port of links in messages that have no HTTP request behind them (order emails from webhooks and sweeps) |
| `App:FrontendUrl` | `http://localhost:5173` (`appsettings.json`) | same, as fallback |

`StoreOrigins` combines the store's **primary domain** with this URL's scheme and port; a store with no primary domain falls back to this URL's authority. Not validated: a malformed value throws when the first message is dispatched, not at startup.

### Logging

`appsettings.json` sets `Logging:LogLevel:Default=Information`, `Microsoft.AspNetCore=Warning`, `Microsoft.EntityFrameworkCore=Warning`. `appsettings.Production.json` switches the console to the JSON formatter with scopes and UTC timestamps. Override per environment with `Logging:LogLevel:Default` and friends. Note the JSON format applies to the `Production` environment only — `Staging` would log text.

### Hosts and ports

| Key | Default | Notes |
|---|---|---|
| `AllowedHosts` | `*` | **deliberate, not an oversight.** Host filtering matches a fixed list decided at startup, but this platform's valid hosts are its stores' domains, which live in `TenantDomains` and change while the process runs. Host validation is therefore done by `TenantResolutionMiddleware`, which rejects an unknown host with `404 StoreNotFound` on `/api` and `/uploads`. Narrowing this key would break custom domains without adding protection |
| `ASPNETCORE_ENVIRONMENT` | `Development` from `launchSettings.json`, `Production` from compose | decides every "local only" behaviour |
| `ASPNETCORE_HTTP_PORTS` | `8080` (set in `src/Souq.API/Dockerfile`) | container listen port; the local profile uses `applicationUrl` 5200 instead |

## 14. Settings that are not validated (gaps)

Honest list of what a wrong value does **not** stop at startup:

| Setting | What happens instead |
|---|---|
| `Brevo:SenderEmail` / `Gmail:Username` | every message fails at send time (`Brevo مضبوط بلا عنوان مرسِل (Brevo:SenderEmail)`), retries 8 times, then dies in the outbox |
| `Resend:From` | Resend rejects at send time; the error is logged and retried |
| `Tenancy:PlatformHosts` | an empty list in production simply means there is no platform area; no warning |
| `Cors:AllowedOrigins` | a wrong shape falls back to the environment default — the localhost origin in Development/Testing, nothing elsewhere |
| `FRONTEND_URL` / `App:FrontendUrl` | a malformed URL throws when notifications are dispatched |
| `RateLimiting:*` | not validated at all |
| `Auth:RefreshCookie:Secure` | still not enforced, but setting it to `false` outside Development/Testing now logs a startup **warning** naming the risk (the refresh token would travel over http). It stays permitted because a local http deployment on a LAN address is a legitimate, if rare, use |
| `ForwardedHeaders:KnownNetworks` | a value that is not CIDR fails when the pipeline builds it, not in the startup validator |

## 15. Configuration in the test suites

`SouqApiFactory` builds the real `Program` against SQL Server in Testcontainers with: the container's connection string, a long `Jwt:Key`, explicit seed accounts (so the test exercises the production seeding path), a temporary `Storage:Local:RootPath`, all three background intervals set to `0`, a `Secrets` key pair, `Payments:Fake:WebhookSecret`, and rate limits raised to 100000. It runs as `Testing`, which keeps the fake gateway and log-only email legal without touching developer secrets.

## 16. Related documents

- [Deployment.md](Deployment.md) — how these settings reach a running stack, and the production checklist.
- [Troubleshooting.md](Troubleshooting.md) — the failures these settings cause, symptom first.
- [DevelopmentGuide.md](DevelopmentGuide.md) — the minimum set for a local machine.
- [ADR-0020](../11-ADR/0020-configuration-and-secrets.md) — why configuration is typed, validated and fail-fast.
- [Security.md](../07-SECURITY/Security.md) §6 — secret classification and phase-gate checks.
