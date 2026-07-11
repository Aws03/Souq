using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// رفع صورة منتج قائم. يستقبل المحتوى كـ Stream (تفاصيل HTTP/IFormFile تبقى في
// الـ Controller). يخزّن الملف عبر IFileStorage ويحفظ رابطه في المنتج، ويُعيده.
public record UploadProductImageCommand(int ProductId, Stream Content, string FileName)
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
            return Result<string>.Failure("المنتج غير موجود", "NotFound");

        var url = await _storage.SaveAsync(cmd.Content, cmd.FileName, ct);
        product.SetImageUrl(url);
        _products.Update(product);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(url);
    }
}
