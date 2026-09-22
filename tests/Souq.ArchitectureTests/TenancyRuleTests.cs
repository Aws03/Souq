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
        // ============================================================================
        // C3 (TD-66): إبطال جلسات متجر عند أرشفته. أوّل **كاتب** في هذه القائمة — وما قبله كلّه
        // قراءة، فالمراجعة هنا أثقل.
        //
        // ولا بديل عنه: `User` و`RefreshToken` مُرشَّحان بنطاق الطلب، والأرشفة فعلُ منصّةٍ يقع في
        // نطاق المنصّة — فحسابات المتجر غير مرئيّة له أصلاً، لا بحثاً عنها ولا خطأً. والشرط
        // `TenantId == tenantId` مكتوب بيد في كلّ من جملتيه، وهو القاعدة نفسها التي يحملها
        // `PlatformQueries`.
        // ============================================================================
        "Souq.Infrastructure.Security.StoreSessionRevoker",
    };

    // ============================================================================
    // جداول المنصّة **بمفتاح متجر** (الشكل B في ADR-0047 §1): تعيش في Souq.Domain.Platform وتحمل
    // TenantId، فلا مرشّح مستأجر عليها ولا حارس كتابة يختمها. ولهذا لا يستطيع
    // `تجاوز_مرشّح_المستأجر_فقط_في_مسار_المنصّة_المراجَع` أن يحرسها أبداً: **لا مرشّح لتتجاوزه**.
    // الشكل الوحيد الذي لا شبكة أمان له إطلاقاً هو أقلّ الأشكال حراسةً اليوم.
    //
    // فالحارس هنا من نوع آخر: مَن **يلمس** هذه الجداول أصلاً محصورٌ في قائمة مراجَعة، وكل صنف فيها
    // يحمل في رأسه القاعدة نفسها التي يحملها PlatformQueries — شرط TenantId صريح في كل قراءة تخصّ
    // متجراً. إضافة صنف هنا قرار يُراجَع، لا سطر يمرّ.
    //
    // حدّان مكتوبان لا مُغطّيان: الفحص يقرأ Souq.Infrastructure وحدها (Souq.API ثغرة قائمة، كما
    // لسائر فحوص IL هنا)، و**الخاصّية الملاحية تهرب منه** — لو صار Subscription ملاحةً على Tenant
    // لقُرئ عبرها بلا لمس DbSet، كما يقرأ TenantDirectory نطاقاتِ المتجر عبر t.Domains اليوم.
    // ولذلك لا ملاحة من Tenant إلى أيٍّ من هذه الجداول، عمداً.
    // ============================================================================
    private static readonly HashSet<string> ReviewedPlatformKeyedReads = new(StringComparer.Ordinal)
    {
        // موضع **الإعلان** لا القراءة: AppDbContext هو من يصرّح بـ DbSet<T> أصلاً.
        "Souq.Infrastructure.Persistence.AppDbContext",
        // القارئ المُراجَع الأصلي: نطاقات المتجر، بشرط المتجر الصريح.
        "Souq.Infrastructure.Persistence.Queries.PlatformQueries",
        "Souq.Infrastructure.Persistence.Repositories.TenantRepository",
        // C1: الدليل يحلّ الاستحقاق الفعّال (اشتراك + استثناءات) بشرط `== t.Id` في كل استعلام فرعي.
        "Souq.Infrastructure.Tenancy.TenantDirectory",
        // C1: منافذ الكتابة والقراءة لوحدة Billing — رأس كل منهما يحمل القاعدة مكتوبة.
        "Souq.Infrastructure.Persistence.Repositories.PlanRepository",
        "Souq.Infrastructure.Persistence.Repositories.SubscriptionRepository",
        "Souq.Infrastructure.Persistence.Repositories.EntitlementOverrideRepository",
        "Souq.Infrastructure.Persistence.Queries.BillingQueries",
        // أصل واجهة المتجر لرسائل بلا طلب HTTP (المرحلة 14): نطاقه الأساسي بشرط `t.Id == tenantId`.
        // قارئٌ قائم كشفه هذا الاختبار عند إدخاله — وهو بالضبط ما يُفترض أن يفعله: الجرد كان ناقصاً.
        "Souq.Infrastructure.Notifications.StoreOrigins",
        // البذر ينشئ المتجر الافتراضي ونطاقه وخطته التأسيسية قبل أن يوجد مستأجر أصلاً.
        "Souq.Infrastructure.Persistence.DbSeeder",
        // ============================================================================
        // C5 (ADR-0056): فوترةُ التاجر. الفواتيرُ وإشعاراتُ الدائن وفتراتُ الفوترة وأحداثُها
        // جداولُ الشكل B — بلا مرشّحٍ وبلا حارس كتابة — ورأسُ كل ملفٍّ منها يحمل القاعدة مكتوبة:
        // كلُّ قراءةٍ تخصّ متجراً تحمل شرط `TenantId` صريحاً، وما يقرؤه تاجرٌ عن نفسه يمرّ
        // بـ `GetForTenantAsync` حصراً.
        // ============================================================================
        "Souq.Infrastructure.Persistence.Repositories.PlatformInvoiceRepository",
        "Souq.Infrastructure.Persistence.Repositories.CreditNoteRepository",
        "Souq.Infrastructure.Persistence.Repositories.BillingPeriodRepository",
        "Souq.Infrastructure.Persistence.Repositories.BillableEventRepository",
        "Souq.Infrastructure.Persistence.Queries.PlatformBillingQueries",
        // ============================================================================
        // C6 (ADR-0058): المطالبة الآلية. **هذا الصنف الوحيد هنا الذي يقرأ عبر المتاجر عمداً**
        // ولا يحمل شرط `TenantId` — والسبب أنّ سؤاله ليس «ما على هذا المتجر» بل «ما استحقّ على
        // المنصّة كلّها»، وهو استعلامٌ واحد لا مرورٌ على المتاجر. ولذلك يعمل بنطاق المنصّة صراحةً
        // (`UsePlatform`)، وتحت عقد إيجارٍ فلا نسختان تفعلانه معاً.
        //
        // وما يحفظ العزل هنا ليس شرطاً في استعلام بل أنّ **كل فعلٍ يأخذ متجره من الفاتورة نفسها**:
        // التعليقُ يقرأ `invoice.TenantId`، ورسالةُ الصادر تُبنى به صراحةً لا من سياقٍ لا متجر فيه.
        // ============================================================================
        "Souq.Infrastructure.BackgroundJobs.DunningService",
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
        // ============================================================================
        // C2 (ADR-0049 §الالتزام الثالث): حارس الحصص. التحديث المجمَّع هنا ليس اختصار أداء بل
        // **هو الآلية**: `SET Used = Used + 1 WHERE Used < @limit` جملةٌ واحدة ذرّية، وتفكيكها إلى
        // قراءةٍ فقرار فكتابة يُعيد بالضبط السباق الذي وُجدت لتمنعه.
        //
        // وآمنٌ بلا شرط TenantId مكتوب بيد: `TenantUsageCounter` كيان `ITenantOwned`، فمرشّح
        // المستأجر يُطبَّق على الجملة كما يُطبَّق على OrderNumbers تماماً. والطوابع المفوَّتة لا
        // تضرّ هنا: الصفّ عدّادٌ لا سجلّ، و`UpdatedAt` عليه لا يقرؤه أحد.
        // ============================================================================
        "Souq.Infrastructure.Persistence.TenantQuotaGuard",
        // ============================================================================
        // C5 (ADR-0056): سلسلةُ ترقيم مستندات المنصّة. التحديثُ المجمَّع هنا **هو الآلية** لا
        // اختصارُ أداء — جملةٌ واحدة ذرّية تزيد العدّاد وتقفل صفَّه حتى الالتزام، وتفكيكُها إلى
        // قراءةٍ فكتابة يُنتج رقمَين متطابقَين لفاتورتين. ونطاقُها عالميّ لا متجريّ (الشكل C)،
        // فهي تكتب شرطَ سلسلتها بيدها ولا ترث مرشّحاً — خلافاً لـ `OrderNumbers`.
        // والطوابعُ المفوَّتة لا تضرّ: الصفُّ عدّادٌ لا سجلّ.
        // ============================================================================
        "Souq.Infrastructure.Persistence.PlatformDocumentNumbers",
        // ============================================================================
        // C4 (ADR-0057): القفلُ الذي يعبر النسخ. التحديثُ المجمَّع **هو الآلية** لا اختصارَ أداء:
        // الشرطُ والحجزُ في جملةٍ ذرّية واحدة، وتفكيكُها إلى قراءةٍ فكتابة يُعيد بالضبط السباقَ
        // الذي وُجد القفلُ لمنعه. والجدولُ عالميٌّ بلا متجر (الشكل C) فلا مرشّحَ يُتجاوَز، والطوابعُ
        // المفوَّتة لا تضرّ: الصفُّ عقدُ إيجارٍ لا سجلّ.
        // ============================================================================
        "Souq.Infrastructure.Coordination.SqlDistributedLock",
        // C4 (ADR-0057): إشاراتُ الإبطال. `SET Version = Version + 1` جملةٌ ذرّية واحدة —
        // وقراءةٌ ثمّ كتابة كانت ستُضيع قفزةً حين تُبطل نسختان معاً، فتبقى نسخةٌ ثالثة على
        // القديم. والجدولُ عالميّ بلا متجر، والطوابعُ المفوَّتة لا تضرّ: الصفُّ عدّادٌ لا سجلّ.
        "Souq.Infrastructure.Coordination.SqlCacheSignals",
        // ============================================================================
        // C3 (TD-66): إبطال جلسات متجر عند أرشفته. البديل تحميلُ كل حسابات المتجر — عملاؤه لا
        // موظّفوه وحدهم، فقد تكون آلافاً — لتدوير ختم كلٍّ منها. جملتان ثابتتا التكلفة تكفيان،
        // والطوابع المفوَّتة لا تضرّ: الختم علامةُ إصدار لا سجلّ، ورمز التجديد المُبطَل يحمل وقته
        // في `RevokedAt` نفسه. الشرط `TenantId == tenantId` صريح في الجملتين.
        // ============================================================================
        "Souq.Infrastructure.Security.StoreSessionRevoker",
        // C9 (ADR-0050 §6): مسح الأحداث السلوكية. حذفٌ مجمَّع بدفعاتٍ بالمفتاح، كنظيره في
        // `SearchLogRetention` وللسبب نفسه: `DELETE TOP` لا يُعبَّر عنه في LINQ. و`BehaviouralEvent`
        // كيان `ITenantOwned` فمرشّح المستأجر يُطبَّق على الجملة، والصفّ يُحذف لا يُعدَّل فلا طوابع
        // تُفوَّت. ومَن يحرس **ألّا يُمسح ما لم يُجمَّع** هو المعالج لا هذه القائمة.
        "Souq.Infrastructure.Persistence.EventStoreRetention",
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
    public void قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع()
    {
        // الشكل B يُكتشف بالانعكاس لا بقائمة أسماء: كيان في نطاق Souq.Domain.Platform يحمل TenantId.
        // جدول جديد بهذا الشكل يدخل الحراسة من تلقاء نفسه — وهو الفرق بين قاعدة وقائمة تتقادم.
        var tenantKeyed = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Entity)))
            .Where(t => t.Namespace == typeof(Tenant).Namespace)
            .Where(t => t.GetProperty(nameof(ITenantOwned.TenantId)) is not null)
            .Select(t => t.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        tenantKeyed.Should().NotBeEmpty("TenantDomain وحده يكفي؛ مجموعة فارغة تعني أن الفحص لا يفحص شيئاً");

        // "يلمسه" = يمرّ النوع وسيطاً نوعياً في نداء (DbSet<T>، IQueryable<T>، Set<T>()): أي أن هذا
        // الصنف يتعامل مع **صفوف** الجدول، لا أنه يستدعي دالّة ساكنة عليه.
        // استثناءان بنيويان لا قائمة أسماء: الهجرات تصف المخطّط لا الصفوف، وصنف الإعداد
        // (IEntityTypeConfiguration) لا يملك DbContext أصلاً فلا يستطيع قراءة صفّ ولو أراد.
        var offenders = CallersOf(m => MentionsAsGenericArgument(m, tenantKeyed))
            .Where(type => !type.StartsWith("Souq.Infrastructure.Migrations", StringComparison.Ordinal))
            .Where(type => !type.StartsWith("Souq.Infrastructure.Persistence.Configurations.", StringComparison.Ordinal))
            .Where(type => !ReviewedPlatformKeyedReads.Contains(type))
            .ToList();

        offenders.Should().BeEmpty(
            "جداول المنصّة بمفتاح متجر بلا مرشّح وبلا حارس كتابة: عزلها شرطُ TenantId الذي يكتبه "
            + "المستدعي بيده. صنف جديد يقرؤها يُضاف إلى ReviewedPlatformKeyedReads عمداً وبسببه");
    }

    // النوع مذكور وسيطاً نوعياً في توقيع النداء (مباشرةً أو داخل وسيط مركّب مثل List<T>).
    private static bool MentionsAsGenericArgument(MethodReference method, HashSet<string> types)
    {
        if (method is GenericInstanceMethod generic && generic.GenericArguments.Any(a => Mentions(a, types)))
            return true;
        return method.DeclaringType is GenericInstanceType declaring
               && declaring.GenericArguments.Any(a => Mentions(a, types));

        static bool Mentions(TypeReference type, HashSet<string> types) =>
            types.Contains(type.FullName)
            || (type is GenericInstanceType nested && nested.GenericArguments.Any(a => Mentions(a, types)));
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
