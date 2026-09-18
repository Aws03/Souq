using Souq.Application.Features.Auth.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Customers;

// ============================================================================
// تنفيذ `IAccountProfiles` (M9) — وهو **العبور الوحيد** الذي تحتاجه Identity من هذه الوحدة الآن.
//
// العقد يُعلنه Identity وتُنفّذه Customers (انعكاس تبعية، كـ `IVariantStockInitializer`)، فلا يبقى
// في Identity ذكرٌ لـ `Customer` ولا لـ `ICustomerRepository`: السهم من Identity إلى Customers
// اختفى بالكامل، وهو ما يكسر الدورة — لا مجرّد توثيقها.
//
// والكيان يُبنى هنا لا هناك: تجمّعُ العميل يعرف قواعده، ومن يُنشئه يجب أن يكون داخل وحدته.
// ============================================================================
public sealed class CustomerAccountProfiles : IAccountProfiles
{
    private readonly ICustomerRepository _customers;

    public CustomerAccountProfiles(ICustomerRepository customers) => _customers = customers;

    // بلا حفظ: التسجيل وحدةٌ واحدة، فالحفظ لمعاملة `RegisterHandler`.
    public Task CreateForAccountAsync(int userId, string fullName, string email, CancellationToken ct) =>
        _customers.AddAsync(new Customer(userId, fullName, email), ct);

    public Task<int?> FindIdForAccountAsync(int userId, CancellationToken ct) =>
        _customers.FindIdByUserIdAsync(userId, ct);
}
