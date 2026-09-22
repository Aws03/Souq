using System.Globalization;
using System.Text.RegularExpressions;
using Souq.Domain.Exceptions;

namespace Souq.Domain.Platform;

// ============================================================================
// إعدادات واجهة المتجر (WhiteLabel.md §2، D-12) — قيم ثابتة يستبدلها Tenant كاملةً عبر دوال محروسة.
// التحقّق هنا مرّة واحدة لمسارَي التعديل (المنصّة عند التجهيز، ومدير المتجر بعد التسليم):
//   • نصوص لكل لغة مدعومة فقط وبحدود طول (اسم العرض، العنوان، SEO، شريط الإعلان).
//   • ألوان hex بتباين WCAG AA: النص على الخلفية، ونص الأزرار على الأساسي والمميّز — لوحة غير مقروءة
//     تُرفض ولا تُحفَظ.
//   • خطوط وقوالب من قائمة معتمدة — لا روابط خطوط ولا CSS حرّ (WhiteLabel.md §4–§5).
//   • روابط التواصل https لشبكة معروفة وعلى نطاقها — لا javascript: ولا روابط متنكّرة.
// الشعار والأيقونة وصورة المشاركة لا تأتي نصاً من العميل: يولّد الخادم مسارها عند رفع ملف مُتحقَّق منه.
// المُنشئات الداخلية بلا تحقّق لإعادة البناء من التخزين فقط (تشديد قاعدة لاحقاً لا يُسقط متجراً قائماً).
// ============================================================================
public sealed class StoreSettings
{
    public const int DisplayNameMaxLength = 80;
    public const int AnnouncementMaxLength = 200;
    public const int MaxSocialLinks = 8;

    public IReadOnlyDictionary<string, string> DisplayName { get; }
    public IReadOnlyList<string> EnabledCultures { get; }
    public StoreBranding Branding { get; }
    public StoreContact Contact { get; }
    public IReadOnlyList<SocialLink> Social { get; }
    public SeoSettings Seo { get; }
    public IReadOnlyDictionary<string, string> Announcement { get; }

    // روابط السياسات (TD-42). صفوف كُتبت قبل هذا الحقل تُقرأ بلا قيمة فتأخذ "لا روابط" — لا رابط
    // يظهر فجأةً في تذييل متجر لم يضبطه.
    public StorePolicyLinks Policies { get; } = StorePolicyLinks.Empty;

    // أقسام الرئيسية بترتيبها (C8). صفوف كُتبت قبل هذا الحقل تُقرأ بلا قيمة فتأخذ الترتيب
    // الافتراضي — وهو ترتيب الصفحة اليوم حرفياً، فلا تتغيّر رئيسيةُ متجرٍ قائم عند الترقية.
    public StoreSections Sections { get; } = StoreSections.Default;

    // نصوصٌ يُعيد المتجر تسميتها (C8). صفوفٌ كُتبت قبل هذا الحقل تُقرأ بلا تجاوزات — فلا نصّ
    // يتبدّل في متجرٍ لم يطلب تبديله.
    public StoreTextOverrides Texts { get; } = StoreTextOverrides.Empty;

    internal StoreSettings(
        IReadOnlyDictionary<string, string> displayName, IReadOnlyList<string> enabledCultures, StoreBranding branding,
        StoreContact contact, IReadOnlyList<SocialLink> social, SeoSettings seo, IReadOnlyDictionary<string, string> announcement,
        StorePolicyLinks? policies = null, StoreSections? sections = null, StoreTextOverrides? texts = null)
    {
        DisplayName = displayName; EnabledCultures = enabledCultures; Branding = branding;
        Contact = contact; Social = social; Seo = seo; Announcement = announcement;
        Policies = policies ?? StorePolicyLinks.Empty;
        Sections = sections ?? StoreSections.Default;
        Texts = texts ?? StoreTextOverrides.Empty;
    }

    // متجر جديد: اسمه بلغته الافتراضية وهوية محايدة مقروءة — والباقي حتى يضبطه أحد.
    public static StoreSettings Default(string storeName, string defaultCulture) => new(
        new Dictionary<string, string> { [defaultCulture] = storeName }, [defaultCulture], StoreBranding.Default,
        StoreContact.Empty, [], SeoSettings.Empty, new Dictionary<string, string>());

    internal StoreSettings With(
        IReadOnlyDictionary<string, string>? displayName = null, IReadOnlyList<string>? enabledCultures = null,
        StoreBranding? branding = null, StoreContact? contact = null, IReadOnlyList<SocialLink>? social = null,
        SeoSettings? seo = null, IReadOnlyDictionary<string, string>? announcement = null,
        StorePolicyLinks? policies = null, StoreSections? sections = null,
        StoreTextOverrides? texts = null) => new(
        displayName ?? DisplayName, enabledCultures ?? EnabledCultures, branding ?? Branding, contact ?? Contact,
        social ?? Social, seo ?? Seo, announcement ?? Announcement, policies ?? Policies, sections ?? Sections,
        texts ?? Texts);
}

// ============================================================================
// روابط سياسات المتجر — الخصوصية والشروط والإرجاع والشحن والأسئلة الشائعة.
//
// **قرار المالك TD-42 = C (2026-09-21): روابط الآن، وصفحات مؤلَّفة حين يطلبها تاجر حقيقي.** فالمنصّة
// تحفظ عنواناً واحداً لكل نوع، والنصّ نفسه يستضيفه التاجر حيث يشاء. تذييل المتجر يرسم رابطاً حين
// يُضبط وحده — والغياب أصدق من رابط ميّت، وهو السبب الذي حُذفت به روابط `href="#"` في المرحلة 16.
//
// الأنواع **قائمة مغلقة** لا نصّ حرّ: نوعٌ لا تعرفه الواجهة يُرسم مفتاحاً خامّاً بحروف لاتينية في
// تذييل متجر عربي، وهو بعينه العطب الذي وقع في أيقونات الشبكات. يحرس التطابقَ اختبار معمارية.
//
// https وحدها وعنوان مطلق: سياسةٌ على http في متجرٍ على https محتوى مختلط يحجبه المتصفّح، وقابلةٌ
// للتلاعب في الطريق — وهي الصفحة التي يُفترض أن يثق بها المشتري. ولا javascript: ولا مسار نسبيّ
// يُوهم بأنّ الصفحة من المنصّة.
// ============================================================================
public sealed class StorePolicyLinks
{
    public const int UrlMaxLength = 300;

    public static readonly IReadOnlyList<string> Kinds = ["privacy", "terms", "returns", "shipping", "faq"];

    public IReadOnlyDictionary<string, string> Urls { get; }

    internal StorePolicyLinks(IReadOnlyDictionary<string, string> urls) => Urls = urls;

    public static StorePolicyLinks Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public string? UrlFor(string kind) => Urls.TryGetValue(kind, out var url) ? url : null;

    public static StorePolicyLinks Create(IReadOnlyDictionary<string, string?>? urls)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (urls is null) return new StorePolicyLinks(result);

        foreach (var (key, raw) in urls)
        {
            var kind = key?.Trim().ToLowerInvariant() ?? "";
            if (!Kinds.Contains(kind))
                throw new InvalidTenantOperationException($"نوع سياسة غير مدعوم: {key}");

            var value = raw?.Trim() ?? "";
            if (value.Length == 0) continue;   // الفارغ حذف، لا رابط بلا هدف.
            if (value.Length > UrlMaxLength)
                throw new InvalidTenantOperationException($"رابط {kind} يتجاوز {UrlMaxLength} حرفاً");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidTenantOperationException($"رابط {kind} يجب أن يكون https كاملاً");
            result[kind] = uri.AbsoluteUri;
        }
        return new StorePolicyLinks(result);
    }
}

// ============================================================================
// أقسام الصفحة الرئيسية، مرتَّبةً ومُصنَّفة (C8، [ADR-0060](0060)).
//
// كانت الرئيسيةُ `JSX` ثابتاً: بانرٌ ثمّ صدارةٌ ثمّ صفّان ثمّ الكتالوج، بهذا الترتيب لكلّ متجر.
// والمكوّناتُ نفسها كانت تأخذ كلَّ ما تعرضه وسائطَ — أي أنّها **عارضاتٌ تنتظر وصفاً**، لا صفحة.
// وهذا الصنف هو الوصف: قائمةٌ مرتَّبة من أنواعٍ معروفة، كلٌّ منها مُفعَّلٌ أو لا.
//
// **والقائمةُ مُغلقة عمداً.** نوعُ قسمٍ جديد ميزةٌ تُضاف لكلّ المتاجر، لا فرعٌ لعميل — القاعدة
// نفسها التي تحكم القوالب والخطوط (WhiteLabel.md §4–§5). فلا يصل من العميل اسمُ مكوّنٍ يُرسَم.
//
// وثلاث قواعد تحرس ما لا يُصلحه ترتيب:
//   • **نوعٌ مجهول يُرفض** ولا يُتجاهَل: تجاهلُه يعني متجراً حفظ ترتيباً وحصل على غيره.
//   • **تكرارُ نوعٍ يُرفض**: «الأحدث» مرّتين في صفحةٍ واحدة خطأٌ لا تفضيل.
//   • **الكتالوج لا يُطفأ**: رئيسيةٌ بلا كتالوج رئيسيةٌ بلا منتجات — وهي الشاشةُ التي يصلها
//     الزائر أوّلاً. يُحرَّك موضعُه، ولا يُحذف.
//
// **وما لم يُذكر يُلحَق مُطفأً** — لا مُشغَّلاً. فنوعٌ يُضاف في إصدارٍ قادم لا يظهر من تلقاء نفسه
// في رئيسيةِ متجرٍ لم يطلبه، وهو المبدأ نفسه الذي يحكم `StoreModules`: لا قدرةَ تصل متجراً
// بالصدفة. وذلك يخصّ متجراً **ضبط** أقسامه؛ ومتجرٌ لم يضبطها قطّ يأخذ `Default` كاملاً.
// ============================================================================
public sealed record StoreSection(string Type, bool Enabled);

public sealed class StoreSections
{
    // الترتيبُ الافتراضي هو ترتيبُ `Storefront.jsx` اليوم حرفياً. تغييرُه هنا يُعيد ترتيب
    // رئيسيةِ كلّ متجرٍ لم يضبط أقسامه — فهو قرارُ منتجٍ لا تنظيفُ شيفرة.
    public const string Hero = "hero";
    public const string Featured = "featured";
    public const string NewArrivals = "newArrivals";
    public const string Offers = "offers";
    public const string Catalog = "catalog";

    public static readonly IReadOnlyList<string> Types = [Hero, Featured, NewArrivals, Offers, Catalog];

    // الأقسامُ التي لا يجوز إطفاؤها. مجموعةٌ لا حالةٌ خاصّة واحدة: قسمٌ ثانٍ يصير إلزامياً غداً
    // يُضاف هنا، لا في شرطٍ داخل حلقة.
    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.Ordinal) { Catalog };

    public IReadOnlyList<StoreSection> Items { get; }

    internal StoreSections(IReadOnlyList<StoreSection> items) => Items = items;

    public static StoreSections Default { get; } = new([.. Types.Select(type => new StoreSection(type, true))]);

    // ما يُرسَم فعلاً، بترتيبه. الواجهةُ تقرأ هذه لا `Items`: فلا تُعيد حساب «أيُّها مُفعَّل» في
    // كلّ عارض، ولا تختلف شاشتان في الجواب.
    public IReadOnlyList<string> EnabledTypes => [.. Items.Where(i => i.Enabled).Select(i => i.Type)];

    public static StoreSections Create(IReadOnlyList<StoreSection>? sections)
    {
        if (sections is null || sections.Count == 0) return Default;

        var ordered = new List<StoreSection>(Types.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in sections)
        {
            var type = section?.Type?.Trim() ?? "";
            var known = Types.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
            if (known is null)
                throw new InvalidTenantOperationException($"نوع قسم غير مدعوم: {section?.Type}");
            if (!seen.Add(known))
                throw new InvalidTenantOperationException($"القسم {known} مذكور أكثر من مرّة");

            var enabled = section!.Enabled;
            if (!enabled && Required.Contains(known))
                throw new InvalidTenantOperationException($"القسم {known} لا يُطفأ: رئيسيةٌ بدونه بلا منتجات");

            ordered.Add(new StoreSection(known, enabled));
        }

        // ما لم يُذكر يُلحَق مُطفأً — إلّا ما لا يُطفأ، فيُلحق مُشغَّلاً: متجرٌ حفظ ترتيباً لا
        // يذكر الكتالوج يجب أن يبقى له كتالوج، والرفضُ هنا كان سيعاقبه على عميلٍ قديم.
        foreach (var type in Types.Where(t => !seen.Contains(t)))
            ordered.Add(new StoreSection(type, Required.Contains(type)));

        return new StoreSections(ordered);
    }
}

// ============================================================================
// نصوصٌ يُعيد المتجر تسميتها (C8، [ADR-0062](0062)) — **بقائمةٍ مغلقة، وهذا هو القرار كلُّه.**
//
// تخصيصُ النصّ مطلبٌ حقيقيّ لمنتجٍ أبيض العلامة: متجرُ عطورٍ يقول «مجموعاتنا» لا «وصل حديثاً»،
// ومتجرُ جملةٍ يقول «طلبيّة» لا «سلّة». وحتى هنا القاعدةُ نفسها: التخصيصُ إعداد، لا فرع.
//
// **ولا يُفتح هذا لكلّ مفتاح.** ملفُّ الترجمة يحمل — إلى جانب نصوص العرض — رسائلَ الأخطاء،
// وما تقوله المنصّةُ عن نفسها، ونصوصَ الإتاحة التي يقرؤها قارئُ الشاشة وحده. تاجرٌ يُعيد كتابة
// «تعذّر إتمام الدفع» أو يُفرغ عنوان زرٍّ لا يراه إلّا الأعمى لا يُخصّص متجره — يكسره، وقد يكسره
// على مَن لا حيلة له. فالمسموحُ قائمةٌ منصوصة من **نصوص العرض وحدها**، ومفتاحٌ خارجها يُرفض
// ولا يُتجاهَل: تجاهلُه يعني تاجراً حفظ تسميةً ولم تظهر، بلا سببٍ يقرؤه.
//
// وتوسيعُ القائمة قرارٌ يُراجَع، لا سطرٌ يمرّ — وهو ما يجعلها قائمةً لا نمطاً.
// ============================================================================
public sealed class StoreTextOverrides
{
    public const int ValueMaxLength = 120;
    public const int MaxKeys = 40;

    // نصوصُ عرضٍ بحتة: عناوينُ أقسامٍ وأزرارُ دعوةٍ لا تحمل معنى قانونياً ولا تصف خطأً.
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "store.newArrivals", "store.bestSellers", "store.allProducts", "store.featuredTitle",
        "store.shopNow", "store.viewAll", "store.heroCta",
        "nav.offers", "nav.viewCart",
    };

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values { get; }

    internal StoreTextOverrides(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values) =>
        Values = values;

    public static StoreTextOverrides Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    public static StoreTextOverrides Create(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>?>? values)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (values is null) return new StoreTextOverrides(result);

        if (values.Count > MaxKeys)
            throw new InvalidTenantOperationException($"حتى {MaxKeys} نصّاً مُعاد تسميته");

        foreach (var (rawKey, byCulture) in values)
        {
            var key = rawKey?.Trim() ?? "";
            if (!Allowed.Contains(key))
                throw new InvalidTenantOperationException($"نصٌّ لا يُعاد تسميته: {rawKey}");

            // `LocalizedText` نفسها: اللغاتُ المدعومة وحدها، والفارغُ حذفٌ لا قيمةٌ فارغة —
            // فإفراغُ تسميةٍ يعيد النصّ الأصليّ بدل أن يترك زرّاً بلا كلمة.
            var texts = LocalizedText.Normalize(byCulture, ValueMaxLength, $"نصّ {key}");
            if (texts.Count > 0) result[key] = texts;
        }

        return new StoreTextOverrides(result);
    }
}

// نص لكل لغة: مفاتيح اللغات المدعومة فقط، قيم مقصوصة، الفارغ يُحذف (الواجهة تعود للّغة الافتراضية).
public static class LocalizedText
{
    public static IReadOnlyDictionary<string, string> Normalize(
        IReadOnlyDictionary<string, string?>? values, int maxLength, string field)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is null) return result;
        foreach (var (key, raw) in values)
        {
            var culture = key?.Trim().ToLowerInvariant() ?? "";
            if (!Tenant.SupportedCultures.Contains(culture))
                throw new InvalidTenantOperationException($"{field}: لغة غير مدعومة ({key})");
            var text = raw?.Trim() ?? "";
            if (text.Length == 0) continue;
            if (text.Length > maxLength)
                throw new InvalidTenantOperationException($"{field} يتجاوز {maxLength} حرفاً");
            result[culture] = text;
        }
        return result;
    }
}

// الخطوط والقوالب المعتمدة — مفاتيح تترجمها الواجهة إلى خطوط وتخطيطات (المرحلة 15). قالب جديد ميزة لكل
// المتاجر لا فرع لعميل.
public static class BrandPresets
{
    public const string DefaultTypography = "tajawal";
    public const string DefaultTheme = "classic";

    // تفضيل المتجر للوضع: فاتح، داكن، أو "اترك الأمر لنظام الزائر". القيمة الافتراضية system
    // عمداً — متجرٌ لم يقرّر لا يجب أن يفرض على زائرٍ ضوءاً في وضع ليليّ اختاره لجهازه كلّه.
    public const string DefaultThemeMode = "system";

    public static readonly IReadOnlyList<string> Typography = ["kufi-tajawal", "tajawal", "cairo", "almarai", "ibm-plex"];
    public static readonly IReadOnlyList<string> Themes = ["classic", "minimal", "bold"];
    public static readonly IReadOnlyList<string> ThemeModes = ["light", "dark", "system"];

    // أنماط كشف الافتتاح. القائمة موجودة كي تكون إضافة نمطٍ ثانٍ قراراً لكل المتاجر لا فرعاً
    // لعميل — وهي معطّلة افتراضياً: متجرٌ لم يطلب كشفاً يفتح فوراً.
    public static readonly IReadOnlyList<string> OpeningStyles = ["doors"];
    public const string DefaultOpeningStyle = "doors";
}

public enum BrandingAsset { Logo, Favicon, SocialImage }

// ============================================================================
// كشف الافتتاح: ترحيبٌ قصير يراه زائر المتجر أوّل مرّة. معطّل افتراضياً عمداً — لو كان مفعّلاً
// لصار حركةً إجبارية على كل متجر في المنصّة، وهو نقيض المقصود. الواجهة تفرض الباقي (مرّة لكل
// جلسة، لا مع تقليل الحركة، لا على رابط عميق).
// ============================================================================
public sealed record StoreOpening(bool Enabled, string Style)
{
    public static StoreOpening Disabled { get; } = new(false, BrandPresets.DefaultOpeningStyle);

    public static StoreOpening Create(bool enabled, string? style)
    {
        var chosen = style?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrEmpty(chosen) && !BrandPresets.OpeningStyles.Contains(chosen))
            throw new InvalidTenantOperationException($"نمط افتتاح غير معتمد: {style}");
        return new StoreOpening(enabled, string.IsNullOrEmpty(chosen) ? BrandPresets.DefaultOpeningStyle : chosen);
    }
}

public sealed class StoreBranding
{
    public BrandColors Colors { get; }
    public string Typography { get; }
    public string ThemePreset { get; }

    // تفضيل الوضع (فاتح/داكن/نظام الزائر). إعدادات المتجر عمود JSON، فالحقل الجديد يُضاف بلا
    // هجرة — وصفوف كُتبت قبله تُقرأ بلا الحقل فتأخذ الافتراضي (system) لا null.
    public string ThemeMode { get; } = BrandPresets.DefaultThemeMode;

    public string? LogoUrl { get; }
    public string? FaviconUrl { get; }
    public string? SocialImageUrl { get; }

    // كشف الافتتاح. صفوف كُتبت قبل هذا الحقل تُقرأ بلا قيمة فتأخذ "معطّل".
    public StoreOpening Opening { get; } = StoreOpening.Disabled;

    internal StoreBranding(BrandColors colors, string typography, string themePreset,
        string? logoUrl, string? faviconUrl, string? socialImageUrl, string? themeMode = null,
        StoreOpening? opening = null)
    {
        Colors = colors; Typography = typography; ThemePreset = themePreset;
        ThemeMode = Normalize(themeMode);
        Opening = opening ?? StoreOpening.Disabled;
        LogoUrl = logoUrl; FaviconUrl = faviconUrl; SocialImageUrl = socialImageUrl;
    }

    public static StoreBranding Default { get; } =
        new(BrandColors.Neutral, BrandPresets.DefaultTypography, BrandPresets.DefaultTheme, null, null, null);

    // الألوان والخط والقالب والوضع معاً؛ الملفات المرفوعة تبقى كما هي.
    internal StoreBranding WithStyle(BrandColors colors, string typography, string themePreset,
        string? themeMode = null, StoreOpening? opening = null)
    {
        var font = typography?.Trim().ToLowerInvariant() ?? "";
        if (!BrandPresets.Typography.Contains(font))
            throw new InvalidTenantOperationException($"خط غير معتمد: {typography}");
        var theme = themePreset?.Trim().ToLowerInvariant() ?? "";
        if (!BrandPresets.Themes.Contains(theme))
            throw new InvalidTenantOperationException($"قالب غير معتمد: {themePreset}");
        // وضع غير معروف يُرفض لا يُتجاهَل: قيمة صامتة تعني متجراً يظنّ أنه ضبط شيئاً ولم يفعل.
        if (themeMode is not null && !BrandPresets.ThemeModes.Contains(themeMode.Trim().ToLowerInvariant()))
            throw new InvalidTenantOperationException($"وضع عرض غير معتمد: {themeMode}");
        return new StoreBranding(colors, font, theme, LogoUrl, FaviconUrl, SocialImageUrl,
            themeMode ?? ThemeMode, opening ?? Opening);
    }

    internal StoreBranding WithAsset(BrandingAsset asset, string url) => asset switch
    {
        BrandingAsset.Logo => new(Colors, Typography, ThemePreset, url, FaviconUrl, SocialImageUrl, ThemeMode, Opening),
        BrandingAsset.Favicon => new(Colors, Typography, ThemePreset, LogoUrl, url, SocialImageUrl, ThemeMode, Opening),
        _ => new(Colors, Typography, ThemePreset, LogoUrl, FaviconUrl, url, ThemeMode, Opening),
    };

    private static string Normalize(string? mode)
    {
        var value = mode?.Trim().ToLowerInvariant() ?? "";
        return BrandPresets.ThemeModes.Contains(value) ? value : BrandPresets.DefaultThemeMode;
    }
}

// ============================================================================
// لوحة الألوان بتباين WCAG 2.x (نسبة الإضاءة النسبية). نص الأزرار فوق الأساسي/المميّز يُشتقّ: الأبيض أو
// لون النص أيّهما أوضح — وإن لم يبلغ أيّهما 4.5:1 فاللون لا يصلح خلفية لزرّ مقروء فيُرفض.
// ============================================================================
public sealed partial class BrandColors
{
    public const double MinimumTextContrast = 4.5;   // WCAG AA — نص عادي
    public const double MinimumUiContrast = 3.0;     // WCAG AA — عناصر واجهة ونص كبير
    private const string White = "#FFFFFF";

    public string Primary { get; }
    public string Secondary { get; }
    public string Accent { get; }
    public string Background { get; }
    public string Text { get; }

    internal BrandColors(string primary, string secondary, string accent, string background, string text)
    {
        Primary = primary; Secondary = secondary; Accent = accent; Background = background; Text = text;
    }

    // هوية محايدة لمتجر جديد (كحلي + كهرماني على أبيض) — تجتاز قواعد Create نفسها (اختبار وحدة).
    public static BrandColors Neutral { get; } = new("#1E3A5F", "#E9EEF3", "#F2A541", "#FFFFFF", "#1F2933");

    public string OnPrimary => ReadableOn(Primary);
    public string OnAccent => ReadableOn(Accent);

    public static BrandColors Create(string primary, string secondary, string accent, string background, string text)
    {
        var colors = new BrandColors(
            Hex(primary, "primary"), Hex(secondary, "secondary"), Hex(accent, "accent"),
            Hex(background, "background"), Hex(text, "text"));

        if (ContrastRatio(colors.Text, colors.Background) < MinimumTextContrast)
            throw new InvalidTenantOperationException("لون النص على الخلفية غير مقروء (تباين أقل من 4.5:1 — WCAG AA)");
        if (ContrastRatio(colors.Primary, colors.OnPrimary) < MinimumTextContrast)
            throw new InvalidTenantOperationException("اللون الأساسي لا يصلح خلفية لأزرار مقروءة — اختر درجة أغمق أو أفتح");
        if (ContrastRatio(colors.Accent, colors.OnAccent) < MinimumTextContrast)
            throw new InvalidTenantOperationException("اللون المميّز لا يصلح خلفية لأزرار مقروءة — اختر درجة أغمق أو أفتح");
        if (ContrastRatio(colors.Primary, colors.Background) < MinimumUiContrast)
            throw new InvalidTenantOperationException("اللون الأساسي لا يتميّز عن الخلفية (تباين أقل من 3:1)");
        return colors;
    }

    public static double ContrastRatio(string first, string second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second)) is var (a, b) && a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private string ReadableOn(string background) =>
        ContrastRatio(background, White) >= ContrastRatio(background, Text) ? White : Text;

    private static double Luminance(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var srgb = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }

    private static string Hex(string value, string field)
    {
        var trimmed = value?.Trim() ?? "";
        if (!HexPattern().IsMatch(trimmed))
            throw new InvalidTenantOperationException($"لون {field} يجب أن يكون بصيغة #RRGGBB");
        return trimmed.ToUpperInvariant();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();
}

public sealed partial class StoreContact
{
    public const int EmailMaxLength = 256;
    public const int AddressMaxLength = 200;

    public string? Email { get; }
    public string? Phone { get; }
    public IReadOnlyDictionary<string, string> Address { get; }

    internal StoreContact(string? email, string? phone, IReadOnlyDictionary<string, string> address)
    {
        Email = email; Phone = phone; Address = address;
    }

    public static StoreContact Empty { get; } = new(null, null, new Dictionary<string, string>());

    public static StoreContact Create(string? email, string? phone, IReadOnlyDictionary<string, string?>? address)
    {
        var mail = Blank(email);
        if (mail is not null && (mail.Length > EmailMaxLength || !EmailPattern().IsMatch(mail)))
            throw new InvalidTenantOperationException("بريد التواصل غير صالح");
        var tel = Blank(phone);
        if (tel is not null && !PhonePattern().IsMatch(tel))
            throw new InvalidTenantOperationException("رقم الهاتف يقبل أرقاماً ومسافات و+ و- وأقواساً (6–30 خانة)");
        return new StoreContact(mail?.ToLowerInvariant(), tel,
            LocalizedText.Normalize(address, AddressMaxLength, "العنوان"));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,30}$")]
    private static partial Regex PhonePattern();
}

public sealed class SocialLink
{
    public const int UrlMaxLength = 300;

    // الشبكة ⇒ نطاقاتها المقبولة: رابط "instagram" يجب أن يكون على instagram.com فعلاً.
    public static readonly IReadOnlyDictionary<string, string[]> Networks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["facebook"] = ["facebook.com", "fb.com"],
        ["instagram"] = ["instagram.com"],
        ["x"] = ["x.com", "twitter.com"],
        ["tiktok"] = ["tiktok.com"],
        ["youtube"] = ["youtube.com", "youtu.be"],
        ["snapchat"] = ["snapchat.com"],
        ["linkedin"] = ["linkedin.com"],
        ["whatsapp"] = ["wa.me", "whatsapp.com"],
    };

    public string Network { get; }
    public string Url { get; }

    internal SocialLink(string network, string url)
    {
        Network = network; Url = url;
    }

    public static SocialLink Create(string network, string url)
    {
        var key = network?.Trim().ToLowerInvariant() ?? "";
        if (!Networks.TryGetValue(key, out var domains))
            throw new InvalidTenantOperationException($"شبكة تواصل غير مدعومة: {network}");

        var raw = url?.Trim() ?? "";
        if (raw.Length > UrlMaxLength || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidTenantOperationException($"رابط {key} يجب أن يكون https كاملاً");
        var host = uri.IdnHost.ToLowerInvariant();
        if (!domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal)))
            throw new InvalidTenantOperationException($"رابط {key} ليس على نطاق الشبكة ({string.Join(" / ", domains)})");
        return new SocialLink(key, uri.AbsoluteUri);
    }
}

public sealed class SeoSettings
{
    public const int TitleMaxLength = 70;
    public const int DescriptionMaxLength = 160;

    public IReadOnlyDictionary<string, string> Title { get; }
    public IReadOnlyDictionary<string, string> Description { get; }

    internal SeoSettings(IReadOnlyDictionary<string, string> title, IReadOnlyDictionary<string, string> description)
    {
        Title = title; Description = description;
    }

    public static SeoSettings Empty { get; } = new(new Dictionary<string, string>(), new Dictionary<string, string>());

    public static SeoSettings Create(IReadOnlyDictionary<string, string?>? title, IReadOnlyDictionary<string, string?>? description) =>
        new(LocalizedText.Normalize(title, TitleMaxLength, "عنوان SEO"),
            LocalizedText.Normalize(description, DescriptionMaxLength, "وصف SEO"));
}
