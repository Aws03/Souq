using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Features.Orders.Queries;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// OrderQueries — تنفيذ IOrderQueries (ADR-0008).
//   القوائم: صف SQL واحد لكل طلب بمجاميع محسوبة في القاعدة (مجموع الأسطر، عددها، الخصم)
//            — كانت تحمّل كل أسطر كل طلب في الصفحة لتعدّها فقط.
//   التفاصيل: طلب واحد بأسطره بلا تتبّع، وإجمالياته من قواعد المجال نفسها (Order.Subtotal/
//            TotalAmount) — لا نسخة ثانية من قاعدة الحساب لعرض طلب واحد.
// الإجماليات لا تُخزَّن بعد؛ المرحلة 9 تجمّدها أعمدةً على الطلب فتسقط حسابات القوائم هنا.
// ============================================================================
internal sealed class OrderQueries : IOrderQueries
{
    private readonly AppDbContext _db;
    public OrderQueries(AppDbContext db) => _db = db;

    public Task<PaginatedList<OrderSummaryDto>> ListAsync(PageRequest page, CancellationToken ct) =>
        Summaries(_db.Orders.AsNoTracking(), page, ct);

    public Task<PaginatedList<OrderSummaryDto>> ListForCustomerAsync(int customerId, PageRequest page, CancellationToken ct) =>
        Summaries(_db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId), page, ct);

    public async Task<OrderDto?> FindAsync(int id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        return new OrderDto(
            order.Id, order.CustomerId, order.Status.ToString(), order.ShippingAddress,
            order.Subtotal.Amount, order.DiscountAmount?.Amount, order.CouponCode,
            order.TotalAmount.Amount, order.TotalAmount.Currency, order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId, i.ProductName, i.UnitPrice.Amount, i.Quantity, i.LineTotal.Amount)).ToList(),
            order.TrackingNumber, order.ShippingCarrier);
    }

    public async Task<OrderTrackingDto?> FindTrackingAsync(int id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;

        // ترتيب زمني تصاعدي يطابق الخط الزمني في الواجهة؛ المعرّف يكسر تعادل انتقالين حُفظا
        // في اللحظة نفسها ضمن معاملة واحدة.
        var history = order.StatusHistory
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Select(h => new OrderStatusHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt))
            .ToList();

        return new OrderTrackingDto(
            order.Id, order.Status.ToString(), order.TrackingNumber, order.ShippingCarrier, order.CreatedAt, history);
    }

    // الأحدث أولاً؛ المعرّف يكسر تعادل طلبين في اللحظة نفسها.
    private static async Task<PaginatedList<OrderSummaryDto>> Summaries(
        IQueryable<Order> orders, PageRequest page, CancellationToken ct)
    {
        var rows = await orders
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .ToPageAsync(o => new SummaryRow(
                o.Id, o.CustomerId, o.Status, o.CreatedAt,
                o.Items.Sum(i => i.UnitPrice.Amount * i.Quantity),
                (decimal?)o.DiscountAmount!.Amount,
                o.Items.Select(i => i.UnitPrice.Currency).FirstOrDefault(),
                o.Items.Count()), page, ct);

        return rows.Map(r => new OrderSummaryDto(
            r.Id, r.CustomerId, r.Status.ToString(),
            r.Subtotal - (r.Discount ?? 0m), r.Currency ?? Money.DefaultCurrency,
            r.CreatedAt, r.ItemCount));
    }

    private sealed record SummaryRow(
        int Id, int CustomerId, OrderStatus Status, DateTime CreatedAt,
        decimal Subtotal, decimal? Discount, string? Currency, int ItemCount);
}
