namespace Souq.Domain.Platform;

// دورة حياة المتجر (Architecture.md §7): Provisioning ← Active ⇄ Suspended ← Archived.
//   Provisioning — أنشأه مالك المنصّة ولم يُفتح للزوّار بعد (الإدارة تجهّزه).
//   Active       — يعمل للجميع.
//   Suspended    — موقوف مؤقتاً (عقد، دفع، إساءة): المتجر يعرض "غير متاح".
//   Archived     — مُنهى نهائياً؛ بياناته محفوظة للتصدير لكنه لا يعود للعمل.
public enum TenantStatus
{
    Provisioning = 0,
    Active = 1,
    Suspended = 2,
    Archived = 3,
}
