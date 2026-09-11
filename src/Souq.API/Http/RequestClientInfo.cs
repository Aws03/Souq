using Souq.Application.Common.Auditing;

namespace Souq.API.Http;

// عنوان العميل لسطر التدقيق — بعد UseForwardedHeaders: الحقيقي خلف الوكيل الموثوق وحده؛ X-Forwarded-For من عميل
// مباشر لا يُصدَّق (Program.cs، ForwardedHeaders:KnownNetworks).
public sealed class RequestClientInfo : IClientInfo
{
    private readonly IHttpContextAccessor _accessor;
    public RequestClientInfo(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
