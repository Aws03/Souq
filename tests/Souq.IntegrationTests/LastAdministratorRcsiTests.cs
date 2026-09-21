using System.Data;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// F-29 — حارس "آخر مدير" على قاعدة تعمل بـ READ_COMMITTED_SNAPSHOT.
//
// **لماذا قاعدة خاصّة بهذا الاختبار؟** لأن حاوية المجموعة تعمل بـ RCSI **مطفأة**، وهناك يمرّ
// النمط القديم (اكتب ثمّ أعد العدّ) لأن العدّ الثاني يُحجَب بقفل الكاتب الآخر. الخطر كلّه في
// الحال المقابلة — و**هي الحال الافتراضية لكل نشر**: EF Core يُفعّل RCSI على كل قاعدة يُنشئها.
// فاختبارٌ على قاعدة المجموعة كان سيقيس البيئة التي لا يقع فيها العطل، ويبقى أخضر أبداً.
//
// فالقاعدة هنا تُنشأ، ويُفعَّل عليها RCSI صراحةً، ثم يُشغَّل السباق نفسه. تحت اللقطات يقرأ كل
// متسابق حالةً ملتزمة قبل بدئه، فيرى مديراً آخر فعّالاً ويمضي — ويُوقَف آخر مديرَين للمتجر بلا
// خطأ ولا سجلّ. و`SERIALIZABLE` هو ما يمنع ذلك: المدى المعدود لا يتغيّر قبل الالتزام.
//
// والسباق يُشغَّل بـ `IUnitOfWork` نفسه الذي يستعمله `AccountStatusChanger`، وبنفس ترتيبه
// (أوقف ⇒ احفظ ⇒ أعد العدّ ⇒ تراجَع إن صفر) — فما يُقاس هو الآليّة لا محاكاةٌ لها.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class LastAdministratorRcsiTests
{
    private readonly SouqApiFactory _factory;

    public LastAdministratorRcsiTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public async Task تحت_لقطات_القراءة_الملتزمة_لا_يُوقَف_آخر_مديرَين()
    {
        var database = $"rcsi_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(_factory.ConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        var tenant = new TenantInfo(1, "rcsi", "RCSI", TenantStatus.Active, "JOD", "ar", "UTC",
            new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal));

        try
        {
            // ============================================================
            // القاعدة تُنشأ **ويُضبط عزلها قبل أن يتصل بها أحد**. الترتيب مقصود: `ALTER DATABASE`
            // يحتاج ألّا يكون لأحد اتصال، وضبطُه بعد الترحيل كان يُلزم `WITH ROLLBACK IMMEDIATE`
            // ثمّ تفريغ مجمّع الاتصالات — وهو **عامّ للعملية كلّها**، فيقطع اتصالات بقيّة
            // المجموعة على القاعدة المشتركة. قِيس: الاختبار سقط بـ "connection reset by peer".
            //
            // والحال التي تُصنع هنا هي التي يصنعها EF Core نفسه على كل قاعدة يُنشئها.
            // ============================================================
            await ExecuteOnMasterAsync(connectionString,
                $"CREATE DATABASE [{database}]; ALTER DATABASE [{database}] SET READ_COMMITTED_SNAPSHOT ON;");

            await using (var setup = Context(connectionString, tenant))
            {
                await setup.Database.MigrateAsync();
            }

            (await SnapshotOnAsync(connectionString)).Should().BeTrue("الفرضية نفسها: القاعدة تعمل بلقطات");

            var (first, second) = await SeedTwoAdministratorsAsync(connectionString, tenant);
            (await ActiveAdministratorsAsync(connectionString, tenant)).Should().Be(2,
                "الفرضية: مديران فعّالان بالضبط، فالمقعد المتنازع عليه واحد");

            // ============================================================
            // **حاجزٌ بين الكتابة وإعادة العدّ، وبدونه لا يُقاس شيء.**
            //
            // بلا مزامنة ينتهي أحد المتسابقَين قبل أن يبدأ الآخر، فالنافذة لا تُفتح أصلاً: جُرّب،
            // ومرّ النمط القديم ثلاث مرّات من ثلاث على قاعدة تعمل بلقطات. وهذا بعينه ما يحذّر منه
            // ADR-0049 §الالتزام الرابع — اختبار لا يبلغ الفرع المحروس يمرّ بالحساب لا بالحراسة.
            //
            // فالحاجز يُلزم الاثنين بأن يكتبا **قبل** أن يَعُدّ أيٌّ منهما، وهي اللحظة التي يفترق
            // فيها المستويان: تحت اللقطات يقرأ كلٌّ حالةً سابقة لكتابة الآخر فيرى زميلاً فعّالاً
            // ويمضي؛ وتحت التسلسلي لا يُقرأ المدى قبل أن يلتزم من يملكه.
            // ============================================================
            using var wrote = new Barrier(2);

            var results = await Task.WhenAll(
                DisableGuardedAsync(connectionString, tenant, first, wrote),
                DisableGuardedAsync(connectionString, tenant, second, wrote));

            var remaining = await ActiveAdministratorsAsync(connectionString, tenant);

            remaining.Should().BeGreaterThan(0,
                $"لا يجوز أن يبقى المتجر بلا مدير فعّال — النتيجتان: {results[0]} و {results[1]}");
            results.Count(r => r == Outcome.Disabled).Should().Be(1, "واحد فقط ينجح");
        }
        finally
        {
            await DropAsync(connectionString, database);
        }
    }

    private enum Outcome { Disabled, RefusedLastAdministrator, Conflict }

    // نفس ترتيب `AccountStatusChanger.SetActiveAsync` للحالة المحروسة: أوقف، احفظ، أعد العدّ داخل
    // المعاملة، وتراجَع إن لم يبقَ أحد — وبالعزل الذي تطلبه.
    private static async Task<Outcome> DisableGuardedAsync(
        string connectionString, TenantInfo tenant, int userId, Barrier wrote)
    {
        await using var db = Context(connectionString, tenant);
        try
        {
            await ((IUnitOfWork)db).InTransactionAsync(async () =>
            {
                var user = await db.Users.FirstAsync(u => u.Id == userId);
                user.Disable();
                await db.SaveChangesAsync();

                // كلاهما كتب الآن؛ لا يَعُدّ أحد قبل ذلك. المهلة تمنع تعليق الاختبار إن حُجب
                // أحدهما بقفل — وهو ما يقع تحت التسلسلي عمداً.
                try { wrote.SignalAndWait(TimeSpan.FromSeconds(10)); }
                catch (BarrierPostPhaseException) { /* الشريك خرج بجمود: نمضي ونَعُدّ */ }

                // نفس مُسنَد `UserRepository.CountActiveByRoleAsync` حرفياً: `IsInvitationPending`
                // خاصّية محسوبة لا عمود، و"الفعّال" في القاعدة هو مَن له كلمة مرور.
                var active = await db.Users.CountAsync(
                    u => u.Role == Roles.TenantAdmin && u.Status == UserStatus.Active && u.PasswordHash != "");
                if (active == 0) throw new LastAdministratorRace();
            }, TransactionIsolation.Serializable);
            return Outcome.Disabled;
        }
        catch (LastAdministratorRace)
        {
            return Outcome.RefusedLastAdministrator;
        }
        catch (Souq.Application.Common.Exceptions.ConcurrencyConflictException)
        {
            // الجمود مُترجَم: المستدعي الحقيقي يُعيد العدّ ويردّ رفضاً تجارياً واضحاً.
            return Outcome.Conflict;
        }
    }

    private sealed class LastAdministratorRace : Exception;

    private static AppDbContext Context(string connectionString, TenantInfo tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options,
            SouqApiFactory.ContextFor(tenant));

    private static async Task<(int First, int Second)> SeedTwoAdministratorsAsync(string connectionString, TenantInfo tenant)
    {
        await using var db = Context(connectionString, tenant);

        // الهجرات تبذر متجراً؛ نُبقي مديرَين فعّالَين اثنين بالضبط كي يكون المقعد المتنازع عليه واحداً.
        var existing = await db.Users.Where(u => u.Role == Roles.TenantAdmin).ToListAsync();
        foreach (var user in existing.Where(u => u.Status == UserStatus.Active)) user.Disable();

        var first = new User("مدير أول", $"a-{Guid.NewGuid():N}@rcsi.test", "hash", Roles.TenantAdmin);
        var second = new User("مدير ثانٍ", $"b-{Guid.NewGuid():N}@rcsi.test", "hash", Roles.TenantAdmin);
        db.Users.AddRange(first, second);

        // المتجر يُختَم بيدٍ هنا: `TenantWriteGuardInterceptor` يُركَّب عبر حقن التبعيات، وهذا
        // السياق مبنيٌّ يدوياً على قاعدة خاصّة بهذا الاختبار فلا معترِضات فيه — فبلا الختم
        // يُكتب الحسابان بلا متجر (حسابا منصّة) ويُخفيهما المرشّح عن الاختبار نفسه.
        foreach (var user in new[] { first, second })
            db.Entry(user).Property(nameof(ITenantOwned.TenantId)).CurrentValue = tenant.Id;
        await db.SaveChangesAsync();

        return (first.Id, second.Id);
    }

    private static async Task<int> ActiveAdministratorsAsync(string connectionString, TenantInfo tenant)
    {
        await using var db = Context(connectionString, tenant);
        return await db.Users.CountAsync(
            u => u.Role == Roles.TenantAdmin && u.Status == UserStatus.Active && u.PasswordHash != "");
    }

    private static async Task<bool?> SnapshotOnAsync(string connectionString)
    {
        await using var db = Context(connectionString, new TenantInfo(1, "x", "x", TenantStatus.Active, "JOD", "ar", "UTC",
            new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal)));
        return await DatabaseIsolation.IsReadCommittedSnapshotOnAsync(db);
    }

    private static async Task DropAsync(string connectionString, string database)
    {
        try
        {
            await ExecuteOnMasterAsync(connectionString,
                $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
        }
        catch (SqlException)
        {
            // قاعدة تجريب لم تُنشأ أصلاً، أو أُسقطت: لا يُخفي فشلَ الاختبار نفسه.
        }
    }

    private static async Task ExecuteOnMasterAsync(string connectionString, string sql)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
