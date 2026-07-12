using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// إعدادات تخزين الفيديو المحلي — RootPath/PublicBasePath منفصلان عن الصور
// (wwwroot/uploads/videos و /uploads/videos) رغم أنّ مزوّد الملفات الثابتة نفسه
// في Program.cs يخدم كليهما (videos مجرد مجلد فرعي تحت uploads الذي يُخدَّم أصلاً).
public class VideoStorageOptions
{
    public string RootPath { get; set; } = "";
    public string PublicBasePath { get; set; } = "/uploads/videos";
}

// تنفيذ تطوير مطابق لـ LocalFileStorage تماماً في الشكل، لكن لمجلّد الفيديوهات.
public class LocalVideoStorage : IVideoStorage
{
    private readonly VideoStorageOptions _opts;
    public LocalVideoStorage(IOptions<VideoStorageOptions> opts) => _opts = opts.Value;

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_opts.RootPath);

        var ext = Path.GetExtension(fileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(_opts.RootPath, storedName);

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);

        return $"{_opts.PublicBasePath}/{storedName}";
    }
}
