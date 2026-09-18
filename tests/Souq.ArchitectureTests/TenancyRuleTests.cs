using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Identity;
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

    // كتابة مجمّعة: تُترجَم إلى UPDATE/DELETE واحد ولا تمرّ بـ SaveChanges إطلاقاً — فلا
    // TenantWriteGuardInterceptor يفحصها ولا AuditTimestampsInterceptor يختمها. عزلها يقوم على مرشّح
    // المستأجر وحده.
    private static readonly HashSet<string> BulkWriteMethods = new(StringComparer.Ordinal)
    {
        "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync",
    };

    // المواضع الستّة المراجَعة اليوم (F-1). إضافة نوع هنا قرار يُراجَع كتوأمه أعلاه — لا طريق مختصر لأداء:
    //   • OrderNumbers — زيادة عدّاد المتجر بلا Where إطلاقاً: المرشّح وحده يختار الصفّ.
    //   • NotificationRepository — "اقرأ الكلّ" بشرط المستلم وحده؛ المتجر من المرشّح.
    //   • OutboxProcessor — جدول غير مُرشَّح عمداً، وخدمة واحدة بنطاق المنصّة تقرؤه ثم تدخل نطاق كل رسالة.
    private static readonly HashSet<string> ReviewedBulkWrites = new(StringComparer.Ordinal)
    {
        "Souq.Infrastructure.Persistence.OrderNumbers",
        "Souq.Infrastructure.Persistence.Repositories.NotificationRepository",
        "Souq.Infrastructure.Persistence.Outbox.OutboxProcessor",
        // M13: مسح سجلّ البحث. حذفٌ مجمَّع مراجَع — `SearchQueryLog` كيان `ITenantOwned` فمرشّح المستأجر
        // يُطبَّق على الجملة، والحذف يقع في نطاق متجرٍ يُرسله المنسّق. ولا طوابع تُفوَّت: الصفّ يُحذف لا يُعدَّل.
        "Souq.Infrastructure.Persistence.SearchLogRetention",
    };

    [Fact]
    public void كل_كيان_تجاري_ملك_لمتجر_وجداول_المنصّة_وحدها_بلا_متجر()
    {
        var entities = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Entity)))
            .ToList();

        entities.Where(t => t.Namespace != typeof(Tenant).Namespace
                            && !typeof(ITenantOwned).IsAssignableFrom(t) && !typeof(ITenantOrPlatformOwned).IsAssignableFrom(t))
            .Select(t => t.FullName).Should().BeEmpty("كل بيانات المتاجر تحمل TenantId؛ جداول المنصّة في Souq.Domain.Platform");
        entities.Where(t => t.Namespace == typeof(Tenant).Namespace
                            && (typeof(ITenantOwned).IsAssignableFrom(t) || typeof(ITenantOrPlatformOwned).IsAssignableFrom(t)))
            .Select(t => t.FullName).Should().BeEmpty("جداول المنصّة تُقرأ قبل معرفة المتجر — مرشّح المستأجر عليها يكسر التحديد");

        // TenantId الاختياري (متجر أو منصّة، ADR-0010) للحسابات وجلساتها وحدها: بيانات تجارية بلا متجر
        // إلزامي تسريب ينتظر الحدوث.
        entities.Where(t => typeof(ITenantOrPlatformOwned).IsAssignableFrom(t) && t.Namespace != typeof(User).Namespace)
            .Select(t => t.FullName).Should().BeEmpty("TenantId الاختياري لكيانات Souq.Domain.Identity فقط");
        entities.Where(t => typeof(ITenantOwned).IsAssignableFrom(t) && typeof(ITenantOrPlatformOwned).IsAssignableFrom(t))
            .Select(t => t.FullName).Should().BeEmpty("نطاق واحد لكل كيان");
    }

    [Fact]
    public void كل_كيان_ملك_لمتجر_عليه_مرشّح_المستأجر_ومفتاح_أجنبي_للمتاجر()
    {
        // بناء النموذج لا يتصل بقاعدة: نفحص ما بناه الانعكاس في AppDbContext فعلاً، لا ما نفترضه.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new TenantContext());

        // الحسابات (ITenantOrPlatformOwned) بالمرشّح نفسه: صفوف متجر السياق، أو صفوف المنصّة في نطاقها.
        var tenantOwned = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && (typeof(ITenantOwned).IsAssignableFrom(t.ClrType)
                                         || typeof(ITenantOrPlatformOwned).IsAssignableFrom(t.ClrType)))
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
            // استثناء أبناء التجمّع بمفتاح ظلّ واحد فقط؛ ابن تجمّع بمفتاح مركّب (استردادات الدفعة، المرحلة 11) يمرّ للفحص
            // الأخير كغيره — ويجتازه لأنه يحمل TenantId.
            .Where(fk => !(fk.PrincipalToDependent?.IsCollection == true && fk.Properties is [var only] && only.IsShadowProperty()))
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

    // F-1: الكتابة المجمّعة تتجاوز حارس الكتابة وختم الطوابع معاً، فيبقى مرشّح المستأجر دفاعها الوحيد.
    // المرشّح يُطبَّق فعلاً على هذه العمليات، فالمواضع القائمة سليمة — لكن الموضع التالي لن يكون له دفاع ولا اختبار
    // ما لم يُكتشف عند البناء. هذا الاختبار يجعله يُكتشف.
    [Fact]
    public void الكتابة_المجمّعة_تتجاوز_حارس_الكتابة_فمواضعها_مراجَعة()
    {
        var offenders = CallersOf(m => BulkWriteMethods.Contains(m.Name)
                                       && m.DeclaringType.Namespace == "Microsoft.EntityFrameworkCore")
            .Where(type => !ReviewedBulkWrites.Contains(type))
            .ToList();

        offenders.Should().BeEmpty(
            "ExecuteUpdate/ExecuteDelete لا تمرّ بـ SaveChanges: لا حارس كتابة ولا طوابع. موضع جديد يحتاج مراجعة"
            + " ويُضاف إلى ReviewedBulkWrites عمداً — مع شرط TenantId صريح إن لم يكن الكيان مُرشَّحاً");
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
