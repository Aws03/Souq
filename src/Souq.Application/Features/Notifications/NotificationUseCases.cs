using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Notifications;

// ============================================================================
// إشعارات الحساب الحالي داخل التطبيق (المرحلة 14): قائمته، عدد غير المقروء (للشارة — الواجهة تسأله دورياً)، وتعليم المقروء.
// المستلم هو المستخدم الحالي دائماً — لا معرّف حساب في أي طلب؛ إشعار حساب آخر أو متجر آخر ⇒ 404.
// ============================================================================

public sealed record NotificationDto(int Id, string Kind, IReadOnlyDictionary<string, string> Data, DateTime CreatedAt, bool IsRead);

public interface INotificationQueries
{
    Task<PaginatedList<NotificationDto>> ListForRecipientAsync(int userId, bool unreadOnly, PageRequest page, CancellationToken ct);

    Task<int> CountUnreadAsync(int userId, CancellationToken ct);
}

public record ListMyNotificationsQuery(bool UnreadOnly = false, int Page = 1, int PageSize = 10)
    : IRequest<PaginatedList<NotificationDto>>, IPagedQuery;

public sealed class ListMyNotificationsValidator : PagedQueryValidator<ListMyNotificationsQuery>;

public class ListMyNotificationsHandler : IRequestHandler<ListMyNotificationsQuery, PaginatedList<NotificationDto>>
{
    private readonly INotificationQueries _queries;
    private readonly ICurrentUser _currentUser;

    public ListMyNotificationsHandler(INotificationQueries queries, ICurrentUser currentUser)
    {
        _queries = queries; _currentUser = currentUser;
    }

    public Task<PaginatedList<NotificationDto>> Handle(ListMyNotificationsQuery q, CancellationToken ct) =>
        _queries.ListForRecipientAsync(_currentUser.RequireUserId(), q.UnreadOnly, PageRequest.From(q), ct);
}

public record CountMyUnreadNotificationsQuery : IRequest<int>;

public class CountMyUnreadNotificationsHandler : IRequestHandler<CountMyUnreadNotificationsQuery, int>
{
    private readonly INotificationQueries _queries;
    private readonly ICurrentUser _currentUser;

    public CountMyUnreadNotificationsHandler(INotificationQueries queries, ICurrentUser currentUser)
    {
        _queries = queries; _currentUser = currentUser;
    }

    public Task<int> Handle(CountMyUnreadNotificationsQuery q, CancellationToken ct) =>
        _queries.CountUnreadAsync(_currentUser.RequireUserId(), ct);
}

public record MarkNotificationReadCommand(int Id) : IRequest<Result>;

public class MarkNotificationReadHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public MarkNotificationReadHandler(
        INotificationRepository notifications, ICurrentUser currentUser, IUnitOfWork uow, TimeProvider clock)
    {
        _notifications = notifications; _currentUser = currentUser; _uow = uow; _clock = clock;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand cmd, CancellationToken ct)
    {
        var notification = await _notifications.FindForRecipientAsync(cmd.Id, _currentUser.RequireUserId(), ct);
        if (notification is null)
            return Result.Failure(Error.NotFound("الإشعار غير موجود"));

        notification.MarkRead(_clock.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public record MarkAllNotificationsReadCommand : IRequest<Result>;

public class MarkAllNotificationsReadHandler : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public MarkAllNotificationsReadHandler(INotificationRepository notifications, ICurrentUser currentUser, TimeProvider clock)
    {
        _notifications = notifications; _currentUser = currentUser; _clock = clock;
    }

    public async Task<Result> Handle(MarkAllNotificationsReadCommand cmd, CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(_currentUser.RequireUserId(), _clock.GetUtcNow().UtcDateTime, ct);
        return Result.Success();
    }
}
