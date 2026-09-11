using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// DELETE على منتج = أرشفة (لا حذف أبداً: سطور الطلبات والتقييمات تشير إليه). يُستعاد بتغيير حالته.
public record DeleteProductCommand(int Id) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.archived", "Product", Id.ToString());
}
