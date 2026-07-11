using Souq.Domain.Entities;

namespace Souq.Application.Common.Interfaces;

// عقد إصدار توكن JWT. التنفيذ (المفاتيح، الخوارزمية، الصلاحية) في Infrastructure.
public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) Generate(Customer customer);
}
