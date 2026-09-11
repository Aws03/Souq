using MediatR;
using Souq.Application.Common.Files;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// رفع صورة منتج قائم. يستقبل المحتوى كـ Stream وطوله فقط — لا اسم ملف ولا نوع من
// العميل (كلاهما غير موثوق). النوع يُكشف من محتوى الملف نفسه (ADR-0016).
public record UploadProductImageCommand(int ProductId, Stream Content, long Length)
    : IRequest<Result<string>>;

public class UploadProductImageHandler : IRequestHandler<UploadProductImageCommand, Result<string>>
{
    private readonly IProductRepository _products;
    private readonly IFileStorage _storage;
    private readonly IUnitOfWork _uow;

    public UploadProductImageHandler(IProductRepository products, IFileStorage storage, IUnitOfWork uow)
    {
        _products = products; _storage = storage; _uow = uow;
    }

    public async Task<Result<string>> Handle(UploadProductImageCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null)
            return Result<string>.Failure(Error.NotFound("المنتج غير موجود"));

        if (cmd.Length > MediaFileInspector.MaxImageBytes)
            return Result<string>.Failure(Error.Validation("FileTooLarge", "حجم الصورة يتجاوز 5 ميغابايت"));

        var type = await MediaFileInspector.DetectAsync(cmd.Content, ct);
        if (type is null || type.Category != MediaCategory.Image)
            return Result<string>.Failure(Error.Validation(
                "UnsupportedMediaType", "صيغة الصورة غير مدعومة (JPEG/PNG/WebP/GIF فقط)"));

        var url = await _storage.SaveAsync(cmd.Content, "images", type.Extension, ct);
        product.SetImageUrl(url);
        _products.Update(product);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(url);
    }
}
