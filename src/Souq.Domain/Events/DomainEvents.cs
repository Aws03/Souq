using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Domain.Events;

// ============================================================================
// أحداث المجال (المرحلة 14، ADR-0034): حقائق حدثت في تجمّع ولها أكثر من مستهلك (بريد العميل، تنبيه الإدارة). الكيان يرفعها
// ولا يعرف من يستهلكها؛ وحدة العمل تكتبها في صندوق الصادر مع التغيير نفسه في حفظ واحد — فلا يضيع إشعار ولا يُرسل لتغيير
// تراجع. تحمل معرّفات وقيماً صغيرة فقط: المستهلك يقرأ الحالة الحيّة عند المعالجة، ولا بيانات شخصية في الصندوق.
// ============================================================================
public interface IDomainEvent { }

// انتقال حالة طلب قائم (دفع، شحن، تسليم، إلغاء). From وBy يقرّران ما يستحقّ بريداً (إلغاء طلب مدفوع، أو بيد المتجر).
public sealed record OrderStatusChanged(int OrderId, int CustomerId, OrderStatus From, OrderStatus To, OrderActorKind By) : IDomainEvent;

// نزل المتاح لمتغيّر عن حدّ التنبيه (عبورٌ لا بقاء): تنبيه واحد لكل هبوط، لا لكل بيع بعده.
public sealed record StockBecameLow(int ProductId, int VariantId, int Available, int Threshold) : IDomainEvent;
