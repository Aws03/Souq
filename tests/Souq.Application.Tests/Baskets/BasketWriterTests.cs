using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Baskets;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Baskets;

// ============================================================================
// آلية إعادة المحاولة على تعارض دمج السلّة وحدها (F-28). السباق الحقيقي — قراءتان متزامنتان
// للحظة الدمج فوق قاعدة بيانات حقيقية — في `BasketConcurrencyTests`؛ هنا يُثبَّت العقد الذي
// تعتمد عليه: كم محاولة، ومتى تُنسى السلال، وما الذي لا يُعاد إطلاقاً.
// ============================================================================
public class BasketWriterTests
{
    private readonly IBasketRepository _baskets = Substitute.For<IBasketRepository>();

    private BasketWriter Writer() => new(_baskets);

    [Fact]
    public async Task المحاولة_الناجحة_لا_تُعاد_ولا_تُنسى()
    {
        var attempts = 0;

        var result = await Writer().SaveAsync(() => { attempts++; return Task.FromResult("تمّ"); }, default);

        result.Should().Be("تمّ");
        attempts.Should().Be(1);
        _baskets.DidNotReceive().Reset();
    }

    [Fact]
    public async Task تعارض_الدمج_يُعاد_على_الحالة_الملتزمة_فيجد_سلّة_الزائر_محذوفة()
    {
        var attempts = 0;

        // الفائزة دمجت وحذفت؛ الخاسرة تُعيد فتجد الحالة النهائية ولا تجد ما تتنازع عليه.
        var result = await Writer().SaveAsync(() =>
        {
            if (++attempts < 2) throw new ConcurrencyConflictException();
            return Task.FromResult(attempts);
        }, default);

        result.Should().Be(2);
        // مرّة واحدة لمحاولة واحدة فاشلة: النسيان جزء من المحاولة التالية لا طقس دائم.
        _baskets.Received(1).Reset();
    }

    [Fact]
    public async Task سباق_إنشاء_سلّة_العميل_يُعاد_كذلك_لا_تعارض_الحذف_وحده()
    {
        var attempts = 0;

        // الفهرس الفريد على (TenantId, CustomerId) يقبل أول سلّة عميل ويرفض البقية. الإعادة
        // تجد السلّة التي أنشأتها الفائزة فتستعملها — وهذا السباق ظلّ مفتوحاً بعد إصلاح الحذف وحده.
        var result = await Writer().SaveAsync(() =>
        {
            if (++attempts < 2) throw new UniqueConstraintViolationException(new InvalidOperationException("فهرس فريد"));
            return Task.FromResult(attempts);
        }, default);

        result.Should().Be(2);
        _baskets.Received(1).Reset();
    }

    [Fact]
    public async Task تعارض_لا_ينتهي_يُرفع_بعد_الحدّ_لا_يدور_أبداً()
    {
        var attempts = 0;

        var act = () => Writer().SaveAsync<int>(() =>
        {
            attempts++;
            throw new ConcurrencyConflictException();
        }, default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        attempts.Should().Be(BasketWriter.MaxAttempts);
        _baskets.Received(BasketWriter.MaxAttempts).Reset();
    }

    [Fact]
    public async Task خطأ_ليس_تعارضاً_يُرفع_فوراً_بلا_إعادة_لكن_بعد_النسيان()
    {
        var attempts = 0;

        // نفاد المخزون ليس تعارضاً: إعادته تُنتج الرفض نفسه وتُخفي سببه خلف محاولات.
        var act = () => Writer().SaveAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("ليس تعارضاً");
        }, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
        // ومع ذلك تُنسى السلال: ما كتبته المحاولة الفاشلة لا يُحمل إلى ما بعدها.
        _baskets.Received(1).Reset();
    }

    [Fact]
    public async Task طلبٌ_أُلغي_لا_يبدأ_محاولة_جديدة()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var attempts = 0;

        var act = () => Writer().SaveAsync(() => { attempts++; return Task.FromResult(0); }, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(0);
    }
}
