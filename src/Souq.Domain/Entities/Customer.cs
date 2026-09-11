using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Customer — ملف الشراء لحساب داخل متجر واحد (وحدة Customers، D-06). منذ المرحلة 3 لا يحمل أي اعتماد دخول: البريد
// وكلمة المرور والدور والرموز في User (Identity)، وهذا الملف يشير إليه بـ UserId. الطلبات والتقييمات تشير إلى
// Customer.Id. Email هنا بريد التواصل (رسائل الطلبات) — نسخة من بريد الحساب عند التسجيل، لا هوية دخول.
//
// المرحلة 7: جذر تجمّع لدفتر عناوينه (حتى 20)، بعنوان افتراضي واحد للشحن وآخر للفوترة ما دام له عنوان؛ حالة تجارية
// (نشط/محظور — المحظور لا يطلب ولا يقيّم)؛ ومحو (حقّ الحذف) يزيل البيانات الشخصية ويُبقي المعرّف لأن الطلبات
// والتقييمات سجلّ يُحتفَظ به.
// ============================================================================
public class Customer : Entity, ITenantOwned
{
    public const int FullNameMaxLength = 150;
    public const int MaxAddresses = 20;
    public const string ErasedName = "عميل محذوف";

    private readonly List<CustomerAddress> _addresses = new();

    public int TenantId { get; private set; }
    public int UserId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Phone { get; private set; }
    public CustomerStatus Status { get; private set; }
    public DateTime? BlockedAt { get; private set; }
    public DateTime? ErasedAt { get; private set; }

    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses.AsReadOnly();
    public bool IsBlocked => Status == CustomerStatus.Blocked;
    public bool IsErased => ErasedAt is not null;
    public CustomerAddress? DefaultShippingAddress => _addresses.FirstOrDefault(a => a.IsDefaultShipping);

    private Customer() { }

    public Customer(int userId, string fullName, string email)
    {
        if (userId <= 0)
            throw new InvalidIdentityOperationException("ملف العميل يحتاج حساب دخول محفوظاً");
        UserId = userId;
        Rename(fullName);
        Email = string.IsNullOrWhiteSpace(email)
            ? throw new InvalidIdentityOperationException("بريد التواصل مطلوب")
            : email.Trim().ToLowerInvariant();
        Status = CustomerStatus.Active;
    }

    public void Rename(string fullName)
    {
        var trimmed = fullName?.Trim() ?? "";
        if (trimmed.Length is 0 or > FullNameMaxLength)
            throw new InvalidIdentityOperationException($"الاسم مطلوب (حتى {FullNameMaxLength} حرفاً)");
        FullName = trimmed;
    }

    public void UpdateProfile(string fullName, string? phone)
    {
        EnsureNotErased();
        Rename(fullName);
        Phone = PostalAddress.NormalizePhone(phone);
    }

    // ── دفتر العناوين ─────────────────────────────────────────────────────────

    // أول عنوان يصبح الافتراضي للشحن وللفوترة تلقائياً — لا عميل بعناوين بلا افتراضي.
    public CustomerAddress AddAddress(PostalAddress address, string? label, bool defaultShipping = false, bool defaultBilling = false)
    {
        EnsureNotErased();
        if (_addresses.Count >= MaxAddresses)
            throw new InvalidCustomerDataException($"دفتر العناوين حتى {MaxAddresses} عنواناً");

        var entry = new CustomerAddress(address, label);
        _addresses.Add(entry);
        if (defaultShipping || DefaultShippingAddress is null) MakeDefault(entry, shipping: true);
        if (defaultBilling || _addresses.All(a => !a.IsDefaultBilling)) MakeDefault(entry, shipping: false);
        return entry;
    }

    public void UpdateAddress(int addressId, PostalAddress address, string? label)
    {
        EnsureNotErased();
        FindAddress(addressId).Update(address, label);
    }

    // حذف الافتراضي ينقل صفته لأقدم عنوان باقٍ.
    public void RemoveAddress(int addressId)
    {
        EnsureNotErased();
        var entry = FindAddress(addressId);
        _addresses.Remove(entry);
        if (entry.IsDefaultShipping && _addresses.FirstOrDefault() is { } nextShipping) MakeDefault(nextShipping, shipping: true);
        if (entry.IsDefaultBilling && _addresses.FirstOrDefault() is { } nextBilling) MakeDefault(nextBilling, shipping: false);
    }

    public void SetDefaultShipping(int addressId) => MakeDefault(FindAddress(addressId), shipping: true);

    public void SetDefaultBilling(int addressId) => MakeDefault(FindAddress(addressId), shipping: false);

    public CustomerAddress FindAddress(int addressId) =>
        _addresses.FirstOrDefault(a => a.Id == addressId)
        ?? throw new InvalidCustomerDataException("العنوان غير موجود في دفتر هذا العميل");

    // ── الحالة والمحو ─────────────────────────────────────────────────────────

    public void Block(DateTime now)
    {
        EnsureNotErased();
        if (IsBlocked) return;
        Status = CustomerStatus.Blocked;
        BlockedAt = now;
    }

    public void Unblock()
    {
        EnsureNotErased();
        Status = CustomerStatus.Active;
        BlockedAt = null;
    }

    // حقّ الحذف: الاسم والبريد والهاتف والعناوين تُزال، والملف يُحظر نهائياً. المعرّف يبقى (الطلبات والتقييمات تشير
    // إليه — سجلّ مالي يُحتفَظ به). مضمون التكرار.
    public void Erase(DateTime now)
    {
        if (IsErased) return;
        FullName = ErasedName;
        Email = $"erased-{Id}@erased.invalid";
        Phone = null;
        _addresses.Clear();
        Status = CustomerStatus.Blocked;
        BlockedAt ??= now;
        ErasedAt = now;
    }

    private void MakeDefault(CustomerAddress target, bool shipping)
    {
        EnsureNotErased();
        foreach (var address in _addresses)
            address.SetDefault(shipping, ReferenceEquals(address, target));
    }

    private void EnsureNotErased()
    {
        if (IsErased) throw new InvalidCustomerDataException("ملف العميل محذوف");
    }
}

// عنوان في دفتر العميل — ابن تجمّع لا يُنشأ ولا يُعدَّل إلا عبر Customer (مفتاح ظلّ CustomerId)، ويحمل متجره.
public class CustomerAddress : Entity, ITenantOwned
{
    public const int LabelMaxLength = 50;

    public int TenantId { get; private set; }
    public string? Label { get; private set; }
    public string RecipientName { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public string Country { get; private set; } = default!;
    public string City { get; private set; } = default!;
    public string? Region { get; private set; }
    public string Line1 { get; private set; } = default!;
    public string? Line2 { get; private set; }
    public string? PostalCode { get; private set; }
    public bool IsDefaultShipping { get; private set; }
    public bool IsDefaultBilling { get; private set; }

    private CustomerAddress() { }

    internal CustomerAddress(PostalAddress address, string? label) => Update(address, label);

    public PostalAddress ToPostalAddress() => new(RecipientName, Phone, Country, City, Line1, Region, Line2, PostalCode);

    internal void Update(PostalAddress address, string? label)
    {
        var trimmed = label?.Trim();
        if (trimmed is { Length: > LabelMaxLength })
            throw new InvalidCustomerDataException($"اسم العنوان حتى {LabelMaxLength} حرفاً");
        Label = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        RecipientName = address.RecipientName;
        Phone = address.Phone;
        Country = address.Country;
        City = address.City;
        Region = address.Region;
        Line1 = address.Line1;
        Line2 = address.Line2;
        PostalCode = address.PostalCode;
    }

    internal void SetDefault(bool shipping, bool isDefault)
    {
        if (shipping) IsDefaultShipping = isDefault;
        else IsDefaultBilling = isDefault;
    }
}
