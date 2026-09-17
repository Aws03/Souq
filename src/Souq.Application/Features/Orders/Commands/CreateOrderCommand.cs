using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Orders.Commands;

// الأمر يحمل ما يحتاجه إنشاء الطلب: العنوان، الأسطر المطلوبة، وكوبون اختياري. لا معرّف
// عميل: العميل هو المستخدم الحالي (ICurrentUser) دائماً — حقل في الجسم كان يمكن التلاعب به
// لو نسي Controller استبداله. لا رمز دفع هنا — Stripe.js يتولّى تفاصيل البطاقة مباشرة من
// المتصفّح؛ هذا الأمر ينشئ الطلب Pending فقط ويعيد نيّة دفع (ClientSecret) للواجهة.
// ShippingAddressId (المرحلة 7): عنوان من دفتر العميل نفسه — تُحفظ لقطته النصّية على الطلب. بدونه يُرسل العنوان نصّاً.
// Items (المرحلة 9): اختيارية — بلا أسطر يُنشأ الطلب من سلة العميل. BillingAddressId: عنوان فوترة من الدفتر؛ بدونه
// الافتراضي للفوترة، وإلا عنوان الشحن. ShippingMethodId (المرحلة 12): طريقة شحن تخدم دولة العنوان — لازمة حين للمتجر طرق.
public record CreateOrderCommand(
    string? ShippingAddress,
    List<OrderLineInput>? Items,
    string? CouponCode,
    int? ShippingAddressId = null,
    int? BillingAddressId = null,
    int? ShippingMethodId = null
) : IRequest<Result<OrderCreatedDto>>;

// VariantId اختياري بقاعدة السلة نفسها: بدونه متغيّر المنتج الضمني، ولمنتج بأكثر من متغيّر نشط VariantRequired.
public record OrderLineInput(int ProductId, int Quantity, int? VariantId = null);

public record OrderCreatedDto(
    int OrderId, int OrderNumber, string Status,
    decimal Subtotal, decimal? DiscountAmount, decimal TotalAmount, string Currency,
    string ClientSecret, decimal ShippingCost = 0);
