using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Souq.Application.Common.Interfaces;

namespace Souq.Infrastructure.Services;

// إعدادات التخزين المحلي. RootPath هو المسار الفيزيائي لمجلد الرفع (تضبطه طبقة
// الـ API من Storage:Local:RootPath أو wwwroot/uploads)، وPublicBasePath هو البادئة العامة.
public class FileStorageOptions
{
    public string RootPath { get; set; } = "";
    public string PublicBasePath { get; set; } = "/uploads";
}

// ============================================================================
// LocalFileStorage — تنفيذ تطوير/نشر بسيط: يحفظ الملف على القرص ويعيد مساره العام.
// الإنتاج السحابي يستبدله بتنفيذ Blob خلف نفس IFileStorage (المرحلة 5).
// الاسم عشوائي (Guid) والامتداد يأتي من النوع المكتشَف — لا شيء من مدخلات العميل
// يصل إلى مسار الملف. نتحقّق دفاعياً من صيغة المجلّد والامتداد أيضاً (لا "../").
// ============================================================================
public partial class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _opts;
    public LocalFileStorage(IOptions<FileStorageOptions> opts) => _opts = opts.Value;

    public async Task<string> SaveAsync(Stream content, string folder, string extension, CancellationToken ct = default)
    {
        if (!FolderPattern().IsMatch(folder))
            throw new ArgumentException($"اسم مجلّد غير صالح: {folder}", nameof(folder));
        if (!ExtensionPattern().IsMatch(extension))
            throw new ArgumentException($"امتداد غير صالح: {extension}", nameof(extension));

        var directory = Path.Combine(_opts.RootPath, folder);
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        await using var fs = File.Create(Path.Combine(directory, storedName));
        await content.CopyToAsync(fs, ct);

        return $"{_opts.PublicBasePath}/{folder}/{storedName}";
    }

    [GeneratedRegex("^[a-z]{1,32}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^\.[a-z0-9]{1,5}$")]
    private static partial Regex ExtensionPattern();
}
