using Souq.Domain.Entities;
using Souq.Domain.ValueObjects;

namespace Souq.Application.Features.Products.Queries;

// ============================================================================
// عقود الكتالوج (المرحلة 5). الكيانات لا تعبر الحدود: الـ DTO هو العقد الثابت مع الواجهة.
//   • النصوص لكل لغة (Translations) + Name/Description بلغة المتجر الافتراضية: الواجهة تعرض لغتها وتعود
//     للافتراضية بلا طلب جديد عند تبديل اللغة.
//   • السعر وسعر المقارنة من المتغيّر الافتراضي (D-21)؛ CompareAtPrice > Price ⇒ المنتج في العروض.
// ============================================================================

public sealed record CatalogTextDto(string Name, string? Description, string? MetaTitle, string? MetaDescription);

// مدخل نص لغة واحدة من الإدارة (الشكل نفسه للمنتج والفئة).
public sealed record CatalogTextInput(string Name, string? Description = null, string? MetaTitle = null, string? MetaDescription = null);

public static class CatalogTexts
{
    public static IReadOnlyDictionary<string, CatalogText> ToDomain(IReadOnlyDictionary<string, CatalogTextInput>? input) =>
        (input ?? new Dictionary<string, CatalogTextInput>()).ToDictionary(
            p => p.Key, p => new CatalogText(p.Value.Name, p.Value.Description, p.Value.MetaTitle, p.Value.MetaDescription));

    // نص لغة المتجر الافتراضية شرط: منه يُشتقّ الاسم في كل مكان لا يعرف لغة الزائر (لقطة الطلب، السجلات).
    public static bool HasCulture(IReadOnlyDictionary<string, CatalogTextInput>? input, string culture) =>
        input is not null && input.Keys.Any(k => string.Equals(k?.Trim(), culture, StringComparison.OrdinalIgnoreCase));
}

// ============================================================================
// منتج في المتجر (قائمة أو تفاصيل). Images: كل الصور المرتّبة في التفاصيل، وnull في القوائم (الرئيسية تكفي).
//
// السعر (V3، ADR-0041): سعر أرخص متغيّر يمكن شراؤه الآن (نشط وله متاح) — "ابتداءً من" حين تختلف أسعار ما يمكن شراؤه
// (PriceIsFrom، قرار P-08b). لا متغيّر قابلاً للشراء ⇒ سعر أرخص متغيّر نشط، والمنتج يبقى معروضاً غير متاح (StockQuantity=0).
// CompareAtPrice لمتغيّر السعر المعروض نفسه — لا تركيب بين متغيّرين.
//
// VariantChoiceRequired: للمنتج أكثر من متغيّر معروض ⇒ لا يُضاف بمعرّف المنتج وحده (VariantRequired من الخادم)، فالقائمة
// تقود لصفحته بدل زرّ إضافة يفشل. وهو شرط الخادم نفسه (Product.ImplicitVariant is null) لا تخميناً في الواجهة.
//
// Options وVariants في التفاصيل وحدها، ولمنتج له خيارات فقط (منتج بسيط: null، فعقده كما كان حرفياً). المعطّل لا يُعرض
// إطلاقاً: لا متغيّراً ولا قيمةَ خيار لا يستخدمها متغيّر نشط. SKU ليس في العقد العام (رمز مستودع، يظهر للمشتري على طلبه).
// ============================================================================
public sealed record ProductDto(
    int Id, string Slug, string Name, string? Description, IReadOnlyDictionary<string, CatalogTextDto> Translations,
    decimal Price, decimal? CompareAtPrice, string Currency, int StockQuantity,
    string? ImageUrl, IReadOnlyList<string>? Images, string? VideoUrl,
    int CategoryId, string? CategoryName, string? Brand,
    bool PriceIsFrom = false,
    bool VariantChoiceRequired = false,
    IReadOnlyList<ProductOptionDto>? Options = null,
    IReadOnlyList<ProductVariantDto>? Variants = null,
    // لماذا اقتُرح هذا المنتج (C10). null لكلّ قراءةٍ ليست توصية — الحقلُ لا يُملأ إلّا حيث
    // يعني شيئاً، فغيابُه ليس نقصاً في البيانات بل «هذه ليست قائمة توصيات».
    RecommendationReason? Reason = null);

// ============================================================================
// **لماذا** اقتُرح منتج (C10، [ADR-0064](0064)). قائمةٌ مغلقة، وهذا مقصود: السببُ يُعرض للمتسوّق،
// ونصٌّ حرٌّ في هذا الموضع يصير ادّعاءً لا يسنده حساب.
//
// وكلُّ سببٍ هنا يقوم على بياناتٍ **يملكها المتجر بالفعل** — طلباتُه وكتالوجه — لا على تتبّعٍ
// سلوكيّ: الالتقاط السلوكيّ معطَّلٌ افتراضاً في هذا المستودع، وتوصيةٌ تنتظره لا تعمل أبداً.
// ============================================================================
public enum RecommendationReason
{
    // اشتُريا معاً في طلباتٍ **سُلّمت** — أقوى إشارةٍ يملكها متجرٌ بلا تتبّع، وأصدقُها: مالٌ
    // دُفع وبضاعةٌ وصلت، لا نقرةٌ عابرة.
    BoughtTogether = 1,

    // من الفئة نفسها، الأكثر مبيعاً أوّلاً.
    SameCategory = 2,

    // آخرُ ما أُضيف — وهو ما يبقى لمتجرٍ جديد بلا طلباتٍ ولا فئةٍ كافية. تسميتُه سبباً بدل
    // إخفائه أصدقُ: المتسوّق يرى «جديد» لا «موصى به لك».
    NewArrival = 3,
}

// خيار وقيمه بكل لغات المتجر (الواجهة تعرض لغتها وتعود لغيرها) — بترتيب التاجر.
public sealed record ProductOptionDto(
    int Id, IReadOnlyDictionary<string, string> Names, IReadOnlyList<ProductOptionValueDto> Values);

public sealed record ProductOptionValueDto(int Id, IReadOnlyDictionary<string, string> Names);

// متغيّر نشط كما يحتاجه اختيار المتسوّق: قيمه، سعره وسعر مقارنته، والمتاح الآن (0 ⇒ نفد: يُعرض ولا يُشترى، P-08c).
public sealed record ProductVariantDto(
    int Id, IReadOnlyList<int> OptionValueIds, decimal Price, decimal? CompareAtPrice, int Available);

public sealed record ProductImageDto(int Id, string Url, int SortOrder);

// سطر في جدول منتجات الإدارة — كل الحالات. Available = المتاح للبيع من وحدة Inventory (المرحلة 6).
public sealed record AdminProductListItemDto(
    int Id, string Slug, string Name, string Status, string? Sku, decimal Price, decimal? CompareAtPrice, string Currency,
    int Available, int LowStockThreshold, string? ImageUrl, int CategoryId, string? CategoryName, DateTime CreatedAt,
    int VariantCount);

// نموذج تعديل منتج كاملاً (كل اللغات، كل الصور بمعرّفاتها). المخزون للعرض فقط: تعديله تصحيحات في وحدة Inventory.
// Sku/Price/CompareAtPrice للمتغيّر الافتراضي، والمخزون مجموع متغيّراته (منتج بسيط: متغيّره الوحيد) وحدّ تنبيه الافتراضي.
// Options وVariants (ADR-0040): بمعرّفاتها لنموذج الخيارات وجدول المتغيّرات، وVariantLimits حدود Product كما يطبّقها الخادم —
// تُنشر ولا تُنسخ في الواجهة.
// **Cost سرٌّ تجاري**: يظهر في نماذج الإدارة وحدها وفي أي استجابة يراها متسوّق — أبداً
// (`ProductDto`/`ProductListItemDto` أعلاه لا تحملانه، ويحرس ذلك اختبارٌ معماري).
public sealed record AdminProductDto(
    int Id, string Slug, string Status, IReadOnlyDictionary<string, CatalogTextDto> Translations,
    string? Sku, decimal Price, decimal? CompareAtPrice, decimal? Cost, string Currency,
    int OnHand, int Reserved, int Available, int LowStockThreshold,
    IReadOnlyList<ProductImageDto> Images, string? VideoUrl, int CategoryId, string? Brand,
    DateTime CreatedAt, DateTime? UpdatedAt,
    IReadOnlyList<AdminProductOptionDto> Options, IReadOnlyList<AdminProductVariantDto> Variants, ProductVariantLimitsDto VariantLimits);

public sealed record AdminProductOptionDto(
    int Id, int Position, IReadOnlyDictionary<string, string> Names, IReadOnlyList<AdminProductOptionValueDto> Values);

public sealed record AdminProductOptionValueDto(int Id, int Position, IReadOnlyDictionary<string, string> Names);

// متغيّر في جدول الإدارة: قيمه بمعرّفاتها (الوصف يُبنى بلغة الواجهة من أسماء الخيارات)، وتسعيره، وأرقام مخزونه للعرض من Inventory.
public sealed record AdminProductVariantDto(
    int Id, bool IsDefault, bool IsActive, string? Sku, decimal Price, decimal? CompareAtPrice, decimal? Cost,
    IReadOnlyList<int> OptionValueIds,
    int OnHand, int Reserved, int Available, int LowStockThreshold);

public sealed record ProductVariantLimitsDto(int MaxOptions, int MaxValuesPerOption, int MaxVariants, int NameMaxLength, int SkuMaxLength)
{
    public static ProductVariantLimitsDto Current { get; } = new(
        Product.MaxOptions, Product.MaxValuesPerOption, Product.MaxVariants, OptionTranslation.NameMaxLength, ProductVariant.SkuMaxLength);
}
