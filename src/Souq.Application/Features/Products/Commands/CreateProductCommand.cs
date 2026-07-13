using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// نمط CQRS: هذا "أمر" (Command) — يُعدّل الحالة (ينشئ منتجاً). فصلناه عن
// الاستعلامات لأن لهما اهتمامات مختلفة: الأوامر تتحقّق وتُعدّل، الاستعلامات تقرأ بسرعة.
// NameEn اختياري: يتردّد إلى NameAr تلقائياً في الكيان إن غاب (منتج لم يُترجم بعد).
// VideoUrl اختياري دوماً — لا كل منتج له فيديو، ويُرفع عادة عبر نقطة رفع منفصلة
// بعد الإنشاء (مثل الصورة تماماً).
// LowStockThreshold اختياري: يتردّد إلى الحدّ الافتراضي (5) في الكيان إن غاب.
public record CreateProductCommand(
    string NameAr, string Description, decimal Price,
    int StockQuantity, string ImageUrl, int CategoryId, string? NameEn = null, string? VideoUrl = null,
    int LowStockThreshold = Souq.Domain.Entities.Product.DefaultLowStockThreshold
) : IRequest<Result<int>>;
