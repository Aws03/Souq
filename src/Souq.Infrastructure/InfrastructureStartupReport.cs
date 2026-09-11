using Souq.Infrastructure.Services;

namespace Souq.Infrastructure;

// ============================================================================
// ما قرّره AddInfrastructure عند الإقلاع، وما يستحق تنبيه المشغّل. لا مُسجِّل متاح أثناء تسجيل
// الخدمات، فيُجمع هنا ويُسجَّل مرة واحدة في Program بعد البناء. تحذير = إعداد يعمل لكنه غير
// مناسب لزبائن حقيقيين (بوّابة تجريبية، بريد لا يُرسَل، Webhook غير مضبوط). القيم السرّية لا
// تُكتب هنا أبداً — أسماء المفاتيح فقط.
// ============================================================================
public sealed class InfrastructureStartupReport
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;
    public PaymentProvider PaymentProvider { get; internal set; }
    public string EmailProvider { get; internal set; } = "";

    internal void Warn(string message) => _warnings.Add(message);
}
