using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// استخدام كوبون لطلب (المرحلة 10، ADR-0030): سجلّ واحد لكل طلب طبّق كوبوناً — محجوز عند الإنشاء، مؤكَّد بالدفع، محرَّر
// بالإلغاء. الاستخدامات الفعّالة (محجوزة أو مؤكَّدة) هي ما يُعدّ لحدّ العميل، وعدّاد الكوبون نفسه يطابقها. الخصم المطبَّق
// لقطة لتقارير الإدارة.
// ============================================================================
public class CouponRedemption : Entity, ITenantOwned
{
    private CouponRedemption() { }

    public CouponRedemption(int couponId, int orderId, int customerId, Money discount)
    {
        if (couponId <= 0 || orderId <= 0 || customerId <= 0)
            throw new InvalidCouponException("استخدام الكوبون يحتاج كوبوناً وطلباً وعميلاً");
        CouponId = couponId;
        OrderId = orderId;
        CustomerId = customerId;
        Discount = discount;
        Status = CouponRedemptionStatus.Reserved;
    }

    public int TenantId { get; private set; }
    public int CouponId { get; private set; }
    public int OrderId { get; private set; }
    public int CustomerId { get; private set; }
    public Money Discount { get; private set; } = default!;
    public CouponRedemptionStatus Status { get; private set; }

    public bool IsActive => Status != CouponRedemptionStatus.Released;

    // الدفع يؤكّد الاستخدام (مضمون التكرار). استخدام حُرّر بإلغاء طلبه لا يعود.
    public void Confirm()
    {
        if (Status == CouponRedemptionStatus.Released)
            throw new InvalidCouponException("لا يُؤكَّد استخدام حُرّر بإلغاء طلبه");
        Status = CouponRedemptionStatus.Confirmed;
    }

    // true إن حُرّر الآن (فيُعاد الاستخدام للكوبون)، false إن كان محرَّراً أصلاً — تحرير مكرّر لا يعيد مرتين.
    public bool Release()
    {
        if (Status == CouponRedemptionStatus.Released) return false;
        Status = CouponRedemptionStatus.Released;
        return true;
    }
}
