using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Notifications;

namespace Souq.Infrastructure.Persistence.Queries;

// إشعارات حساب في متجر السياق (المرحلة 14، ADR-0008): الأحدث أولاً، والبيانات JSON صغير يُفكّ لقاموس نصوص للواجهة.
internal sealed class NotificationQueries : INotificationQueries
{
    private readonly AppDbContext _db;
    public NotificationQueries(AppDbContext db) => _db = db;

    public async Task<PaginatedList<NotificationDto>> ListForRecipientAsync(
        int userId, bool unreadOnly, PageRequest page, CancellationToken ct)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .ToPageAsync(n => new Row(n.Id, n.Kind, n.Data, n.CreatedAt, n.ReadAt != null), page, ct);

        return new PaginatedList<NotificationDto>(
            rows.Items.Select(r => new NotificationDto(r.Id, r.Kind, Parse(r.Data), r.CreatedAt, r.IsRead)).ToList(),
            rows.TotalCount, page.Page, page.PageSize);
    }

    public Task<int> CountUnreadAsync(int userId, CancellationToken ct) =>
        _db.Notifications.CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct);

    private static IReadOnlyDictionary<string, string> Parse(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    private sealed record Row(int Id, string Kind, string Data, DateTime CreatedAt, bool IsRead);
}
