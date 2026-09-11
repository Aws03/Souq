using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Common.Security;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Reviews.Commands;

// ============================================================================
// CreateReviewHandler — يفرض قاعدة "لا تقييم بلا شراء واستلام فعليّين" (تمنع
// تقييمات مزيّفة). القاعدة تُفرَض هنا لا في كيان Review نفسه، لأنها تتقاطع بين
// تجمّعين (Order وReview) — المعالج ينسّق، الكيان يحرس قواعده الخاصة فقط.
// المقيِّم هو المستخدم الحالي دائماً (ICurrentUser) — لا تقييم باسم عميل آخر، ولا تقييم من محظور (المرحلة 7).
// النشر (المرحلة 13): معتمد فوراً إن فعّل المتجر الاعتماد التلقائي، وإلا معلّق حتى يقرّر المشرف — الإعداد يُقرأ من صفّ
// المتجر لحظة الكتابة (لا من ذاكرة دليل المتاجر) فيسري تغييره على التقييم التالي مباشرةً.
// ============================================================================
public class CreateReviewHandler : IRequestHandler<CreateReviewCommand, Result<ReviewCreatedDto>>
{
    private readonly IReviewRepository _reviews;
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public CreateReviewHandler(
        IReviewRepository reviews, IOrderRepository orders, ICustomerRepository customers, ITenantRepository tenants,
        ITenantContext tenant, ICurrentUser currentUser, IUnitOfWork uow)
    {
        _reviews = reviews; _orders = orders; _customers = customers; _tenants = tenants; _tenant = tenant;
        _currentUser = currentUser; _uow = uow;
    }

    public async Task<Result<ReviewCreatedDto>> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var customerId = _currentUser.RequireCustomerId();

        var customer = await _customers.GetByIdAsync(customerId, ct);
        if (customer is null || customer.IsBlocked)
            return Result<ReviewCreatedDto>.Failure(Error.Forbidden("CustomerBlocked", "حسابك موقوف عن التقييم في هذا المتجر."));

        if (await _reviews.HasCustomerReviewedProductAsync(customerId, cmd.ProductId, ct))
            return Result<ReviewCreatedDto>.Failure(Error.Conflict("AlreadyReviewed", "قيّمت هذا المنتج مسبقاً"));

        // استعلام واحد لأحدث طلب مُسلَّم يحوي المنتج — لا تحميل كل طلبات العميل بأسطرها.
        var eligibleOrderId = await _orders.FindDeliveredOrderIdContainingAsync(customerId, cmd.ProductId, ct);
        if (eligibleOrderId is null)
            return Result<ReviewCreatedDto>.Failure(Error.BusinessRule("NotEligible", "يمكنك تقييم منتج اشتريته واستلمته فقط"));

        var store = await _tenants.GetByIdAsync(_tenant.RequireTenant().Id, ct);

        // تقييم خارج 1-5 أو تعليق فارغ ⇒ InvalidReviewException (422 مركزياً).
        var review = new Review(cmd.ProductId, customerId, eligibleOrderId.Value, cmd.Rating, cmd.Comment,
            approved: store?.ReviewsAutoApprove == true);

        await _reviews.AddAsync(review, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<ReviewCreatedDto>.Success(new ReviewCreatedDto(review.Id, review.Status.ToString()));
    }
}
