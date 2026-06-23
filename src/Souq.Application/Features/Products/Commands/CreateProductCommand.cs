using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// نمط CQRS: هذا "أمر" (Command) — يُعدّل الحالة (ينشئ منتجاً). فصلناه عن
// الاستعلامات لأن لهما اهتمامات مختلفة: الأوامر تتحقّق وتُعدّل، الاستعلامات تقرأ بسرعة.
public record CreateProductCommand(
    string Name, string Description, decimal Price,
    int StockQuantity, string ImageUrl, int CategoryId
) : IRequest<Result<int>>;
