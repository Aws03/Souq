using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // الساعة: كل قراءة "الآن" في حالات الاستخدام تمرّ عبر TimeProvider (مدمج في .NET)
        // لا DateTime.UtcNow — فتُختبر الصلاحيات والانتهاء بساعة ثابتة (Phase 0 D12).
        services.TryAddSingleton(TimeProvider.System);

        // خدمة تطبيق مشتركة بين مسارَي الإلغاء (الإدارة + تعويض الدفع) — صنف ملموس
        // بلا واجهة: لا تنفيذ بديل له، فالواجهة ستكون تجريداً بلا سبب.
        services.AddScoped<Features.Orders.OrderStockRelease>();
        // منطق تأكيد الدفع الواحد لمدخلين بتفويض مختلف (العميل المالك / توقيع البوّابة).
        services.AddScoped<Features.Orders.OrderPaymentConfirmation>();

        return services;
    }
}
