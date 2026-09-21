using System.Text.RegularExpressions;
using Souq.Domain.Common;
using Souq.Domain.Exceptions;
using Souq.Domain.ValueObjects;

namespace Souq.Domain.Entities;

// ============================================================================
// أبناء تجمّع المنتج (المرحلة 5). كلها تُنشأ وتُعدَّل عبر Product وحده، بمفتاح ظلّ إلى المنتج، وتحمل متجرها.
// ============================================================================

// صورة منتج: مسار ولّده التخزين بعد فحص المحتوى، وترتيب عرض (الأولى هي الرئيسية).
public class ProductImage : Entity, ITenantOwned
{
    public const int UrlMaxLength = 500;

    public int TenantId { get; private set; }
    public string Url { get; private set; } = default!;
    public int SortOrder { get; private set; }

    private ProductImage() { }

    internal ProductImage(string url, int sortOrder)
    {
        Url = url;
        SortOrder = sortOrder;
    }

    internal void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}

// ============================================================================
// ProductVariant — الوحدة القابلة للبيع (D-21): SKU والسعر وسعر المقارنة هنا لا على المنتج، ولكل منتج متغيّر
// افتراضي واحد بالضبط (فهرس فريد مرشَّح في القاعدة). المنتج البسيط = متغيّره الافتراضي بلا خيارات؛ منتج بخيارات
// (ADR-0040) لكل متغيّر فيه قيمة من كل خيار، وتركيبته فريدة. المخزون ليس هنا: لكل متغيّر InventoryItem تملكه Inventory.
// ============================================================================
public partial class ProductVariant : Entity, ITenantOwned
{
    public const int SkuMaxLength = 64;
    public const int CombinationKeyMaxLength = 110;

    private readonly List<ProductVariantOptionValue> _optionValues = new();

    public int TenantId { get; private set; }
    public string? Sku { get; private set; }
    public Money Price { get; private set; } = default!;
    public bool IsDefault { get; private set; }

    // المتغيّر لا يُحذف أبداً بعد وجوده — أسطر الطلبات والحجوزات وسجلّ المخزون وأسطر السلال تشير إليه — بل يُعطَّل.
    // المعطّل لا يُشترى، والافتراضي نشط دائماً (يحرسه Product وقيد فحص في القاعدة).
    public bool IsActive { get; private set; } = true;

    // قيمه من خيارات المنتج (واحدة لكل خيار)، ومفتاحها المُطبَّع: مفاتيح القيم مرتّبة — null لمنتج بلا خيارات. فهرس فريد
    // على (المنتج، المفتاح) يجعل تكرار التركيبة مستحيلاً حتى مع مديرَين متزامنين.
    public IReadOnlyCollection<ProductVariantOptionValue> OptionValues => _optionValues.AsReadOnly();
    public string? CombinationKey { get; private set; }

    // سعر المقارنة (قبل الخصم) بعملة السعر نفسها — عمود مبلغ واحد، والعملة من السعر.
    private decimal? _compareAtAmount;
    public Money? CompareAtPrice => _compareAtAmount is decimal amount ? new Money(amount, Price.Currency) : null;
    public bool IsOnSale => _compareAtAmount is decimal amount && amount > Price.Amount;

    // ============================================================================
    // تكلفة الوحدة (C11) — بعملة السعر نفسها، بالشكل نفسه الذي يتّبعه سعر المقارنة: عمود مبلغ
    // واحد والعملة من السعر، فلا يمكن أصلاً أن تحمل عملةً أخرى.
    //
    // **اختيارية عن قصد، و`null` تعني "غير معروفة" لا "صفر".** تاجرٌ لا يمسك تكاليفه يجب أن يرى
    // "الهامش غير متاح"، لا هامشاً بنسبة مئة بالمئة. الفرق بين الغياب والصفر هو الفرق بين تقريرٍ
    // صادق وتقريرٍ يخترع ربحاً — والتقارير تحمل تغطيةَ التكلفة بجانب الهامش لهذا السبب.
    //
    // ولا تُقيَّد بالسعر: البيع بخسارة قرارٌ تجاري مشروع (منتج جاذب، تصفية مخزون)، ورفضُ تكلفةٍ
    // أعلى من السعر كان سيمنع التاجر من قول الحقيقة عن متجره.
    //
    // وهي القيمة **الحالية**: الهامش التاريخي لا يُحسب منها بل من لقطة `OrderItem.UnitCost`،
    // وإلّا تحرّك ربح العام الماضي كلّما صحّح التاجر رقماً.
    // ============================================================================
    private decimal? _costAmount;
    public Money? Cost => _costAmount is decimal amount ? new Money(amount, Price.Currency) : null;

    internal void SetCost(Money? cost)
    {
        if (cost is not { } value)
        {
            _costAmount = null;
            return;
        }
        if (value.Currency != Price.Currency)
            throw new InvalidProductDataException("التكلفة بعملة السعر نفسها");
        // ولا فحص للسالب هنا: `Money` نفسه يرفضه، وتكراره كان سيكون شيفرةً ميّتة تُوهم بحراسة.
        // المدقّق عند الحافّة يحوّل نفس الخطأ إلى 400 برسالة حقل بدل 422 من المجال.
        _costAmount = value.Amount;
    }

    private ProductVariant() { }

    internal ProductVariant(bool isDefault, Money price, Money? compareAtPrice, string? sku)
    {
        IsDefault = isDefault;
        SetPricing(price, compareAtPrice, sku);
    }

    internal void SetActive(bool active) => IsActive = active;

    internal void SetDefault(bool isDefault) => IsDefault = isDefault;

    internal bool Uses(ProductOptionValue value) => _optionValues.Any(v => v.OptionValue == value);

    internal ProductOptionValue? ValueOf(ProductOption option) =>
        _optionValues.Select(v => v.OptionValue).FirstOrDefault(option.Values.Contains);

    internal void SetOptionValues(IEnumerable<ProductOptionValue> values)
    {
        var target = values.ToList();
        _optionValues.RemoveAll(current => !target.Contains(current.OptionValue));
        foreach (var value in target.Where(value => _optionValues.All(current => current.OptionValue != value)))
            _optionValues.Add(new ProductVariantOptionValue(value));
        CombinationKey = KeyOf(target);
    }

    internal static string? KeyOf(IReadOnlyCollection<ProductOptionValue> values) =>
        values.Count == 0 ? null : string.Join('.', values.Select(v => v.Key.ToString("N")).Order(StringComparer.Ordinal));

    internal void SetPricing(Money price, Money? compareAtPrice, string? sku)
    {
        if (price.Amount <= 0)
            throw new InvalidProductDataException("السعر يجب أن يكون أكبر من صفر");
        if (compareAtPrice is { } compare)
        {
            if (compare.Currency != price.Currency)
                throw new InvalidProductDataException("سعر المقارنة بعملة السعر نفسها");
            if (compare.Amount <= price.Amount)
                throw new InvalidProductDataException("سعر المقارنة (قبل الخصم) يجب أن يكون أعلى من السعر");
        }

        Price = price;
        _compareAtAmount = compareAtPrice?.Amount;
        Sku = NormalizeSku(sku);
    }

    internal static string? NormalizeSku(string? sku)
    {
        var trimmed = sku?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= SkuMaxLength && SkuPattern().IsMatch(trimmed)
            ? trimmed.ToUpperInvariant()
            : throw new InvalidProductDataException($"SKU يقبل أحرفاً لاتينية وأرقاماً و . _ - (حتى {SkuMaxLength})");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SkuPattern();
}
