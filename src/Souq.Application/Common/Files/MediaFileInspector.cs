namespace Souq.Application.Common.Files;

public enum MediaCategory { Image, Video, Icon }

// نوع وسائط مكتشَف فعلياً: الامتداد الذي سيُخزَّن به الملف يُشتقّ من هنا حصراً.
public sealed record MediaFileType(string Extension, string ContentType, MediaCategory Category);

// ============================================================================
// MediaFileInspector — يكشف نوع الملف من "توقيعه" (البايتات الأولى)، لا من اسمه
// ولا من Content-Type الذي يرسله العميل؛ كلاهما يتحكّم به المهاجم. قبل Phase 1A
// كان ملف x.html مُعلَن image/png يُخزَّن .html ويُخدَم من أصل الموقع نفسه ⇒ XSS
// مخزَّن يقرأ التوكن (Phase 0 B3, ADR-0016). أي توقيع غير مدرج هنا مرفوض — بما فيه
// SVG (قد يحمل سكربت) وHTML وأي ملف متنكّر.
// ============================================================================
public static class MediaFileInspector
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxVideoBytes = 50 * 1024 * 1024;
    private const int HeaderLength = 16;

    public static readonly MediaFileType Jpeg = new(".jpg", "image/jpeg", MediaCategory.Image);
    public static readonly MediaFileType Png = new(".png", "image/png", MediaCategory.Image);
    public static readonly MediaFileType Gif = new(".gif", "image/gif", MediaCategory.Image);
    public static readonly MediaFileType Webp = new(".webp", "image/webp", MediaCategory.Image);
    public static readonly MediaFileType Mp4 = new(".mp4", "video/mp4", MediaCategory.Video);
    public static readonly MediaFileType Webm = new(".webm", "video/webm", MediaCategory.Video);
    // أيقونة المتجر (favicon) فقط — فئة مستقلّة كي لا تُقبل ICO صورةً لمنتج.
    public static readonly MediaFileType Ico = new(".ico", "image/x-icon", MediaCategory.Icon);

    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] IcoMagic = { 0x00, 0x00, 0x01, 0x00 };
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] EbmlMagic = { 0x1A, 0x45, 0xDF, 0xA3 };

    // يقرأ الترويسة ثم يعيد التدفّق لبدايته — التخزين بعدها يحتاج الملف كاملاً.
    public static async Task<MediaFileType?> DetectAsync(Stream content, CancellationToken ct = default)
    {
        if (!content.CanSeek)
            throw new ArgumentException("تدفّق الملف يجب أن يدعم الرجوع لبدايته", nameof(content));

        var header = new byte[HeaderLength];
        var read = await content.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, ct);
        content.Position = 0;
        return Detect(header.AsSpan(0, read));
    }

    public static MediaFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegMagic)) return Jpeg;
        if (header.StartsWith(PngMagic)) return Png;
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return Gif;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return Webp;
        // حاوية ISO BMFF (MP4/MOV): "ftyp" عند الإزاحة 4.
        if (header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8)) return Mp4;
        // حاوية EBML (WebM/Matroska).
        if (header.StartsWith(EbmlMagic)) return Webm;
        // بعد MP4 عمداً: صندوق MP4 بطول 256 يبدأ بالبايتات نفسها (00 00 01 00) ثم "ftyp".
        if (header.StartsWith(IcoMagic)) return Ico;
        return null;
    }
}
