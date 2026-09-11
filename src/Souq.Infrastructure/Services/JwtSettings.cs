using System.Text;
using Microsoft.Extensions.Options;

namespace Souq.Infrastructure.Services;

// إعدادات التوكن. تُربط من قسم "Jwt" في التهيئة. المفتاح (Key) سرّ: يأتي من
// user-secrets/متغيرات بيئة لا من appsettings المرفوع.
public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";

    // توكن الوصول قصير العمر (ADR-0010): تسريبه يضرّ دقائق، والجلسة الطويلة في رمز التجديد.
    public int ExpiryMinutes { get; set; } = 15;

    // عمر جلسة رمز التجديد (يُدوَّر مع كل تجديد، ويسقط بتسجيل الخروج أو تغيير كلمة المرور).
    public int RefreshTokenDays { get; set; } = 30;
}

// ============================================================================
// يُتحقَّق من إعدادات التوكن عند الإقلاع قبل أي طلب (ADR-0020). مفتاح HS256 أقصر من 256 بت
// كان يقبله الإعداد ثم يرفضه IdentityModel عند أول تسجيل دخول (IDX10720) — فشل متأخر في
// الإنتاج بدل رفض واضح عند النشر. الرسائل تسمّي المفتاح ولا تطبع قيمته أبداً.
// ============================================================================
public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtSettings settings)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Key))
            failures.Add("Jwt:Key غير مضبوط — اضبطه في user-secrets أو متغيّرات البيئة (Jwt__Key).");
        else if (Encoding.UTF8.GetByteCount(settings.Key) < MinimumKeyBytes)
            failures.Add($"Jwt:Key أقصر من {MinimumKeyBytes} بايت (256 بت) — الحدّ الأدنى لتوقيع HS256.");
        if (string.IsNullOrWhiteSpace(settings.Issuer))
            failures.Add("Jwt:Issuer مطلوب.");
        if (string.IsNullOrWhiteSpace(settings.Audience))
            failures.Add("Jwt:Audience مطلوب.");
        if (settings.ExpiryMinutes is < 5 or > 60)
            failures.Add("Jwt:ExpiryMinutes (عمر توكن الوصول) يجب أن يكون بين 5 و60 دقيقة.");
        if (settings.RefreshTokenDays is < 1 or > 90)
            failures.Add("Jwt:RefreshTokenDays يجب أن يكون بين 1 و90 يوماً.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
