using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Auditing;

namespace Souq.Application.Common.Behaviors;

// ============================================================================
// AuditBehavior — سجلّ التدقيق لكل طلب IAuditable (D-17) بعد التحقّق وقبل المعالج:
//   • من؟ الحساب ودوره من ICurrentUser (التوكن)، والمنطقة من نطاق المستأجر، والعنوان من IClientInfo.
//   • السطر يُدرج قبل المعالج فيُحفظ ذرّياً مع أول حفظ له؛ فشل النتيجة أو استثناء ⇒ يُسقط.
//   • طلب مرفوض بالتحقّق لا يصل هنا (ValidationBehavior قبله) — لا ضجيج من مدخلات فاسدة.
// غير مقيَّد بقيد عام على TRequest عمداً: حاوية DI تبني السلوك لكل طلب، والفحص هنا صريح ورخيص.
// ============================================================================
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    private readonly IAuditTrail _trail;
    private readonly ICurrentUser _user;
    private readonly ITenantContext _tenancy;
    private readonly IClientInfo _client;
    private readonly TimeProvider _clock;

    public AuditBehavior(IAuditTrail trail, ICurrentUser user, ITenantContext tenancy, IClientInfo client, TimeProvider clock)
    {
        _trail = trail; _user = user; _tenancy = tenancy; _client = client; _clock = clock;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IAuditable auditable) return await next();

        var record = auditable.ToAuditRecord();
        _trail.Stage(new AuditEntry(
            _clock.GetUtcNow().UtcDateTime, Area(), record.Action,
            record.TenantId ?? _tenancy.Tenant?.Id,
            _user.UserId, _user.Roles.FirstOrDefault(),
            record.TargetType, record.TargetId,
            record.Metadata is { Count: > 0 } metadata ? JsonSerializer.Serialize(metadata, MetadataJson) : null,
            _client.IpAddress, Activity.Current?.TraceId.ToString()));

        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            _trail.Discard();
            throw;
        }

        if (response is IResultStatus { IsSuccess: false })
        {
            _trail.Discard();
            return response;
        }

        await _trail.FlushAsync(ct);
        return response;
    }

    private string Area() => _tenancy.Scope switch
    {
        TenantScope.Platform => AuditAreas.Platform,
        TenantScope.Tenant => AuditAreas.Store,
        _ => AuditAreas.System,
    };
}
