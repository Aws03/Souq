using Souq.Domain.Platform;

namespace Souq.Domain.ValueObjects;

// ============================================================================
// نص كتالوج بلغة واحدة (D-10، المرحلة 5): جداول ترجمة بدل عمودَي NameAr/NameEn — لغات المتجر من إعداداته لا
// من المخطّط. الاسم، الوصف (نص عادي — الوصف الغني بمنقٍّ HTML مؤجَّل)، وعنوان ووصف SEO.
// كائن قيمة ثابت (مثل Money): يُقارَن بمحتواه، ويُطبَّع ويُتحقَّق منه عند إسناده لتجمّع (منتج أو فئة).
// ============================================================================
public sealed record CatalogText
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MetaTitleMaxLength = 70;
    public const int MetaDescriptionMaxLength = 160;

    public string Name { get; }
    public string? Description { get; }
    public string? MetaTitle { get; }
    public string? MetaDescription { get; }

    public CatalogText(string name, string? description = null, string? metaTitle = null, string? metaDescription = null)
    {
        Name = name;
        Description = description;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
    }

    // يُطبَّع عند الإسناد للتجمّع: مقصوص، الفارغ null، وحدود الطول — المصدر قد يكون أي مدخل.
    internal CatalogText Normalized(Func<string, Exception> invalid)
    {
        var name = Name?.Trim() ?? "";
        if (name.Length == 0) throw invalid("الاسم مطلوب لكل لغة مُدخلة");
        if (name.Length > NameMaxLength) throw invalid($"الاسم يتجاوز {NameMaxLength} حرفاً");
        return new CatalogText(name,
            Optional(Description, DescriptionMaxLength, "الوصف", invalid),
            Optional(MetaTitle, MetaTitleMaxLength, "عنوان SEO", invalid),
            Optional(MetaDescription, MetaDescriptionMaxLength, "وصف SEO", invalid));
    }

    private static string? Optional(string? value, int max, string field, Func<string, Exception> invalid)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : throw invalid($"{field} يتجاوز {max} حرفاً");
    }

    // مفاتيح اللغات المدعومة وحدها، بلا تكرار، ولغة واحدة على الأقل.
    internal static Dictionary<string, CatalogText> NormalizeAll(
        IReadOnlyDictionary<string, CatalogText>? texts, Func<string, Exception> invalid)
    {
        if (texts is null || texts.Count == 0) throw invalid("الاسم مطلوب بلغة واحدة على الأقل");
        var normalized = new Dictionary<string, CatalogText>(StringComparer.Ordinal);
        foreach (var (key, text) in texts)
        {
            var culture = key?.Trim().ToLowerInvariant() ?? "";
            if (!Tenant.SupportedCultures.Contains(culture)) throw invalid($"لغة غير مدعومة: {key}");
            if (text is null) throw invalid($"نص اللغة {culture} مطلوب");
            normalized[culture] = text.Normalized(invalid);
        }
        return normalized;
    }
}
