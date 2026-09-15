using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Accounts;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Coupons.Queries;
using Souq.Application.Features.Customers;
using Souq.Application.Features.Inventory.Queries;
using Souq.Application.Features.Inventory.Reservations;
using Souq.Application.Features.Notifications;
using Souq.Application.Features.Orders.Queries;
using Souq.Application.Features.Platform;
using Souq.Application.Features.Products.Queries;
using Souq.Application.Features.Reporting;
using Souq.Application.Features.Reviews.Queries;
using Souq.Application.Features.Stores;
using Souq.Domain.Interfaces;
using Souq.Infrastructure.Auditing;
using Souq.Infrastructure.BackgroundJobs;
using Souq.Infrastructure.Notifications;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Interceptors;
using Souq.Infrastructure.Persistence.Outbox;
using Souq.Infrastructure.Persistence.Queries;
using Souq.Infrastructure.Persistence.Repositories;
using Souq.Infrastructure.Services;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure;

// ============================================================================
// تسجيل المحوّلات (Adapters) — "مكان الحقيقة" لقرارات التقنية. اصطلاحات الإعداد (ADR-0020):
//   • كل قسم إعداد صنف مطبوع (Options) مُتحقَّق منه ValidateOnStart ⇒ يُفحص قبل أي طلب.
//   • الأسرار من user-secrets/متغيّرات البيئة فقط؛ appsettings المرفوع بلا أسرار.
//   • وسائل التطوير (بوّابة تجريبية، بريد في السجل) لا تعمل ضمنياً خارج Development/Testing.
//   • كل تحذير تشغيلي يُجمع في InfrastructureStartupReport ويُسجَّل مرة عند الإقلاع.
// ============================================================================
public static class DependencyInjection
{
    // مهلة أي استدعاء HTTP لمزوّد خارجي: مزوّد بطيء لا يحبس طلب العميل 100 ثانية (الافتراضي).
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var report = new InfrastructureStartupReport();
        services.AddSingleton(report);

        AddPersistence(services, config);
        AddInventory(services, config);
        AddBaskets(services, config);
        AddPayments(services, config, environment, report);
        AddEmail(services, config, environment, report);
        AddNotifications(services, config);
        AddStorage(services, config, environment);
        AddAuthentication(services, config);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration config)
    {
        // الاتصال بـ SQL Server. سلسلة الاتصال سرّ: تأتي من user-secrets (تطوير)
        // أو متغيرات البيئة (إنتاج) — لا تُخزّن في appsettings المرفوع أبداً.
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "سلسلة الاتصال 'Default' غير مضبوطة (ConnectionStrings:Default). للتطوير: " +
                "dotnet user-secrets set \"ConnectionStrings:Default\" \"...\" --project src/Souq.API");

        // هوية الهجرات (R-12): سلسلة اختيارية تُستخدم لتطبيق الهجرات وحدها. غيابها يعني
        // "نفس هوية التشغيل" — سلوك ما قبل الفصل بالضبط.
        var migrationConnectionString = config.GetConnectionString("Migrations");
        services.AddSingleton(new MigrationConnection(
            string.IsNullOrWhiteSpace(migrationConnectionString) ? connectionString : migrationConnectionString,
            !string.IsNullOrWhiteSpace(migrationConnectionString)));

        // الساعة الوحيدة في النظام (Phase 0 D12) — TryAdd: قد تكون Application سجّلتها أولاً.
        services.TryAddSingleton(TimeProvider.System);

        // المعترِضات تعمل داخل كل SaveChanges بلا استثناء: ختم التواريخ، وحارس المستأجر (ختم TenantId
        // عند الإضافة ورفض الكتابة عبر المتاجر — MultiTenancy.md §4).
        services.AddSingleton<AuditTimestampsInterceptor>();
        services.AddSingleton<TenantWriteGuardInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseSqlServer(connectionString)
                   .AddInterceptors(
                       sp.GetRequiredService<AuditTimestampsInterceptor>(),
                       sp.GetRequiredService<TenantWriteGuardInterceptor>()));

        // دليل المتاجر (المضيف ⇒ المتجر) مخزَّن مؤقتاً في العملية؛ الذاكرة Singleton والاستعلام لكل نطاق.
        services.AddSingleton<TenantDirectoryCache>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        // منافذ الكتابة (مستودعات التجمّعات) + وحدة العمل.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IBasketRepository, BasketRepository>();
        services.AddScoped<ICouponRedemptionRepository, CouponRedemptionRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IStorePaymentAccountRepository, StorePaymentAccountRepository>();
        services.AddScoped<IShippingMethodRepository, ShippingMethodRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<Application.Features.Orders.IOrderNumbers, OrderNumbers>();
        services.AddScoped<ITenantRepository, TenantRepository>();

        // خدمات القراءة (ADR-0008): إسقاطات بلا تتبّع خلف منافذ Application، لكل وحدة منفذها.
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<ICouponQueries, CouponQueries>();
        services.AddScoped<Application.Features.Payments.Contracts.IPaymentQueries, PaymentQueries>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<Application.Features.Wishlist.IWishlistQueries, WishlistQueries>();
        services.AddScoped<IInventoryQueries, InventoryQueries>();
        services.AddScoped<IAccountQueries, AccountQueries>();
        services.AddScoped<ICustomerQueries, CustomerQueries>();
        // منطقة المنصّة والإحصاءات: الصنف المُراجَع الوحيد لتجاوز مرشّح المستأجر يخدم منفذيهما.
        services.AddScoped<PlatformQueries>();
        services.AddScoped<IPlatformQueries>(sp => sp.GetRequiredService<PlatformQueries>());
        services.AddScoped<IPlatformReports>(sp => sp.GetRequiredService<PlatformQueries>());
        // تقارير متجر واحد: بلا تجاوز للمرشّح — المرشّح العادي يضيّق كل جدول داخل نطاق المتجر.
        services.AddScoped<IStoreReports, StoreReportQueries>();
        services.AddScoped<IStoreConfiguration, StoreConfiguration>();

        // سجلّ التدقيق في وحدة العمل الحالية، والعمل داخل متجر بعينه من منطقة المنصّة.
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<ITenantScopeRunner, TenantScopeRunner>();
    }

    // المخزون (المرحلة 6): مهلة الحجز ودورة منسّق الانتهاء من Inventory:* — مُتحقَّق منهما عند الإقلاع — والمنسّق
    // خادم خلفي (D-15). SweepIntervalSeconds = 0 يعطّله (الاختبارات تشغّل أمر الانتهاء مباشرة).
    private static void AddInventory(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<InventorySettings>()
            .Bind(config.GetSection("Inventory"))
            .Validate(s => s.ReservationMinutes is >= 5 and <= 1440, "Inventory:ReservationMinutes بين 5 و1440 دقيقة.")
            .Validate(s => s.SweepIntervalSeconds == 0 || s.SweepIntervalSeconds is >= 10 and <= 3600,
                "Inventory:SweepIntervalSeconds صفر (معطّل) أو بين 10 و3600 ثانية.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<InventorySettings>>().Value);
        services.AddHostedService<BackgroundJobs.ReservationExpiryService>();
    }

    // السلال (المرحلة 8): أعمار سلة الزائر وسلة العميل ودورة منسّق حذف المنتهية من Basket:* — مُتحقَّق منها عند الإقلاع.
    // CleanupIntervalMinutes = 0 يعطّل المنسّق (الاختبارات ترسل أمر الحذف مباشرة).
    private static void AddBaskets(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<Application.Features.Baskets.BasketSettings>()
            .Bind(config.GetSection("Basket"))
            .Validate(s => s.GuestLifetimeDays is >= 1 and <= 365, "Basket:GuestLifetimeDays بين 1 و365 يوماً.")
            .Validate(s => s.CustomerLifetimeDays is >= 1 and <= 730, "Basket:CustomerLifetimeDays بين 1 و730 يوماً.")
            .Validate(s => s.CleanupIntervalMinutes == 0 || s.CleanupIntervalMinutes is >= 5 and <= 1440,
                "Basket:CleanupIntervalMinutes صفر (معطّل) أو بين 5 و1440 دقيقة.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<Application.Features.Baskets.BasketSettings>>().Value);
        services.AddHostedService<BackgroundJobs.BasketCleanupService>();
    }

    // بوّابة الدفع: القرار هنا فقط — لا كود آخر في النظام يعرف أيّها يعمل. البوّابة التجريبية لا تعمل ضمنياً خارج
    // Development/Testing (PaymentProviderSelector). المرحلة 11 (ADR-0031): هذا حساب النشر الافتراضي؛ متجر ربط حسابه
    // يقبض فيه، والموجّه (PaymentGatewayRouter) — التنفيذ الوحيد لـ IPaymentService — يختار لكل استدعاء.
    private static void AddPayments(
        IServiceCollection services, IConfiguration config, IHostEnvironment environment, InfrastructureStartupReport report)
    {
        var provider = PaymentProviderSelector.Select(
            config[PaymentProviderSelector.ConfigKey], config["Stripe:SecretKey"], environment.EnvironmentName);
        report.PaymentProvider = provider;
        var local = PaymentProviderSelector.IsLocal(environment.EnvironmentName);

        if (provider == PaymentProvider.Stripe)
        {
            services.AddOptions<StripeSettings>().Bind(config.GetSection("Stripe")).ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeSettings>>(
                new StripeSettingsValidator(requirePublishableKey: !environment.IsDevelopment()));
            services.AddSingleton(sp =>
            {
                var stripe = sp.GetRequiredService<IOptions<StripeSettings>>().Value;
                return new Payments.DeploymentPaymentGateway(new Payments.StripeGateway(Payments.StripeGateway.DeploymentAccount,
                    new Payments.StripeCredentials(stripe.SecretKey, stripe.PublishableKey, stripe.WebhookSecret),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Payments.StripeGateway>>()));
            });

            if (string.IsNullOrWhiteSpace(config["Stripe:WebhookSecret"]))
                report.Warn("Stripe:WebhookSecret غير مضبوط — تأكيد الدفع يعتمد على متصفّح العميل وحده؛ " +
                            "طلب يُغلق صاحبه الصفحة قبل التأكيد يبقى معلّقاً.");
        }
        else
        {
            services.AddSingleton<Payments.FakeGatewayLedger>();
            services.AddSingleton(sp => new Payments.DeploymentPaymentGateway(new Payments.FakeGateway(
                sp.GetRequiredService<Payments.FakeGatewayLedger>(), config["Payments:Fake:WebhookSecret"])));
            if (!local)
                report.Warn($"بوّابة الدفع التجريبية مفعّلة صراحةً ({PaymentProviderSelector.ConfigKey}=Fake): " +
                            "كل دفع يُعتبر ناجحاً بلا مال — للعرض التوضيحي فقط، لا زبائن حقيقيون.");
        }

        services.AddScoped<IPaymentService, Payments.PaymentGatewayRouter>();

        // أسرار حسابات المتاجر: AES-GCM بمفتاح من Secrets:* (سرّ بيئة). اختياري — بدونه لا تُربط حسابات متاجر.
        services.AddOptions<Security.SecretsSettings>().Bind(config.GetSection("Secrets")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<Security.SecretsSettings>, Security.SecretsSettingsValidator>();
        services.AddSingleton<ISecretProtector, Security.AesGcmSecretProtector>();
        if (!local && string.IsNullOrWhiteSpace(config["Secrets:ActiveKeyId"]))
            report.Warn("Secrets:ActiveKeyId غير مضبوط — لا تُربط حسابات دفع خاصة بالمتاجر؛ كل المتاجر تقبض في حساب النشر.");

        // مفاتيح Stripe التجريبية لحسابات المتاجر: التطوير والاختبار وحدهما، إلا بإذن صريح مُحذَّر منه.
        var allowTestKeys = local || config.GetValue<bool>("Payments:AllowTestModeStoreAccounts");
        services.AddSingleton(new Application.Features.Stores.StorePaymentPolicy { AllowTestKeys = allowTestKeys });
        if (allowTestKeys && !local)
            report.Warn("Payments:AllowTestModeStoreAccounts مفعّل: متجر بمفاتيح Stripe تجريبية يقبل بطاقات الاختبار بلا مال حقيقي.");
    }

    // البريد: ترتيب الأولوية Resend ← Brevo ← Gmail SMTP. كل مفتاح سرّ (متغيّر بيئة/user-secrets). بلا مزوّد (المرحلة 14): السجل
    // بدل البريد في Development/Testing تلقائياً (الروابط تظهر في Development وحده، Phase 0 B2)، وخارجهما بإذن صريح
    // Email:Provider=Log فقط — وإلا يرفض الـ API الإقلاع: لا بديل طرفي صامت في الإنتاج (رسائل إعادة التعيين وتأكيد الطلبات لا
    // تصل ولا يلاحظ أحد). المزوّد لا يُستدعى من مسار الطلب أبداً — معالجو صندوق الصادر وحدهم (اختبار معماري).
    private static void AddEmail(
        IServiceCollection services, IConfiguration config, IHostEnvironment environment, InfrastructureStartupReport report)
    {
        // عنوان المرسِل البديل المشترك بين المزوّدين (Gmail:Username أو اسمه البديل
        // Gmail:SenderEmail) — لا عنوان شخصي مكتوب في الكود أو appsettings المرفوع.
        var fallbackSender = FirstNonEmpty(config["Gmail:Username"], config["Gmail:SenderEmail"]);

        if (!string.IsNullOrWhiteSpace(config["Resend:ApiKey"]))
        {
            services.Configure<ResendOptions>(config.GetSection("Resend"));
            // عميل HTTP من المصنع: مهلة، وتدوير اتصالات يحترم تغيّر DNS (لا HttpClient ساكن).
            services.AddHttpClient<IEmailSender, ResendEmailService>(c => c.Timeout = ProviderTimeout);
            report.EmailProvider = "Resend";
        }
        else if (!string.IsNullOrWhiteSpace(config["Brevo:ApiKey"]))
        {
            services.Configure<BrevoOptions>(o =>
            {
                config.GetSection("Brevo").Bind(o);
                o.SenderEmail = FirstNonEmpty(o.SenderEmail, fallbackSender) ?? "";
            });
            services.AddHttpClient<IEmailSender, BrevoEmailService>(c => c.Timeout = ProviderTimeout);
            report.EmailProvider = "Brevo";
        }
        else if (!string.IsNullOrWhiteSpace(config["Gmail:AppPassword"]))
        {
            services.Configure<GmailSmtpOptions>(o =>
            {
                config.GetSection("Gmail").Bind(o);
                // كلمات مرور التطبيقات تُلصَق أحياناً بمسافات (حتى U+00A0) — ننظّفها دفاعياً.
                o.AppPassword = string.Concat(o.AppPassword.Where(c => !char.IsWhiteSpace(c)));
                o.Username = fallbackSender ?? "";
            });
            services.AddScoped<IEmailSender, GmailEmailService>();
            report.EmailProvider = "Gmail";
        }
        else
        {
            var local = PaymentProviderSelector.IsLocal(environment.EnvironmentName);
            var explicitLog = string.Equals(config["Email:Provider"], "Log", StringComparison.OrdinalIgnoreCase);
            if (!local && !explicitLog)
                throw new InvalidOperationException(
                    $"لا مزوّد بريد مضبوط في بيئة {environment.EnvironmentName}: اضبط Resend:ApiKey أو Brevo:ApiKey أو " +
                    "Gmail:AppPassword، أو Email:Provider=Log صراحةً لعرض توضيحي لا تصل فيه أي رسالة.");

            services.Configure<ConsoleEmailOptions>(o => o.IncludeLinksInLog = environment.IsDevelopment());
            services.AddScoped<IEmailSender, ConsoleEmailService>();
            report.EmailProvider = "Log";
            if (!local)
                report.Warn("Email:Provider=Log — لا تُرسَل أي رسالة (إعادة تعيين، تأكيد، دعوة، تأكيد طلب): عرض توضيحي فقط.");
        }
    }

    // الإشعارات (المرحلة 14، D-14): صندوق الصادر ومُرسِله الخلفي، القوالب، أصول واجهات المتاجر، والإشعارات داخل التطبيق.
    // DispatchIntervalSeconds = 0 يعطّل المُرسِل (الاختبارات تشغّل دورة المعالجة مباشرة).
    private static void AddNotifications(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<NotificationSettings>()
            .Bind(config.GetSection("Notifications"))
            .Validate(s => s.DispatchIntervalSeconds == 0 || s.DispatchIntervalSeconds is >= 1 and <= 300,
                "Notifications:DispatchIntervalSeconds صفر (معطّل) أو بين 1 و300 ثانية.")
            .Validate(s => s.RetentionDays is >= 1 and <= 365, "Notifications:RetentionDays بين 1 و365 يوماً.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<NotificationSettings>>().Value);

        services.AddScoped<INotificationOutbox, NotificationOutbox>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddSingleton<IEmailComposer, EmailComposer>();
        services.AddScoped<IStoreOrigins, StoreOrigins>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationQueries, NotificationQueries>();
        services.AddHostedService<OutboxDispatcherService>();
    }

    // تخزين الوسائط محلياً (قرص) خلف IFileStorage — يُستبدل بتخزين سحابي بتبديل هذا التسجيل
    // وحده. Storage:Local:RootPath: قرص مُثبَّت في الإنتاج، مجلّد مؤقت في الاختبارات؛ وإلا
    // wwwroot/uploads. (كان يُضبط في Program.cs — Phase 0 D10.)
    private static void AddStorage(IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        services.AddOptions<FileStorageOptions>()
            .Configure(o =>
            {
                o.RootPath = config["Storage:Local:RootPath"] is { Length: > 0 } root
                    ? root
                    : Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
                o.PublicBasePath = "/uploads";
            })
            .Validate(o => Path.IsPathRooted(o.RootPath), "Storage:Local:RootPath يجب أن يكون مساراً مطلقاً.")
            .ValidateOnStart();
        services.AddScoped<IFileStorage, LocalFileStorage>();
    }

    // المصادقة: تجزئة كلمة المرور + إصدار التوكن (عديمة الحالة ⇒ Singleton). إعدادات التوكن
    // مُتحقَّق منها عند الإقلاع (JwtSettingsValidator) — الـ API يقرؤها منها للتحقّق من التوكن.
    private static void AddAuthentication(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtSettings>().Bind(config.GetSection("Jwt")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // ختم الأمان لكل طلب مُصادَق: ذاكرة قصيرة (Singleton) + استعلام مُرشَّح بالنطاق (لكل طلب).
        services.AddSingleton<SessionStampCache>();
        services.AddScoped<ISessionValidator, SessionValidator>();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
