namespace Souq.Infrastructure.Services;

// إعدادات التوكن. تُربط من قسم "Jwt" في التهيئة. المفتاح (Key) سرّ: يأتي من
// user-secrets/متغيرات بيئة لا من appsettings المرفوع.
public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 120;
}
