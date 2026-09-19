using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Souq.API.Security;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;

namespace Souq.IntegrationTests;

// ============================================================================
// فحص ختم الأمان حين يختفي العميل (ADR-0010).
//
// الفحص يقرأ القاعدة بـ `RequestAborted` نفسه، فهجرُ الزائر صفحته وسط الطلب يُخرج
// `OperationCanceledException` من هذا المعالج. وبلا التقاطه يبتلعه `JwtBearerHandler`
// ويكتب ERROR بمكدّسه ("Exception occurred while processing message") — أي أنّ كل صفحة
// يهجرها مستخدمٌ داخلٌ تصير عطل خادم في السجلّ، فيُصطنع معدّل أخطاء يُخفي الأعطال الحقيقية.
// قِيس على حزمة الحاويات: TaskCanceledException من فتح اتصال EF داخل طبقة المصادقة.
//
// والمطلوب شيئان معاً، ولذلك اختباران: **ألّا يخرج الاستثناء** (فلا يُسجَّل خطأ)،
// و**ألّا تُقبل الجلسة** (ختمٌ لم يُقرأ ليس ختماً صالحاً — الإغلاق الآمن).
//
// يُقاد المعالج مباشرةً: طلبٌ يُلغى في منتصف قراءة القاعدة لا يُحدَث بحتمية عبر HTTP.
// نفس ما يفعله `EnforcementDiagnosticsTests` و`ObservabilityTests.ScopeFor`، ولنفس السبب.
// ============================================================================
public class AccessTokenValidationTests
{
    [Fact]
    public async Task هجر_العميل_للطلب_وسط_فحص_الختم_لا_يخرج_استثناءً_من_المصادقة()
    {
        var context = ContextFor(new CancellingSessionValidator());

        var act = () => AccessTokenValidation.ValidateAsync(context);

        await act.Should().NotThrowAsync(
            "الاستثناء الخارج من هنا يكتبه JwtBearerHandler خطأَ خادم بمكدّسه");
    }

    [Fact]
    public async Task ختمٌ_لم_يُقرأ_لأنّ_العميل_انصرف_لا_يُقبل_جلسةً()
    {
        var context = ContextFor(new CancellingSessionValidator());

        await AccessTokenValidation.ValidateAsync(context);

        context.Result?.Succeeded.Should().NotBe(true, "جلسةٌ لم يُتحقَّق ختمها لا تُقبل");
    }

    // الإلغاء الذي **ليس** هجراً من العميل يبقى كما هو: خطأٌ يُرى، لا يُبتلع.
    [Fact]
    public async Task إلغاءٌ_لا_يصاحبه_هجر_العميل_يبقى_استثناءً_يُرى()
    {
        var context = ContextFor(new CancellingSessionValidator(), clientAborted: false);

        var act = () => AccessTokenValidation.ValidateAsync(context);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "ابتلاعُ كل إلغاء يُخفي عطلاً حقيقياً في القاعدة خلف صمتٍ تامّ");
    }

    private static TokenValidatedContext ContextFor(ISessionValidator validator, bool clientAborted = true)
    {
        var tenancy = new TenantContext();
        tenancy.UseTenant(new TenantInfo(
            1, "marka", "ماركة", TenantStatus.Active, "JOD", "ar", "Asia/Amman"));

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenancy);
        services.AddSingleton(validator);

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (clientAborted)
        {
            var aborted = new CancellationTokenSource();
            aborted.Cancel();
            http.RequestAborted = aborted.Token;
        }

        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(http, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(SouqClaimTypes.TenantId, "1"),
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(SouqClaimTypes.SecurityStamp, "stamp-7"),
            ], "test")),
        };
    }

    // يتصرّف كما تتصرّف القاعدة حين يُلغى الطلب أثناء فتح الاتصال.
    private sealed class CancellingSessionValidator : ISessionValidator
    {
        public Task<bool> IsCurrentAsync(int userId, string securityStamp, CancellationToken ct) =>
            Task.FromException<bool>(new TaskCanceledException());

        public void Forget(int userId) { }
    }
}
