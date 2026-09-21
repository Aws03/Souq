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
// `AccountStatusChanger.SetActiveAsync` — حارس "آخر مدير" — آمنٌ فقط لأن READ COMMITTED الافتراضي
// **قافل**: يكتب أوّلاً فيأخذ القفل، فيضطرّ المتسابق إلى الانتظار ثم يَعُدّ الحقيقة الملتزمة. وتحت
// READ_COMMITTED_SNAPSHOT يقرأ العدُّ لقطةً ولا ينتظر شيئاً، **فيمرّ المتسابقان معاً ويُوقَف آخر
// مديرَين**. يفشل مغلقاً؟ لا: يفشل **مفتوحاً** وبلا استثناء ولا سطر سجلّ ولا اختبار أحمر.
//
// ولماذا فحصٌ عند الإقلاع لا اختبار؟ لأن الاختبار لا يُثبت إلّا حال حاوية الاختبار، وهي مطفأة
// الخاصيّة. والخطر في مكان آخر تماماً: Azure SQL Database — التي تسمّيها BackupAndRestore.md
// هدفاً ممكناً — تُفعّلها **افتراضياً**. فالقياس يجب أن يقع على القاعدة التي تعمل عليها فعلاً،
// وهو بالضبط ما تفعله DatabasePrivileges أعلاه للهوية، وبالشكل نفسه.
//
// حارس الحصص (TenantQuotaGuard) لا يتأثّر بهذا إطلاقاً — تحديثه المشروط يأخذ قفلاً مهما كان مستوى
// العزل، وهو سبب اختياره في ADR-0049. لكن حارس المدير القائم ما زال يعتمد عليه، فيبقى الفحص لازماً.
// تشخيصي بالكامل: أي فشل يعيد null ولا يمنع الإقلاع.
// ============================================================================
public static class DatabaseIsolation
{
    private const string Query = "SELECT CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'IsReadCommittedSnapshotOn'));";

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
        "قاعدة البيانات تعمل بـ READ_COMMITTED_SNAPSHOT. حارس \"آخر مدير\" (AccountStatusChanger) "
        + "يعتمد على قراءةٍ **قافلة** ليُسلسل المتسابقَين، وتحت اللقطات لا يقفل شيء فيمرّان معاً "
        + "ويُوقَف آخر مديرَين للمتجر — بلا خطأ ولا أثر. عطّلها لهذه القاعدة "
        + "(ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT OFF) أو حوّل ذلك الحارس إلى شكل العدّاد "
        + "في ADR-0049. حصص الخطط (TenantQuotaGuard) غير متأثّرة. — TD-68";
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
