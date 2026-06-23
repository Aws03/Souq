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
        // الاتصال بـ SQL Server. سلسلة الاتصال تأتي من الإعدادات لا من الكود (Config).
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("Default")));

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
