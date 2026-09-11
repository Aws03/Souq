using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Shipping.Contracts;

// ============================================================================
// عقد وحدة Shipping (المرحلة 12، Modules.md): IShippingRateProvider — استراتيجية أسعار الشحن. اليوم جدول طرق المتجر
// (StoreShippingRates)، وغداً مزوّد ناقل بأسعار من واجهته — بالعقد نفسه. خطّ التسعير (Shopping) يسأله، والدفع (Ordering)
// يأخذ لقطة الطريقة المختارة منه عبر التسعير.
// ============================================================================
public interface IShippingRateProvider
{
    // الطرق التي تخدم هذه الدولة (null: غير معروفة ⇒ غير المقيَّدة بدول وحدها)، بسعرها لسلة بهذا الإجمالي بعد الخصم.
    Task<ShippingQuote> QuoteAsync(Money goods, string? country, CancellationToken ct);
}

public sealed record ShippingOption(
    int MethodId, string Name, Money Cost, string? Carrier, string? TrackingUrlTemplate, int? MinDays, int? MaxDays);

// StoreShips: للمتجر طرق شحن مفعّلة (وإن لم يخدم أيٌّ منها هذا العنوان). بلا طرق أصلاً فالشحن مجاني بلا اختيار — كما قبل
// المرحلة 12، فلا يتعطّل دفع متجر لم يضبط الشحن بعد.
public sealed record ShippingQuote(IReadOnlyList<ShippingOption> Options, bool StoreShips);
