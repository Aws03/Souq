using Microsoft.EntityFrameworkCore;
using Souq.Application.Common.Models;
using Souq.Application.Common.Tenancy;
using Souq.Application.Features.Customers;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ ICustomerQueries (ADR-0008) — إسقاطات بلا تتبّع مُرشَّحة بالمتجر. أرقام الطلبات (العدد، الإنفاق، آخر طلب) قراءة
// مركّبة من جداول Ordering هنا في Infrastructure: وحدة Customers لا تعتمد على Ordering في طبقة Application. الإنفاق
// مجموع أسطر الطلبات المدفوعة فما بعدها ناقص خصوماتها — استعلامان منفصلان لأن SQL Server لا يجمع فوق استعلام فرعي مجمَّع.
// ============================================================================
internal sealed class CustomerQueries : ICustomerQueries
{
    private static readonly OrderStatus[] Settled = [OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered];

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public CustomerQueries(AppDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    public Task<PaginatedList<CustomerListItemDto>> ListAsync(CustomerSearch search, PageRequest page, CancellationToken ct)
    {
        var customers = _db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var keyword = search.Keyword.Trim();
            customers = customers.Where(c => c.FullName.Contains(keyword) || c.Email.Contains(keyword)
                                             || (c.Phone != null && c.Phone.Contains(keyword)));
        }
        if (search.Status is { } status) customers = customers.Where(c => c.Status == status);
        var currency = _tenant.RequireTenant().Currency;

        return customers.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .ToPageAsync(c => new CustomerListItemDto(
                c.Id, c.FullName, c.Email, c.Phone, c.Status.ToString(),
                _db.Orders.Count(o => o.CustomerId == c.Id),
                (_db.OrderItems
                     .Where(i => _db.Orders.Any(o => o.Id == EF.Property<int>(i, "OrderId")
                                                     && o.CustomerId == c.Id && Settled.Contains(o.Status)))
                     .Sum(i => (decimal?)(i.UnitPrice.Amount * i.Quantity)) ?? 0m)
                - (_db.Orders.Where(o => o.CustomerId == c.Id && Settled.Contains(o.Status))
                       .Sum(o => (decimal?)o.DiscountAmount!.Amount) ?? 0m),
                currency,
                _db.Orders.Where(o => o.CustomerId == c.Id).Max(o => (DateTime?)o.CreatedAt),
                c.CreatedAt), page, ct);
    }

    public async Task<CustomerDetailDto?> FindDetailAsync(int customerId, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().Include(c => c.Addresses).FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return null;

        var account = await _db.Users.AsNoTracking().Where(u => u.Id == customer.UserId)
            .Select(u => new { u.EmailConfirmedAt, u.LastLoginAt }).FirstOrDefaultAsync(ct);
        var (orderCount, spent) = await StatsAsync(customerId, ct);

        return new CustomerDetailDto(
            customer.Id, customer.FullName, customer.Email, customer.Phone, customer.Status.ToString(), customer.BlockedAt,
            customer.IsErased, customer.CreatedAt, account?.EmailConfirmedAt is not null, account?.LastLoginAt,
            orderCount, spent, _tenant.RequireTenant().Currency, CustomerAddressDto.Ordered(customer.Addresses));
    }

    public async Task<CustomerExportDto?> ExportAsync(int customerId, DateTime exportedAt, CancellationToken ct)
    {
        var customer = await _db.Customers.AsNoTracking().Include(c => c.Addresses).FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return null;

        // الطلب محمَّل بأسطره كي يُحسب إجماليه بقاعدة المجال نفسها (Order.TotalAmount).
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .ToListAsync(ct);
        var reviews = await _db.Reviews.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new CustomerExportReview(r.ProductId, r.Rating, r.Comment, r.CreatedAt))
            .ToListAsync(ct);

        return new CustomerExportDto(
            exportedAt,
            new CustomerExportProfile(customer.Id, customer.FullName, customer.Email, customer.Phone, customer.Status.ToString(), customer.CreatedAt),
            CustomerAddressDto.Ordered(customer.Addresses),
            orders.Select(o => new CustomerExportOrder(
                o.Id, o.Status.ToString(), o.ShippingAddress, o.TotalAmount.Amount, o.Currency, o.CreatedAt,
                o.Items.Select(i => new CustomerExportOrderLine(i.ProductName, i.UnitPrice.Amount, i.Quantity)).ToList())).ToList(),
            reviews);
    }

    private async Task<(int Count, decimal Spent)> StatsAsync(int customerId, CancellationToken ct)
    {
        var count = await _db.Orders.CountAsync(o => o.CustomerId == customerId, ct);
        var lines = await _db.OrderItems
            .Where(i => _db.Orders.Any(o => o.Id == EF.Property<int>(i, "OrderId")
                                            && o.CustomerId == customerId && Settled.Contains(o.Status)))
            .SumAsync(i => (decimal?)(i.UnitPrice.Amount * i.Quantity), ct) ?? 0m;
        var discounts = await _db.Orders.Where(o => o.CustomerId == customerId && Settled.Contains(o.Status))
            .SumAsync(o => (decimal?)o.DiscountAmount!.Amount, ct) ?? 0m;
        return (count, lines - discounts);
    }
}
