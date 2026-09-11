using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Reviews.Commands;

// ============================================================================
// CreateReviewHandler — يفرض قاعدة "لا تقييم بلا شراء واستلام فعليّين" (تمنع
// تقييمات مزيّفة). القاعدة تُفرَض هنا لا في كيان Review نفسه، لأنها تتقاطع بين
// تجمّعين (Order وReview) — المعالج ينسّق، الكيان يحرس قواعده الخاصة فقط.
// المقيِّم هو المستخدم الحالي دائماً (ICurrentUser) — لا تقييم باسم عميل آخر.
// ============================================================================
public class CreateReviewHandler : IRequestHandler<CreateReviewCommand, Result<int>>
{
    private readonly IReviewRepository _reviews;
    private readonly IOrderRepository _orders;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public CreateReviewHandler(IReviewRepository reviews, IOrderRepository orders, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _reviews = reviews; _orders = orders; _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();

        if (await _reviews.HasCustomerReviewedProductAsync(customerId, cmd.ProductId, ct))
            return Result<int>.Failure(Error.Conflict("AlreadyReviewed", "قيّمت هذا المنتج مسبقاً"));

        // استعلام واحد لأحدث طلب مُسلَّم يحوي المنتج — لا تحميل كل طلبات العميل بأسطرها.
        var eligibleOrderId = await _orders.FindDeliveredOrderIdContainingAsync(customerId, cmd.ProductId, ct);
        if (eligibleOrderId is null)
            return Result<int>.Failure(Error.BusinessRule("NotEligible", "يمكنك تقييم منتج اشتريته واستلمته فقط"));

        // تقييم خارج 1-5 أو تعليق فارغ ⇒ InvalidReviewException (422 مركزياً).
        var review = new Review(cmd.ProductId, customerId, eligibleOrderId.Value, cmd.Rating, cmd.Comment);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(review.Id);
    }
}
