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

        // خط أنابيب MediatR — ترتيب التسجيل هو ترتيب التنفيذ (الأول هو الأبعد):
        // نطاق سجلّ حالة الاستخدام وزمنها أولاً (يشمل التحقّق)، ثم التحقّق.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UseCaseLoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // الساعة: كل قراءة "الآن" في حالات الاستخدام تمرّ عبر TimeProvider (مدمج في .NET)
        // لا DateTime.UtcNow — فتُختبر الصلاحيات والانتهاء بساعة ثابتة (Phase 0 D12).
        services.TryAddSingleton(TimeProvider.System);

        // سياق المستأجر لكل نطاق خدمات (طلب HTTP أو تكرار مهمة خلفية): من يحدّد المتجر يطلب
        // TenantContext لضبطه مرة واحدة، وكل ما عداه يرى ITenantContext للقراءة فقط (ADR-0006).
        services.AddScoped<Common.Tenancy.TenantContext>();
        services.AddScoped<Common.Tenancy.ITenantContext>(sp => sp.GetRequiredService<Common.Tenancy.TenantContext>());

        // خدمة تطبيق مشتركة بين مسارَي الإلغاء (الإدارة + تعويض الدفع) — صنف ملموس
        // بلا واجهة: لا تنفيذ بديل له، فالواجهة ستكون تجريداً بلا سبب.
        services.AddScoped<Features.Orders.OrderStockRelease>();
        // منطق تأكيد الدفع الواحد لمدخلين بتفويض مختلف (العميل المالك / توقيع البوّابة).
        services.AddScoped<Features.Orders.OrderPaymentConfirmation>();
        // إصدار الجلسات (رمز تجديد + توكن وصول) لكل مداخلها: دخول، تسجيل، تجديد، تغيير كلمة مرور.
        services.AddScoped<Features.Auth.AuthSessionIssuer>();

        return services;
    }
}
