using NSubstitute;
using Souq.Application.Common.Interfaces;

namespace Souq.Application.Tests;

// ============================================================================
// بديلُ منفذ الدفع، **بقدراتٍ مضبوطة**.
//
// `Substitute.For<T>` يعيد `null` لكلّ دالّةٍ لم تُضبط، و`GetCapabilitiesAsync` تُنادى الآن في
// مسارَي بدء الدفع والاسترداد (ADR-0048 §5) — فبديلٌ خامٌ يُسقط الاختبار بـ`NullReference` في
// شيفرةٍ صحيحة، وهو أسوأُ أنواع الفشل: يوجّه القارئ إلى المنتج والعيبُ في التجهيز.
//
// والافتراضُ «مزوّدٌ بلا قيود»، وهو بالضبط السلوك الذي كان قبل إعلان القدرات — فاختبارٌ لا يعني
// بالقدرات لا يتغيّر معناه. ومَن يقصدها يضبطها في اختباره.
// ============================================================================
internal static class PaymentServiceFake
{
    public static IPaymentService Create()
    {
        var gateway = Substitute.For<IPaymentService>();
        gateway.GetCapabilitiesAsync(Arg.Any<CancellationToken>()).Returns(new PaymentCapabilities());
        return gateway;
    }
}
