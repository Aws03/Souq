using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ كتابة الدفعات (المرحلة 11): دفعة واحدة لكل طلب، باستردادها.
public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetForOrderAsync(int orderId, CancellationToken ct = default);

    // الحساب الذي أنشأ نيّة دفع — ليمرّ تأكيدها وإلغاؤها واستردادها بالحساب نفسه. null ⇒ نيّة لا دفعة لها في هذا المتجر.
    // **النوعُ والهويّة معاً** (TD-50): النوعُ يختار المسار، والهويّةُ تمنع تنفيذه على حسابٍ آخر من النوع نفسه.
    Task<PaymentAccountRef?> GetAccountAsync(string providerPaymentId, CancellationToken ct = default);

    // ينسى الدفعات والاستردادات المحمَّلة بعد تعارض — المحاولة التالية تقرأ من جديد.
    void Reset();
}

// نوعُ الحساب وهويّتُه كما سُجّلا على الدفعة. `Account` قد تكون null لدفعةٍ كُتبت قبل TD-50
// أو أخذتها البوّابة التجريبية — وتلك تبقى على سلوكها السابق.
public sealed record PaymentAccountRef(string Kind, string? Account);
