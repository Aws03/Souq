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
