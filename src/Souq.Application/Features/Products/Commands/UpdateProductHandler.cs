using MediatR;
using Souq.Application.Common.Models;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// UpdateProductHandler — ينسّق تحديث منتج قائم. مسؤوليته تنسيق الخطوات فقط؛
// قواعد الصحّة تعيش في الكيان (UpdateDetails/SetStock) وفي المدقّق (Validator).
// الخطوات:
//   1) نجلب المنتج — غير موجود ⇒ Result فشل واضح (يترجمه الـ API إلى 404).
//   2) نتحقّق من وجود الفئة الهدف (تكامل مرجعي) قبل أي تعديل — نفشل مبكراً
//      برسالة واضحة بدل ترك قاعدة البيانات ترمي خطأ مفتاح أجنبي (500).
//   3) نطبّق التغييرات عبر دوال الكيان المحروسة.
//   4) نحفظ ذرّياً عبر وحدة العمل.
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

        // نحافظ على عملة المنتج الحالية بدل فرض عملة افتراضية عند التحديث.
        product.UpdateDetails(
            cmd.NameAr, cmd.Description,
            new Money(cmd.Price, product.Price.Currency),
            cmd.ImageUrl, cmd.CategoryId, cmd.NameEn, cmd.VideoUrl);

        // تعديل المخزون من الإدارة حركة تصحيح (Adjustment) — نسجّلها فقط إن تغيّر
        // المخزون فعلاً (لا حركة صفرية عند تعديل الاسم/السعر وحده). نحسب الفرق قبل
        // تطبيق SetStock كي نلتقط الاتجاه بإشارته.
        var stockDelta = cmd.StockQuantity - product.StockQuantity;
        product.SetStock(cmd.StockQuantity);
        if (cmd.LowStockThreshold is int threshold)
            product.SetLowStockThreshold(threshold);

        _products.Update(product);
        if (stockDelta != 0)
            await _stockMovements.AddAsync(
                StockMovement.For(product, StockMovementType.Adjustment, stockDelta,
                    "تعديل يدوي من الإدارة"), ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
