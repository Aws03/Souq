using Souq.Domain.Auditing;

namespace Souq.Application.Common.Auditing;

// ============================================================================
// طلب (أمر أو استعلام) يُسجَّل في سجلّ التدقيق (D-17، Security.md §10). الطلب نفسه يصف ما يُسجَّل — الفعل
// والهدف والبيانات الوصفية الآمنة حقلاً حقلاً — ولا يُنسخ جسمه أبداً (قد يحمل كلمات مرور ورموزاً).
// كل طلب في منطقة المنصّة مُدقَّق (اختبار معماري)، وأوامر المكتب الخلفي للمتجر كذلك.
// ============================================================================
public interface IAuditable
{
    AuditRecord ToAuditRecord();
}

// Action بصيغة "tenant.created" (أحرف صغيرة ونقاط). TenantId: المتجر المتأثّر حين يعمل الطلب من المنصّة؛
// داخل متجر يؤخذ من السياق تلقائياً. الهدف عند الإنشاء مفتاحه الطبيعي (slug، بريد) — المعرّف لم يولَد بعد.
public sealed record AuditRecord(
    string Action, string? TargetType = null, string? TargetId = null, int? TenantId = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

// ============================================================================
// منفذ سجلّ التدقيق (Infrastructure): السطر يُدرج في وحدة العمل الحالية قبل تنفيذ الطلب، فيُحفظ في المعاملة
// نفسها مع التغيير — لا تغيير بلا سطر ولا سطر بلا تغيير. طلب لم يحفظ شيئاً (استعلام، أمر بلا أثر) يحفظ
// سطره FlushAsync بعد النجاح؛ الفشل يُسقطه (Discard).
// ============================================================================
public interface IAuditTrail
{
    void Stage(AuditEntry entry);
    Task FlushAsync(CancellationToken ct);
    void Discard();
}

// عنوان العميل كما رآه الخادم خلف الوكيل الموثوق (ForwardedHeaders) — لسطر التدقيق وحده.
public interface IClientInfo
{
    string? IpAddress { get; }
}
