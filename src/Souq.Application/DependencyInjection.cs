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
        // وحدة Shopping (المرحلة 8): خطّ التسعير الواحد (السلة والدفع)، وسلة المتصل وعرضها. الإعدادات (أعمار السلال)
        // يسجّلها Infrastructure من Basket:* بعد التحقّق منها.
        services.AddScoped<Features.Baskets.Contracts.IPricing, Features.Baskets.Pricing.PricingService>();
        services.AddScoped<Features.Baskets.BasketResolver>();
        services.AddScoped<Features.Baskets.BasketViews>();
        services.AddScoped<Features.Baskets.BasketLines>();
        // وحدة Promotions (المرحلة 10): استخدامات الكوبونات — حجز عند إنشاء الطلب، تأكيد بالدفع، تحرير بالإلغاء.
        services.AddScoped<Features.Coupons.Contracts.ICouponRedemptions, Features.Coupons.Redemptions.CouponRedemptions>();
        // وحدة Payments (المرحلة 11): دفعة الطلب واستردادها لـ Ordering، ومحرّر حساب بوّابة المتجر لمساري المتجر والمنصّة.
        // سياسة مفاتيحه (StorePaymentPolicy) يسجّلها Infrastructure من الإعداد والبيئة.
        services.AddScoped<Features.Payments.Contracts.IOrderPayments, Features.Payments.OrderPayments>();
        // المحرّر مسجَّل بنوعه الفعلي ليُحلّ محلّياً (معالجات Payments)، وبعقده أيضاً (يفتح نفس نسخة النطاق) —
        // فقط عبر ذلك العقد يصل إليه مسار المنصّة في وحدة أخرى (Features/Platform/TenantPaymentAccounts.cs).
        services.AddScoped<Features.Payments.StorePaymentAccountEditor>();
        services.AddScoped<Features.Payments.Contracts.IStorePaymentAccountEditor>(
            sp => sp.GetRequiredService<Features.Payments.StorePaymentAccountEditor>());
        // وحدة Shipping (المرحلة 12): استراتيجية أسعار الشحن — اليوم جدول طرق المتجر؛ مزوّد ناقل يستبدل هذا التسجيل.
        services.AddScoped<Features.Shipping.Contracts.IShippingRateProvider, Features.Shipping.StoreShippingRates>();
        // الدفع من السلة (المرحلة 9): Ordering يقرأ أسطرها ويستهلك المشترى منها عند تأكيد الدفع.
        services.AddScoped<Features.Baskets.Contracts.IBasketCheckout, Features.Baskets.BasketCheckout>();
        // منطق تأكيد الدفع وإلغاء الطلب غير المشحون، لكل مداخله (العميل المالك، توقيع البوّابة، منسّق المهلة).
        services.AddScoped<Features.Orders.OrderPaymentConfirmation>();
        // محو العميل (حقّ الحذف) — مسار واحد للعميل نفسه وللإدارة (المرحلة 7).
        services.AddScoped<Features.Customers.CustomerErasure>();
        // إصدار الجلسات (رمز تجديد + توكن وصول) لكل مداخلها: دخول، تسجيل، تجديد، تغيير كلمة مرور.
        services.AddScoped<Features.Auth.AuthSessionIssuer>();
        // حسابات الإدارة (دعوة، تفعيل/إيقاف) مشتركة بين منطقة المنصّة وإدارة موظّفي المتجر.
        services.AddScoped<Common.Accounts.AccountInvitations>();
        services.AddScoped<Common.Accounts.AccountStatusChanger>();
        // الإشعارات (المرحلة 14): معالجو رسائل صندوق الصادر — يستدعيهم المُرسِل الخلفي داخل نطاق متجر كل رسالة.
        services.AddScoped<Features.Notifications.NotificationEmails>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Common.Notifications.PasswordResetRequested>,
            Features.Notifications.PasswordResetEmailHandler>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Common.Notifications.EmailVerificationRequested>,
            Features.Notifications.EmailVerificationEmailHandler>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Common.Notifications.AccountInvited>,
            Features.Notifications.InvitationEmailHandler>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Common.Notifications.OrderEmailRequested>,
            Features.Notifications.OrderEmailHandler>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Domain.Events.OrderStatusChanged>,
            Features.Notifications.OrderStatusChangedHandler>();
        services.AddScoped<Common.Notifications.INotificationMessageHandler<Domain.Events.StockBecameLow>,
            Features.Notifications.StockBecameLowHandler>();

        return services;
    }
}
