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

    // ============================================================================
    // المتاجر التي تمرّ عليها المهام الخلفية، بلا ذاكرة مؤقتة (المرحلة 6: انتهاء مهلة الدفع، وتنظيف السلال).
    //
    // **النشط والموقوف معاً، لا النشط وحده** (R-24، قُرِّر في M5). متجر موقوف لا يستطيع متسوّقوه إتمام أي دفع —
    // البوّابة تُغلق عليهم — فحجوزات مخزونه المنتهية لا يُصفّيها شيء أبداً ما دام خارج المسح: يبقى المخزون
    // محجوزاً بلا طلب يكمل، ويظهر الخطأ عند إعادة التفعيل لا عند الإيقاف. والتنظيف نفسه ينطبق على سلاله.
    //
    // المُهيَّأ (Provisioning) والمؤرشف (Archived) خارج المسح عمداً: الأول لم يخدم متسوّقاً بعد فلا شيء لديه
    // ينتهي، والثاني نهائي — فتعديل بياناته عملٌ على سجلّ مغلق لا صيانة.
    // ============================================================================
    Task<IReadOnlyList<TenantInfo>> ListForBackgroundSweepsAsync(CancellationToken ct = default);

    void Invalidate();
}
