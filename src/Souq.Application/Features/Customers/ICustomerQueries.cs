using Souq.Application.Common.Models;

namespace Souq.Application.Features.Customers;

// منفذ القراءة لوحدة Customers (ADR-0008): قائمة الإدارة وتفاصيلها وتصدير البيانات — إسقاطات بلا تتبّع، مُرشَّحة بالمتجر.
public interface ICustomerQueries
{
    Task<PaginatedList<CustomerListItemDto>> ListAsync(CustomerSearch search, PageRequest page, CancellationToken ct);

    Task<CustomerDetailDto?> FindDetailAsync(int customerId, CancellationToken ct);

    Task<CustomerExportDto?> ExportAsync(int customerId, DateTime exportedAt, CancellationToken ct);
}
