namespace Souq.Application.Features.Baskets.Contracts;

// ============================================================================
// الدفع من السلة (المرحلة 9، العقد IBasketCheckout): Ordering يقرأ أسطر سلة العميل ليسعّرها وينشئ الطلب منها،
// ويستهلك منها ما دُفع ثمنه عند تأكيد الدفع — في وحدة العمل نفسها (لا حفظ هنا)، فتنجح مع التأكيد أو تُلغى معه. طلب
// فشل دفعه أو أُلغي يترك السلة كما هي.
// ============================================================================
public interface IBasketCheckout
{
    Task<IReadOnlyList<PricingLine>> LinesForCustomerAsync(int customerId, CancellationToken ct);

    // يُنقص من سلة العميل الكميات المشتراة لكل منتج (ويحذف ما نفد) — ما أضافه العميل بعد الطلب يبقى.
    Task ConsumeAsync(int customerId, IReadOnlyList<PricingLine> purchased, CancellationToken ct);
}
