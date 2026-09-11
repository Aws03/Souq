using MediatR;
using Souq.Application.Common.Models;

namespace Souq.Application.Features.Reviews.Commands;

// لا OrderId ولا معرّف عميل هنا: هوية المقيِّم من ICurrentUser (التوكن) لا من الطلب، والمعالج
// يكتشف تلقائياً أيّ طلب مُسلَّم (Delivered) للعميل يحتوي هذا المنتج ويثبت به أحقّية التقييم.
public record CreateReviewCommand(int ProductId, int Rating, string Comment) : IRequest<Result<ReviewCreatedDto>>;

// Status (المرحلة 13): Approved ⇒ منشور الآن، Pending ⇒ الواجهة تخبر العميل أنه ينتظر المراجعة.
public record ReviewCreatedDto(int Id, string Status);
