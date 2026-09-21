using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Features.Tax;
using Souq.Application.Features.Tax.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Tax;

// ============================================================================
// حسابُ الضريبة (ADR-0055).
//
// **أربعُ بوّاباتٍ تُعيد صفراً بسببٍ مسمّى، وواحدةٌ منها هي التي يوجد الملفّ لأجلها**: إصدارٌ
// منشورٌ ومختارٌ ومُفعَّل، ونسبتُه صحيحةُ الشكل — ولا يُحصَّل شيء، لأنّ أحداً لم يؤكّدها. لو انقلبت
// هذه البوّابة يوماً لَصار رقمٌ وجدته الهندسة في وثيقةٍ يُفرَض على مشترٍ حقيقيّ.
//
// والصيغتان تختلفان في القاسم: «مضاف» ÷ 10000، و«شامل» ÷ (10000 + النقاط) — وعكسُهما يُنتج رقماً
// أكبر من الحقيقة بنسبة الضريبة نفسها.
// ============================================================================
public class TaxCalculatorTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly IStoreTaxSettingsRepository _settings = Substitute.For<IStoreTaxSettingsRepository>();
    private readonly ITaxProfileRepository _profiles = Substitute.For<ITaxProfileRepository>();

    private TaxCalculator Calculator() => new(_settings, _profiles);

    private static TaxBasis Basis(decimal goods, decimal shipping = 0m) =>
        new(new Money(goods, "JOD"), new Money(shipping, "JOD"));

    // ملفٌّ بإصدارٍ واحد: منشورٌ دائماً، ومتحقَّقٌ منه إن طُلب.
    private TaxProfile Profile(
        int basisPoints = 1600, TaxPriceMode mode = TaxPriceMode.Exclusive,
        bool verified = true, bool shippingTaxable = false, string category = TaxRate.DefaultCategory)
    {
        var profile = new TaxProfile("JO", "Test jurisdiction");
        var version = profile.AddDraft(Now.AddYears(-1), mode, shippingTaxable);
        version.SetRates([new TaxRate("standard", "Standard (test)", basisPoints, category)]);
        version.Publish();
        if (verified) version.Verify("محاسب (اختبار)", Now.AddMonths(-1), null);
        _profiles.GetWithVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(profile);
        return profile;
    }

    private void Selected(bool collecting = true)
    {
        var settings = StoreTaxSettings.None();
        settings.SelectProfile(1, Now);
        if (collecting) settings.SetCollection(true);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(settings);
    }

    [Fact]
    public async Task بلا_ملفّ_مختار_صفر_بسببه()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns((StoreTaxSettings?)null);

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Amount.Amount.Should().Be(0m);
        quote.Collected.Should().BeFalse();
        quote.Snapshot.Should().BeNull();
        quote.Reason.Should().Be(TaxCollectionReasons.NoProfileSelected);
    }

    [Fact]
    public async Task الجمع_معطّلاً_صفر_بسببه()
    {
        Selected(collecting: false);
        Profile();

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Reason.Should().Be(TaxCollectionReasons.CollectionDisabled);
        quote.Snapshot.Should().BeNull();
    }

    // لحظةٌ قبل نفاذ أيّ إصدار: لا قاعدةَ تنطبق، فلا ضريبة — وليس «صفراً افتراضياً».
    [Fact]
    public async Task بلا_إصدار_نافذ_في_تلك_اللحظة_صفر_بسببه()
    {
        Selected();
        var profile = Profile();

        var quote = await Calculator().QuoteAsync(Basis(100m), profile.Versions.First().EffectiveFrom.AddDays(-1));

        quote.Reason.Should().Be(TaxCollectionReasons.NoEffectiveVersion);
        quote.Snapshot.Should().BeNull();
    }

    // ============================================================================
    // **البوّابة التي يوجد الملفّ لأجلها.** انظر رأس الملفّ.
    // ============================================================================
    [Fact]
    public async Task إصدار_منشور_لم_يتحقّق_منه_أحد_لا_يُحصَّل()
    {
        Selected();
        Profile(verified: false);

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Amount.Amount.Should().Be(0m);
        quote.Snapshot.Should().BeNull("لا لقطةَ لضريبةٍ لم تُجمَع");
        quote.Reason.Should().Be(TaxCollectionReasons.VersionNotVerified);
    }

    [Fact]
    public async Task عُرف_مضاف_يحتسب_على_الأساس_ويُضاف_للإجمالي()
    {
        Selected();
        Profile(basisPoints: 1600, mode: TaxPriceMode.Exclusive);

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Amount.Amount.Should().Be(16m, "100 × 1600 ÷ 10000");
        quote.PriceMode.Should().Be(TaxPriceMode.Exclusive);
        quote.AddedToTotal("JOD").Amount.Should().Be(16m, "المشتري يدفع أكثر من المعروض");
        quote.Collected.Should().BeTrue();
    }

    // «شامل»: الجزءُ المستخرَج من سعرٍ يحتويه — القسمةُ على (10000 + النقاط)، ولا يُضاف للإجمالي.
    [Fact]
    public async Task عُرف_شامل_يستخرج_الجزء_ولا_يُضاف_للإجمالي()
    {
        Selected();
        Profile(basisPoints: 1600, mode: TaxPriceMode.Inclusive);

        var quote = await Calculator().QuoteAsync(Basis(116m), Now);

        quote.Amount.Amount.Should().Be(16m, "116 × 1600 ÷ 11600 — السعر يحتوي الضريبة أصلاً");
        quote.AddedToTotal("JOD").Amount.Should().Be(0m, "إضافتُها تُحصّلها مرّتين");
    }

    // ضريبةُ الشحن قاعدةُ اختصاصٍ: تدخل الأساس أو لا، كما يقول الإصدار — ولا افتراضَ من الكود.
    [Fact]
    public async Task الشحن_يدخل_الأساس_إن_قال_الإصدار_ذلك()
    {
        Selected();
        Profile(basisPoints: 1000, mode: TaxPriceMode.Exclusive, shippingTaxable: false);
        (await Calculator().QuoteAsync(Basis(100m, shipping: 50m), Now)).Amount.Amount.Should().Be(10m);

        Selected();
        Profile(basisPoints: 1000, mode: TaxPriceMode.Exclusive, shippingTaxable: true);
        (await Calculator().QuoteAsync(Basis(100m, shipping: 50m), Now)).Amount.Amount.Should().Be(15m);
    }

    // ============================================================================
    // فئةٌ بلا نسبة ⇒ لقطةٌ بصفرٍ **وبلا أسطر**، لا «لم يُضبَط»: غيابُ نسبةٍ لفئةٍ عدمُ خضوعها،
    // وتجميدُ ذلك يقول إنّ الضريبة حُسبت وكانت صفراً.
    // ============================================================================
    [Fact]
    public async Task فئة_بلا_نسبة_تُنتج_لقطة_بصفر_لا_غياب_إعداد()
    {
        Selected();
        Profile(category: "books");

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Amount.Amount.Should().Be(0m);
        quote.Collected.Should().BeTrue("حُسبت وكانت صفراً");
        quote.Snapshot!.Lines.Should().BeEmpty();
        quote.Reason.Should().Be(TaxCollectionReasons.Collecting);
    }

    // ============================================================================
    // اللقطةُ تحمل ما يكفي لإعادة الاشتقاق من الطلب وحده: الملفّ وإصداره واختصاصه وعُرفه وحالةُ
    // تحقّقه، وكلُّ نسبةٍ بنقاطها ومبلغها — ومجموعُ الأسطر يطابق المحصَّل (شرطُ `Order.ApplyTax`).
    // ============================================================================
    [Fact]
    public async Task اللقطة_تكفي_لإعادة_الاشتقاق_ومجموعها_يطابق_المحصَّل()
    {
        Selected();
        var profile = Profile(basisPoints: 1600);

        var quote = await Calculator().QuoteAsync(Basis(250m), Now);
        var snapshot = quote.Snapshot!;

        snapshot.TaxProfileId.Should().Be(profile.Id);
        snapshot.Version.Should().Be(1);
        snapshot.Jurisdiction.Should().Be("JO");
        snapshot.PriceMode.Should().Be(TaxPriceMode.Exclusive);
        snapshot.Verification.Should().Be(TaxVerificationState.Verified);
        snapshot.Lines.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new TaxSnapshotLine("standard", "Standard (test)", 1600, 40m));
        snapshot.TotalAmount.Should().Be(quote.Amount.Amount);
    }

    // نسبتان على الفئة نفسها (وطنية وبلدية): سطران، ومجموعُهما هو المحصَّل.
    [Fact]
    public async Task نسبتان_على_الفئة_نفسها_سطران_ومجموعهما_المحصَّل()
    {
        Selected();
        var profile = new TaxProfile("JO", "Two rates (test)");
        var version = profile.AddDraft(Now.AddYears(-1), TaxPriceMode.Exclusive, false);
        version.SetRates([
            new TaxRate("national", "National", 1000),
            new TaxRate("municipal", "Municipal", 200),
        ]);
        version.Publish();
        version.Verify("محاسب", Now, null);
        _profiles.GetWithVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(profile);

        var quote = await Calculator().QuoteAsync(Basis(100m), Now);

        quote.Amount.Amount.Should().Be(12m);
        quote.Snapshot!.Lines.Should().HaveCount(2);
        quote.Snapshot.TotalAmount.Should().Be(12m);
    }
}
