using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Baskets.Contracts;
using Souq.Application.Features.Inventory.Contracts;
using Souq.Application.Features.Orders.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Orders.Checkout;

// ============================================================================
// المرحلة الأولى من الدفع (TD-13، قُسِّم في M5): **تحقّق بلا أي أثر**.
//
// كل ما هنا قراءة: من هو العميل، وأي عنوانين، وما أسطره، وبكم — ثم هل كلّها قابلة للبيع، وهل المتاح يكفي،
// وهل الكوبون والشحن مقبولان. لا صفّ يُكتب، فأي رفض هنا يترك النظام كما كان تماماً.
//
// **ترتيب الفحوص عقد، لا تفصيلة.** العميل ثم العنوان ثم الأسطر ثم المتاح ثم الكوبون ثم الشحن: كل واحد يعيد
// رمزه الثابت (ADR-0017) والعميل يفرّع عليه. تبديل الترتيب يغيّر أي خطأ يراه المتسوّق حين تكون مشكلتان معاً،
// وهو تغيير سلوك مرئي لا إعادة تنظيم — CreateOrderHandlerTests تحرس هذا صراحةً.
// ============================================================================
public sealed class CheckoutQuote
{
    private readonly ICustomerRepository _customers;
    private readonly IPricing _pricing;
    private readonly IBasketCheckout _baskets;
    private readonly IStockAvailability _availability;
    private readonly ICurrentUser _currentUser;

    public CheckoutQuote(
        ICustomerRepository customers, IPricing pricing, IBasketCheckout baskets,
        IStockAvailability availability, ICurrentUser currentUser)
    {
        _customers = customers; _pricing = pricing; _baskets = baskets;
        _availability = availability; _currentUser = currentUser;
    }

    public async Task<Result<CheckoutDraft>> PrepareAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        // العميل هو المستخدم الحالي دائماً — الأمر لا يحمل معرّف عميل يمكن التلاعب به (B7).
        var customerId = _currentUser.RequireCustomerId();
        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer is null)
            // توكن صالح لحساب لم يعد موجوداً ⇒ الهوية نفسها لم تعد صالحة (401 ⇒ إعادة دخول).
            return Fail(Error.Unauthorized("CustomerNotFound", "العميل غير موجود"));

        // المحظور تجارياً (المرحلة 7) يرى حسابه وطلباته لكنه لا يطلب.
        if (customer.IsBlocked)
            return Fail(Error.Forbidden("CustomerBlocked", "حسابك موقوف عن الشراء في هذا المتجر."));

        // العنوانان لقطتان: من دفتر العميل نفسه (معرّف عنوان غيره ⇒ غير موجود) أو النصّ المُرسَل للشحن. الفوترة بلا
        // اختيار ⇒ عنوان الفوترة الافتراضي في الدفتر، وإلا عنوان الشحن نفسه (يقرّره الكيان).
        // دولة الشحن (المرحلة 12) من عنوان الدفتر نفسه — لا يرسلها العميل؛ عنوان نصّي حرّ بلا دولة ⇒ الطرق غير المقيَّدة بدول.
        var shippingAddress = cmd.ShippingAddress ?? "";
        string? shippingCountry = null;
        if (cmd.ShippingAddressId is int shippingId)
        {
            if (FromBook(customer, shippingId) is not { } saved) return Fail(AddressNotFound);
            shippingAddress = saved;
            shippingCountry = customer.Addresses.First(a => a.Id == shippingId).ToPostalAddress().Country;
        }
        var billingAddress = customer.Addresses.FirstOrDefault(a => a.IsDefaultBilling) is { } defaultBilling
            ? FromBook(customer, defaultBilling.Id)
            : null;
        if (cmd.BillingAddressId is int billingId)
        {
            if (FromBook(customer, billingId) is not { } saved) return Fail(AddressNotFound);
            billingAddress = saved;
        }

        // الأسطر: المُرسَلة، وإلا سلة العميل. ثم التسعير: كل سطر من الكتالوج الحيّ لهذا المتجر (منتج غير منشور أو من
        // متجر آخر غير قابل للبيع).
        var lines = cmd.Items is { Count: > 0 }
            ? cmd.Items.Select(i => new PricingLine(i.ProductId, i.Quantity, i.VariantId)).ToList()
            : await _baskets.LinesForCustomerAsync(customerId, ct);
        if (lines.Count == 0)
            return Fail(Error.Validation("BasketEmpty", "السلة فارغة — أضف منتجات قبل إتمام الطلب"));

        var quote = await _pricing.QuoteAsync(
            lines, cmd.CouponCode, customerId, new ShippingRequest(cmd.ShippingMethodId, shippingCountry), ct);
        if (quote.Lines.FirstOrDefault(l => !l.Sellable) is { } unsellable)
            return Fail(unsellable.VariantRequired
                ? Error.BusinessRule("VariantRequired", $"للمنتج رقم {unsellable.ProductId} أكثر من متغيّر: حدّد المتغيّر المطلوب")
                : Error.Validation("ProductNotFound", $"المنتج رقم {unsellable.ProductId} غير متاح"));

        // قراءة مبكرة للمتاح: رسالة واضحة قبل أي كتابة. الحارس الحقيقي هو الحجز داخل المعاملة (OrderPlacement)،
        // الذي يرفض السباق الذي يقع *بعد* هذه القراءة — فهذه للرسالة، وذاك للصحّة.
        var available = await _availability.AvailableAsync(quote.Lines.Select(l => l.VariantId).Distinct().ToList(), ct);
        foreach (var group in quote.Lines.GroupBy(l => l.VariantId))
        {
            var requested = group.Sum(l => l.Quantity);
            var inStock = available.GetValueOrDefault(group.Key);
            if (requested > inStock)
                return Fail(Error.BusinessRule("InsufficientStock",
                    $"الكمية المطلوبة ({requested}) من \"{group.First().Name}\" غير متوفرة. المتاح: {Math.Max(inStock, 0)}"));
        }

        // كوبون مرفوض يوقف الطلب برمزه (ModuleDisabled، CouponNotFound، InvalidCoupon — 422) قبل أي كتابة.
        if (quote.Coupon is { Applied: false } rejected)
            return Fail(Error.BusinessRule(rejected.ErrorCode!, rejected.Message!));

        // الشحن (المرحلة 12): متجر له طرق يلزمه اختيار طريقة تخدم العنوان — قبل أي كتابة، برمزها (422).
        if (quote.ShippingOutcome is { ErrorCode: { } shippingError } delivery)
            return Fail(Error.BusinessRule(shippingError, delivery.Message!));

        return Result<CheckoutDraft>.Success(
            new CheckoutDraft(customerId, shippingAddress, billingAddress, shippingCountry, quote));
    }

    private static Error AddressNotFound => Error.Validation("AddressNotFound", "العنوان غير موجود في دفترك");

    private static Result<CheckoutDraft> Fail(Error error) => Result<CheckoutDraft>.Failure(error);

    private static string? FromBook(Customer customer, int addressId) =>
        customer.Addresses.FirstOrDefault(a => a.Id == addressId)?.ToPostalAddress().ToSingleLine(Order.ShippingAddressMaxLength);
}

// ما خرج من التحقّق بلا أثر: كل ما تحتاجه المرحلتان التاليتان، ولا شيء يُقرأ من جديد بعده.
public sealed record CheckoutDraft(
    int CustomerId, string ShippingAddress, string? BillingAddress, string? ShippingCountry, PriceQuote Quote);
