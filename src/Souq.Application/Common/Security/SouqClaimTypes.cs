namespace Souq.Application.Common.Security;

// أسماء المطالبات الخاصة بنا في التوكن — عقد بين المُصدِر (Infrastructure) والمحوّل (API).
//   tid    — المتجر الذي صدر له التوكن؛ يجب أن يطابق المتجر المحدَّد من المضيف وإلا 401 (ADR-0006).
//   cid    — ملف العميل في هذا المتجر (حسابات بلا ملف شراء لا تحملها).
//   sstamp — ختم أمان الحساب لحظة الإصدار؛ تغيّره (كلمة مرور، تعطيل، سرقة) يُسقط التوكن فوراً.
public static class SouqClaimTypes
{
    public const string TenantId = "tid";
    public const string CustomerId = "cid";
    public const string SecurityStamp = "sstamp";
}
