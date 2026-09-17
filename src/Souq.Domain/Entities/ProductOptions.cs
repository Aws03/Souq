using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.Platform;

namespace Souq.Domain.Entities;

// ============================================================================
// خيارات المنتج وقيمها (P-08a، ADR-0040): أبعاد مسمّاة (مقاس، لون…) لكلٍّ منها قائمة قيم قصيرة، ولكل متغيّر قيمة واحدة من كل
// خيار. كلها أبناء تجمّع Product — تُنشأ وتُعدَّل عبر Product.SetOptions وحده، وتحمل متجرها، ونصوصها لكل لغة (D-10).
// ============================================================================

public class ProductOption : Entity, ITenantOwned
{
    private readonly List<ProductOptionTranslation> _translations = new();
    private readonly List<ProductOptionValue> _values = new();

    public int TenantId { get; private set; }
    public int Position { get; private set; }
    public IReadOnlyCollection<ProductOptionTranslation> Translations => _translations.AsReadOnly();
    public IReadOnlyCollection<ProductOptionValue> Values => _values.AsReadOnly();

    private ProductOption() { }

    internal ProductOption(int position, IReadOnlyDictionary<string, string> names)
    {
        Position = position;
        SetNames(names);
    }

    public string NameIn(string? culture) => OptionTranslation.NameIn(_translations, culture);

    internal void SetPosition(int position) => Position = position;

    internal void SetNames(IReadOnlyDictionary<string, string> names) =>
        OptionTranslation.Replace(_translations, names, (culture, name) => new ProductOptionTranslation(culture, name));

    internal ProductOptionValue AddValue(int position, IReadOnlyDictionary<string, string> names)
    {
        var value = new ProductOptionValue(position, names);
        _values.Add(value);
        return value;
    }

    internal void RemoveValue(ProductOptionValue value) => _values.Remove(value);
}

public class ProductOptionValue : Entity, ITenantOwned
{
    private readonly List<ProductOptionValueTranslation> _translations = new();

    public int TenantId { get; private set; }
    public int Position { get; private set; }

    // هوية ثابتة تولد مع القيمة (لا معرّف القاعدة الذي لا يوجد قبل الحفظ): منها يُبنى مفتاح تركيبة المتغيّر في الذاكرة، فلا
    // يتغيّر المفتاح بإعادة الترتيب، ويُفرض تفرّد التركيبة بفهرس في القاعدة حتى لقيمة أُضيفت في الحفظ نفسه.
    public Guid Key { get; private set; }

    public IReadOnlyCollection<ProductOptionValueTranslation> Translations => _translations.AsReadOnly();

    private ProductOptionValue() { }

    internal ProductOptionValue(int position, IReadOnlyDictionary<string, string> names)
    {
        Position = position;
        Key = Guid.NewGuid();
        SetNames(names);
    }

    public string NameIn(string? culture) => OptionTranslation.NameIn(_translations, culture);

    internal void SetPosition(int position) => Position = position;

    internal void SetNames(IReadOnlyDictionary<string, string> names) =>
        OptionTranslation.Replace(_translations, names, (culture, name) => new ProductOptionValueTranslation(culture, name));
}

// قيمة خيار يحملها متغيّر (ابن المتغيّر). المرجع إلى القيمة داخل المتجر نفسه في القاعدة، ومقيَّد: قيمة يستخدمها متغيّر لا تُحذف.
public class ProductVariantOptionValue : Entity, ITenantOwned
{
    public int TenantId { get; private set; }
    public int OptionValueId { get; private set; }
    public ProductOptionValue OptionValue { get; private set; } = default!;

    private ProductVariantOptionValue() { }

    internal ProductVariantOptionValue(ProductOptionValue value) => OptionValue = value;
}

// ── نصوص الخيارات ─────────────────────────────────────────────────────────────

// اسم خيار أو قيمة بلغة واحدة. أخفّ من CatalogTranslation (لا وصف ولا SEO): الاسم وحده يظهر في المتجر وفي لقطة سطر الطلب.
public abstract class OptionTranslation : Entity, ITenantOwned
{
    public const int NameMaxLength = 50;

    public int TenantId { get; private set; }
    public string Culture { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    protected OptionTranslation() { }

    protected OptionTranslation(string culture, string name)
    {
        Culture = culture;
        Name = name;
    }

    // المدخل مُطبَّع مسبقاً (OptionNames.Normalize): يحدّث الموجود، يضيف الجديد، ويحذف لغة لم تعد مُدخلة.
    internal static void Replace<T>(List<T> current, IReadOnlyDictionary<string, string> names, Func<string, string, T> create)
        where T : OptionTranslation
    {
        current.RemoveAll(t => !names.ContainsKey(t.Culture));
        foreach (var (culture, name) in names)
        {
            var existing = current.FirstOrDefault(t => t.Culture == culture);
            if (existing is null) current.Add(create(culture, name));
            else existing.Name = name;
        }
    }

    // القاعدة نفسها لاسم المنتج: اللغة المطلوبة، وإلا أول لغة بترتيب ثابت.
    internal static string NameIn<T>(IEnumerable<T> translations, string? culture) where T : OptionTranslation
    {
        var list = translations as IReadOnlyCollection<T> ?? translations.ToList();
        return (culture is null ? null : list.FirstOrDefault(t => t.Culture == culture))?.Name
               ?? list.OrderBy(t => t.Culture, StringComparer.Ordinal).FirstOrDefault()?.Name
               ?? "";
    }
}

public sealed class ProductOptionTranslation : OptionTranslation
{
    private ProductOptionTranslation() { }
    internal ProductOptionTranslation(string culture, string name) : base(culture, name) { }
}

public sealed class ProductOptionValueTranslation : OptionTranslation
{
    private ProductOptionValueTranslation() { }
    internal ProductOptionValueTranslation(string culture, string name) : base(culture, name) { }
}

// ── المدخلات ─────────────────────────────────────────────────────────────────

// تعريف الخيارات الكامل كما يريده المدير، بالترتيب. Id لخيار أو قيمة قائمة (تعديل اسمها وموضعها)، وnull لجديد.
// ExistingVariantsValue: لخيار جديد وحده — موضع القيمة (في Values) التي تأخذها متغيّرات المنتج القائمة؛ كل متغيّر يحمل
// قيمة من كل خيار، والمدير يختارها صراحةً بدل أن تُفترض.
public sealed record ProductOptionDefinition(
    int? Id, IReadOnlyDictionary<string, string> Names, IReadOnlyList<ProductOptionValueDefinition> Values,
    int? ExistingVariantsValue = null);

public sealed record ProductOptionValueDefinition(int? Id, IReadOnlyDictionary<string, string> Names);

// أسماء الخيارات والقيم: لغات مدعومة، مقصوصة، غير فارغة، ضمن الحدّ، وبلا محارف تحكّم — تصل لسطر الطلب ورسائل البريد
// (قاعدة اسم المتجر نفسها).
public static class OptionNames
{
    internal static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? names, string what)
    {
        if (names is null || names.Count == 0)
            throw ProductVariantRules.Invalid("OptionNameRequired", $"{what}: الاسم مطلوب بلغة واحدة على الأقل");

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, name) in names)
        {
            var culture = key?.Trim().ToLowerInvariant() ?? "";
            if (!Tenant.SupportedCultures.Contains(culture))
                throw ProductVariantRules.Invalid("OptionNameRequired", $"لغة غير مدعومة: {key}");
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0)
                throw ProductVariantRules.Invalid("OptionNameRequired", $"{what}: الاسم مطلوب لكل لغة مُدخلة");
            if (trimmed.Length > OptionTranslation.NameMaxLength)
                throw ProductVariantRules.Invalid("OptionNameTooLong", $"{what}: الاسم حتى {OptionTranslation.NameMaxLength} حرفاً");
            if (trimmed.Any(char.IsControl))
                throw ProductVariantRules.Invalid("OptionNameInvalid", $"{what}: الاسم يحتوي محارف غير مسموحة");
            normalized[culture] = trimmed;
        }
        return normalized;
    }

    // اسمان متطابقان في أي لغة مشتركة (بلا اعتبار لحالة الأحرف) ⇒ المدير والعميل لا يميّزان بينهما.
    internal static bool Clash(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Any(pair => b.TryGetValue(pair.Key, out var other) && string.Equals(pair.Value, other, StringComparison.OrdinalIgnoreCase));
}

// وصف المتغيّر: أسماء قيمه بترتيب الخيارات مفصولة بـ " / " ("M / أحمر")، كلٌّ بلغة مطلوبة وإلا أول لغة. قاعدة واحدة يستخدمها
// Product.VariantLabel (لقطة سطر الطلب، الإشعار) وإسقاطات القراءة التي تبني الوصف من SQL (الجرد).
public static class VariantLabels
{
    public const string Separator = " / ";

    public static string? Compose(IEnumerable<IReadOnlyDictionary<string, string>> valueNamesInOptionOrder, string? culture)
    {
        var names = valueNamesInOptionOrder
            .Select(names => (culture is not null && names.TryGetValue(culture, out var name) ? name : null)
                             ?? names.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Value).FirstOrDefault())
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
        return names.Count == 0 ? null : string.Join(Separator, names);
    }
}

internal static class ProductVariantRules
{
    public static InvalidProductVariantException Invalid(string code, string message) => new(code, message);
}
