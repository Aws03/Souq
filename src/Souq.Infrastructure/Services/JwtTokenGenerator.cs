using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Services;

// ============================================================================
// يُصدر توكن JWT موقّعاً بـ HMAC-SHA256. المطالبات (claims) تحمل هوية المستخدم
// ودوره — كي تفكّها طبقة الـ API وتفرض الصلاحيات دون استعلام قاعدة البيانات.
// نستخدم أسماء مطالبات ClaimTypes الصريحة (مع تعطيل إعادة التخطيط في الـ API)
// لتطابق مضبوط بين ما نكتبه هنا وما نقرؤه هناك.
// ============================================================================
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;
    private readonly TimeProvider _clock;

    public JwtTokenGenerator(IOptions<JwtSettings> settings, TimeProvider clock)
    {
        _settings = settings.Value; _clock = clock;
    }

    public (string Token, DateTime ExpiresAt) Generate(Customer customer)
    {
        // حساب بلا متجر لا يُصدَر له توكن: tid هو ما يربط التوكن بمضيف متجره (TenantTokenBinding).
        if (customer.TenantId == 0)
            throw new InvalidOperationException("لا يُصدَر توكن لحساب غير محفوظ في متجر.");

        var expiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(_settings.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(SouqClaimTypes.TenantId, customer.TenantId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Email, customer.Email),
            new Claim(ClaimTypes.Name, customer.FullName),
            new Claim(ClaimTypes.Role, customer.Role),
        };

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
