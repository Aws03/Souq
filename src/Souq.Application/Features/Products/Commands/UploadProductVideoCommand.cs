using MediatR;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Models;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Products.Commands;

// رفع فيديو منتج قائم — مطابق تماماً لـ UploadProductImageCommand في الشكل،
// لكن عبر IVideoStorage (مجلّد ونوع محتوى مختلفان تماماً).
public record UploadProductVideoCommand(int ProductId, Stream Content, string FileName)
    : IRequest<Result<string>>;

public class UploadProductVideoHandler : IRequestHandler<UploadProductVideoCommand, Result<string>>
{
    private readonly IProductRepository _products;
    private readonly IVideoStorage _storage;
    private readonly IUnitOfWork _uow;

    public UploadProductVideoHandler(IProductRepository products, IVideoStorage storage, IUnitOfWork uow)
    {
        _products = products; _storage = storage; _uow = uow;
    }

    public async Task<Result<string>> Handle(UploadProductVideoCommand cmd, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(cmd.ProductId, ct);
        if (product is null)
            return Result<string>.Failure("المنتج غير موجود", "NotFound");

        var url = await _storage.SaveAsync(cmd.Content, cmd.FileName, ct);
        product.SetVideoUrl(url);
        _products.Update(product);
        await _uow.SaveChangesAsync(ct);

        return Result<string>.Success(url);
    }
}
