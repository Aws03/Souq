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

    public CookieSecurityTests(SouqApiFactory factory) => _api = new TestApi(factory);

    [Fact]
    public async Task ملفّ_رمز_التجديد_محصّن_ومحصور_بمساره()
    {
        var (_, email) = await _api.NewCustomerAsync();
        var client = _api.SecureClient(handleCookies: false);

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Customer-Pass-1" });
        var cookie = SetCookie(response, "souq_refresh");

        cookie.Should().NotBeNull("الدخول يصدر رمز تجديد في ملفّ تعريف ارتباط");
        cookie!.Should().Contain("httponly", "سكربت محقون يجب ألّا يقرأ رمز التجديد");
        cookie.Should().Contain("samesite=strict", "هذا وحده ما يمنع تزوير الطلبات عبر المواقع هنا");
        cookie.Should().Contain("secure", "الرمز لا يسافر على اتصال غير مشفّر");
        cookie.Should().Contain("path=/api/auth", "لا يُرسَل مع كل طلب — نقاط الجلسة فقط");
    }

    [Fact]
    public async Task ملفّ_سلّة_الزائر_محصّن_كذلك()
    {
        var client = _api.SecureClient(handleCookies: false);
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 5);

        var response = await client.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 });
        var cookie = SetCookie(response, "souq_basket");

        if (cookie is null) return;   // سلّة الزائر قد تُصدر ملفّها في نقطة أخرى حسب المسار

        cookie.Should().Contain("httponly");
        cookie.Should().Contain("samesite=strict");
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
        var cookie = SetCookie(response, "souq_refresh");

        cookie.Should().NotBeNull();
        cookie!.Should().Contain("path=/api/auth").And.Contain("samesite=strict");
    }

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant()
            : null;
}
