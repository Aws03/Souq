using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Domain.Identity;

namespace Souq.Infrastructure.Services;

// ============================================================================
// يُصدر توكن الوصول (JWT) موقّعاً بـ HMAC-SHA256 — قصير العمر (15 دقيقة افتراضياً). المطالبات عقد
// صغير يعتمد عليه باقي النظام (ADR-0010): sub، الدور، tid (متجر الحساب — تربطه الـ API بالمضيف)،
// cid (ملف العميل إن وُجد)، وsstamp (ختم الأمان — تغيّره يُسقط التوكن فوراً). أسماء ClaimTypes
// الصريحة مع تعطيل إعادة التخطيط في الـ API لتطابق مضبوط بين ما نكتبه هنا وما نقرؤه هناك.
// ============================================================================
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _clock;

    public JwtTokenGenerator(IOptions<JwtSettings> settings, TimeProvider clock)
    {
        _settings = settings.Value; _clock = clock;
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    public (string Token, DateTime ExpiresAt) Generate(User user, int? customerId)
    {
        // حساب غير محفوظ، أو حساب متجر بلا متجر، لا يُصدَر له توكن: tid ما يربطه بمضيف متجره.
        if (user.Id == 0 || (!user.BelongsToPlatform && user.TenantId is null))
            throw new InvalidOperationException("لا يُصدَر توكن لحساب غير محفوظ في متجره أو في المنصّة.");

        var expiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(_settings.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new(SouqClaimTypes.SecurityStamp, user.SecurityStamp),
        };
        if (user.TenantId is int tenantId)
            claims.Add(new Claim(SouqClaimTypes.TenantId, tenantId.ToString(CultureInfo.InvariantCulture)));
        if (customerId is int id)
            claims.Add(new Claim(SouqClaimTypes.CustomerId, id.ToString(CultureInfo.InvariantCulture)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
