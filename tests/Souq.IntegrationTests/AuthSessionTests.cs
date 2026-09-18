using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Identity;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// الجلسات عبر HTTP الحقيقي (ADR-0010): رمز التجديد في ملف تعريف ارتباط محصّن، التدوير وكشف إعادة
// الاستخدام، الخروج، تغيير كلمة المرور يُسقط الأجهزة الأخرى فوراً، القفل، حسابات المنصّة على مضيفها
// وحده، تأكيد البريد، روابط البريد على مضيف المتجر، وحدّ المعدّل.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class AuthSessionTests
{
    private const string Password = "Customer-Pass-1";
    // الاسم الفعلي بالبادئة (M15): الاختبارات تعمل بـ Secure مُفعَّل، فالخادم يكتب `__Host-souq_refresh`.
    // يُبنى من نفس الثابت الذي يبنيه الخادم منه، فلا ينفصل الاختبار عن الكود إن تغيّرت البادئة.
    private static readonly string RefreshCookie =
        Souq.API.Security.HostOnlyCookie.NameFor(Souq.API.Controllers.AuthController.RefreshCookieBareName, secure: true);

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public AuthSessionTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الدخول_يضع_رمز_التجديد_في_ملف_تعريف_ارتباط_محصّن_لا_في_الجسم()
    {
        var (_, email) = await _api.NewCustomerAsync();

        var response = await _api.SecureClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = SetCookie(response);
        // `path=/` منذ M15: شرط بادئة `__Host-`، التي تمنع مضيفاً شقيقاً تحت نطاق التاجر من زرع جلسة.
        // التفصيل وسببه في CookieSecurityTests و`HostOnlyCookie`.
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure")
            .And.Contain("samesite=strict").And.Contain("path=/");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("accessToken").And.NotContain(RefreshValue(cookie));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            (await response.Content.ReadFromJsonAsync<TestApi.AuthBody>(TestApi.Json))!.AccessToken);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(2));
        jwt.Claims.Select(c => c.Type).Should().Contain(["tid", "cid", "sstamp"]);
    }

    [Fact]
    public async Task التجديد_يدوّر_الرمز_وإعادة_رمز_قديم_تُسقط_الجلسة_كلها()
    {
        var (_, email) = await _api.NewCustomerAsync();
        var browser = _api.SecureClient();
        var login = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var firstRefresh = RefreshValue(SetCookie(login));
        var firstAccess = (await login.Content.ReadFromJsonAsync<TestApi.AuthBody>(TestApi.Json))!.AccessToken;

        var rotated = await browser.PostAsync("/api/auth/refresh", null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshValue(SetCookie(rotated)).Should().NotBe(firstRefresh);

        // تجاوز مهلة السباق بلا انتظار: الرمز الأول "استُهلك قبل دقيقة".
        var firstHash = User.HashToken(firstRefresh);
        await _api.WithDbAsync(db => db.RefreshTokens.Where(t => t.TokenHash == firstHash)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, t => t.UsedAt!.Value.AddMinutes(-1))));

        var replay = await RefreshWith(firstRefresh);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCode(replay)).Should().Be("RefreshTokenReused");

        // العائلة كلها سقطت: الرمز الأحدث لدى المالك، وتوكن الوصول القديم (الختم تدوّر).
        (await browser.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Authorized(firstAccess).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task تسجيل_الخروج_يُبطل_الجلسة_ويمسح_ملف_تعريف_الارتباط()
    {
        var (_, email) = await _api.NewCustomerAsync();
        var browser = _api.SecureClient();
        var refresh = RefreshValue(SetCookie(await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password })));

        var logout = await browser.PostAsync("/api/auth/logout", null);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookie(logout).Should().StartWith($"{RefreshCookie}=;");
        (await RefreshWith(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task تغيير_كلمة_المرور_يُسقط_توكنات_الأجهزة_الأخرى_فوراً()
    {
        var (deviceA, email) = await _api.NewCustomerAsync();
        var deviceB = await _api.LoginAsync(email, Password);
        (await deviceB.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);   // الختم في الذاكرة الآن

        var change = await deviceA.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Password, newPassword = "Changed-Pass-2" });
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        var fresh = (await change.Content.ReadFromJsonAsync<TestApi.AuthBody>(TestApi.Json))!.AccessToken;

        (await deviceB.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await deviceA.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Authorized(fresh).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.TokenAsync(email, "Changed-Pass-2")).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task تغيير_كلمة_المرور_يقتل_رمز_تجديد_جهاز_آخر_ولو_كان_مستهلَكاً_داخل_مهلة_السباق()
    {
        // ============================================================================
        // الهجوم الذي يجعل تغيير كلمة المرور بلا معنى، وهو ما لم يكن يغطّيه اختبار (M9):
        // الاختبار أعلاه يتحقّق من **توكنات الوصول** وحدها (/auth/me)، ولا يقدّم رمز تجديد جهازٍ آخر
        // إلى /auth/refresh إطلاقاً. والفرق ليس شكلياً:
        //
        //   `RevokeAllAsync` يمرّ على `ListActiveForUserAsync`، ومرشّحها `RevokedAt == null &&
        //   UsedAt == null` — فالرمز الذي **استُهلك للتوّ** بالتدوير لا يُبطَل. و`IsWithinReuseGrace`
        //   لا يشترط إلا `RevokedAt is null`، ويُفحص **قبل** فرع كشف إعادة الاستخدام. فمن يحمل ملفّ
        //   تعريف ارتباط جهازٍ دوّره قبل ثوانٍ يستطيع، خلال عشر ثوان من تغيير كلمة المرور، أن يصنع
        //   جلسة جديدة صالحة تماماً — بالختم الجديد.
        //
        // وعشر ثوان ليست نافذة ضيّقة لمن يعرفها: مهاجم يدوّر رمزه كل خمس ثوان يبقى داخلاً بعد أيّ
        // تغيير لكلمة المرور. والمهلة نفسها وُضعت لسباق تبويبات بريء، ولا يصحّ أن تُعمّر إبطالاً صريحاً.
        // ============================================================================
        var (deviceA, email) = await _api.NewCustomerAsync();

        var deviceB = _api.SecureClient();
        var loginB = await deviceB.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        var usedByB = RefreshValue(SetCookie(loginB));
        var rotatedB = await deviceB.PostAsync("/api/auth/refresh", null);
        rotatedB.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeForB = RefreshValue(SetCookie(rotatedB));
        activeForB.Should().NotBe(usedByB, "التدوير يستبدل الرمز — وإلا لم يكن هذا هو السباق المقصود");

        // المالك يغيّر كلمة مروره من جهازه. بلا انتظار: الرمز المستهلك ما زال داخل مهلة السباق.
        var change = await deviceA.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Password, newPassword = "Changed-Pass-3" });
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());

        // الرمز غير المستهلك يسقط (كان يسقط أصلاً) — والمستهلك يجب أن يسقط معه.
        (await RefreshWith(activeForB)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "الرمز الفعّال للجهاز الآخر أُبطل بتغيير كلمة المرور");

        var graceReplay = await RefreshWith(usedByB);
        graceReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "رمز مستهلَك داخل مهلة السباق لا يُحيي جلسةً أُبطلت صراحةً");

        // ولا تُعَدّ سرقةً فتُدوّر الختم: المالك غيّر كلمة مروره قبل ثانية، وجلسته الجديدة يجب أن تبقى.
        (await ProblemCode(graceReplay)).Should().NotBe("RefreshTokenReused",
            "تبويب بريء على جهاز خرج لا يجوز أن يُطرد به صاحبُ التغيير نفسه من جلسته الجديدة");
        var fresh = (await change.Content.ReadFromJsonAsync<TestApi.AuthBody>(TestApi.Json))!.AccessToken;
        (await _api.Authorized(fresh).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "جلسة الجهاز الذي غيّر كلمة المرور تبقى");
    }

    [Fact]
    public async Task خمس_محاولات_فاشلة_تقفل_الحساب_حتى_بالكلمة_الصحيحة()
    {
        var (_, email) = await _api.NewCustomerAsync();

        for (var i = 0; i < User.MaxFailedLogins; i++)
            (await ProblemCode(await _api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Pass-9" })))
                .Should().Be("InvalidCredentials");

        var locked = await _api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCode(locked)).Should().Be("AccountLocked");
    }

    [Fact]
    public async Task مالك_المنصّة_يدخل_على_مضيف_المنصّة_وحده_وتوكنه_بلا_متجر()
    {
        var login = await _api.Client(SouqApiFactory.PlatformHost).PostAsJsonAsync("/api/auth/login",
            new { email = SouqApiFactory.PlatformOwnerEmail, password = SouqApiFactory.PlatformOwnerPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var auth = (await login.Content.ReadFromJsonAsync<TestApi.AuthBody>(TestApi.Json))!;

        auth.User.Area.Should().Be("Platform");
        auth.User.Permissions.Should().Contain("platform.tenants.manage").And.NotContain("catalog.manage");
        new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).Claims.Should().NotContain(c => c.Type == "tid");
        (await _api.Authorized(auth.AccessToken, SouqApiFactory.PlatformHost).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // من منظور مضيف متجر حساب المنصّة غير موجود، وتوكنه لا يصلح عليه — والعكس لتوكن مدير المتجر.
        (await ProblemCode(await _api.Anonymous().PostAsJsonAsync("/api/auth/login",
                new { email = SouqApiFactory.PlatformOwnerEmail, password = SouqApiFactory.PlatformOwnerPassword })))
            .Should().Be("InvalidCredentials");
        (await _api.Authorized(auth.AccessToken).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Authorized(await _api.AdminTokenAsync(), SouqApiFactory.PlatformHost).GetAsync("/api/auth/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task التسجيل_الذاتي_ممنوع_على_مضيف_المنصّة()
    {
        var response = await _api.Client(SouqApiFactory.PlatformHost).PostAsJsonAsync("/api/auth/register",
            new { fullName = "دخيل", email = $"intruder-{Guid.NewGuid():N}@souq.test", password = "Intruder-Pass-1" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCode(response)).Should().Be("RegistrationNotAllowed");
    }

    [Fact]
    public async Task تأكيد_البريد_برابط_الرسالة_مرّة_واحدة()
    {
        var (customer, email) = await _api.NewCustomerAsync();
        (await customer.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.EmailConfirmed.Should().BeFalse();

        await _factory.DispatchNotificationsAsync();   // المرحلة 14: الرسالة من صندوق الصادر
        var token = _factory.Emails.LastVerificationTokenFor(email);
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await customer.GetFromJsonAsync<TestApi.UserBody>("/api/auth/me", TestApi.Json))!.EmailConfirmed.Should().BeTrue();
        (await ProblemCode(await _api.Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token })))
            .Should().Be("InvalidVerificationToken");
    }

    [Fact]
    public async Task روابط_البريد_على_مضيف_المتجر_الذي_جاء_منه_الطلب()
    {
        var store = await _factory.CreateStoreAsync();
        var storeApi = _api.ForStore(store);
        var (_, email) = await storeApi.NewCustomerAsync();

        (await storeApi.Anonymous().PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        await _factory.DispatchNotificationsAsync();

        new Uri(_factory.Emails.LastResetLinkFor(email)).Host.Should().Be(store.Host);
    }

    [Fact]
    public async Task حدّ_المعدّل_يرفض_بـ429_مع_Retry_After()
    {
        await using var strict = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Auth:PermitLimit", "3"));
        var client = strict.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
            last = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@souq.test", password = "Wrong-Pass-9" });

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Headers.RetryAfter.Should().NotBeNull();
        (await ProblemCode(last)).Should().Be("TooManyRequests");
    }

    // ========================================================================
    // حدّ المعدّل يُقاس بالمتجر، لا بصيغة كتابة مضيفه (M15).
    //
    // كان `Request.Host.Host` يدخل مفتاح الدلو كما وصل، بينما يُطبّعه تحديد المتجر — فـ"shop.test"
    // و"Shop.test" و"shop.test." متجرٌ واحد وثلاثة دلاء. وتبديل حالة الأحرف لا يحتاج شيئاً: لا وكيلاً
    // يُتجاوز، ولا ترويسةً تُزوَّر، ولا عنواناً يُبدَّل. أُثبت على حزمة حاويات تعمل قبل الإصلاح: عشرة
    // طلبات بمضيف ثابت ثمّ 429، وأربعة عشر باختلاف الحالة وحدها — كلّها مرّت.
    //
    // وما كان مفتوحاً هو بالضبط ما وُضع الحدّ لأجله: حشو بيانات الاعتماد، وإغراق بريد إعادة التعيين،
    // وتخمين الكوبونات، وإنشاء الحسابات.
    // ========================================================================
    [Theory]
    [InlineData("localhost", "LOCALHOST")]      // حالة الأحرف
    [InlineData("localhost", "LocalHost")]      // حالة مختلطة
    [InlineData("localhost", "localhost.")]     // النقطة الأخيرة (جذر DNS)
    public async Task صيغة_كتابة_المضيف_لا_تفتح_دلو_حدٍّ_جديداً(string first, string variant)
    {
        await using var strict = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Auth:PermitLimit", "3"));

        async Task<HttpStatusCode> LoginOn(string host)
        {
            var client = strict.CreateClient();
            client.DefaultRequestHeaders.Host = host;
            var response = await client.PostAsJsonAsync(
                "/api/auth/login", new { email = "nobody@souq.test", password = "Wrong-Pass-9" });
            return response.StatusCode;
        }

        // يُستهلك الحدّ بالصيغة الأولى حتى الرفض — فيصير الرفض هو الحالة القائمة.
        HttpStatusCode last = default;
        for (var i = 0; i < 4; i++) last = await LoginOn(first);
        last.Should().Be(HttpStatusCode.TooManyRequests, "الحدّ لا يعمل أصلاً — الاختبار بعده بلا معنى");

        // ثمّ الصيغة الأخرى لنفس المضيف: يجب أن تجد الدلو نفسه مستنفداً.
        (await LoginOn(variant)).Should().Be(HttpStatusCode.TooManyRequests,
            $"'{variant}' و'{first}' مضيف واحد ومتجر واحد — فدلو الحدّ واحد");
    }

    private async Task<HttpResponseMessage> RefreshWith(string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");
        return await _api.SecureClient(handleCookies: false).SendAsync(request);
    }

    private static string SetCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{RefreshCookie}=", StringComparison.Ordinal));

    private static string RefreshValue(string setCookie) =>
        setCookie[(RefreshCookie.Length + 1)..setCookie.IndexOf(';')];

    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code;
}
