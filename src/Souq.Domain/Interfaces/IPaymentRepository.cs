using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ كتابة الدفعات (المرحلة 11): دفعة واحدة لكل طلب، باستردادها.
public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetForOrderAsync(int orderId, CancellationToken ct = default);

    // الحساب الذي أنشأ نيّة دفع — ليمرّ تأكيدها وإلغاؤها واستردادها بالحساب نفسه. null ⇒ نيّة لا دفعة لها في هذا المتجر.
    Task<string?> GetGatewayAsync(string providerPaymentId, CancellationToken ct = default);

    // ينسى الدفعات والاستردادات المحمَّلة بعد تعارض — المحاولة التالية تقرأ من جديد.
    void Reset();
}
