using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Customers;

// ============================================================================
// عقود وحدة Customers (المرحلة 7): الملف ودفتر العناوين للعميل نفسه، والقائمة والتفاصيل والتصدير للإدارة. الكيانات
// لا تعبر الحدود. أرقام الطلبات في القائمة والتفاصيل إسقاطات قراءة (Infrastructure)، لا اعتماد على وحدة Ordering.
// ============================================================================

// مدخل عنوان من العميل (الشكل نفسه للإضافة والتعديل). الكيان يطبّع ويتحقّق (PostalAddress).
public sealed record AddressInput(
    string RecipientName, string Phone, string Country, string City, string Line1,
    string? Region = null, string? Line2 = null, string? PostalCode = null, string? Label = null)
{
    internal PostalAddress ToDomain() => new(RecipientName, Phone, Country, City, Line1, Region, Line2, PostalCode);
}

public sealed record CustomerAddressDto(
    int Id, string? Label, string RecipientName, string Phone, string Country, string City, string? Region,
    string Line1, string? Line2, string? PostalCode, bool IsDefaultShipping, bool IsDefaultBilling)
{
    public static CustomerAddressDto From(CustomerAddress a) => new(
        a.Id, a.Label, a.RecipientName, a.Phone, a.Country, a.City, a.Region, a.Line1, a.Line2, a.PostalCode,
        a.IsDefaultShipping, a.IsDefaultBilling);

    // الافتراضي للشحن أولاً، ثم الأقدم.
    public static IReadOnlyList<CustomerAddressDto> Ordered(IEnumerable<CustomerAddress> addresses) =>
        addresses.OrderByDescending(a => a.IsDefaultShipping).ThenBy(a => a.Id).Select(From).ToList();
}

// ملف العميل كما يراه هو (المرحلة 7).
public sealed record CustomerProfileDto(
    int Id, string FullName, string Email, string? Phone, string Status, IReadOnlyList<CustomerAddressDto> Addresses)
{
    internal static CustomerProfileDto From(Customer c) =>
        new(c.Id, c.FullName, c.Email, c.Phone, c.Status.ToString(), CustomerAddressDto.Ordered(c.Addresses));
}

public sealed record CustomerSearch(string? Keyword = null, CustomerStatus? Status = null);

// سطر في قائمة عملاء الإدارة: الإنفاق مجموع الطلبات المدفوعة فما بعدها بعملة المتجر.
public sealed record CustomerListItemDto(
    int Id, string FullName, string Email, string? Phone, string Status,
    int OrderCount, decimal TotalSpent, string Currency, DateTime? LastOrderAt, DateTime CreatedAt);

// تفاصيل عميل للإدارة. سجلّ طلباته من /api/orders?customerId= (وحدة Ordering) لا من هنا.
public sealed record CustomerDetailDto(
    int Id, string FullName, string Email, string? Phone, string Status, DateTime? BlockedAt, bool IsErased,
    DateTime CreatedAt, bool EmailConfirmed, DateTime? LastLoginAt,
    int OrderCount, decimal TotalSpent, string Currency, IReadOnlyList<CustomerAddressDto> Addresses);

// تصدير بيانات العميل (قابلية نقل البيانات): الملف، العناوين، الطلبات بأسطرها، والتقييمات — بلا أي اعتماد دخول.
public sealed record CustomerExportDto(
    DateTime ExportedAt, CustomerExportProfile Profile, IReadOnlyList<CustomerAddressDto> Addresses,
    IReadOnlyList<CustomerExportOrder> Orders, IReadOnlyList<CustomerExportReview> Reviews);

public sealed record CustomerExportProfile(int Id, string FullName, string Email, string? Phone, string Status, DateTime CreatedAt);

public sealed record CustomerExportOrder(
    int Id, string Status, string ShippingAddress, decimal Total, string Currency, DateTime CreatedAt,
    IReadOnlyList<CustomerExportOrderLine> Lines);

public sealed record CustomerExportOrderLine(string ProductName, decimal UnitPrice, int Quantity);

public sealed record CustomerExportReview(int ProductId, int Rating, string? Comment, DateTime CreatedAt);
