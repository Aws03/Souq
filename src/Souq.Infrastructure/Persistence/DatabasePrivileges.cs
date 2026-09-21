using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// هل يعمل التطبيق بهوية تستطيع تغيير مخطّط قاعدته؟ (R-12)
//
// القياس تمّ مرّة في مهمة الجاهزية التشغيلية وأثبت أن db_datareader + db_datawriter تكفيان.
// لكن القياس لا يمنع نشراً يصل بـ sa، وهو الحال الافتراضي اليوم — فيبقى الفرق بين "نعرف أن
// الأقلّ يكفي" و"نعمل بالأقلّ فعلاً" غير مرئي إطلاقاً. هذا الفحص يجعله مرئياً عند كل إقلاع.
//
// استعلام ADO.NET مباشر لا عبر FromSql/ExecuteSql: القاعدة المعمارية تمنع تلك الطرق لأنها
// تتجاوز مرشّح المستأجر — وهذا الاستعلام لا يلمس بيانات مستأجر أصلاً، بل عضوية أدوار الخادم
// والقاعدة. استعمال الطرق الممنوعة هنا كان سيخفي القصد خلف استثناء في قائمة سماح.
// تشخيصي بالكامل: أي فشل يعيد null ولا يمنع الإقلاع — فحص لا يجوز أن يُسقِط ما يفحصه.
// ============================================================================
public sealed record DatabasePrivilegeReport(string Login, bool IsSysadmin, bool IsDbOwner, bool IsDdlAdmin)
{
    // أيّها كافٍ لتغيير المخطّط، أي: أكثر مما يحتاجه التشغيل العادي.
    public bool CanChangeSchema => IsSysadmin || IsDbOwner || IsDdlAdmin;

    public string Roles => string.Join(", ", new[]
    {
        IsSysadmin ? "sysadmin" : null,
        IsDbOwner ? "db_owner" : null,
        IsDdlAdmin ? "db_ddladmin" : null,
    }.Where(r => r is not null));
}

// ============================================================================
// هل تعمل القاعدة بلقطات القراءة المُلتزَمة؟ (TD-68، ADR-0049)
//
// حارس "آخر مدير" كان آمناً فقط لأن READ COMMITTED الافتراضي **قافل**: يكتب أوّلاً فيأخذ القفل،
// فيضطرّ المتسابق إلى الانتظار ثم يَعُدّ الحقيقة الملتزمة. وتحت READ_COMMITTED_SNAPSHOT يقرأ العدُّ
// لقطةً ولا ينتظر شيئاً، **فيمرّ المتسابقان معاً ويُوقَف آخر مديرَين** — بلا استثناء ولا سطر سجلّ
// ولا اختبار أحمر. **وذلك الحارس أُصلح** (F-29): يفتح معاملته بـ SERIALIZABLE حين يكون الثابت في
// خطر، فيصحّ مهما كان إعداد القاعدة.
//
// فلماذا يبقى الفحص إذن؟ لأنّ ما يقيسه **حقيقة عن القاعدة** لا عطباً في حارس بعينه: القياس أثبت
// أنّ EF Core يُفعّل RCSI على كل قاعدة يُنشئها، أي أنّ هذه هي حال كل نشر. ومن يعرف ذلك يعرف أنّ
// أيّ ثابت جديد يُقاس عبر **عدّة صفوف** لا يصحّ هنا بالعدّ وحده — يحتاج عزلاً صريحاً، أو شكل
// العدّاد في ADR-0049. والافتراض الصامت بأن "العدّ يحجب" هو ما أنتج العطب أوّل مرّة.
//
// وحارس الحصص (TenantQuotaGuard) لم يتأثّر يوماً: تحديثه المشروط يأخذ قفلاً مهما كان مستوى العزل،
// وهو سبب اختياره في ADR-0049.
//
// ولماذا فحصٌ عند الإقلاع أصلاً؟ لأن اختباراً لا يُثبت إلّا حال حاوية الاختبار؛ والقياس يجب أن
// يقع على القاعدة التي تعمل عليها فعلاً — وهو بالضبط ما تفعله DatabasePrivileges أعلاه للهوية.
// تشخيصي بالكامل: أي فشل يعيد null ولا يمنع الإقلاع.
// ============================================================================
public static class DatabaseIsolation
{
    // ============================================================================
    // من `sys.databases` لا من `DATABASEPROPERTYEX`، رغم أن TD-68 وADR-0049 سمّيا الثانية:
    // **`DATABASEPROPERTYEX(DB_NAME(), 'IsReadCommittedSnapshotOn')` تعيد NULL** — لا خاصّية
    // بهذا الاسم، والدالة تجيب NULL عن المجهول بدل أن تخطئ. قِيس على SQL Server 2022 في الحاوية:
    // العمود في `sys.databases` يقول 1 والدالة تقول NULL على القاعدة نفسها في اللحظة نفسها.
    //
    // وهذا بالضبط ما لا يكشفه إلا تشغيل التطبيق: الفحص كان "يعمل" ويسجّل "تعذّرت القراءة" في كل
    // إقلاع — أي أنه يفشل **صامتاً**، وهو العيب الذي وُجد هو نفسه لعلاجه.
    //
    // والصفّ مرئيّ لهوية منخفضة الصلاحية: `sys.databases` تُظهر لكل مستفيد قواعده، وشرط
    // `DB_ID()` يحصره في القاعدة العاملة.
    // ============================================================================
    private const string Query =
        "SELECT CONVERT(int, is_read_committed_snapshot_on) FROM sys.databases WHERE database_id = DB_ID();";

    public static async Task<bool?> IsReadCommittedSnapshotOnAsync(AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = Query;
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : Convert.ToInt32(value) == 1;
        }
        catch (Exception)
        {
            return null;   // مزوّد مختلف أو صلاحية ناقصة: لا يعني خطأً، ولا يعني أماناً أيضاً
        }
    }

    public const string Warning =
        "قاعدة البيانات تعمل بـ READ_COMMITTED_SNAPSHOT، وهذه هي الحال **المتوقَّعة** لا الاستثناء: "
        + "EF Core يُفعّلها على أي قاعدة يُنشئها هو (قِيس: model = 0 وقاعدة التطبيق = 1، ولا شيء في "
        + "المستودع يضبطها). أثرها أنّ حارس \"آخر مدير\" (AccountStatusChanger) يعتمد على قراءةٍ "
        + "**قافلة** لو تُرك على العزل الافتراضي. وهو **لم يُترك**: منذ F-29 يفتح ذلك الحارس معاملته "
        + "بـ SERIALIZABLE حين يكون الثابت في خطر، فيصحّ تحت اللقطات وتحت الأقفال سواء "
        + "(`LastAdministratorRcsiTests` يقيسه على قاعدة بلقطات فعلاً). وحصص الخطط غير متأثّرة أصلاً. "
        + "فما يقوله هذا السطر إذن ليس عطباً بل **حقيقة عن القاعدة تستحقّ أن تكون معلومة**: أي ثابت "
        + "جديد يُقاس عبر عدّة صفوف لا يصحّ هنا بالعدّ وحده، ويحتاج عزلاً صريحاً أو شكل العدّاد في "
        + "ADR-0049. — TD-68";
}

public static class DatabasePrivileges
{
    private const string Query = """
        SELECT SUSER_SNAME(),
               CONVERT(int, ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0)),
               CONVERT(int, ISNULL(IS_MEMBER('db_owner'), 0)),
               CONVERT(int, ISNULL(IS_MEMBER('db_ddladmin'), 0));
        """;

    public static async Task<DatabasePrivilegeReport?> InspectAsync(AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = Query;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;

            return new DatabasePrivilegeReport(
                reader.IsDBNull(0) ? "(unknown)" : reader.GetString(0),
                reader.GetInt32(1) == 1, reader.GetInt32(2) == 1, reader.GetInt32(3) == 1);
        }
        catch (Exception)
        {
            return null;   // مزوّد مختلف، أو صلاحية ناقصة حتى لقراءة العضوية: لا يعني خطأً
        }
    }

    // هل تحمل سلسلتا التشغيل والهجرات الهوية نفسها؟ ضبط ConnectionStrings:Migrations بنسخة من
    // Default يُنتج فصلاً اسمياً: يبدو مطبَّقاً في الإعداد وهو ليس كذلك في القاعدة.
    public static bool SameLogin(string runtime, string migrations)
    {
        try
        {
            var a = new SqlConnectionStringBuilder(runtime);
            var b = new SqlConnectionStringBuilder(migrations);
            return string.Equals(a.UserID, b.UserID, StringComparison.OrdinalIgnoreCase)
                   && a.IntegratedSecurity == b.IntegratedSecurity;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
