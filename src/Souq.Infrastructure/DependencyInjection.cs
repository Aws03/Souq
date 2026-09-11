using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Interfaces;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Repositories;
using Souq.Infrastructure.Services;

namespace Souq.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        // الاتصال بـ SQL Server. سلسلة الاتصال سرّ: تأتي من user-secrets (تطوير)
        // أو متغيرات البيئة (إنتاج) — لا تُخزّن في appsettings المرفوع أبداً.
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "سلسلة الاتصال 'Default' غير مضبوطة. للتطوير: " +
                "dotnet user-secrets set \"ConnectionStrings:Default\" \"...\" --project src/Souq.API");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        // ربط كل واجهة بتنفيذها. هذا هو "مكان الحقيقة" لقرارات التقنية.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();

        AddEmail(services, config, environment);

        // بوّابة الدفع: Stripe حقيقي إن وُجد مفتاح سرّي مضبوط، وإلا محاكاة تجريبية.
        // القرار هنا فقط — لا كود آخر في النظام يعرف أيّهما يعمل.
        if (!string.IsNullOrWhiteSpace(config["Stripe:SecretKey"]))
        {
            services.AddOptions<StripeSettings>().Bind(config.GetSection("Stripe"));
            services.AddScoped<IPaymentService, StripePaymentService>();
        }
        else
        {
            services.AddScoped<IPaymentService, FakePaymentService>();
        }

        // تخزين ملفات الوسائط محلياً (قرص) — يُبدَّل بتخزين سحابي في الإنتاج. المسار
        // يُضبط في طبقة الـ API (Configure<FileStorageOptions>).
        services.AddScoped<IFileStorage, LocalFileStorage>();

        // ── المصادقة: تجزئة كلمة المرور + إصدار التوكن (عديمة الحالة ⇒ Singleton) ──
        services.AddOptions<JwtSettings>()
            .Bind(config.GetSection("Jwt"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.Key),
                "مفتاح JWT (Jwt:Key) غير مضبوط. اضبطه في user-secrets/متغيرات البيئة.")
            .ValidateOnStart();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }

    // البريد: ترتيب الأولوية Resend ← Brevo ← Gmail SMTP ← طباعة في السجل. كل مفتاح
    // سرّ (متغيّر بيئة/user-secrets). الطباعة في السجل تُظهر الروابط في Development فقط
    // (لا بريد حقيقي يُرسَل هناك)؛ خارجها لا تظهر أي رموز أو روابط أبداً (Phase 0 B2).
    private static void AddEmail(IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        // FRONTEND_URL (متغيّر بيئة للنشر) ثم App:FrontendUrl ثم افتراضي التطوير المحلي.
        var frontendUrl = config["FRONTEND_URL"] ?? config["App:FrontendUrl"] ?? "http://localhost:5173";
        // عنوان المرسِل البديل المشترك بين المزوّدين (Gmail:Username أو اسمه البديل
        // Gmail:SenderEmail) — لا عنوان شخصي مكتوب في الكود أو appsettings المرفوع.
        var fallbackSender = FirstNonEmpty(config["Gmail:Username"], config["Gmail:SenderEmail"]);

        if (!string.IsNullOrWhiteSpace(config["Resend:ApiKey"]))
        {
            services.Configure<ResendOptions>(o =>
            {
                config.GetSection("Resend").Bind(o);
                o.FrontendUrl = frontendUrl;
            });
            services.AddScoped<IEmailService, ResendEmailService>();
        }
        else if (!string.IsNullOrWhiteSpace(config["Brevo:ApiKey"]))
        {
            services.Configure<BrevoOptions>(o =>
            {
                config.GetSection("Brevo").Bind(o);
                o.SenderEmail = FirstNonEmpty(o.SenderEmail, fallbackSender) ?? "";
                o.FrontendUrl = frontendUrl;
            });
            services.AddScoped<IEmailService, BrevoEmailService>();
        }
        else if (!string.IsNullOrWhiteSpace(config["Gmail:AppPassword"]))
        {
            services.Configure<GmailSmtpOptions>(o =>
            {
                config.GetSection("Gmail").Bind(o);
                // كلمات مرور التطبيقات تُلصَق أحياناً بمسافات (حتى U+00A0) — ننظّفها دفاعياً.
                o.AppPassword = string.Concat(o.AppPassword.Where(c => !char.IsWhiteSpace(c)));
                o.Username = fallbackSender ?? "";
                o.FrontendUrl = frontendUrl;
            });
            services.AddScoped<IEmailService, GmailEmailService>();
        }
        else
        {
            services.Configure<ConsoleEmailOptions>(o =>
            {
                o.IncludeLinksInLog = environment.IsDevelopment();
                o.FrontendUrl = frontendUrl;
            });
            services.AddScoped<IEmailService, ConsoleEmailService>();
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
