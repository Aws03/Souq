using System.Text.RegularExpressions;

namespace Souq.Infrastructure.Services;

// ============================================================================
// تنقيح البيانات الحسّاسة قبل السجل (Security.md §9). السجلات تُنسخ وتُشارَك
// وتُحفَظ طويلاً — لا رموز ولا روابط ولا عناوين بريد كاملة فيها أبداً.
// ============================================================================
internal static partial class LogRedaction
{
    // a***@example.com — يكفي لربط شكوى دعم بسجلّها، ولا يكفي لجمع العناوين.
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(none)";
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    // عناوين بريد داخل نصّ حرّ (جسم خطأ مزوّد قد يردّد المستلم) ⇒ مُقنَّعة كلها (المرحلة 14).
    public static string MaskEmails(string? text) =>
        string.IsNullOrEmpty(text) ? "" : EmailPattern().Replace(text, m => MaskEmail(m.Value));

    // أجسام أخطاء المزوّدين مفيدة للتشخيص لكنها قد تطول أو تحمل صدى المدخلات.
    public static string Truncate(string? text, int max = 500) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"[^\s""'<>,;:()\[\]]+@[^\s""'<>,;:()\[\]]+")]
    private static partial Regex EmailPattern();
}
