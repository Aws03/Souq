using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Events;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Notifications;

// ============================================================================
// أحداث الطلب والمخزون ⇒ إشعارات داخل التطبيق (المرحلة 14، ADR-0034):
//   • العميل: كل انتقال بعد الإنشاء (دُفع، شُحن، سُلِّم، أُلغي) — وبريد لما يهمّه (الدفع والشحن والتسليم، والإلغاء إن كان مدفوعاً
//     أو بيد المتجر؛ إلغاء العميل طلبه أو انتهاء مهلة دفعه لا يستحقّ بريداً).
//   • الإدارة: طلب جديد مدفوع (orders.view)، ومخزون نزل عن حدّه (inventory.view) — لكل حساب فعّال يملك الصلاحية في المتجر.
// الإشعارات وطلب البريد في حفظ واحد (البريد رسالة صادر مستقلّة يُعاد وحدها إن فشل المزوّد). المعالج يقرأ الطلب الحيّ: بيانات
// الإشعار صغيرة (رقم الطلب، الحالة، اسم المنتج) تعرضها الواجهة بلغة الزائر.
// ============================================================================
public sealed class OrderStatusChangedHandler : INotificationMessageHandler<OrderStatusChanged>
{
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IUserRepository _users;
    private readonly INotificationRepository _notifications;
    private readonly INotificationOutbox _outbox;
    private readonly IUnitOfWork _uow;

    public OrderStatusChangedHandler(
        IOrderRepository orders, ICustomerRepository customers, IUserRepository users, INotificationRepository notifications,
        INotificationOutbox outbox, IUnitOfWork uow)
    {
        _orders = orders; _customers = customers; _users = users; _notifications = notifications; _outbox = outbox; _uow = uow;
    }

    public async Task HandleAsync(OrderStatusChanged change, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(change.OrderId, ct);
        if (order is null) return;

        var data = NotificationData.Of(("orderId", order.Id), ("orderNumber", order.OrderNumber), ("status", change.To));
        var customer = await _customers.GetByIdAsync(change.CustomerId, ct);
        var reachable = customer is { ErasedAt: null };
        if (reachable)
            await _notifications.AddAsync(new Notification(customer!.UserId, NotificationKinds.OrderStatus, data), ct);

        if (change.To == OrderStatus.Paid)
            await NotifyStaffAsync(_users, _notifications, Permissions.Orders.View, NotificationKinds.NewOrder, data, ct);

        if (reachable && WantsEmail(change))
            _outbox.Enqueue(new OrderEmailRequested(order.Id, change.To));

        await _uow.SaveChangesAsync(ct);
    }

    private static bool WantsEmail(OrderStatusChanged change) => change.To switch
    {
        OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered => true,
        OrderStatus.Cancelled => change.From == OrderStatus.Paid || change.By == OrderActorKind.Staff,
        _ => false,
    };

    internal static async Task NotifyStaffAsync(
        IUserRepository users, INotificationRepository notifications, string permission, string kind, string data, CancellationToken ct)
    {
        foreach (var userId in await users.ListActiveIdsByRolesAsync(RolePermissions.RolesGranting(permission), ct))
            await notifications.AddAsync(new Notification(userId, kind, data), ct);
    }
}

public sealed class StockBecameLowHandler : INotificationMessageHandler<StockBecameLow>
{
    private readonly IProductRepository _products;
    private readonly IUserRepository _users;
    private readonly INotificationRepository _notifications;
    private readonly ITenantContext _tenant;
    private readonly IUnitOfWork _uow;

    public StockBecameLowHandler(
        IProductRepository products, IUserRepository users, INotificationRepository notifications, ITenantContext tenant, IUnitOfWork uow)
    {
        _products = products; _users = users; _notifications = notifications; _tenant = tenant; _uow = uow;
    }

    public async Task HandleAsync(StockBecameLow low, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(low.ProductId, ct);
        var culture = _tenant.RequireTenant().DefaultCulture;
        var name = NotificationData.Short(product?.NameIn(culture) ?? $"#{low.ProductId}");

        // الحدث من Inventory يحمل معرّف المتغيّر وحده؛ الوصف ("M / أحمر") يُقرأ هنا من تجمّع المنتج نفسه الذي يُحمَّل للاسم —
        // فلا تعرف Inventory شيئاً عن الخيارات. منتج بلا خيارات لا وصف له، والإشعار كما كان.
        var label = product?.VariantLabel(low.VariantId, culture);
        var fields = new List<(string Key, object Value)>
        {
            ("productId", low.ProductId), ("productName", name), ("variantId", low.VariantId),
            ("available", low.Available), ("threshold", low.Threshold),
        };
        if (!string.IsNullOrEmpty(label)) fields.Add(("variantLabel", NotificationData.Short(label)));
        var data = NotificationData.Of([.. fields]);

        await OrderStatusChangedHandler.NotifyStaffAsync(_users, _notifications, Permissions.Inventory.View, NotificationKinds.LowStock, data, ct);
        await _uow.SaveChangesAsync(ct);
    }
}

// بريد العميل عن طلبه بهوية المتجر ورابط صفحة التتبّع العامة على نطاقه الأساسي.
public sealed class OrderEmailHandler : INotificationMessageHandler<OrderEmailRequested>
{
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly IStoreOrigins _origins;
    private readonly ITenantContext _tenant;
    private readonly NotificationEmails _emails;

    public OrderEmailHandler(
        IOrderRepository orders, ICustomerRepository customers, IStoreOrigins origins, ITenantContext tenant, NotificationEmails emails)
    {
        _orders = orders; _customers = customers; _origins = origins; _tenant = tenant; _emails = emails;
    }

    public async Task HandleAsync(OrderEmailRequested request, CancellationToken ct)
    {
        EmailTemplate? template = request.Status switch
        {
            OrderStatus.Paid => EmailTemplate.OrderConfirmed,
            OrderStatus.Shipped => EmailTemplate.OrderShipped,
            OrderStatus.Delivered => EmailTemplate.OrderDelivered,
            OrderStatus.Cancelled => EmailTemplate.OrderCancelled,
            _ => null,
        };
        // التجمّع كاملاً: بلا أسطره كان الإجمالي يُجمع من صفر سطر — رسالة بقيمة الشحن وحدها، وطلب بخصم يرمي
        // (خصم أكبر من فرعي صفر) فتموت الرسالة بعد آخر محاولة (R-06).
        var order = template is null ? null : await _orders.GetWithItemsAsync(request.OrderId, ct);
        var customer = order is null ? null : await _customers.GetByIdAsync(order.CustomerId, ct);
        if (order is null || customer is not { ErasedAt: null }) return;

        var origin = await _origins.ForStoreAsync(_tenant.RequireTenant().Id, ct);

        // الإجماليات المجمّدة لحظة التثبيت (PlacedSubtotal/PlacedTotal) لا محسوبة الآن: الفاتورة لا تتغيّر بتغيّر الكتالوج.
        var values = new Dictionary<string, string>
        {
            ["orderNumber"] = order.OrderNumber.ToString(CultureInfo.InvariantCulture),
            ["subtotal"] = Amount(order.PlacedSubtotal),
            ["total"] = Amount(order.PlacedTotal),
            ["currency"] = order.Currency,
            ["trackingNumber"] = order.TrackingNumber ?? "",
            ["carrier"] = order.ShippingCarrier ?? "",
        };
        // الخصم والشحن يُعرضان حين يوجدان فقط — القالب لا يقرّر، والصفر لا يُطبع سطراً.
        if (order.DiscountAmount is { Amount: > 0 } discount) values["discount"] = Amount(discount.Amount);
        if (order.ShippingAmount > 0) values["shipping"] = Amount(order.ShippingAmount);

        var lines = order.Items
            .Select(item => new EmailLine(item.ProductName, item.Quantity, Amount(item.LineTotal.Amount)))
            .ToList();

        await _emails.SendAsync(customer.Email, template!.Value, origin, StorefrontLinks.OrderTracking(origin, order.TrackingToken),
            values, ct, lines);
    }

    private static string Amount(decimal value) => value.ToString("0.00#", CultureInfo.InvariantCulture);
}

internal static class NotificationData
{
    // النصوص العربية كما هي لا \uXXXX: الحرف المرمَّز ستّة أحرف، فاسم منتج طويل كان يتجاوز حدّ البيانات (1000) فيموت الإشعار.
    // البيانات JSON يُخزَّن ويُقرأ ولا يُحقن في HTML — والواجهة تعرض قيمه نصّاً.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public const int TextMaxLength = 150;

    public static string Of(params (string Key, object Value)[] values) =>
        JsonSerializer.Serialize(values.ToDictionary(v => v.Key, v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? ""), Json);

    public static string Short(string text) => text.Length <= TextMaxLength ? text : text[..TextMaxLength] + "…";
}
