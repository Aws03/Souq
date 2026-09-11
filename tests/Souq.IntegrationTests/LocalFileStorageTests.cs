using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Souq.Infrastructure.Services;

namespace Souq.IntegrationTests;

// محوّل التخزين المحلي خلف IFileStorage: اسم عشوائي، امتداد من النوع المكتشَف (لا من العميل)،
// ومسار لا يخرج من الجذر أبداً (ADR-0016). بديل سحابي يجب أن يحقّق العقد نفسه.
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"souq-storage-{Guid.NewGuid():N}");

    private LocalFileStorage Storage() =>
        new(Options.Create(new FileStorageOptions { RootPath = _root, PublicBasePath = "/uploads" }));

    [Fact]
    public async Task يحفظ_تحت_المجلّد_باسم_عشوائي_ويعيد_المسار_العام()
    {
        var url = await Storage().SaveAsync(new MemoryStream([1, 2, 3]), "images", ".png");

        url.Should().MatchRegex(@"^/uploads/images/[0-9a-f]{32}\.png$");
        (await File.ReadAllBytesAsync(Path.Combine(_root, "images", Path.GetFileName(url)))).Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("images/..")]
    [InlineData("IMAGES")]
    [InlineData("")]
    public async Task مجلّد_غير_صالح_يُرفض_ولا_يُكتب_شيء(string folder)
    {
        var act = () => Storage().SaveAsync(new MemoryStream([1]), folder, ".png");

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Theory]
    [InlineData("png")]
    [InlineData(".html5x")]
    [InlineData(".p/ng")]
    [InlineData("")]
    public async Task امتداد_غير_صالح_يُرفض(string extension)
    {
        var act = () => Storage().SaveAsync(new MemoryStream([1]), "images", extension);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
