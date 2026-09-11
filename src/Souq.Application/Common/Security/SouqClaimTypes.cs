namespace Souq.Application.Common.Security;

// أسماء المطالبات الخاصة بنا في التوكن — عقد بين المُصدِر (Infrastructure) والمحوّل (API).
// tid: المتجر الذي صدر له التوكن؛ يجب أن يطابق المتجر المحدَّد من المضيف وإلا 401 (ADR-0006).
public static class SouqClaimTypes
{
    public const string TenantId = "tid";
}
