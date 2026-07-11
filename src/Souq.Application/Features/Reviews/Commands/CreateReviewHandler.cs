using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Reviews.Commands;

// ============================================================================
// CreateReviewHandler — يفرض قاعدة "لا تقييم بلا شراء واستلام فعليّين" (تمنع
// تقييمات مزيّفة). القاعدة تُفرَض هنا لا في كيان Review نفسه، لأنها تتقاطع بين
// تجمّعين (Order وReview) — بالضبط النمط الموصوف في تعليق CreateOrderHandler:
// "المعالج ينسّق، الكيان يحرس قواعده الخاصة فقط".
// ============================================================================
public class CreateReviewHandler : IRequestHandler<CreateReviewCommand, Result<int>>
{
    private readonly IReviewRepository _reviews;
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _uow;

    public CreateReviewHandler(IReviewRepository reviews, IOrderRepository orders, IUnitOfWork uow)
    {
        _reviews = reviews; _orders = orders; _uow = uow;
    }

    public async Task<Result<int>> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        if (await _reviews.HasCustomerReviewedProductAsync(cmd.CustomerId, cmd.ProductId, ct))
            return Result<int>.Failure("قيّمت هذا المنتج مسبقاً", "AlreadyReviewed");

        var orders = await _orders.GetByCustomerAsync(cmd.CustomerId, ct);
        var eligibleOrder = orders.FirstOrDefault(o =>
            o.Status == OrderStatus.Delivered && o.Items.Any(i => i.ProductId == cmd.ProductId));

        if (eligibleOrder is null)
            return Result<int>.Failure("يمكنك تقييم منتج اشتريته واستلمته فقط", "NotEligible");

        Review review;
        try { review = new Review(cmd.ProductId, cmd.CustomerId, eligibleOrder.Id, cmd.Rating, cmd.Comment); }
        catch (InvalidReviewException ex) { return Result<int>.Failure(ex.Message, "InvalidReview"); }

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<int>.Success(review.Id);
    }
}
