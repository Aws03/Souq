using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد عزل المستأجرين كاختبارات (MultiTenancy.md §4، §7 — "أوضاع الفشل"): كل خطأ هنا يُكتشف عند
// البناء لا في الإنتاج. كيان جديد نسي ITenantOwned، تجاوز للمرشّح خارج مسار المنصّة، SQL خام يبني
// استعلاماً بلا TenantId، أو حالة استخدام تبدّل المتجر — كلها تُفشل هذا الملف.
// ============================================================================
public class TenancyRuleTests
{
    private static readonly Assembly Domain = typeof(Entity).Assembly;
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;
    private static readonly string InfrastructurePath = typeof(Souq.Infrastructure.DependencyInjection).Assembly.Location;

    // الأنواع الوحيدة المسموح لها بـ IgnoreQueryFilters: مسار المنصّة المُراجَع والمُدقَّق (المرحلة 4).
    // إضافة نوع هنا قرار أمني يُراجَع — لا طريق مختصر لميزة.
    private static readonly HashSet<string> ReviewedFilterBypasses = new(StringComparer.Ordinal)
    {
        "Souq.Infrastructure.Persistence.Queries.PlatformQueries",
    };

    private static readonly HashSet<string> RawSqlMethods = new(StringComparer.Ordinal)
    {
        "FromSql", "FromSqlRaw", "FromSqlInterpolated", "SqlQuery", "SqlQueryRaw",
        "ExecuteSql", "ExecuteSqlAsync", "ExecuteSqlRaw", "ExecuteSqlRawAsync",
        "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
    };

    [Fact]
    public void كل_كيان_تجاري_ملك_لمتجر_وجداول_المنصّة_وحدها_بلا_متجر()
    {
        var entities = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Entity)))
            .ToList();

        entities.Where(t => t.Namespace != typeof(Tenant).Namespace && !typeof(ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.FullName).Should().BeEmpty("كل بيانات المتاجر تحمل TenantId؛ جداول المنصّة في Souq.Domain.Platform");
        entities.Where(t => t.Namespace == typeof(Tenant).Namespace && typeof(ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.FullName).Should().BeEmpty("جداول المنصّة تُقرأ قبل معرفة المتجر — مرشّح المستأجر عليها يكسر التحديد");
    }

    [Fact]
    public void كل_كيان_ملك_لمتجر_عليه_مرشّح_المستأجر_ومفتاح_أجنبي_للمتاجر()
    {
        // بناء النموذج لا يتصل بقاعدة: نفحص ما بناه الانعكاس في AppDbContext فعلاً، لا ما نفترضه.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new TenantContext());

        var tenantOwned = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

        tenantOwned.Should().NotBeEmpty();
        tenantOwned.Where(t => t.FindDeclaredQueryFilter(AppDbContext.TenantFilter) is null)
            .Select(t => t.ClrType.Name).Should().BeEmpty("مرشّح Tenant على كل كيان ملك لمتجر");
        tenantOwned.Where(t => !t.GetForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == typeof(Tenant)))
            .Select(t => t.ClrType.Name).Should().BeEmpty("مفتاح أجنبي إلى Tenants على كل كيان ملك لمتجر");
    }

    [Fact]
    public void المفاتيح_الأجنبية_بين_بيانات_المتاجر_تحمل_المتجر()
    {
        // (TenantId, XId) ⇒ (TenantId, Id): يستحيل حتى على مستوى القاعدة أن يشير صف متجر لصف متجر
        // آخر. استثناء واحد مقصود: أبناء التجمّع بمفتاح ظلّ إلى جذرهم (يُنشآن معاً في سياق واحد) والفئة الأب.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new TenantContext());

        var crossRowKeys = db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => typeof(ITenantOwned).IsAssignableFrom(fk.PrincipalEntityType.ClrType) && !fk.IsOwnership)
            .Where(fk => !(fk.PrincipalToDependent?.IsCollection == true && fk.Properties.Single().IsShadowProperty()))
            .Where(fk => !(fk.DeclaringEntityType == fk.PrincipalEntityType))
            .Where(fk => !fk.Properties.Any(p => p.Name == nameof(ITenantOwned.TenantId)))
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name}.{string.Join(",", fk.Properties.Select(p => p.Name))}")
            .ToList();

        crossRowKeys.Should().BeEmpty();
    }

    [Fact]
    public void تجاوز_مرشّح_المستأجر_فقط_في_مسار_المنصّة_المراجَع()
    {
        var offenders = CallersOf(m => m.Name == "IgnoreQueryFilters"
                                       && m.DeclaringType.FullName == "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")
            .Where(type => !ReviewedFilterBypasses.Contains(type))
            .ToList();

        offenders.Should().BeEmpty("IgnoreQueryFilters يعيد صفوف كل المتاجر — مسموح فقط في مسار المنصّة المُدقَّق");
    }

    [Fact]
    public void لا_SQL_خام_في_Infrastructure_خارج_الهجرات()
    {
        var offenders = CallersOf(m => RawSqlMethods.Contains(m.Name)
                                       && m.DeclaringType.Namespace == "Microsoft.EntityFrameworkCore")
            .Where(type => !type.StartsWith("Souq.Infrastructure.Migrations", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty("SQL خام لا يمرّ بمرشّح المستأجر — استعلامات LINQ فقط");
    }

    [Fact]
    public void حالات_الاستخدام_لا_تضبط_المتجر_بل_تقرؤه()
    {
        // TenantContext (القابل للضبط) لوسيط التحديد والمهام الخلفية فقط؛ الميزات ترى ITenantContext.
        var offenders = Application.GetTypes()
            .Where(t => (t.Namespace ?? "").StartsWith("Souq.Application.Features", StringComparison.Ordinal))
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(TenantContext))
                        || t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                            .Any(f => f.FieldType == typeof(TenantContext)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty();
    }

    // الأنواع (العليا — لا أنواع آلة الحالة المولَّدة) في Infrastructure التي تستدعي طريقة تطابق الشرط.
    private static IEnumerable<string> CallersOf(Func<MethodReference, bool> matches)
    {
        using var module = ModuleDefinition.ReadModule(InfrastructurePath);
        return module.GetTypes()
            .SelectMany(type => type.Methods.Where(m => m.HasBody).Select(method => (type, method)))
            .Where(x => x.method.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference reference && matches(reference)))
            .Select(x => TopLevel(x.type).FullName)
            .Distinct()
            .ToList();
    }

    private static TypeDefinition TopLevel(TypeDefinition type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }
}
