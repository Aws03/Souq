using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

public interface ICustomerRepository : IRepository<Customer>
{
    // البريد هو هوية الدخول — الاستعلام الأساسي في التسجيل وتسجيل الدخول.
    Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default);
}
