using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// TD-60 — حدود المعدّل الأربعة، مُثبَّتة.
//
// **العلّة التي يعالجها هذا الملف ليست حدّاً خاطئاً بل حدّاً لا يقيسه شيء.** المجموعة كلّها ترفع
// كل حدّ إلى 100000 (`SouqApiFactory`) كي لا تُفلِت الاختبارات، فكان انحدارٌ يُلغي أيّ سياسة يمرّ
// أخضر. وليس افتراضياً: M15 وجدت ثغرة حقيقية هنا بالضبط — تبديل حالة أحرف المضيف كان يفتح دلواً
// جديداً ويُلغي الحدّ تماماً، بلا وكيل يُتجاوَز ولا ترويسة تُزوَّر.
//
// والأسلوب هو أسلوب `AuthSessionTests`: تبقى حدود المجموعة مرفوعة، ويُخفَض **سياسةٌ واحدة** في
// مصنع مشتق لكل اختبار. فما يُقاس هو السياسة المقصودة وحدها، ولا تتأثّر بقيّة المجموعة.
//
// **وحدٌّ واحد لا يُقاس هنا، ويُقال بدل أن يُدَّعى:** مفتاح الدلو `{المضيف}|{عنوان العميل}`،
// و`TestServer` لا يمنح اتصالاً عنواناً حقيقياً — فكل عملاء هذه المجموعة يتقاسمون شطر العنوان.
// أي أنّ التجزئة **بالمضيف** مُثبَتة أدناه، والتجزئة **بالعنوان** تبقى بلا اختبار هنا: إثباتها
// يحتاج حزمة حقيقية خلف الوكيل الموثوق، وهو ما تقيسه رحلة المتصفّح لا هذه المجموعة.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class RateLimitPolicyTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public RateLimitPolicyTests(SouqApiFactory factory)
    {
        _factory = factory; _api = new TestApi(factory);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestApi.Json))
        .TryGetProperty("code", out var code) ? code.GetString() : null;

    // كل سياسة وطريق يُفعّلها ومدخله. الرفض يجب أن يقع **قبل** أيّ منطق، فالمدخل غير صالح عمداً:
    // ما يُقاس هو الحدّ لا نجاح العملية.
    public static TheoryData<string, string> Policies => new()
    {
        { "Auth", "/api/auth/login" },
        { "Refresh", "/api/auth/refresh" },
        { "Basket", "/api/basket/items" },
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public async Task كل_سياسة_ترفض_بـ429_وRetry_After_ورمز_ثابت(string policy, string path)
    {
        const int permit = 3;
        await using var strict = _factory.WithWebHostBuilder(
            b => b.UseSetting($"RateLimiting:{policy}:PermitLimit", permit.ToString()));
        var client = strict.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < permit + 1; i++)
            last = await client.PostAsJsonAsync(path, new { productId = 1, quantity = 1, email = "x@souq.test", password = "Wrong-Pass-9" });

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"سياسة {policy} على {path} يجب أن ترفض بعد {permit} طلبات");
        last.Headers.RetryAfter.Should().NotBeNull("العميل يحتاج أن يعرف متى يعيد المحاولة");
        (await ProblemCodeAsync(last)).Should().Be("TooManyRequests", "العميل يتفرّع على الرمز لا على الرسالة");
    }

    // معاينة الكوبون على `GET`، فلها اختبارها: تخمين الرموز هو ما وُضعت السياسة لأجله.
    [Fact]
    public async Task معاينة_الكوبون_محدودة_المعدّل()
    {
        const int permit = 3;
        await using var strict = _factory.WithWebHostBuilder(
            b => b.UseSetting("RateLimiting:CouponPreview:PermitLimit", permit.ToString()));
        var client = strict.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < permit + 1; i++)
            last = await client.GetAsync($"/api/basket/quote?couponCode=GUESS{i}");

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "تخمين رموز الكوبونات هو سبب هذه السياسة");
        (await ProblemCodeAsync(last)).Should().Be("TooManyRequests");
    }

    // ========================================================================
    // **التجزئة بالمضيف هي كل المبرّر**: متجرٌ مزدحم لا يستنفد حدّ متجر آخر.
    //
    // اختبار M15 يثبت العكس المتمّم — أن **صيغ كتابة** مضيفٍ واحد لا تفتح دلاءً جديدة. وهذا يثبت
    // أنّ مضيفَين **مختلفَين فعلاً** لا يتقاسمان دلواً: بلا هذا الاتجاه، حدٌّ يُجمَّع عالمياً
    // يمرّ كلا الاختبارين ويحوّل كل متجر إلى رهينة ازدحام جاره.
    // ========================================================================
    [Fact]
    public async Task متجرٌ_يستنفد_حدّه_لا_يستنفد_حدّ_متجر_آخر()
    {
        var first = await _factory.CreateStoreAsync();
        var second = await _factory.CreateStoreAsync();

        const int permit = 3;
        await using var strict = _factory.WithWebHostBuilder(
            b => b.UseSetting("RateLimiting:Auth:PermitLimit", permit.ToString()));

        async Task<HttpStatusCode> LoginOn(string host)
        {
            var client = strict.CreateClient();
            client.DefaultRequestHeaders.Host = host;
            return (await client.PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@souq.test", password = "Wrong-Pass-9" })).StatusCode;
        }

        HttpStatusCode last = default;
        for (var i = 0; i < permit + 1; i++) last = await LoginOn(first.Host);
        last.Should().Be(HttpStatusCode.TooManyRequests, "الحدّ لا يعمل أصلاً — ما بعده بلا معنى");

        (await LoginOn(second.Host)).Should().NotBe(HttpStatusCode.TooManyRequests,
            "متجر آخر، دلوٌ آخر — وإلّا صار كل متجر رهينة ازدحام جاره");
    }

    // ========================================================================
    // تصدير البيانات محدود **التكرار** لا المحتوى (F-21).
    //
    // الاستجابة تكبر بعمر الحساب — كل طلب بكل أسطره وكل تقييم — ويستدعيها أيّ عميل مسجَّل. وقصُّها
    // مرفوض: تصديرٌ مبتور ليس تصديراً، والنقطة موجودة لتلبية حقّ صاحب البيانات. فالمقيَّد هو عدد
    // المرّات. والصلاحية لا تُغني: هي تقول **من** يصدّر، لا **كم مرّة**.
    // ========================================================================
    [Fact]
    public async Task تصدير_بيانات_العميل_محدود_المعدّل()
    {
        const int permit = 2;
        await using var strict = _factory.WithWebHostBuilder(
            b => b.UseSetting("RateLimiting:Export:PermitLimit", permit.ToString()));

        var api = new TestApi((SouqApiFactory)_factory);
        var (customer, _) = await api.NewCustomerAsync();
        var token = customer.DefaultRequestHeaders.Authorization!.Parameter!;

        HttpResponseMessage? last = null;
        for (var i = 0; i < permit + 1; i++)
        {
            var client = strict.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            last = await client.GetAsync("/api/account/export");
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "التصدير نقطة ثقيلة: تُقيَّد بالتكرار لأن قصّ محتواها ليس خياراً");
        (await ProblemCodeAsync(last)).Should().Be("TooManyRequests");
    }

    // ========================================================================
    // الحدّ يحرس نقطةً **قبل** المصادقة، فلا يُلغيه توكن صالح ولا وجودُ حساب: حشو بيانات الاعتماد
    // كلّه طلباتٌ فاشلة، ولو عُدّ الفاشل خارج الحدّ لما حرس شيئاً.
    // ========================================================================
    [Fact]
    public async Task الطلبات_الفاشلة_تُعَدّ_في_الحدّ()
    {
        const int permit = 3;
        await using var strict = _factory.WithWebHostBuilder(
            b => b.UseSetting("RateLimiting:Auth:PermitLimit", permit.ToString()));
        var client = strict.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < permit + 1; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login",
                new { email = $"missing-{i}@souq.test", password = "Wrong-Pass-9" })).StatusCode);

        statuses.Take(permit).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized,
            "الأولى تصل المنطق وتفشل بالمصادقة");
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests, "والفاشلة محسوبة — وإلّا فالحدّ لا يحرس الحشو");
    }

    // ========================================================================
    // **قسمةُ الحدّ على عدد النسخ** (C4، ADR-0057): نوافذُ الحدّ في الذاكرة، فالحدُّ الفعليّ عبر
    // موازِن حِمل هو الحدُّ × عددُ النسخ. وهذا تقريبٌ لا ضبط — إشارةُ الإبطال لا تُصلحه، لأنّ
    // الحدَّ عدّادٌ يجب أن يُتشارَك لا لقطةٌ تُبطَل.
    //
    // والقيمةُ الافتراضية 1، أي **لا تغيير** لمن يشغّل نسخةً واحدة — وهو حالُ كلّ نشرٍ اليوم.
    // ========================================================================
    [Theory]
    [InlineData(10, 1, 10)]      // نسخةٌ واحدة: الحدُّ كما هو، بلا قسمة
    [InlineData(10, 0, 10)]      // إعدادٌ مشوَّه لا يُغلق النقطة
    [InlineData(10, 2, 5)]
    [InlineData(10, 3, 3)]       // قسمةٌ صحيحة تنزل إلى الأدنى
    [InlineData(2, 5, 1)]        // **ولا تصل صفراً أبداً**: صفرٌ كان سيُغلق النقطة تماماً
    public void الحدّ_لكل_نسخة_يُقسَم_ولا_يبلغ_صفراً(int permitLimit, int instances, int expected) =>
        Souq.API.Security.RateLimitingSetup.PerInstance(permitLimit, instances).Should().Be(expected);
}
