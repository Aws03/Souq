using Souq.Domain.Entities;

namespace Souq.Domain.Interfaces;

// منفذ الكتابة لملفات العملاء (مُرشَّح بالمتجر). الهوية والاعتماد في IUserRepository منذ المرحلة 3.
public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetByUserIdAsync(int userId, CancellationToken ct = default);

    // معرّف ملف العميل لحساب — لمطالبة cid في التوكن (null لحساب بلا ملف شراء: موظّف، مالك).
    Task<int?> FindIdByUserIdAsync(int userId, CancellationToken ct = default);
}
