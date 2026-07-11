using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Commands;

// لا OrderId هنا: العميل لا يحتاج معرفة رقم طلبه — المعالج يكتشف تلقائياً أيّ
// طلب مُسلَّم (Delivered) للعميل يحتوي هذا المنتج، ويثبت به أحقّية التقييم.
public record CreateReviewCommand(int ProductId, int CustomerId, int Rating, string Comment) : IRequest<Result<int>>;
