using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Souq.Application.Common.Notifications;
using Souq.Infrastructure.Persistence;

namespace Souq.Infrastructure.Notifications;

// أصل واجهة متجر لرسائل بلا طلب HTTP خلفها (المرحلة 14): نطاقه الأساسي بمخطّط عنوان الواجهة ومنفذه — http://acme.localhost:5173
// في التطوير، https://acme.example في الإنتاج. متجر بلا نطاق ⇒ عنوان الواجهة نفسه.
internal sealed class StoreOrigins : IStoreOrigins
{
    private readonly AppDbContext _db;
    private readonly Uri _frontend;

    public StoreOrigins(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _frontend = new Uri(configuration["FRONTEND_URL"] ?? configuration["App:FrontendUrl"] ?? "http://localhost:5173");
    }

    public async Task<string> ForStoreAsync(int tenantId, CancellationToken ct)
    {
        var host = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .SelectMany(t => t.Domains)
            .Where(d => d.IsPrimary)
            .Select(d => d.Host)
            .FirstOrDefaultAsync(ct);

        if (host is null) return _frontend.GetLeftPart(UriPartial.Authority);
        return _frontend.IsDefaultPort ? $"{_frontend.Scheme}://{host}" : $"{_frontend.Scheme}://{host}:{_frontend.Port}";
    }
}
