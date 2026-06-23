using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Behaviors;

namespace Souq.Application;

// ============================================================================
// كل طبقة تسجّل خدماتها بنفسها (Modularity). طبقة API لا تحتاج معرفة تفاصيل
// تسجيل Application — تنادي AddApplication() فقط. هذا يقلّل الترابط.
// ============================================================================
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // يكتشف كل المعالجات (Handlers) تلقائياً ويسجّلها.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        // يكتشف كل المدقّقات (Validators) تلقائياً.
        services.AddValidatorsFromAssembly(assembly);

        // يُدخل سلوك التحقّق في خط أنابيب MediatR.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
