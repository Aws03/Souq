using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// نمط CQRS: "أمر" يُعدّل منتجاً قائماً. يُرجع Result (بلا قيمة) لأن التحديث ناجح
// أو فاشل فقط. Id جزء من الأمر، لكن الـ Controller يفرض قيمة المسار عليه.
//
// المخزون (ADR-0013 — compare-and-set): StockQuantity اختياري. null ⇒ لا نمسّ
// المخزون إطلاقاً (تعديل الاسم/السعر وحده لا يكتب فوق مبيعات حدثت أثناء فتح النموذج).
// إن أُرسل، فـ ExpectedStockQuantity إلزامي = المخزون كما رآه المدير لحظة فتح
// النموذج؛ إن تغيّر منذ ذلك يُرفض التعديل بتعارض بدل محو بيع حقيقي (Phase 0 C4).
// LowStockThreshold اختياري: null ⇒ نُبقي الحدّ الحالي.
public record UpdateProductCommand(
    int Id,
    string NameAr, string Description, decimal Price,
    int? StockQuantity, string ImageUrl, int CategoryId, string? NameEn = null, string? VideoUrl = null,
    int? LowStockThreshold = null, int? ExpectedStockQuantity = null
) : IRequest<Result>;
