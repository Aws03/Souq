using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Identity;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-25 — تعارض تزامن في المصادقة.
//
// كل دخول ناجح يكتب في صفّ `User` نفسه (يصفّر عدّاد الإخفاقات، يرفع القفل، يسجّل وقت آخر
// دخول)، والصفّ يحمل rowversion. فطلبان متزامنان لنفس الحساب — متصفّحان، تبويبان، هاتف
// وحاسوب — يتسابقان على نسخة الصفّ، ويخسر أحدهما بـ `ConcurrencyConflictException` ⇒ 409.
// المقيس قبل الإصلاح: ثمانية دخولات متزامنة ⇒ 200 مرّتين و409 ستّاً، بينما المتتابعة كلها 200.
//
// و409 هنا كذبة على المستخدم: لم يتغيّر شيء يخصّه، ولا شيء ليراجعه أو "يحدّث الصفحة" لأجله.
// كلمة مروره صحيحة وحسابه سليم، والمانع محاسبةٌ داخلية لا يعرف بوجودها.
//
// الحارس نفسه لا يُمسّ: يبقى rowversion على الصفّ، ويبقى 409 صادقاً حين يكون الصفّ متنازعاً
// عليه فعلاً ولا يهدأ. ما تغيّر أن المحاولة تُعاد على الحالة الملتزمة (AccountWriter) —
// **بالقرار كلّه** لا بالحفظ وحده: قراءة جديدة، فحص قفل جديد، وتحقّق جديد من كلمة المرور.
//
// هذه الاختبارات تُشغّل الطلبات متزامنةً فعلاً فوق قاعدة بيانات حقيقية، ولا تقبل 409 مخبَّأً
// خلف "أحد الردود نجح": كل ردّ يُفحص.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthConcurrencyTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthConcurrencyTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    // ثمانية: العدد الذي أظهر العطل عند قياسه أوّل مرّة (2×200 و6×409).
    private const int Concurrent = 8;

    [Fact]
    public async Task دخولات_متزامنة_للحساب_نفسه_كلها_تنجح_بلا_تعارض()
    {
        // ثلاث جولات: السباق توقيتي، وجولةٌ نظيفة واحدة لا تُثبت شيئاً.
        for (var round = 1; round <= 3; round++)
        {
            var (_, email) = await _api.NewCustomerAsync();
            var anonymous = _api.Anonymous();

            // تُطلَق كلها قبل انتظار أيّ منها — وإلّا صارت متتابعة والاختبار بلا معنى.
            var responses = await Task.WhenAll(Enumerable.Range(0, Concurrent).Select(_ =>
                anonymous.PostAsJsonAsync("/api/auth/login",
                    new { email, password = TestApi.CustomerPassword })).ToArray());

            var statuses = string.Join(", ", responses.Select(r => (int)r.StatusCode));
            responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.Conflict,
                $"الجولة {round}: لا يجوز أن يُرفض دخولٌ صحيح بتعارض تزامن (F-25) — الردود: {statuses}");
            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
                $"الجولة {round}: كل دخول ببيانات صحيحة ينجح مهما تزامن — الردود: {statuses}");

            // ونجاحٌ حقيقي لا ردٌّ فارغ: لكل طلب جلسته الخاصة.
            var sessions = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<AuthBody>(TestApi.Json)));
            sessions.Should().OnlyContain(s => s != null && s.AccessToken != "");

            // والمحاسبة التي تسبّبت بالسباق كُتبت فعلاً: الإعادة تحفظ، لا تتخطّى.
            var user = await UserAsync(email);
            user.LastLoginAt.Should().NotBeNull($"الجولة {round}: الدخول يسجّل وقته");
            user.FailedLoginCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task تغييرات_متزامنة_لكلمة_المرور_واحدٌ_ينجح_والبقية_تُرفض_بصدق_لا_بتعارض()
    {
        var (client, email) = await _api.NewCustomerAsync();
        var userId = (await UserAsync(email)).Id;

        // كلمة جديدة مختلفة لكل طلب: لو مرّ اثنان لصار ترتيب الكتابة مرئياً في التجزئة النهائية.
        var candidates = Enumerable.Range(0, Concurrent).Select(i => $"Changed-Pass-{i}-2026!").ToArray();
        var responses = await Task.WhenAll(candidates.Select(newPassword =>
            client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = TestApi.CustomerPassword, newPassword })).ToArray());

        var statuses = string.Join(", ", responses.Select(r => (int)r.StatusCode));
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.Conflict,
            $"الخاسر في السباق يُرفض لسببه الحقيقي لا بتعارض تزامن (F-25) — الردود: {statuses}");

        // واحد فقط ينجح: كلمة المرور "الحالية" لم تعد حالية لمن وصل بعده — وهذا رفضٌ صادق.
        var winners = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        winners.Should().HaveCount(1, $"تغييرٌ واحد فقط يمكن أن يصحّ — الردود: {statuses}");
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(
            r => r.StatusCode == HttpStatusCode.BadRequest || r.StatusCode == HttpStatusCode.Unauthorized,
            $"الرفض يكون إدخالاً خاطئاً أو جلسةً انتهت، لا خطأ خادم — الردود: {statuses}");

        // والحالة النهائية حالةُ فائزٍ واحد بعينه: كلمته تعمل، وكلمات الخاسرين لا.
        var winnerIndex = Array.IndexOf(responses, winners[0]);
        var login = await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email, password = candidates[winnerIndex] });
        login.StatusCode.Should().Be(HttpStatusCode.OK, "كلمة مرور الطلب الذي ردّ 200 هي المحفوظة");

        foreach (var (loser, index) in candidates.Select((c, i) => (c, i)).Where(x => x.i != winnerIndex))
            (await _api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = loser }))
                .StatusCode.Should().NotBe(HttpStatusCode.OK,
                    $"كلمة الطلب {index} رُفض تغييره فلا يجوز أن تفتح الحساب");

        // وإشعار واحد لا ثمانية: محاولةٌ أُلغيت يُلغى ما أضافته للصندوق (UserRepository.Reset).
        // لولا ذلك لوصل صاحب الحساب بريدٌ عن كل محاولة خاسرة — إنذارُ اختراقٍ لم يقع.
        var notifications = await _api.WithDbAsync(db => db.OutboxMessages.CountAsync(m =>
            m.Type == nameof(Souq.Application.Common.Notifications.PasswordChanged) &&
            m.Payload.Contains($"\"userId\":{userId}")));
        notifications.Should().Be(1, "تغييرٌ واحد وقع ⇒ إشعارٌ واحد");
    }

    [Fact]
    public async Task دخول_يزامن_تغيير_كلمة_المرور_لا_يُصدر_جلسة_على_تجزئة_قديمة()
    {
        var (client, email) = await _api.NewCustomerAsync();
        const string newPassword = "Rotated-Pass-2026!";
        var anonymous = _api.Anonymous();

        // دخولات بالكلمة القديمة تتسابق مع تغييرها. الصحيح أن يُقبل الدخول قبل الالتزام ويُرفض
        // بعده — لا أن يُقبل *لأن* المحاولة أعادت الحفظ على قراءةٍ سبقت التغيير.
        var tasks = new List<Task<HttpResponseMessage>>
        {
            client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = TestApi.CustomerPassword, newPassword }),
        };
        tasks.AddRange(Enumerable.Range(0, Concurrent).Select(_ =>
            anonymous.PostAsJsonAsync("/api/auth/login",
                new { email, password = TestApi.CustomerPassword })));

        var responses = await Task.WhenAll(tasks);
        var statuses = string.Join(", ", responses.Select(r => (int)r.StatusCode));
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.Conflict,
            $"خلط الدخول بتغيير كلمة المرور لا ينتج تعارضاً (F-25) — الردود: {statuses}");
        responses.Should().NotContain(r => (int)r.StatusCode >= 500,
            $"ولا خطأ خادم — الردود: {statuses}");

        responses[0].StatusCode.Should().Be(HttpStatusCode.OK, "التغيير وحده في السباق فينجح");

        // وبعد استقرار كل شيء: القديمة لا تفتح الحساب، والجديدة تفتحه.
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email, password = TestApi.CustomerPassword })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "الكلمة القديمة ماتت بالتغيير");
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
            new { email, password = newPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<User> UserAsync(string email)
    {
        var normalized = User.NormalizeEmail(email);
        return await _api.WithDbAsync(db =>
            db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == normalized));
    }

    private sealed record AuthBody(string AccessToken);
}
