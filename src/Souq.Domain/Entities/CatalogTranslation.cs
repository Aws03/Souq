using System.Text.RegularExpressions;
using Souq.Domain.Common;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// صف ترجمة لتجمّع (منتج أو فئة) — ابن لا يُنشأ إلا عبر جذره، بمفتاح ظلّ إلى الجذر، ويحمل متجره كبقية الأبناء.
// أساس مجرّد غير مُعيَّن في EF (مثل Entity): لكل ابن جدوله. النص نفسه كائن قيمة (CatalogText) يُطبَّع قبل الإسناد.
public abstract class CatalogTranslation : Entity, ITenantOwned
{
    public const int CultureMaxLength = 10;

    public int TenantId { get; private set; }
    public string Culture { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }

    protected CatalogTranslation() { }

    protected CatalogTranslation(string culture, CatalogText text)
    {
        Culture = culture;
        Apply(text);
    }

    internal void Apply(CatalogText text)
    {
        Name = text.Name;
        Description = text.Description;
        MetaTitle = text.MetaTitle;
        MetaDescription = text.MetaDescription;
    }

    public CatalogText ToText() => new(Name, Description, MetaTitle, MetaDescription);

    // يستبدل ترجمات جذر بالمجموعة المُطبَّعة: يحدّث الموجود، يضيف الجديد، ويحذف لغة لم تعد مُدخلة.
    internal static void Replace<T>(List<T> current, IReadOnlyDictionary<string, CatalogText> texts, Func<string, CatalogText, T> create)
        where T : CatalogTranslation
    {
        current.RemoveAll(t => !texts.ContainsKey(t.Culture));
        foreach (var (culture, text) in texts)
        {
            var existing = current.FirstOrDefault(t => t.Culture == culture);
            if (existing is null) current.Add(create(culture, text));
            else existing.Apply(text);
        }
    }

    // الاسم بلغة مطلوبة، وإلا العربية ثم أول لغة (لقطة سطر الطلب تمرّر لغة المتجر الافتراضية).
    internal static string NameIn<T>(IEnumerable<T> translations, string? culture) where T : CatalogTranslation
    {
        var list = translations as IReadOnlyCollection<T> ?? translations.ToList();
        return (culture is null ? null : list.FirstOrDefault(t => t.Culture == culture))?.Name
               ?? list.OrderBy(t => t.Culture, StringComparer.Ordinal).FirstOrDefault()?.Name
               ?? "";
    }
}

public sealed class ProductTranslation : CatalogTranslation
{
    private ProductTranslation() { }
    internal ProductTranslation(string culture, CatalogText text) : base(culture, text) { }
}

public sealed class CategoryTranslation : CatalogTranslation
{
    private CategoryTranslation() { }
    internal CategoryTranslation(string culture, CatalogText text) : base(culture, text) { }
}

// معرّف الرابط (slug) للمنتجات والفئات: أحرف لاتينية صغيرة وأرقام وشرطات — فريد داخل المتجر (فهرس في القاعدة).
public static partial class CatalogSlug
{
    public static string? TryNormalize(string? slug, int maxLength)
    {
        var normalized = slug?.Trim().ToLowerInvariant() ?? "";
        return normalized.Length >= 2 && normalized.Length <= maxLength && Pattern().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
