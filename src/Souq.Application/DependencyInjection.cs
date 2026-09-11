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
        // نطاق سجلّ حالة الاستخدام وزمنها أولاً (يشمل التحقّق)، ثم التحقّق، ثم التدقيق (مدخل صالح فقط يُسجَّل،
        // وسطره يُحفظ في معاملة المعالج نفسها — D-17).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UseCaseLoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        // الساعة: كل قراءة "الآن" في حالات الاستخدام تمرّ عبر TimeProvider (مدمج في .NET)
        // لا DateTime.UtcNow — فتُختبر الصلاحيات والانتهاء بساعة ثابتة (Phase 0 D12).
        services.TryAddSingleton(TimeProvider.System);

        // سياق المستأجر لكل نطاق خدمات (طلب HTTP أو تكرار مهمة خلفية): من يحدّد المتجر يطلب
        // TenantContext لضبطه مرة واحدة، وكل ما عداه يرى ITenantContext للقراءة فقط (ADR-0006).
        services.AddScoped<Common.Tenancy.TenantContext>();
        services.AddScoped<Common.Tenancy.ITenantContext>(sp => sp.GetRequiredService<Common.Tenancy.TenantContext>());

        // وحدة Inventory (المرحلة 6): تنفيذ واحد لعقدَي Ordering (الحجز، المتاح) ولمنفذ Catalog (فتح مخزون متغيّر
        // جديد). الإعدادات (مهلة الحجز) يسجّلها Infrastructure من Inventory:* بعد التحقّق منها.
        services.AddScoped<Features.Inventory.Reservations.InventoryWriter>();
        services.AddScoped<Features.Inventory.Reservations.InventoryReservations>();
        services.AddScoped<Features.Inventory.Contracts.IInventoryReservations>(
            sp => sp.GetRequiredService<Features.Inventory.Reservations.InventoryReservations>());
        services.AddScoped<Features.Inventory.Contracts.IStockAvailability>(
            sp => sp.GetRequiredService<Features.Inventory.Reservations.InventoryReservations>());
        services.AddScoped<Features.Products.Contracts.IVariantStockInitializer,
            Features.Inventory.Reservations.VariantStockInitializer>();
        // منطق تأكيد الدفع وإلغاء الطلب غير المشحون، لكل مداخله (العميل المالك، توقيع البوّابة، منسّق المهلة).
        services.AddScoped<Features.Orders.OrderPaymentConfirmation>();
        // محو العميل (حقّ الحذف) — مسار واحد للعميل نفسه وللإدارة (المرحلة 7).
        services.AddScoped<Features.Customers.CustomerErasure>();
        // إصدار الجلسات (رمز تجديد + توكن وصول) لكل مداخلها: دخول، تسجيل، تجديد، تغيير كلمة مرور.
        services.AddScoped<Features.Auth.AuthSessionIssuer>();
        // حسابات الإدارة (دعوة، تفعيل/إيقاف) مشتركة بين منطقة المنصّة وإدارة موظّفي المتجر.
        services.AddScoped<Common.Accounts.AccountInvitations>();
        services.AddScoped<Common.Accounts.AccountStatusChanger>();

        return services;
    }
}
