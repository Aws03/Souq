namespace Souq.Application.Common.Tenancy;

// ============================================================================
// ITenantDirectory — دليل المتاجر (عقد وحدة Platform في Modules.md): من المضيف أو المعرّف إلى
// لقطة المتجر. يُسأل مع كل طلب، لذا التنفيذ مخزَّن مؤقتاً (Infrastructure) بمفاتيح تحمل المضيف
// أو المتجر؛ وكل تغيير في متجر (المرحلة 4) يستدعي Invalidate كي لا يُخدَم متجر موقوف من الذاكرة.
// ============================================================================
public interface ITenantDirectory
{
    Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct = default);
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default);
    Task<TenantInfo?> FindByIdAsync(int tenantId, CancellationToken ct = default);
    void Invalidate();
}
