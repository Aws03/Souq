using FluentValidation;
using MediatR;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Models;
using Souq.Application.Features.Billing.Contracts;
using Souq.Domain.Enums;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;

namespace Souq.Application.Features.Products.Commands;

// ============================================================================
// دورة حياة المنتج وترتيب صوره (المرحلة 5، صلاحية catalog.manage). القواعد في Product (ChangeStatus، RemoveImage،
// ReorderImages)؛ هنا التنسيق. معرّف منتج متجر آخر ⇒ غير موجود (المستودع مُرشَّح بالمتجر).
// ============================================================================

// نشر مسودّة، إخفاء منتج نشط مؤقتاً، أرشفته، أو استعادته.
public record ChangeProductStatusCommand(int Id, ProductStatus Status) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.status-changed", "Product", Id.ToString(),
        Metadata: new Dictionary<string, object?> { ["status"] = Status.ToString() });
}

public sealed class ChangeProductStatusValidator : AbstractValidator<ChangeProductStatusCommand>
{
    public ChangeProductStatusValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Status).IsInEnum();
    }
}

// ============================================================================
// هذا هو الموضع الوحيد الذي **يستعيد** مؤرشفاً، فهو الموضع الوحيد الذي يُعيد حجز حصّته (C2).
// بغيره كان الحدّ يُتجاوَز بأرشفةٍ واستعادة: الأرشفة تُطلق والاستعادة لا تحجز — ثغرةٌ لا يكشفها
// اختبار إنشاء واحد، وتُصلحها المصالحة بعد فوات الأوان.
// ============================================================================
public class ChangeProductStatusHandler : IRequestHandler<ChangeProductStatusCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly ITenantQuotaGuard _quota;
    private readonly IUnitOfWork _uow;

    public ChangeProductStatusHandler(IProductRepository products, ITenantQuotaGuard quota, IUnitOfWork uow)
    {
        _products = products; _quota = quota; _uow = uow;
    }

    public async Task<Result> Handle(ChangeProductStatusCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.Id, ct);
        if (product is null) return Result.Failure(Error.NotFound("المنتج غير موجود"));

        var wasCounted = product.Status != ProductStatus.Archived;
        var willBeCounted = cmd.Status != ProductStatus.Archived;

        // استعادة: تُحجَز أوّلاً داخل معاملة، فمتجرٌ بلغ سقفه لا يستعيد فوقه.
        if (!wasCounted && willBeCounted)
        {
            var quota = await _uow.InTransactionAsync(async () =>
            {
                var decision = await _quota.ReserveAsync(LimitNames.CatalogProducts, ct);
                if (!decision.Allowed) return decision;

                product.ChangeStatus(cmd.Status);
                await _uow.SaveChangesAsync(ct);
                return decision;
            }, ct);

            return quota.Allowed ? Result.Success() : Result.Failure(quota.ToError());
        }

        product.ChangeStatus(cmd.Status);
        await _uow.SaveChangesAsync(ct);

        if (wasCounted && !willBeCounted) await _quota.ReleaseAsync(LimitNames.CatalogProducts, ct: ct);
        return Result.Success();
    }
}

// إزالة صورة من المنتج (الملف يبقى في التخزين — تنظيف الملفات اليتيمة مهمة خلفية لاحقة).
public record RemoveProductImageCommand(int ProductId, int ImageId) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.image-removed", "Product", ProductId.ToString(),
        Metadata: new Dictionary<string, object?> { ["imageId"] = ImageId });
}

public class RemoveProductImageHandler : IRequestHandler<RemoveProductImageCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public RemoveProductImageHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(RemoveProductImageCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null || product.Images.All(i => i.Id != cmd.ImageId))
            return Result.Failure(Error.NotFound("الصورة غير موجودة"));

        product.RemoveImage(cmd.ImageId);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ترتيب جديد لكل صور المنتج (الأولى رئيسية).
public record ReorderProductImagesCommand(int ProductId, IReadOnlyList<int> ImageIds) : IRequest<Result>, IAuditable
{
    public AuditRecord ToAuditRecord() => new("catalog.product.images-reordered", "Product", ProductId.ToString());
}

public sealed class ReorderProductImagesValidator : AbstractValidator<ReorderProductImagesCommand>
{
    public ReorderProductImagesValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.ImageIds).NotNull();
    }
}

public class ReorderProductImagesHandler : IRequestHandler<ReorderProductImagesCommand, Result>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _uow;

    public ReorderProductImagesHandler(IProductRepository products, IUnitOfWork uow)
    {
        _products = products; _uow = uow;
    }

    public async Task<Result> Handle(ReorderProductImagesCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null) return Result.Failure(Error.NotFound("المنتج غير موجود"));

        product.ReorderImages(cmd.ImageIds);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
