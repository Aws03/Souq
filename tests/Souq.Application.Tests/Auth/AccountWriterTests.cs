using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Auth;
using Souq.Domain.Interfaces;

namespace Souq.Application.Tests.Auth;

// ============================================================================
// آلية إعادة المحاولة وحدها (F-25). الطلبات المتزامنة الحقيقية مُختبَرة فوق قاعدة بيانات
// حقيقية في `AuthConcurrencyTests` — هنا نثبّت العقد الذي تعتمد عليه: كم محاولة، ومتى
// يُنسى ما كتبته محاولةٌ فشلت، وما الذي لا يُعاد إطلاقاً.
// ============================================================================
public class AccountWriterTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private AccountWriter Writer() => new(_users);

    [Fact]
    public async Task المحاولة_الناجحة_لا_تُعاد_ولا_تُنسى()
    {
        var attempts = 0;

        var result = await Writer().SaveAsync(() => { attempts++; return Task.FromResult("تمّ"); }, default);

        result.Should().Be("تمّ");
        attempts.Should().Be(1);
        _users.DidNotReceive().Reset();
    }

    [Fact]
    public async Task تعارض_عابر_يُعاد_على_الحالة_الملتزمة_ويعيد_نتيجة_المحاولة_الناجحة()
    {
        var attempts = 0;

        var result = await Writer().SaveAsync(() =>
        {
            // يفشل مرّتين ثم ينجح — كما يحدث حين يسبق طلبان متزامنان هذا الطلب إلى الصفّ نفسه.
            if (++attempts < 3) throw new ConcurrencyConflictException();
            return Task.FromResult(attempts);
        }, default);

        result.Should().Be(3);
        // مرّة لكل محاولة فاشلة، لا أكثر: النسيان جزء من المحاولة التالية، لا طقس يُؤدّى دائماً.
        _users.Received(2).Reset();
    }

    [Fact]
    public async Task تعارض_لا_ينتهي_يُرفع_بعد_الحدّ_لا_يدور_أبداً()
    {
        var attempts = 0;

        var act = () => Writer().SaveAsync<string>(() =>
        {
            attempts++;
            throw new ConcurrencyConflictException();
        }, default);

        // العميل يرى 409 في النهاية — وهو صادق حينها: الصفّ متنازع عليه فعلاً ولا يهدأ.
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        attempts.Should().Be(AccountWriter.MaxAttempts);
        _users.Received(AccountWriter.MaxAttempts).Reset();
    }

    [Fact]
    public async Task خطأ_ليس_تعارضاً_يُرفع_فوراً_بلا_إعادة_لكن_بعد_النسيان()
    {
        var attempts = 0;

        var act = () => Writer().SaveAsync<string>(() =>
        {
            attempts++;
            throw new InvalidOperationException("عطل حقيقي");
        }, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        // لا يُعاد: إعادة عمليةٍ فشلت لسبب غير التزامن تكرّر العطل أو — أسوأ — تكرّر أثره.
        attempts.Should().Be(1);
        // ويُنسى مع ذلك: كتابات نصف مكتملة في المتعقّب تتسرّب إلى حفظ الطلب التالي في النطاق نفسه.
        _users.Received(1).Reset();
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

    [Fact]
    public async Task الإلغاء_أثناء_التعارض_يوقف_الإعادة_بدل_أن_يستنفد_المحاولات()
    {
        using var cancelled = new CancellationTokenSource();
        var attempts = 0;

        var act = () => Writer().SaveAsync<int>(async () =>
        {
            // المستخدم أغلق الصفحة بين محاولتين: لا معنى للتسابق على صفٍّ لأجل ردٍّ لن يصل أحداً.
            attempts++;
            await cancelled.CancelAsync();
            throw new ConcurrencyConflictException();
        }, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }
}
