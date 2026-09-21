using NSubstitute;
using Souq.Application.Features.Tax;
using Souq.Application.Features.Tax.Contracts;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.TestDoubles;

// ============================================================================
// حاسبُ ضريبةٍ للاختبار (ADR-0055).
//
// **`None()` هو الحالة الافتراضية في كل اختبار**، وهي حالُ كل متجرٍ حقيقيّ: لم يختر ملفّ اختصاص،
// فلا ضريبة وسببُها مسمّى. ولهذا تصف اختباراتُ التسعير القائمة سلوكَ الإنتاج الفعليّ ولم يتغيّر
// منها رقمٌ واحد يوم صارت الضريبة حساباً بعد أن كانت صفراً صريحاً.
//
// و`Collecting()` للحالة الأخرى: ملفٌّ مختار، متحقَّقٌ منه، والجمع مفعَّل.
// ============================================================================
public static class TestTax
{
    public static ITaxCalculator None(string reason = TaxCollectionReasons.NoProfileSelected)
    {
        var calculator = Substitute.For<ITaxCalculator>();
        calculator.QuoteAsync(Arg.Any<TaxBasis>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                TaxQuote.None(call.Arg<TaxBasis>().Goods.Currency, reason)));
        return calculator;
    }

    // نسبةٌ واحدة بنقاط الأساس، بالعُرف المطلوب. الشحن يدخل الأساس أو لا، كما يقول الإصدار.
    public static ITaxCalculator Collecting(
        int basisPoints, TaxPriceMode mode = TaxPriceMode.Exclusive, bool shippingTaxable = false)
    {
        var calculator = Substitute.For<ITaxCalculator>();
        calculator.QuoteAsync(Arg.Any<TaxBasis>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var basis = call.Arg<TaxBasis>();
                var currency = basis.Goods.Currency;
                var taxable = shippingTaxable ? basis.Goods.Add(basis.Shipping) : basis.Goods;
                var raw = mode == TaxPriceMode.Inclusive
                    ? taxable.Amount * basisPoints / (10_000m + basisPoints)
                    : taxable.Amount * basisPoints / 10_000m;
                var amount = Money.FromCalculation(raw, currency);

                var snapshot = new TaxSnapshot(
                    1, 1, "TST", mode, TaxVerificationState.Verified, shippingTaxable,
                    [new TaxSnapshotLine("standard", "Standard (test)", basisPoints, amount.Amount)]);

                return Task.FromResult(new TaxQuote(
                    amount, mode, TaxCollectionReasons.Collecting, snapshot));
            });
        return calculator;
    }
}
