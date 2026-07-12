using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Interfaces;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Repositories;
using Souq.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Souq.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
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

        // البريد: Gmail SMTP حقيقي إن وُجدت كلمة مرور تطبيق مضبوطة (Gmail__AppPassword
        // كمتغيّر بيئة — سرّ، لا يُقرأ أبداً من appsettings المرفوع)، وإلا طباعة في
        // السجل فقط (تطوير محلي بلا حساب Gmail). نفس نمط قرار بوّابة الدفع أدناه.
        var gmailAppPassword = config["Gmail:AppPassword"];
        if (!string.IsNullOrWhiteSpace(gmailAppPassword))
        {
            services.Configure<GmailSmtpOptions>(o =>
            {
                config.GetSection("Gmail").Bind(o);
                // كلمات مرور التطبيقات تعرضها Google بمجموعات تفصلها مسافات
                // (أحياناً U+00A0 غير القابلة للكسر) وتُلصَق كما هي — ننظّف كل
                // المسافات دفاعياً بدل فشل مصادقة SMTP غامض (5.7.8 BadCredentials).
                o.AppPassword = string.Concat(gmailAppPassword.Where(c => !char.IsWhiteSpace(c)));
                // Gmail:SenderEmail اسم بديل مقبول لـ Gmail:Username (يستخدمه بعض
                // الإعداد المحلي) — Username الصريح يتقدّم عليه إن وُجد كلاهما.
                if (string.IsNullOrWhiteSpace(config["Gmail:Username"]) &&
                    !string.IsNullOrWhiteSpace(config["Gmail:SenderEmail"]))
                    o.Username = config["Gmail:SenderEmail"]!;
                o.FrontendUrl = config["App:FrontendUrl"] ?? "http://localhost:5173";
            });
            services.AddScoped<IEmailService, GmailEmailService>();
        }
        else
        {
            services.AddScoped<IEmailService, ConsoleEmailService>();
        }

        // بوّابة الدفع: Stripe حقيقي إن وُجد مفتاح سرّي مضبوط (user-secrets/بيئة)،
        // وإلا محاكاة تجريبية (تطوير محلي بلا حساب Stripe). القرار هنا فقط —
        // لا كود آخر في النظام يعرف أيّهما يعمل، فكلاهما ينفّذ IPaymentService نفسها.
        var stripeSecretKey = config["Stripe:SecretKey"];
        if (!string.IsNullOrWhiteSpace(stripeSecretKey))
        {
            services.AddOptions<StripeSettings>().Bind(config.GetSection("Stripe"));
            services.AddScoped<IPaymentService, StripePaymentService>();
        }
        else
        {
            services.AddScoped<IPaymentService, FakePaymentService>();
        }
        // تخزين الملفات محلياً (قرص) — يُبدَّل بتخزين سحابي في الإنتاج. مسار المجلد
        // يُضبط في طبقة الـ API حيث يُعرف wwwroot (Configure<FileStorageOptions>).
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IVideoStorage, LocalVideoStorage>();

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
}
