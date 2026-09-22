using Souq.Application.Features.Tax.Contracts;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Tax;

// ============================================================================
// التنفيذ الوحيد لحساب الضريبة ([ADR-0055](0055)).
//
// **وكلُّ بوّابةٍ فيه تُعيد سبباً**: لا ملفَّ مختار، أو الجمعُ معطّل، أو لا إصدارَ نافذاً في تلك
// اللحظة، أو إصدارٌ لم يتحقّق منه مهنيّ. والبوّابةُ الأخيرة هي التي يوجد كلُّ الملفّ لأجلها: نسبةٌ
// صحيحةُ الشكل ومنشورةٌ ومختارةٌ ومُفعَّلة، ولا تُحصَّل — لأنّ أحداً لم يؤكّدها.
//
// **والحسابُ بنقاط الأساس وبعملةِ المتجر، وتقريبٌ واحد في موضعٍ واحد** (`Money.FromCalculation`،
// ADR-0014). ولا `double` في أيّ خطوة: ثلاثُ خاناتٍ في الدينار تجعل خطأَ التقريب فرقاً يراه التاجر
// في تسويته.
//
// **والعُرفان يختلفان في الصيغة لا في الشكل:**
//   • مضاف (Exclusive): الضريبة = الأساس × النقاط ÷ 10000، وتُضاف إلى ما يدفعه المشتري.
//   • شامل (Inclusive): السعر المعروض يحتوي الضريبة، فالجزءُ المستخرَج = الأساس × النقاط ÷
//     (10000 + النقاط). والقسمةُ على المجموع لا على 10000 هي كلُّ الفرق، وعكسُها يُنتج رقماً أكبر
//     من الحقيقة بنسبةِ الضريبة نفسها.
// ============================================================================
// عامّ كـ `PricingService`: خدمةُ تطبيقٍ يبنيها اختبارُ وحدةٍ مباشرةً بمنافذ مُقلَّدة.
public sealed class TaxCalculator : ITaxCalculator
{
    private readonly IStoreTaxSettingsRepository _settings;
    private readonly ITaxProfileRepository _profiles;

    public TaxCalculator(IStoreTaxSettingsRepository settings, ITaxProfileRepository profiles)
    {
        _settings = settings; _profiles = profiles;
    }

    public async Task<TaxQuote> QuoteAsync(TaxBasis basis, DateTime at, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        return await QuoteForProfileAsync(
            basis, settings?.TaxProfileId, settings?.CollectionEnabled ?? false, at, ct);
    }

    // الاحتسابُ الفعليّ، ومصدرُ الاختيار وسيطٌ لا قراءة. مسارُ المتجر أعلاه يقرأ إعدادَه ثم
    // يُنادي هذا؛ ومسارُ فاتورة المنصّة (C5) يمرّر اختيارَ المنصّة — **وبوّابةٌ واحدة لكليهما**.
    public async Task<TaxQuote> QuoteForProfileAsync(
        TaxBasis basis, int? taxProfileId, bool collectionEnabled, DateTime at, CancellationToken ct = default)
    {
        var currency = basis.Goods.Currency;

        if (taxProfileId is not int profileId)
            return TaxQuote.None(currency, TaxCollectionReasons.NoProfileSelected);
        if (!collectionEnabled)
            return TaxQuote.None(currency, TaxCollectionReasons.CollectionDisabled);

        var profile = await _profiles.GetWithVersionsAsync(profileId, ct);
        var version = profile?.VersionOn(at);
        if (profile is null || version is null)
            return TaxQuote.None(currency, TaxCollectionReasons.NoEffectiveVersion);

        // **البوّابة التي يوجد الملفّ لأجلها.** انظر رأس الملفّ.
        if (!version.AllowsCollection)
            return TaxQuote.None(currency, TaxCollectionReasons.VersionNotVerified);

        // فئةٌ واحدة اليوم: إسنادُ فئةٍ لمنتجٍ هو الشريحة التالية، فكلُّ سطرٍ على الافتراضية.
        //
        // وفئةٌ بلا نسبةٍ تُنتج لقطةً بصفرٍ وبلا أسطر — **لا «لم يُضبَط»**: غيابُ نسبةٍ لفئةٍ هو
        // عدمُ خضوعها، وتجميدُ ذلك على الطلب يقول إنّ الضريبة حُسبت وكانت صفراً، لا إنّها أُهملت.
        var rates = version.RatesFor(TaxRate.DefaultCategory);

        // الأساس: البضاعة، وزائدها الشحنُ إن كان الاختصاص يُضرّبه — قاعدتُه لا تقديرُنا.
        var taxable = version.ShippingTaxable ? basis.Goods.Add(basis.Shipping) : basis.Goods;

        var lines = new List<TaxSnapshotLine>(rates.Count);
        var total = 0m;
        foreach (var rate in rates)
        {
            var amount = Portion(taxable.Amount, rate.BasisPoints, version.PriceMode, currency);
            lines.Add(new TaxSnapshotLine(rate.Code, rate.Name, rate.BasisPoints, amount));
            total += amount;
        }

        var snapshot = new TaxSnapshot(
            profile.Id, version.Version, profile.Jurisdiction, version.PriceMode,
            version.Verification.State, version.ShippingTaxable, lines);

        return new TaxQuote(
            new Money(total, currency), version.PriceMode, TaxCollectionReasons.Collecting, snapshot);
    }

    // نقطةُ التقريب الوحيدة. `FromCalculation` تُقرّب إلى خانات العملة الصغرى بقاعدةٍ واحدة
    // معتمدة (ADR-0014)، فلا يتسرّب فلسٌ من سطرٍ إلى آخر.
    private static decimal Portion(decimal basis, int basisPoints, TaxPriceMode mode, string currency)
    {
        if (basisPoints == 0) return 0m;
        var raw = mode == TaxPriceMode.Inclusive
            ? basis * basisPoints / (10_000m + basisPoints)
            : basis * basisPoints / 10_000m;
        return Money.FromCalculation(raw, currency).Amount;
    }
}
