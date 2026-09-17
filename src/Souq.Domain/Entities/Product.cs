using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Product — المنتج: جذر تجمّع وحدة Catalog (المرحلة 5). "نموذج غنيّ" يحرس قواعده بنفسه:
//   • نصوصه لكل لغة (ProductTranslation، D-10) — لا أعمدة لغات ثابتة.
//   • يُباع عبر متغيّره الافتراضي (ProductVariant، D-21): السعر وسعر المقارنة وSKU هناك؛ Price هنا اختصار.
//   • دورة حياة Draft ⇄ Active ⇄ Archived (لا حذف أبداً: الطلبات والتقييمات تشير إليه).
//   • حتى 10 صور مرتّبة؛ الأولى رئيسية. معرّف رابط (slug) فريد داخل المتجر.
//   • المخزون ليس هنا: وحدة Inventory تملكه (InventoryItem لكل متغيّر، بحجوزات — المرحلة 6، ADR-0026).
// ============================================================================
public class Product : Entity, ITenantOwned
{
    public const int SlugMaxLength = 120;
    public const int BrandMaxLength = 100;
    public const int VideoUrlMaxLength = 500;
    public const int MaxImages = 10;

    private readonly List<ProductTranslation> _translations = new();
    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariant> _variants = new();

    public int TenantId { get; private set; }
    public string Slug { get; private set; } = default!;
    public ProductStatus Status { get; private set; }
    public string? Brand { get; private set; }
    public string? VideoUrl { get; private set; }
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }          // علاقة تنقّل (Navigation)

    public IReadOnlyCollection<ProductTranslation> Translations => _translations.AsReadOnly();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    public ProductVariant DefaultVariant => _variants.Single(v => v.IsDefault);

    // المتغيّر الذي يُشترى حين لا يسمّي الطلب متغيّراً: الوحيد النشط — أي الافتراضي لمنتج بسيط، فكل عميل قديم يعمل كما
    // كان. لمنتج بأكثر من متغيّر نشط لا يُفترض شيء: الاختيار صريح (P-08c) ⇒ null، والمستدعي يطلب التحديد.
    public ProductVariant? ImplicitVariant => _variants.Count(v => v.IsActive) == 1 ? _variants.Single(v => v.IsActive) : null;
    public Money Price => DefaultVariant.Price;
    public Money? CompareAtPrice => DefaultVariant.CompareAtPrice;
    public string? Sku => DefaultVariant.Sku;
    public bool IsActive => Status == ProductStatus.Active;

    // القابلية للبيع ليست حالة المنتج وحدها: فئة معطّلة تُخفي منتجاتها من واجهة المتجر، لكن الشراء كان يفحص حالة المنتج
    // وحدها — فيبقى منتج فئةٍ "أزالها" المتجر قابلاً للشراء بمعرّفه من السلة والدفع (R-07). القاعدة هنا كي تكون واحدة
    // لكل مسار شراء. الفئة تُحمَّل دائماً مع المنتج في مستودع الكتابة (ProductRepository): بلا فئة محمّلة لا بيع.
    public bool IsSellable => IsActive && Category is { IsActive: true };

    // متغيّر يُشترى: من هذا المنتج نفسه (معرّف متغيّر منتج آخر لا يسعّر هذا المنتج أبداً)، نشط، ومنتجه قابل للبيع.
    // المخزون شأن منفصل: متغيّر نافد قابل للبيع وغير متاح، كما المنتج اليوم.
    public bool CanSell(ProductVariant variant) => IsSellable && variant.IsActive && _variants.Contains(variant);

    public ProductVariant? FindVariant(int variantId) => variantId > 0 ? _variants.FirstOrDefault(v => v.Id == variantId) : null;

    public string? PrimaryImageUrl => _images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).FirstOrDefault()?.Url;

    // اسم للرسائل والسجلات؛ اللقطات التجارية تستخدم NameIn(لغة المتجر).
    public string Name => NameIn(null);

    private Product() { }

    public Product(
        string slug, int categoryId, IReadOnlyDictionary<string, CatalogText> texts, Money price,
        ProductStatus status = ProductStatus.Active, string? sku = null, Money? compareAtPrice = null, string? brand = null)
    {
        if (status == ProductStatus.Archived)
            throw new InvalidProductDataException("المنتج الجديد مسودّة أو نشط — لا يُنشأ مؤرشفاً");

        SetSlug(slug);
        MoveToCategory(categoryId);
        SetTexts(texts);
        _variants.Add(new ProductVariant(isDefault: true, price, compareAtPrice, sku));
        SetBrand(brand);
        Status = status;
    }

    public string NameIn(string? culture) => CatalogTranslation.NameIn(_translations, culture);

    // ── النصوص والتعريف ─────────────────────────────────────────────────────

    public void SetTexts(IReadOnlyDictionary<string, CatalogText> texts) =>
        CatalogTranslation.Replace(_translations, CatalogText.NormalizeAll(texts, Invalid),
            (culture, text) => new ProductTranslation(culture, text));

    public void SetSlug(string slug) =>
        Slug = CatalogSlug.TryNormalize(slug, SlugMaxLength)
               ?? throw new InvalidProductDataException("معرّف الرابط يقبل أحرفاً لاتينية صغيرة وأرقاماً وشرطات (2–120)");

    public void SetBrand(string? brand)
    {
        var trimmed = brand?.Trim();
        if (trimmed is { Length: > BrandMaxLength })
            throw new InvalidProductDataException($"العلامة التجارية حتى {BrandMaxLength} حرفاً");
        Brand = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public void MoveToCategory(int categoryId)
    {
        if (categoryId <= 0) throw new InvalidProductDataException("الفئة مطلوبة");
        CategoryId = categoryId;
    }

    // ── التسعير (المتغيّر الافتراضي) ──────────────────────────────────────────

    public void SetPricing(Money price, Money? compareAtPrice, string? sku) =>
        DefaultVariant.SetPricing(price, compareAtPrice, sku);

    // ── المتغيّرات ───────────────────────────────────────────────────────────

    // تعطيل متغيّر بدل حذفه. الافتراضي لا يُعطَّل: يبقى للمنتج دائماً متغيّر نشط يمثّله لكل عميل لا يعرف المتغيّرات.
    public void DeactivateVariant(int variantId)
    {
        var variant = Variant(variantId);
        if (variant.IsDefault)
            throw new InvalidProductDataException("المتغيّر الافتراضي لا يُعطَّل");
        variant.SetActive(false);
    }

    public void ActivateVariant(int variantId) => Variant(variantId).SetActive(true);

    // متغيّر غير افتراضي. internal عمداً: متغيّر ثانٍ بلا خيارات يصفه يخالف نموذج الخيارات المعتمد (P-08a) — إنشاء
    // المتغيّرات للتاجر يأتي مع الخيارات (V2، ProductVariants.md §11). حتى ذلك الحين تستخدمه الاختبارات وحدها لإثبات أن
    // السلة والتسعير والطلب والمخزون صحيحة لمنتج بأكثر من متغيّر.
    internal ProductVariant AddVariant(Money price, Money? compareAtPrice = null, string? sku = null)
    {
        if (price.Currency != DefaultVariant.Price.Currency)
            throw new InvalidProductDataException("متغيّرات المنتج بعملة واحدة");
        var variant = new ProductVariant(isDefault: false, price, compareAtPrice, sku);
        _variants.Add(variant);
        return variant;
    }

    private ProductVariant Variant(int variantId) =>
        FindVariant(variantId) ?? throw new InvalidProductDataException("المتغيّر غير موجود في هذا المنتج");

    // ── دورة الحياة ───────────────────────────────────────────────────────────

    public void ChangeStatus(ProductStatus target)
    {
        switch (target)
        {
            case ProductStatus.Active:
                Status = ProductStatus.Active;          // نشر مسودّة أو استعادة مؤرشف مباشرةً
                break;
            case ProductStatus.Draft:
                Status = ProductStatus.Draft;           // إخفاء مؤقّت، أو استعادة مؤرشف مسودّةً
                break;
            case ProductStatus.Archived:
                Status = ProductStatus.Archived;
                break;
            default:
                throw new InvalidProductDataException($"حالة غير معروفة: {target}");
        }
    }

    public void Archive() => ChangeStatus(ProductStatus.Archived);

    // ── الوسائط ───────────────────────────────────────────────────────────────

    public ProductImage AddImage(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > ProductImage.UrlMaxLength)
            throw new InvalidProductDataException("رابط الصورة مطلوب");
        if (_images.Count >= MaxImages)
            throw new InvalidProductDataException($"حتى {MaxImages} صور للمنتج");

        var image = new ProductImage(url, _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1);
        _images.Add(image);
        return image;
    }

    public void RemoveImage(int imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId)
                    ?? throw new InvalidProductDataException("الصورة غير موجودة في هذا المنتج");
        _images.Remove(image);
    }

    // الترتيب الجديد يشمل كل صور المنتج مرّة واحدة بالضبط — لا صورة تضيع ولا تتكرّر.
    public void ReorderImages(IReadOnlyList<int> imageIds)
    {
        if (imageIds.Count != _images.Count || imageIds.Distinct().Count() != imageIds.Count
            || imageIds.Any(id => _images.All(i => i.Id != id)))
            throw new InvalidProductDataException("الترتيب يجب أن يشمل كل صور المنتج مرّة واحدة");

        for (var position = 0; position < imageIds.Count; position++)
            _images.Single(i => i.Id == imageIds[position]).SetSortOrder(position);
    }

    public void SetVideoUrl(string? videoUrl)
    {
        var trimmed = videoUrl?.Trim();
        if (trimmed is { Length: > VideoUrlMaxLength })
            throw new InvalidProductDataException("رابط الفيديو طويل جداً");
        VideoUrl = string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static Exception Invalid(string message) => new InvalidProductDataException(message);
}
