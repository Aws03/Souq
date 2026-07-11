using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Products.Commands;

// نمط CQRS: "أمر" يُعدّل منتجاً قائماً. يُرجع Result (بلا قيمة) لأن التحديث ناجح
// أو فاشل فقط — لا بيانات جديدة يحتاجها العميل (يستطيع إعادة الجلب إن أراد).
// Id جزء من الأمر، لكن الـ Controller يفرض قيمة المسار عليه (انظر تعليقه) منعاً
// لتعارض معرّف المسار مع معرّف الجسم.
public record UpdateProductCommand(
    int Id,
    string Name, string Description, decimal Price,
    int StockQuantity, string ImageUrl, int CategoryId
) : IRequest<Result>;
