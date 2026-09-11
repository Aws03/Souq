using System.Reflection;
using AwesomeAssertions;
using MediatR;
using NetArchTest.Rules;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد الاعتماد (Architecture.md §5) كاختبارات: أي انتهاك يُفشل البناء بدل أن
// يُكتشف في مراجعة كود لاحقة. NetArchTest يفحص مراجع الأنواع في الـ IL (بما فيها
// أجسام الدوال) — مراجع المشاريع في csproj وحدها لا تمنع "Controller يستدعي Stripe"
// لأن طبقة الـ API تشير لـ Infrastructure (لتسجيل DI فقط).
// ============================================================================
public class DependencyRuleTests
{
    private static readonly Assembly Domain = typeof(Souq.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Souq.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_لا_يعتمد_على_أي_طبقة_أو_إطار()
    {
        var result = Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "Souq.Application", "Souq.Infrastructure", "Souq.API",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions",
            "MediatR", "FluentValidation", "Stripe").GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Application_لا_يعرف_التقنيات_ولا_طبقات_التنفيذ()
    {
        var result = Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
            "Souq.Infrastructure", "Souq.API",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Data.SqlClient",
            "Stripe", "MailKit", "MimeKit", "BCrypt", "System.IdentityModel.Tokens.Jwt").GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_لا_يعرف_طبقة_الـ_API()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("Souq.API").GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Controllers_رفيعة_بلا_وصول_للبيانات_ولا_مزوّدين_خارجيين()
    {
        // Phase 0 D1: كان PaymentsController يستدعي Stripe SDK مباشرة.
        var result = Types.InAssembly(Api).That().ResideInNamespace("Souq.API.Controllers")
            .ShouldNot().HaveDependencyOnAny(
                "Souq.Infrastructure", "Souq.Domain.Interfaces", "Microsoft.EntityFrameworkCore",
                "Stripe", "MailKit", "MimeKit")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Controllers_لا_تستقبل_كيانات_المجال_مباشرة()
    {
        // الكيانات ليست عقود API: تستقبل الـ Controllers أوامر/طلبات (DTOs) فقط.
        var entityParameters = ControllerActions()
            .SelectMany(m => m.GetParameters().Select(p => (Action: m, p.ParameterType)))
            .Where(x => x.ParameterType.Namespace == "Souq.Domain.Entities")
            .Select(x => $"{x.Action.DeclaringType!.Name}.{x.Action.Name}({x.ParameterType.Name})")
            .ToList();

        entityParameters.Should().BeEmpty();
    }

    [Fact]
    public void معالجات_حالات_الاستخدام_تعيش_في_Application_فقط()
    {
        static IEnumerable<string> Handlers(Assembly assembly) => assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                 i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
            .Select(t => t.FullName!);

        Handlers(Api).Concat(Handlers(Infrastructure)).Concat(Handlers(Domain)).Should().BeEmpty();
    }

    [Fact]
    public void كيانات_المجال_لا_تكشف_أي_setter_عام()
    {
        // التغليف (CLAUDE.md: "الكيانات تحرس قواعدها بنفسها"): الحالة تتغيّر عبر دوال
        // محروسة فقط، لا product.StockQuantity = -5 من الخارج.
        var publicSetters = Domain.GetTypes()
            .Where(t => t.Namespace == "Souq.Domain.Entities" && t.IsClass)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod?.IsPublic == true)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        publicSetters.Should().BeEmpty();
    }

    private static IEnumerable<MethodInfo> ControllerActions() => Api.GetTypes()
        .Where(t => t.Namespace == "Souq.API.Controllers" && t.Name.EndsWith("Controller"))
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    private static string Describe(TestResult result) =>
        "أنواع تخالف القاعدة: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}
