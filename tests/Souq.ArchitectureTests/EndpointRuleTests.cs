using System.Reflection;
using AwesomeAssertions;
using MediatR;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد النقاط وحالات الاستخدام التي تُفحص بنيوياً (ADR-0019، docs/07-SECURITY/SecurityControls.md):
//   • كل نقطة تعلن وصولها صراحةً: لا سياسة احتياطية في Program.cs، فإجراء بلا [AllowAnonymous] ولا [Authorize]/[HasPermission]
//     مفتوح للجميع بصمت — نسيان سطر يصبح ثغرة. التصريح يجعل "العام" قراراً مكتوباً يراه المراجِع.
//   • نقاط المنصّة تتطلّب صلاحية منصّة: مضيف المنصّة وحده لا يكفي — حساب متجر لا يحمل صلاحيات platform.* أبداً.
//   • كل أمر أو استعلام له معالج واحد بالضبط: MediatR يرمي عند الإرسال فقط (أي في الإنتاج)، والمعالج المكرّر يفوز أحدهما بصمت.
// ============================================================================
public class EndpointRuleTests
{
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;

    [Fact]
    public void كل_نقطة_تعلن_وصولها_صراحةً()
    {
        GeneratedDocsTests.Endpoints()
            .Where(e => e.Access == GeneratedDocsTests.NoAccessDeclared)
            .Select(e => $"{e.Verb} {e.Route} ({e.Controller}.{e.Action})")
            .Should().BeEmpty("كل نقطة تحمل [AllowAnonymous] أو [Authorize]/[HasPermission] على الإجراء أو الصنف");
    }

    [Fact]
    public void نقاط_المنصّة_تتطلّب_صلاحية_منصّة()
    {
        GeneratedDocsTests.Endpoints()
            .Where(e => e.Hosts.StartsWith("platform", StringComparison.Ordinal))
            .Where(e => !e.Access.Contains("`platform.", StringComparison.Ordinal))
            .Select(e => $"{e.Verb} {e.Route} → {e.Access}")
            .Should().BeEmpty("نقطة على مضيف المنصّة تتطلّب صلاحية platform.* صريحة");
    }

    [Fact]
    public void كل_أمر_أو_استعلام_له_معالج_واحد_بالضبط()
    {
        var handled = Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                                            || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
            .GroupBy(i => i.GetGenericArguments()[0])
            .ToDictionary(g => g.Key, g => g.Count());

        Application.GetTypes().Where(GeneratedDocsTests.IsRequest)
            .Where(r => handled.GetValueOrDefault(r) != 1)
            .Select(r => $"{r.FullName}: {handled.GetValueOrDefault(r)} معالج")
            .Should().BeEmpty();
    }
}
