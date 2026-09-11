using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// Phase 0 B3 / ADR-0016: ملف HTML مُعلَن image/png كان يُخزَّن .html ويُخدَم من أصل
// الموقع نفسه ⇒ XSS مخزَّن. نثبت المسار كاملاً: الرفع، التخزين، والخدمة الساكنة.
[Collection(IntegrationCollection.Name)]
public class UploadSecurityTests
{
    private static readonly byte[] PngBytes =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
    };

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public UploadSecurityTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task HTML_متنكّر_كصورة_يُرفض_ولا_يُكتب_على_القرص()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin);
        // لقطة قبل/بعد: المجلّد مشترك بين الاختبارات، فنتحقّق أن هذا الرفع تحديداً لم يكتب شيئاً.
        var before = UploadedFiles().ToHashSet();

        var response = await admin.PostAsync($"/api/products/{productId}/image",
            File(Encoding.UTF8.GetBytes("<html><script>alert(localStorage.souq_token)</script></html>"), "evil.html", "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))!.Code.Should().Be("UnsupportedMediaType");
        UploadedFiles().Except(before).Should().BeEmpty();
    }

    [Fact]
    public async Task صورة_حقيقية_تُخزَّن_بامتداد_من_محتواها_وتُخدَم_بترويسات_آمنة()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin);

        // اسم الملف ونوعه من العميل كاذبان عمداً — لا يؤثّران على التخزين.
        var response = await admin.PostAsync($"/api/products/{productId}/image", File(PngBytes, "photo.html", "text/html"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var imageUrl = (await response.Content.ReadFromJsonAsync<ImageBody>(TestApi.Json))!.ImageUrl;
        imageUrl.Should().StartWith("/uploads/images/").And.EndWith(".png");

        var served = await _api.Anonymous().GetAsync(imageUrl);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        served.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        served.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");
    }

    [Fact]
    public async Task ملف_غير_وسائط_في_مجلّد_الرفع_لا_يُخدَم_إطلاقاً()
    {
        // دفاع في العمق: حتى لو وصل ملف HTML للمجلّد بطريقة أخرى، لا يُخدَم كصفحة.
        _api.Anonymous();
        var imagesDir = Path.Combine(_factory.UploadsRoot, "images");
        Directory.CreateDirectory(imagesDir);
        await System.IO.File.WriteAllTextAsync(Path.Combine(imagesDir, "planted.html"), "<script>alert(1)</script>");

        (await _api.Anonymous().GetAsync("/uploads/images/planted.html")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private IEnumerable<string> UploadedFiles() => Directory.Exists(_factory.UploadsRoot)
        ? Directory.EnumerateFiles(_factory.UploadsRoot, "*", SearchOption.AllDirectories)
        : [];

    private static MultipartFormDataContent File(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private sealed record ImageBody(string ImageUrl);
}
