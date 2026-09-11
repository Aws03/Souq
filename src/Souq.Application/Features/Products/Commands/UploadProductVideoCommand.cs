using MediatR;
using Souq.Application.Common.Files;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// رفع فيديو منتج قائم — نفس منهج UploadProductImageCommand: النوع من التوقيع لا من
// العميل، والامتداد المخزَّن مشتقّ من النوع المكتشَف.
public record UploadProductVideoCommand(int ProductId, Stream Content, long Length)
    : IRequest<Result<string>>;

public class UploadProductVideoHandler : IRequestHandler<UploadProductVideoCommand, Result<string>>
{
    private readonly IProductRepository _products;
    private readonly IFileStorage _storage;
    private readonly IUnitOfWork _uow;

    public UploadProductVideoHandler(IProductRepository products, IFileStorage storage, IUnitOfWork uow)
    {
        _products = products; _storage = storage; _uow = uow;
    }

    public async Task<Result<string>> Handle(UploadProductVideoCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null)
            return Result<string>.Failure(Error.NotFound("المنتج غير موجود"));

        if (cmd.Length > MediaFileInspector.MaxVideoBytes)
            return Result<string>.Failure(Error.Validation("FileTooLarge", "حجم الفيديو يتجاوز 50 ميغابايت"));

        var type = await MediaFileInspector.DetectAsync(cmd.Content, ct);
        if (type is null || type.Category != MediaCategory.Video)
            return Result<string>.Failure(Error.Validation("UnsupportedMediaType", "صيغة الفيديو غير مدعومة (MP4/WebM فقط)"));

        var url = await _storage.SaveAsync(cmd.Content, "videos", type.Extension, ct);
        product.SetVideoUrl(url);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(url);
    }
}
