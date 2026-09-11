using Souq.Domain.Common;

namespace Souq.Domain.Entities;

// ============================================================================
// عدّاد أرقام الطلبات لكل متجر (المرحلة 9): صفّ واحد لكل متجر يحمل آخر رقم صدر. يزيده Infrastructure ذرّياً داخل معاملة
// إنشاء الطلب — قفل الصفّ حتى الالتزام يسلسل طلبات المتجر نفسه لحظةً ولا يمسّ متجراً آخر. الترقيم يبدأ بـ FirstNumber كي
// لا يكشف الرقم حجم متجر ناشئ. فجوة بعد معاملة أُلغيت مقبولة (الرقم فريد لا متّصل).
// ============================================================================
public class OrderNumberSequence : Entity, ITenantOwned
{
    public const int FirstNumber = 1001;

    private OrderNumberSequence() { }

    public int TenantId { get; private set; }
    public int LastNumber { get; private set; }

    // أول رقم لمتجر لم يُصدر طلباً بعد.
    public static OrderNumberSequence Start() => new() { LastNumber = FirstNumber };
}
