using System.Net.Http.Json;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// ثلاث نقاط تتعرّف على صاحبها من ملفّ تعريف ارتباط وحده: تجديد الجلسة، الخروج، وسلّة الزائر.
// لا رمز مضاد للتزوير (antiforgery) في هذا النظام — ولا يحتاجه — لأن الدفاع كلّه قائم على
// خصائص هذه الملفّات: SameSite=Strict يمنع المتصفّح من إرسالها في طلب من موقع آخر أصلاً،
// وHttpOnly يمنع سكربتاً محقوناً من قراءتها، والمسار يحصر كلّاً منها في نقاطها.
//
// وهذه الخصائص لم يكن يفحصها شيء. تغييرها إلى Lax في إعادة هيكلة يمرّ بكل الاختبارات الخضراء
// ويفتح تزوير الطلبات عبر المواقع بصمت. هذا الملف يجعل ذلك التغيير مرئياً.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CookieSecurityTests
{
    private readonly TestApi _api;

    private readonly SouqApiFactory _factory;

    public CookieSecurityTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task ملفّ_رمز_التجديد_محصّن_ومحصور_بمساره()
    {
        var (_, email) = await _api.NewCustomerAsync();
        var client = _api.SecureClient(handleCookies: false);

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Customer-Pass-1" });
        var cookie = SetCookie(response, "__Host-souq_refresh");

        cookie.Should().NotBeNull("الدخول يصدر رمز تجديد في ملفّ تعريف ارتباط");
        cookie!.Should().Contain("httponly", "سكربت محقون يجب ألّا يقرأ رمز التجديد");
        cookie.Should().Contain("samesite=strict", "يمنع المتصفّح من إرساله مع طلبٍ من موقعٍ آخر");
        cookie.Should().Contain("secure", "الرمز لا يسافر على اتصال غير مشفّر");

        // ========================================================================
        // البادئة `__Host-` هي الحرس الذي لم يكن موجوداً (M15).
        //
        // الخصائص الثلاث أعلاه تمنع **قراءته** و**إرساله من موقعٍ آخر**، ولا تمنع مضيفاً شقيقاً من
        // **كتابة** ملفٍّ يُرسَل معنا: مدار `SameSite` هو النطاق المُسجَّل، وملفّات تعريف الارتباط لا
        // تخضع لسياسة الأصل الواحد. وسوق منصّةٌ بعلامة بيضاء والتجّار يأتون بنطاقاتهم، فما يعيش تحت
        // نطاق التاجر غير متجره ليس تحت سيطرة المنصّة — ومن ملك مضيفاً هناك زرع جلسته في متصفّح
        // الضحيّة، فتسوّق الضحيّة وأتمّ شراءه داخل حساب المهاجم.
        //
        // والمتصفّح يرفض أي ملفٍّ بهذه البادئة يحمل `Domain`، فيصير مقصوراً على مضيفٍ واحد بالقوّة.
        // وثمنُها `Path=/` (شرطُها) بدل المسار الضيّق — تراجعٌ صغير مقابل إغلاق استيلاءٍ على الحساب.
        // ========================================================================
        cookie.Should().Contain("path=/", "شرط البادئة — وهو ثمنها المدفوع بعلم");
        cookie.Should().NotContain("domain=", "البادئة تمنع النطاق، وهي بذلك تمنع زرع الجلسة من مضيفٍ شقيق");
    }

    // وبلا تشفير تُعلَّق البادئة ويعود المسار الضيّق: المتصفّح يرفض `__Host-` بلا `Secure` رفضاً تامّاً،
    // ففرضُها دائماً كان سيكسر الدخول في التطوير وفي حزمة الحاويات المحلّية كسراً صامتاً.
    [Fact]
    public async Task بلا_تشفير_يعود_الاسم_العاري_والمسار_الضيّق()
    {
        await using var insecure = _factory.WithWebHostBuilder(
            b => b.UseSetting("Auth:RefreshCookie:Secure", "false"));
        var (_, email) = await _api.NewCustomerAsync();

        var response = await insecure.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email, password = TestApi.CustomerPassword });
        var cookie = SetCookie(response, "souq_refresh");

        cookie.Should().NotBeNull("الاسم العاري حين لا تشفير — وإلّا رفضه المتصفّح ولم تُحفظ جلسة");
        cookie!.Should().NotContain("__Host-");
        cookie.Should().Contain("path=/api/auth", "المسار الضيّق يبقى حيث لا بادئة تفرض غيره");
    }

    [Fact]
    public async Task ملفّ_سلّة_الزائر_محصّن_كذلك()
    {
        var client = _api.SecureClient(handleCookies: false);
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 5);

        var response = await client.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 });
        var cookie = SetCookie(response, TestApi.GuestBasketCookie);

        // كان هنا `if (cookie is null) return;` — أي اختبارٌ يمرّ بلا أن يفحص شيئاً إن لم يُصدَر الملفّ.
        // وإعادة تسمية الملفّ في M15 كانت ستجعله يمرّ فارغاً إلى الأبد بلا أن يقول أحد. والسلوك الحقيقي
        // مؤكَّد في BasketTests: هذه النقطة **تُصدر** الملفّ، فيُطالَب به صراحةً.
        cookie.Should().NotBeNull("إضافة عنصر لزائر تُصدر ملفّ سلّته");
        cookie!.Should().Contain("httponly");
        cookie.Should().Contain("samesite=strict");
        cookie.Should().NotContain("domain=", "بادئة __Host- تمنع زرع سلّة من مضيفٍ شقيق");
    }

    [Fact]
    public async Task الخروج_يمسح_الملفّ_بالخصائص_نفسها()
    {
        // ملفّ يُمسح بخصائص مختلفة لا يُمسح فعلاً عند بعض المتصفّحات، فتبقى الجلسة قائمة.
        var (_, email) = await _api.NewCustomerAsync();
        var client = _api.SecureClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Customer-Pass-1" }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/auth/logout", null);
        var cookie = SetCookie(response, "__Host-souq_refresh");

        // بنفس الاسم وبنفس المسار الذي كُتب به (M15: الاسم بالبادئة والمسار `/`) — ملفٌّ يُمسح بخصائص
        // مختلفة لا يُمسح فعلاً عند بعض المتصفّحات، فتبقى الجلسة قائمة.
        cookie.Should().NotBeNull();
        cookie!.Should().Contain("path=/").And.Contain("samesite=strict");
    }

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant()
            : null;
}
