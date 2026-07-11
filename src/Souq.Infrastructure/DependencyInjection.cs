using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Interfaces;
using Souq.Domain.Interfaces;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Persistence.Repositories;
using Souq.Infrastructure.Services;

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
        // لتبديل الدفع لاحقاً: غيّر السطر التالي فقط إلى StripePaymentService.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IPaymentService, FakePaymentService>();
        services.AddScoped<IEmailService, ConsoleEmailService>();

        return services;
    }
}
