using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Orders.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// OrderQueries — تنفيذ IOrderQueries (ADR-0008).
//   القوائم: صف SQL واحد لكل طلب من الإجماليات المثبَّتة (المرحلة 9 — لا جمع أسطر) واسم العميل باستعلام مرتبط، بمرشّحات
//            الحالة والبحث والتاريخ.
//   التفاصيل: طلب واحد بأسطره وسجلّه بلا تتبّع، وإجمالياته من قواعد المجال نفسها؛ أسماء الموظّفين من الحسابات.
//   التتبّع: بالرمز العشوائي، بالحدّ الأدنى من الحقول.
// ============================================================================
internal sealed class OrderQueries : IOrderQueries
{
    private readonly AppDbContext _db;
    public OrderQueries(AppDbContext db) => _db = db;

    public Task<PaginatedList<OrderSummaryDto>> ListAsync(OrderFilter filter, PageRequest page, CancellationToken ct) =>
        Summaries(Filtered(filter), page, ct);

    public Task<PaginatedList<OrderSummaryDto>> ListForCustomerAsync(int customerId, PageRequest page, CancellationToken ct) =>
        Summaries(Filtered(new OrderFilter(CustomerId: customerId)), page, ct);

    public async Task<OrderDto?> FindAsync(int id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.StatusHistory).AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        var staffIds = order.StatusHistory
            .Where(h => h.ChangedBy == OrderActorKind.Staff && h.ChangedByUserId is not null)
            .Select(h => h.ChangedByUserId!.Value).Distinct().ToList();
        var staffNames = staffIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return new OrderDto(
            order.Id, order.OrderNumber, order.CustomerId, order.Status.ToString(), order.ShippingAddress, order.BillingAddress,
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.CouponCode, order.TotalAmount.Amount, order.Currency,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity, i.LineTotal.Amount)).ToList(),
            order.TrackingNumber, order.ShippingCarrier, order.TrackingToken,
            Chronological(order).Select(h => new OrderHistoryEntryDto(
                h.Status.ToString(), h.CreatedAt, h.Note, h.ChangedBy.ToString(),
                h.ChangedByUserId is int userId && staffNames.TryGetValue(userId, out var name) ? name : null)).ToList(),
            [], false,
            ShippingMethod: order.ShippingMethodName, ShippingCost: order.ShippingAmount, ShippingMinDays: order.ShippingMinDays,
            ShippingMaxDays: order.ShippingMaxDays, ShippingCountry: order.ShippingCountry, TrackingUrl: order.TrackingUrl);
    }

    public async Task<OrderTrackingDto?> FindTrackingAsync(string token, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.TrackingToken == token, ct);
        if (order is null) return null;

        return new OrderTrackingDto(
            order.OrderNumber, order.Status.ToString(), order.TrackingNumber, order.ShippingCarrier, order.CreatedAt,
            Chronological(order).Select(h => new OrderTrackingStepDto(h.Status.ToString(), h.CreatedAt)).ToList(),
            order.TrackingUrl);
    }

    // ترتيب زمني تصاعدي يطابق الخط الزمني في الواجهة؛ المعرّف يكسر تعادل انتقالين حُفظا في اللحظة نفسها.
    private static IEnumerable<OrderStatusHistory> Chronological(Order order) =>
        order.StatusHistory.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id);

    private IQueryable<Order> Filtered(OrderFilter filter)
    {
        var orders = _db.Orders.AsNoTracking();
        if (filter.CustomerId is int customerId) orders = orders.Where(o => o.CustomerId == customerId);
        if (filter.Status is OrderStatus status) orders = orders.Where(o => o.Status == status);
        if (filter.From is DateTime from) orders = orders.Where(o => o.CreatedAt >= from);
        if (filter.To is DateTime to) orders = orders.Where(o => o.CreatedAt < to);

        var term = filter.Search?.Trim().TrimStart('#');
        if (!string.IsNullOrEmpty(term))
            orders = int.TryParse(term, out var number)
                ? orders.Where(o => o.OrderNumber == number)
                : orders.Where(o => _db.Customers.Any(c => c.Id == o.CustomerId && (c.FullName.Contains(term) || c.Email.Contains(term))));
        return orders;
    }

    // الأحدث أولاً؛ المعرّف يكسر تعادل طلبين في اللحظة نفسها.
    private async Task<PaginatedList<OrderSummaryDto>> Summaries(IQueryable<Order> orders, PageRequest page, CancellationToken ct)
    {
        var rows = await orders
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .ToPageAsync(o => new SummaryRow(
                o.Id, o.OrderNumber, o.CustomerId,
                _db.Customers.Where(c => c.Id == o.CustomerId).Select(c => c.FullName).FirstOrDefault(),
                o.Status, o.PlacedTotal, o.Currency, o.CreatedAt, o.Items.Count()), page, ct);

        return rows.Map(r => new OrderSummaryDto(
            r.Id, r.OrderNumber, r.CustomerId, r.CustomerName, r.Status.ToString(), r.Total, r.Currency, r.CreatedAt, r.ItemCount));
    }

    private sealed record SummaryRow(
        int Id, int OrderNumber, int CustomerId, string? CustomerName, OrderStatus Status, decimal Total, string Currency,
        DateTime CreatedAt, int ItemCount);
}
