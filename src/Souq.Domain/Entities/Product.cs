using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// Product — المنتج: جذر تجمّع وحدة Catalog (المرحلة 5). "نموذج غنيّ" يحرس قواعده بنفسه:
//   • نصوصه لكل لغة (ProductTranslation، D-10) — لا أعمدة لغات ثابتة.
//   • يُباع عبر متغيّراته (ProductVariant، D-21): السعر وسعر المقارنة وSKU هناك؛ Price هنا اختصار الافتراضي.
//   • خيارات مسمّاة (حتى 3، ولكلٍّ حتى 20 قيمة) ومتغيّرات هي تركيبات قيمها (حتى 100) — ADR-0040. بلا خيارات: متغيّر واحد.
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
    public const int MaxOptions = 3;
    public const int MaxValuesPerOption = 20;
    public const int MaxVariants = 100;

    private readonly List<ProductTranslation> _translations = new();
    private readonly List<ProductImage> _images = new();
    private readonly List<ProductVariant> _variants = new();
    private readonly List<ProductOption> _options = new();

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
    public IReadOnlyCollection<ProductOption> Options => _options.AsReadOnly();
    public bool HasOptions => _options.Count > 0;

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

    // تسعير المنتج من نموذجه = تسعير متغيّره الافتراضي، وهو صحيح لمنتج بسيط. لمنتج بخيارات الأسعار لكل متغيّر: عميل قديم يعيد
    // إرسال ما قرأه (بلا تغيير) يمرّ، أما تغيير فعلي من هنا فيُرفض برمز ثابت — لا يُعدَّل سعر متغيّر لم يقصده المدير.
    public void SetPricing(Money price, Money? compareAtPrice, string? sku)
    {
        if (HasOptions)
        {
            var current = DefaultVariant;
            if (price != current.Price || compareAtPrice?.Amount != current.CompareAtPrice?.Amount
                || ProductVariant.NormalizeSku(sku) != current.Sku)
                throw ProductVariantRules.Invalid("ProductHasVariants", "لهذا المنتج خيارات: السعر وSKU يُعدَّلان لكل متغيّر");
            return;
        }
        DefaultVariant.SetPricing(price, compareAtPrice, sku);
    }

    // ── الخيارات (P-08a، ADR-0040) ───────────────────────────────────────────

    // يستبدل تعريف الخيارات كاملاً بالمُدخل (بترتيبه). كل الفحوص قبل أي تغيير — فشلٌ لا يترك التجمّع نصف معدَّل:
    //   • حتى 3 خيارات، ولكل خيار 1–20 قيمة؛ الأسماء فريدة بين الخيارات، والقيم فريدة داخل خيارها، في كل لغة.
    //   • المعرّفات من هذا المنتج وحده، والقيمة تبقى تحت خيارها (لا نقل قيمة بين خيارين).
    //   • قيمة يستخدمها أي متغيّر — نشطاً أو معطّلاً — لا تُحذف: المتغيّرات لا تُحذف، ولكلٍّ تركيبته كاملة.
    //   • خيار جديد يسمّي القيمة التي تأخذها المتغيّرات القائمة؛ وحذف خيار يُرفض إن تكرّرت بعده تركيبتان.
    public void SetOptions(IReadOnlyList<ProductOptionDefinition> definitions)
    {
        definitions ??= [];
        if (definitions.Count > MaxOptions)
            throw ProductVariantRules.Invalid("TooManyOptions", $"حتى {MaxOptions} خيارات للمنتج");

        var plans = new List<OptionPlan>();
        foreach (var definition in definitions)
        {
            if (definition is null) throw ProductVariantRules.Invalid("OptionNameRequired", "تعريف الخيار مطلوب");
            var names = OptionNames.Normalize(definition.Names, "الخيار");
            var option = definition.Id is int optionId
                ? _options.FirstOrDefault(o => o.Id == optionId && optionId > 0)
                  ?? throw ProductVariantRules.Invalid("OptionNotFound", "الخيار غير موجود في هذا المنتج")
                : null;
            if (option is not null && plans.Any(p => p.Option == option))
                throw ProductVariantRules.Invalid("OptionNotFound", "الخيار مذكور مرّتين");
            if (plans.Any(p => OptionNames.Clash(p.Names, names)))
                throw ProductVariantRules.Invalid("DuplicateOptionName", $"اسم الخيار «{names.First().Value}» مكرّر");

            var values = definition.Values ?? [];
            if (values.Count == 0)
                throw ProductVariantRules.Invalid("OptionValuesRequired", $"الخيار «{names.First().Value}» يحتاج قيمة واحدة على الأقل");
            if (values.Count > MaxValuesPerOption)
                throw ProductVariantRules.Invalid("TooManyOptionValues", $"حتى {MaxValuesPerOption} قيمة لكل خيار");

            var valuePlans = new List<ValuePlan>();
            foreach (var valueDefinition in values)
            {
                if (valueDefinition is null) throw ProductVariantRules.Invalid("OptionNameRequired", "تعريف القيمة مطلوب");
                var valueNames = OptionNames.Normalize(valueDefinition.Names, "القيمة");
                ProductOptionValue? value = null;
                if (valueDefinition.Id is int valueId)
                {
                    value = option?.Values.FirstOrDefault(v => v.Id == valueId && valueId > 0)
                            ?? throw ProductVariantRules.Invalid("OptionValueNotFound", "القيمة غير موجودة في هذا الخيار");
                    if (valuePlans.Any(p => p.Value == value))
                        throw ProductVariantRules.Invalid("OptionValueNotFound", "القيمة مذكورة مرّتين");
                }
                if (valuePlans.Any(p => OptionNames.Clash(p.Names, valueNames)))
                    throw ProductVariantRules.Invalid("DuplicateOptionValue", $"القيمة «{valueNames.First().Value}» مكرّرة في الخيار نفسه");
                valuePlans.Add(new ValuePlan(value, valueNames));
            }

            int? existingVariantsValue = null;
            if (option is null)
            {
                if (definition.ExistingVariantsValue is not int index || index < 0 || index >= valuePlans.Count)
                    throw ProductVariantRules.Invalid("ExistingVariantsValueRequired",
                        $"اختر قيمة «{names.First().Value}» للمتغيّرات القائمة");
                existingVariantsValue = index;
            }
            plans.Add(new OptionPlan(option, names, valuePlans, existingVariantsValue));
        }

        // قيمة مستخدمة لا تُحذف — ولا خيار كامل تستخدم متغيّراتُ المنتج قيمه إلا بحذف الخيار نفسه (تتقلّص التركيبات).
        foreach (var plan in plans.Where(p => p.Option is not null))
        {
            var removed = plan.Option!.Values.Where(v => plan.Values.All(p => p.Value != v));
            if (removed.FirstOrDefault(v => _variants.Any(variant => variant.Uses(v))) is { } used)
                throw ProductVariantRules.Invalid("OptionValueInUse",
                    $"القيمة «{used.NameIn(null)}» مستخدمة في متغيّر — عطّل المتغيّر بدل حذف قيمته");
        }

        // تركيبة كل متغيّر بعد التعديل، بمواضع التعريف الجديد (مستقلّة عن معرّفات لم تُولَد بعد): تكرار ⇒ رفض قبل أي تغيير.
        var signatures = _variants.Select(variant => string.Join('|', plans.Select((plan, i) =>
        {
            var position = plan.Option is null
                ? plan.ExistingVariantsValue!.Value
                : plan.Values.FindIndex(p => p.Value == variant.ValueOf(plan.Option));
            return $"{i}:{position}";
        }))).ToList();
        if (signatures.Distinct(StringComparer.Ordinal).Count() != signatures.Count)
            throw ProductVariantRules.Invalid("OptionRemovalCollides", plans.Count == 0
                ? "لإزالة كل الخيارات يجب ألّا يبقى للمنتج إلا متغيّر واحد"
                : "بعد هذا التعديل يتطابق متغيّران في قيمهما — عدّل قيم المتغيّرات أولاً");

        // ── التطبيق ──
        foreach (var removedOption in _options.Where(o => plans.All(p => p.Option != o)).ToList())
            _options.Remove(removedOption);

        var assigned = _variants.ToDictionary(v => v, _ => new List<ProductOptionValue>());
        for (var position = 0; position < plans.Count; position++)
        {
            var plan = plans[position];
            var option = plan.Option;
            if (option is null)
            {
                option = new ProductOption(position, plan.Names);
                _options.Add(option);
            }
            else
            {
                option.SetPosition(position);
                option.SetNames(plan.Names);
                foreach (var removedValue in option.Values.Where(v => plan.Values.All(p => p.Value != v)).ToList())
                    option.RemoveValue(removedValue);
            }

            var values = plan.Values.Select((valuePlan, valuePosition) =>
            {
                if (valuePlan.Value is null) return option.AddValue(valuePosition, valuePlan.Names);
                valuePlan.Value.SetPosition(valuePosition);
                valuePlan.Value.SetNames(valuePlan.Names);
                return valuePlan.Value;
            }).ToList();

            foreach (var variant in _variants)
                assigned[variant].Add(plan.Option is null
                    ? values[plan.ExistingVariantsValue!.Value]
                    : variant.ValueOf(option)!);
        }

        foreach (var variant in _variants) variant.SetOptionValues(assigned[variant]);
    }

    public ProductOptionValue? FindOptionValue(int valueId) =>
        valueId > 0 ? _options.SelectMany(o => o.Values).FirstOrDefault(v => v.Id == valueId) : null;

    // وصف المتغيّر للعرض ولقطة سطر الطلب: قيمه بترتيب الخيارات ("M / أحمر") بلغة مطلوبة — null لمنتج بلا خيارات.
    public string? VariantLabel(int variantId, string? culture) =>
        FindVariant(variantId) is { } variant ? VariantLabel(variant, culture) : null;

    public string? VariantLabel(ProductVariant variant, string? culture)
    {
        if (!HasOptions || !_variants.Contains(variant)) return null;
        return VariantLabels.Compose(_options.OrderBy(o => o.Position)
            .Select(o => variant.ValueOf(o))
            .OfType<ProductOptionValue>()
            .Select(v => (IReadOnlyDictionary<string, string>)v.Translations.ToDictionary(t => t.Culture, t => t.Name)), culture);
    }

    // ── المتغيّرات ───────────────────────────────────────────────────────────

    // متغيّر جديد لمنتج بخيارات: قيمة واحدة من كل خيار (بمعرّفاتها في هذا المنتج)، تركيبة لم تُستخدم، وSKU غير مكرّر داخل المنتج
    // (تفرّده في المتجر يفحصه المعالج والفهرس). حتى 100 متغيّر — المعطّل منها يُعدّ: صفوفه باقية.
    public ProductVariant AddVariant(
        IReadOnlyCollection<int> optionValueIds, Money price, Money? compareAtPrice = null, string? sku = null, bool isActive = true)
    {
        if (!HasOptions)
            throw ProductVariantRules.Invalid("OptionsRequired", "عرّف خيارات المنتج قبل إضافة متغيّر");
        if (_variants.Count >= MaxVariants)
            throw ProductVariantRules.Invalid("TooManyVariants", $"حتى {MaxVariants} متغيّر للمنتج");
        if (price.Currency != DefaultVariant.Price.Currency)
            throw new InvalidProductDataException("متغيّرات المنتج بعملة واحدة");

        var ids = optionValueIds ?? [];
        var values = ids.Select(id => FindOptionValue(id)
            ?? throw ProductVariantRules.Invalid("OptionValueNotFound", "قيمة خيار غير موجودة في هذا المنتج")).ToList();
        if (values.Count != _options.Count || _options.Any(o => values.Count(o.Values.Contains) != 1))
            throw ProductVariantRules.Invalid("IncompleteVariantCombination", "المتغيّر يحتاج قيمة واحدة من كل خيار");

        var key = ProductVariant.KeyOf(values);
        if (_variants.Any(v => v.CombinationKey == key))
            throw ProductVariantRules.Invalid("DuplicateVariantCombination", "متغيّر بهذه القيم موجود في المنتج");

        var variant = new ProductVariant(isDefault: false, price, compareAtPrice, sku);
        EnsureSkuUnique(variant, variant.Sku);
        variant.SetOptionValues(values);
        variant.SetActive(isActive);
        _variants.Add(variant);
        return variant;
    }

    public void UpdateVariant(int variantId, Money price, Money? compareAtPrice, string? sku)
    {
        var variant = Variant(variantId);
        if (price.Currency != variant.Price.Currency)
            throw new InvalidProductDataException("متغيّرات المنتج بعملة واحدة");
        EnsureSkuUnique(variant, ProductVariant.NormalizeSku(sku));
        variant.SetPricing(price, compareAtPrice, sku);
    }

    // الافتراضي يمثّل المنتج لكل عميل لا يعرف المتغيّرات (السعر في القوائم، الطلب بلا متغيّر) — فيكون نشطاً دائماً.
    public void SetDefaultVariant(int variantId)
    {
        var variant = Variant(variantId);
        if (variant.IsDefault) return;
        if (!variant.IsActive)
            throw ProductVariantRules.Invalid("DefaultVariantMustBeActive", "فعّل المتغيّر قبل جعله افتراضياً");
        DefaultVariant.SetDefault(false);
        variant.SetDefault(true);
    }

    // تعطيل متغيّر بدل حذفه. الافتراضي لا يُعطَّل: يبقى للمنتج دائماً متغيّر نشط يمثّله لكل عميل لا يعرف المتغيّرات.
    public void DeactivateVariant(int variantId)
    {
        var variant = Variant(variantId);
        if (variant.IsDefault)
            throw ProductVariantRules.Invalid("DefaultVariantCannotBeDeactivated", "المتغيّر الافتراضي لا يُعطَّل — اجعل غيره افتراضياً أولاً");
        variant.SetActive(false);
    }

    public void ActivateVariant(int variantId) => Variant(variantId).SetActive(true);

    private void EnsureSkuUnique(ProductVariant variant, string? sku)
    {
        if (sku is not null && _variants.Any(v => v != variant && v.Sku == sku))
            throw ProductVariantRules.Invalid("DuplicateVariantSku", "SKU مستخدم لمتغيّر آخر في هذا المنتج");
    }

    private ProductVariant Variant(int variantId) =>
        FindVariant(variantId) ?? throw ProductVariantRules.Invalid("VariantNotFound", "المتغيّر غير موجود في هذا المنتج");

    private sealed record ValuePlan(ProductOptionValue? Value, Dictionary<string, string> Names);

    private sealed record OptionPlan(ProductOption? Option, Dictionary<string, string> Names, List<ValuePlan> Values, int? ExistingVariantsValue);

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
