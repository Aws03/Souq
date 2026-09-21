using MediatR;
using Souq.Application.Common.Models;
using Souq.Application.Features.Billing.Contracts;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// DeleteProductHandler — أرشفة لا حذف (المرحلة 5، C7). المنتج مرتبط بسطور طلبات تاريخية وتقييمات؛ حذفه يكسر
// سلامة السجلّات. المؤرشف يختفي من المتجر (CatalogQueries: النشط فقط) ويبقى في قائمة الإدارة ويُستعاد منها.
//
// **والأرشفة تُطلق الحصّة** (C2، ADR-0049): إذ هي حذف هذا المستودع، ولو لم تُطلقها لصار حدّ
// المنتجات سقّاطة لا تُفرَّغ أبداً — لا مسار حذفٍ آخر يملكه التاجر. LimitNames يشرح القاعدة.
// ============================================================================
public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ITenantQuotaGuard _quota;
    private readonly IUnitOfWork _uow;

    public DeleteProductHandler(IProductRepository products, ITenantQuotaGuard quota, IUnitOfWork uow)
    {
        _products = products; _quota = quota; _uow = uow;
    }

    public async Task<Result> Handle(DeleteProductCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null)
            return Result.Failure(Error.NotFound("المنتج غير موجود"));

        // أرشفةُ المؤرشَف لا تُطلق شيئاً: نداءان يُنقِصان العدّاد مرّتين عن أرشفةٍ واحدة.
        var wasCounted = product.Status != ProductStatus.Archived;

        product.Archive();
        await _uow.SaveChangesAsync(ct);

        // بعد الحفظ لا قبله: الإطلاق قبل نجاح الأرشفة يُنقِص عدّاداً عن شيء ما زال قائماً.
        // وتأخّره لحظةً يُضيّق ولا يفتح، فلا يحتاج معاملة (عقد ITenantQuotaGuard).
        if (wasCounted) await _quota.ReleaseAsync(LimitNames.CatalogProducts, ct: ct);
        return Result.Success();
    }
}
