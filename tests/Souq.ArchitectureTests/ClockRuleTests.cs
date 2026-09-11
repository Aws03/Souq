using System.Reflection;
using AwesomeAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Souq.ArchitectureTests;

// ============================================================================
// Phase 0 D12: كل قراءة لـ "الآن" تمرّ عبر TimeProvider — فتُختبر الصلاحيات والانتهاء
// والختم الزمني بساعة ثابتة. NetArchTest يرى الأنواع لا الاستدعاءات (كل الكود يعتمد على
// System.DateTime)، فنفحص تعليمات الـ IL مباشرة بحثاً عن استدعاء خصائص الساعة.
// ============================================================================
public class ClockRuleTests
{
    private static readonly HashSet<string> ForbiddenClockReads =
    [
        "System.DateTime::get_UtcNow", "System.DateTime::get_Now", "System.DateTime::get_Today",
        "System.DateTimeOffset::get_UtcNow", "System.DateTimeOffset::get_Now",
    ];

    public static TheoryData<string> Assemblies => new()
    {
        typeof(Souq.Domain.Common.Entity).Assembly.Location,
        typeof(Souq.Application.DependencyInjection).Assembly.Location,
        typeof(Souq.Infrastructure.DependencyInjection).Assembly.Location,
        typeof(Program).Assembly.Location,
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void لا_قراءة_مباشرة_للساعة_خارج_TimeProvider(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);

        var offenders = module.GetTypes()
            .SelectMany(type => type.Methods.Where(m => m.HasBody).Select(method => (type, method)))
            .Where(x => x.method.Body.Instructions.Any(ReadsTheClock))
            .Select(x => $"{x.type.FullName}::{x.method.Name}")
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty($"{Path.GetFileName(assemblyPath)} يجب أن يقرأ الوقت عبر TimeProvider");
    }

    private static bool ReadsTheClock(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference method
        && ForbiddenClockReads.Contains($"{method.DeclaringType.FullName}::{method.Name}");
}
