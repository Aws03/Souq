using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// إعدادات التخزين المحلي. RootPath هو المسار الفيزيائي لمجلد الرفع (تضبطه طبقة
// الـ API حيث يُعرف wwwroot)، وPublicBasePath هو البادئة العامة في الرابط.
public class FileStorageOptions
{
    public string RootPath { get; set; } = "";
    public string PublicBasePath { get; set; } = "/uploads";
}

// ============================================================================
// LocalFileStorage — تنفيذ تطوير: يحفظ الملف على القرص تحت wwwroot/uploads
// ويعيد مساره العام. حلّ مؤقت للتطوير؛ الإنتاج (المرحلة 7) يستبدله بتنفيذ سحابي
// (S3/Azure Blob) خلف نفس IFileStorage دون لمس أي منطق أعمال.
// اسم الملف عشوائي (Guid) لتفادي التصادم وحقن المسار عبر اسم أصلي خبيث.
// ============================================================================
public class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _opts;
    public LocalFileStorage(IOptions<FileStorageOptions> opts) => _opts = opts.Value;

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_opts.RootPath);

        // نأخذ الامتداد فقط من الاسم الأصلي (لا نثق ببقيته)، ونولّد اسماً آمناً.
        var ext = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_opts.RootPath, storedName);

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);

        return $"{_opts.PublicBasePath}/{storedName}";
    }
}
