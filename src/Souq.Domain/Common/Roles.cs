namespace Souq.Domain.Common;

// ============================================================================
// أدوار الحسابات (ADR-0010). ثوابت نصّية عمداً لا enum: مطالبات JWT وتفويض ASP.NET Core نصّية في
// جوهرها، والثوابت تمنع الخطأ الإملائي — وهو الخطر الفعلي — وتبقى المصدر الوحيد للأسماء.
// عالمان منفصلان لا يعبر حساب بينهما: أدوار المنصّة (بلا متجر) وأدوار المتجر (داخل متجر واحد).
// ماذا يستطيع كل دور = جدول RolePermissions في Application، لا مقارنة أسماء أدوار في الكود.
// ============================================================================
public static class Roles
{
    public const string PlatformOwner = "PlatformOwner";   // المنصّة كلها بما فيها إعداداتها ومستخدموها
    public const string PlatformAdmin = "PlatformAdmin";   // تشغيل المتاجر نيابةً عن المالك
    public const string TenantAdmin = "TenantAdmin";       // مالك المتجر: كل شيء داخل متجره
    public const string TenantStaff = "TenantStaff";       // موظّف: تشغيل يومي بلا إعدادات ولا موظّفين
    public const string Customer = "Customer";             // يتسوّق: بياناته فقط (ملكية لا صلاحيات)

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { PlatformOwner, PlatformAdmin, TenantAdmin, TenantStaff, Customer };

    public static bool IsPlatform(string role) => role is PlatformOwner or PlatformAdmin;

    public static bool IsStoreStaff(string role) => role is TenantAdmin or TenantStaff;
}
