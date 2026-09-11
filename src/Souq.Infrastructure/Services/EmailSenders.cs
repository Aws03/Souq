namespace Souq.Infrastructure.Services;

// اسم المرسِل لكل متجر مع عنوان النشر المُتحقَّق منه لدى المزوّد (المرحلة 14). الاسم يضبطه مدير متجر فلا يكسر الترويسة: بلا
// محارف تحكّم ولا أقواس زاوية أو علامات اقتباس.
internal static class EmailSenders
{
    public static string CleanName(string? name, string fallback)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"')).ToArray()).Trim();
        return clean.Length == 0 ? fallback : clean;
    }

    // "Souq <onboarding@resend.dev>" أو "noreply@example.com" ⇒ "اسم المتجر <العنوان>".
    public static string WithDisplayName(string configuredFrom, string name)
    {
        var start = configuredFrom.IndexOf('<');
        var end = configuredFrom.LastIndexOf('>');
        var address = start >= 0 && end > start ? configuredFrom[(start + 1)..end].Trim() : configuredFrom.Trim();
        var fallback = start > 0 ? configuredFrom[..start].Trim() : "Souq";
        return $"{CleanName(name, fallback)} <{address}>";
    }
}
