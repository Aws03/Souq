using Souq.Domain.Platform;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة لتجمّع Tenant (جدول منصّة بلا مرشّح مستأجر) — يُحمَّل بنطاقاته. في منطقة المتجر لا يُطلب إلا
// بمعرّف متجر السياق، لا بمعرّف من الطلب أبداً (الوصول لمتجر آخر مستحيل بالبناء).
public interface ITenantRepository : IRepository<Tenant>
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    // المضيف فريد على المنصّة كلها: نطاق متجر آخر لا يُسرق (القيد الفريد هو الحارس الأخير).
    Task<bool> HostTakenAsync(string host, CancellationToken ct = default);
}
