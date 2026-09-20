using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Exceptions;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Platform;
using Souq.Infrastructure.Services;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// محوّل التخزين المحلي خلف IFileStorage: اسم عشوائي، امتداد من النوع المكتشَف (لا من العميل)،
// ومسار لا يخرج من الجذر أبداً (ADR-0016). بديل سحابي يجب أن يحقّق العقد نفسه.
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"souq-storage-{Guid.NewGuid():N}");

    private LocalFileStorage Storage(int tenantId = 7) =>
        new(Options.Create(new FileStorageOptions { RootPath = _root, PublicBasePath = "/uploads" }),
            SouqApiFactory.ContextFor(new TenantInfo(tenantId, "store", "متجر", TenantStatus.Active, "JOD", "ar", "Asia/Amman", new HashSet<string>(StringComparer.Ordinal))));

    [Fact]
    public async Task يحفظ_تحت_بادئة_المتجر_باسم_عشوائي_ويعيد_المسار_العام()
    {
        var url = await Storage(tenantId: 7).SaveAsync(new MemoryStream([1, 2, 3]), "images", ".png");

        url.Should().MatchRegex(@"^/uploads/tenants/7/images/[0-9a-f]{32}\.png$");
        (await File.ReadAllBytesAsync(Path.Combine(_root, "tenants", "7", "images", Path.GetFileName(url))))
            .Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task بلا_متجر_لا_يُحفَظ_شيء()
    {
        var storage = new LocalFileStorage(
            Options.Create(new FileStorageOptions { RootPath = _root, PublicBasePath = "/uploads" }), new TenantContext());

        var act = () => storage.SaveAsync(new MemoryStream([1]), "images", ".png");

        await act.Should().ThrowAsync<TenantContextMissingException>();
        Directory.Exists(_root).Should().BeFalse();
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
