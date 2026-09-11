using System.Text;
using AwesomeAssertions;
using NSubstitute;
using Souq.Application.Common.Files;
using Souq.Application.Common.Interfaces;
using Souq.Application.Features.Products.Commands;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Tests.Products;

public class UploadProductMediaHandlerTests
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
    private static readonly byte[] WebmBytes = { 0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81 };
    private static readonly byte[] HtmlBytes = Encoding.UTF8.GetBytes("<html><script>fetch('/steal?t='+localStorage.souq_token)</script>");

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static Product NewProduct() => Souq.Application.Tests.TestDoubles.TestCatalog.Product();

    private UploadProductImageHandler ImageHandler() => new(_products, _storage, _uow);
    private UploadProductVideoHandler VideoHandler() => new(_products, _storage, _uow);

    [Fact]
    public async Task صورة_PNG_حقيقية_تُخزَّن_بامتداد_مشتقّ_من_المحتوى_لا_من_العميل()
    {
        var product = NewProduct();
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _storage.SaveAsync(Arg.Any<Stream>(), "images", ".png", Arg.Any<CancellationToken>())
            .Returns("/uploads/images/abc.png");

        var result = await ImageHandler().Handle(
            new UploadProductImageCommand(1, new MemoryStream(PngBytes), PngBytes.Length), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().Be("/uploads/images/abc.png");
        product.PrimaryImageUrl.Should().Be("/uploads/images/abc.png");
        await _storage.Received(1).SaveAsync(Arg.Any<Stream>(), "images", ".png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ملف_HTML_متنكّر_كصورة_يُرفض_ولا_يُخزَّن_إطلاقاً()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await ImageHandler().Handle(
            new UploadProductImageCommand(1, new MemoryStream(HtmlBytes), HtmlBytes.Length), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("UnsupportedMediaType");
        await _storage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task فيديو_مرفوع_عبر_نقطة_الصور_يُرفض()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await ImageHandler().Handle(
            new UploadProductImageCommand(1, new MemoryStream(WebmBytes), WebmBytes.Length), CancellationToken.None);

        result.ErrorCode.Should().Be("UnsupportedMediaType");
    }

    [Fact]
    public async Task صورة_أكبر_من_الحدّ_تُرفض()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await ImageHandler().Handle(
            new UploadProductImageCommand(1, new MemoryStream(PngBytes), MediaFileInspector.MaxImageBytes + 1),
            CancellationToken.None);

        result.ErrorCode.Should().Be("FileTooLarge");
        await _storage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task منتج_غير_موجود_يُرجع_NotFound()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await ImageHandler().Handle(
            new UploadProductImageCommand(1, new MemoryStream(PngBytes), PngBytes.Length), CancellationToken.None);

        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task فيديو_WebM_يُخزَّن_في_مجلّد_الفيديو_بامتداد_webm()
    {
        var product = NewProduct();
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(product);
        _storage.SaveAsync(Arg.Any<Stream>(), "videos", ".webm", Arg.Any<CancellationToken>())
            .Returns("/uploads/videos/v.webm");

        var result = await VideoHandler().Handle(
            new UploadProductVideoCommand(1, new MemoryStream(WebmBytes), WebmBytes.Length), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        product.VideoUrl.Should().Be("/uploads/videos/v.webm");
    }

    [Fact]
    public async Task صورة_مرفوعة_عبر_نقطة_الفيديو_تُرفض()
    {
        _products.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await VideoHandler().Handle(
            new UploadProductVideoCommand(1, new MemoryStream(PngBytes), PngBytes.Length), CancellationToken.None);

        result.ErrorCode.Should().Be("UnsupportedMediaType");
    }
}
