using AwesomeAssertions;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Services;

namespace Souq.IntegrationTests;

// تحويل المبلغ لأصغر وحدة يتوقّعها Stripe — خطأ هنا يعني تحصيل مبلغ مختلف عن الطلب.
// (اختبار وحدة خالص؛ يعيش هنا لأنه المشروع الوحيد الذي يرى طبقة Infrastructure.)
public class StripeAmountConverterTests
{
    [Theory]
    [InlineData(10.99, "USD", 1099)]
    [InlineData(500, "JPY", 500)]      // عديمة الخانات: كما هي
    [InlineData(5, "ISK", 500)]        // استثناء Stripe: ×100 للتوافق الخلفي
    [InlineData(59.9, "JOD", 5990)]    // الدينار يُعامَل ثنائياً حسب الوثائق (P-05 للتحقّق)
    [InlineData(59.955, "JOD", 5996)]  // تقريب تجاري لأقرب 0.01
    public void يحوّل_لأصغر_وحدة_حسب_قواعد_Stripe(decimal amount, string currency, long expected)
    {
        StripeAmountConverter.ToMinorUnits(new Money(amount, currency)).Should().Be(expected);
    }
}
