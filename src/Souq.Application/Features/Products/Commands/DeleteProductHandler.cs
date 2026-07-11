using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// DeleteProductHandler — حذف منطقي (Soft Delete) عبر Deactivate().
// لماذا لا نحذف الصف فعلياً؟ المنتجات مرتبطة بعناصر طلبات تاريخية (OrderItem)؛
// حذفها يكسر سلامة السجلّات ويُفقد تاريخ المبيعات. بدلاً من ذلك نُعطّله فيختفي
// من واجهة المتجر (SearchAsync يصفّي IsActive) مع بقاء أثره محفوظاً.
// هذا تطبيق مباشر للمبدأ الموثّق في الكيان: "حذف منطقي بدل الفعلي".
// ============================================================================
public class DeleteProductHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public DeleteProductHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(DeleteProductCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null)
            return Result.Failure("المنتج غير موجود", "NotFound");

        product.Deactivate();              // قاعدة الحذف المنطقي محروسة داخل الكيان
        _products.Update(product);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
