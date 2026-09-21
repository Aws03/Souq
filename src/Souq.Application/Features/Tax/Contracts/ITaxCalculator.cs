using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Tax.Contracts;

// ============================================================================
// حسابُ ضريبةِ سلّةٍ أو طلبٍ لمتجر السياق ([ADR-0055](0055)).
//
// **يعيد صفراً بسببٍ مسمّى، لا صفراً صامتاً.** متجرٌ لم يختر ملفّاً، أو اختار ولم يفعّل الجمع، أو
// فعّل وإصدارُه لم يتحقّق منه مهنيّ — ثلاثُ حالاتٍ مختلفة يقرؤها التاجر بأسمائها. وصفرٌ بلا سببٍ
// يقرأ كأنه عطب، فيُفتَح له بلاغٌ بدل أن يُكمَل إعداد.
//
// **والوقتُ مدخلٌ لا «الآن»**: القواعدُ تُرجَّح بلحظةِ الحدث، فطلبٌ يُعاد احتسابُه لا يأخذ قواعدَ
// اليوم. والمنفذُ يُشكَّل كي يصير محوّلاً إلى خدمةٍ ضريبية خارجية تنفيذاً ثانياً له لا إعادةَ تصميم
// (ADR-0055 §الخيار ب).
// ============================================================================
public interface ITaxCalculator
{
    Task<TaxQuote> QuoteAsync(TaxBasis basis, DateTime at, CancellationToken ct = default);
}

// أساسُ الاحتساب: قيمةُ البضاعة بعد الخصم، وتكلفةُ الشحن. وضريبةُ الشحن قاعدةُ اختصاصٍ يقرؤها
// الحاسب من الإصدار، فالمُنادي يمرّر الرقمَين ولا يقرّر أيَّهما يُضرَّب.
public sealed record TaxBasis(Money Goods, Money Shipping);

// ============================================================================
// نتيجةُ الاحتساب: المبلغ، والعُرف، والسبب، واللقطة.
//
// اللقطةُ موجودةٌ حين يُجمَع شيءٌ فقط، وهي ما يُجمَّد على الطلب. ووجودُها مشروطٌ بوجود مبلغٍ
// موجَب هو ما يجعل «مبلغٌ بلا قواعدَ أنتجته» مستحيلاً في الطلب (`Order.ApplyTax`).
// ============================================================================
public sealed record TaxQuote(Money Amount, TaxPriceMode? PriceMode, string Reason, TaxSnapshot? Snapshot)
{
    public bool Collected => Snapshot is not null;

    // الضريبةُ تُضاف إلى الإجمالي في «مضاف» وحده؛ في «شامل» هي داخل الأسعار أصلاً.
    public Money AddedToTotal(string currency) =>
        PriceMode == TaxPriceMode.Exclusive ? Amount : Money.Zero(currency);

    public static TaxQuote None(string currency, string reason) =>
        new(Money.Zero(currency), null, reason, null);
}
