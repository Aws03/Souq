using Souq.Application.Common.Exceptions;
using Souq.Application.Features.Coupons.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Coupons.Redemptions;

// ============================================================================
// تنفيذ ICouponRedemptions (المرحلة 10). كل حجز أو تحرير يغيّر عدّاد الكوبون، وrowversion عليه يسلسل طلبات الكوبون نفسه:
// الخاسر في سباق يُنسى ما حمّله ويُعاد من قراءة جديدة — فيرى العدّاد واستخدامات العميل كما التزمها الفائز، فإمّا يأخذ
// آخر استخدام أو يُرفض برسالة الكيان. نمط InventoryWriter نفسه (المرحلة 6): داخل معاملة المستدعي، ونقطة حفظ EF لكل حفظ.
// ============================================================================
public sealed class CouponRedemptions : ICouponRedemptions
{
    // كل تعارض يعني أن طلباً آخر حفظ على الكوبون نفسه — خمس محاولات تتجاوز أي سباق واقعي.
    public const int MaxAttempts = 5;

    private readonly ICouponRepository _coupons;
    private readonly ICouponRedemptionRepository _redemptions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public CouponRedemptions(ICouponRepository coupons, ICouponRedemptionRepository redemptions, IUnitOfWork uow, TimeProvider clock)
    {
        _coupons = coupons; _redemptions = redemptions; _uow = uow; _clock = clock;
    }

    public Task ReserveAsync(string couponCode, int orderId, int customerId, Money subtotal, Money discount, CancellationToken ct) =>
        SaveWithRetryAsync(async () =>
        {
            var coupon = await _coupons.GetByCodeAsync(couponCode, ct)
                         ?? throw new InvalidCouponException("رمز الكوبون غير صحيح");
            var customerUses = await _redemptions.CountActiveAsync(coupon.Id, customerId, ct);
            coupon.Redeem(subtotal, _clock.GetUtcNow().UtcDateTime, customerUses);
            await _redemptions.AddAsync(new CouponRedemption(coupon.Id, orderId, customerId, discount), ct);
        }, ct);

    public async Task ConfirmAsync(int orderId, CancellationToken ct) =>
        (await _redemptions.GetForOrderAsync(orderId, ct))?.Confirm();

    public Task ReleaseAsync(int orderId, CancellationToken ct) =>
        SaveWithRetryAsync(async () =>
        {
            var redemption = await _redemptions.GetForOrderAsync(orderId, ct);
            if (redemption is null || !redemption.Release()) return;
            (await _coupons.GetByIdAsync(redemption.CouponId, ct))?.ReleaseUse();
        }, ct);

    private async Task SaveWithRetryAsync(Func<Task> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await apply();
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                _redemptions.Reset();
            }
            catch
            {
                _redemptions.Reset();
                throw;
            }
        }
    }
}
