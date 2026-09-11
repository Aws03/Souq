using Souq.Application.Common.Interfaces;

namespace Souq.API.Http;

// ============================================================================
// روابط البريد على مضيف الطلب نفسه (مخطّط + مضيف + منفذ كما رآها المتصفّح): عميل المتجر B يعود إلى
// متجر B، ومستخدم المنصّة إلى المنصّة. آمن من تسميم الرابط بترويسة Host مزيّفة لأن المضيف يُتحقَّق منه
// مقابل TenantDomains قبل أي حالة استخدام (مضيف غريب ⇒ 404). المخطّط من X-Forwarded-Proto فقط خلف
// وكيل موثوق (ForwardedHeaders). بلا طلب HTTP (مهمة خلفية لاحقاً) ⇒ عنوان الواجهة من الإعداد.
// ============================================================================
public sealed class RequestStorefrontLinks : IStorefrontLinks
{
    private readonly IHttpContextAccessor _accessor;
    private readonly string _fallbackBase;

    public RequestStorefrontLinks(IHttpContextAccessor accessor, IConfiguration configuration)
    {
        _accessor = accessor;
        _fallbackBase = configuration["FRONTEND_URL"] ?? configuration["App:FrontendUrl"] ?? "http://localhost:5173";
    }

    public string PasswordReset(string token) => Build("/reset-password", token);

    public string EmailVerification(string token) => Build("/verify-email", token);

    // host صريح (نطاق متجر من TenantDomains، لا من الطلب) حين تدعو المنصّة مديره: المخطّط والمنفذ من الطلب نفسه
    // (http://admin.localhost:5173 ⇒ http://acme.localhost:5173 في التطوير، و https بلا منفذ في الإنتاج).
    public string Invitation(string token, string? host = null) => Build("/accept-invitation", token, host);

    private string Build(string path, string token, string? host = null)
    {
        var request = _accessor.HttpContext?.Request;
        string origin;
        if (request is null) origin = host is null ? _fallbackBase : $"https://{host}";
        else if (host is null) origin = $"{request.Scheme}://{request.Host}";
        else origin = $"{request.Scheme}://{host}{(request.Host.Port is int port ? $":{port}" : "")}";
        return $"{origin.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";
    }
}
