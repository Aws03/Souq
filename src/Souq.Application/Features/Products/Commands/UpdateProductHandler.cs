using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// UpdateProductHandler — ينسّق تحديث منتج قائم. قواعد الصحّة تعيش في الكيان
// (UpdateDetails/SetStock) وفي المدقّق. الخطوات:
//   1) نجلب المنتج — غير موجود ⇒ NotFound (404).
//   2) نتحقّق من وجود الفئة الهدف قبل أي تعديل — لا خطأ مفتاح أجنبي غامض (500).
//   3) نطبّق التفاصيل، ثم المخزون فقط إن طُلب تغييره وبشرط أنه لم يتغيّر منذ قراءته.
//   4) نحفظ ذرّياً — وrowversion يغلق نافذة السباق المتبقّية بين القراءة والحفظ.
// ============================================================================
public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IStockMovementRepository _stockMovements;
    private readonly IUnitOfWork _uow;

    public UpdateProductHandler(
        IProductRepository products, ICategoryRepository categories,
        IStockMovementRepository stockMovements, IUnitOfWork uow)
    {
        _products = products; _categories = categories;
        _stockMovements = stockMovements; _uow = uow;
    }

    public async Task<Result> Handle(UpdateProductCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null)
            return Result.Failure("المنتج غير موجود", "NotFound");

        var category = await _categories.GetByIdAsync(cmd.CategoryId, ct);
        if (category is null)
            return Result.Failure("الفئة المحدّدة غير موجودة", "CategoryNotFound");

        // compare-and-set: المدير يريد تعيين المخزون، لكن فقط إن كان لا يزال كما رآه.
        // نفحص قبل أي تعديل كي لا يُحفَظ تعديل جزئي مع رفض المخزون.
        if (cmd.StockQuantity is not null && cmd.ExpectedStockQuantity != product.StockQuantity)
            return Result.Failure(
                $"تغيّر المخزون منذ فتح النموذج (الحالي الآن {product.StockQuantity}). أعد تحميل المنتج ثم عدّل.",
                "Conflict");

        // نحافظ على عملة المنتج الحالية بدل فرض عملة افتراضية عند التحديث.
        product.UpdateDetails(
            cmd.NameAr, cmd.Description,
            new Money(cmd.Price, product.Price.Currency),
            cmd.ImageUrl, cmd.CategoryId, cmd.NameEn, cmd.VideoUrl);

        if (cmd.LowStockThreshold is int threshold)
            product.SetLowStockThreshold(threshold);

        _products.Update(product);

        // تعديل المخزون من الإدارة حركة تصحيح (Adjustment) بإشارة الفارق — تُسجَّل فقط
        // إن تغيّر المخزون فعلاً (لا حركة صفرية).
        if (cmd.StockQuantity is int newStock)
        {
            var stockDelta = newStock - product.StockQuantity;
            product.SetStock(newStock);
            if (stockDelta != 0)
                await _stockMovements.AddAsync(
                    StockMovement.For(product, StockMovementType.Adjustment, stockDelta, "تعديل يدوي من الإدارة"), ct);
        }

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
