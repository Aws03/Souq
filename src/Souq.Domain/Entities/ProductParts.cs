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
// افتراضي واحد بالضبط (فهرس فريد مرشَّح في القاعدة). المنتج البسيط = متغيّره الافتراضي؛ المتغيّرات المتعدّدة
// (مقاس/لون) تُضاف لاحقاً بلا هجرة مؤلمة. المخزون ينتقل إليه في المرحلة 6 (InventoryItem لكل متغيّر).
// ============================================================================
public partial class ProductVariant : Entity, ITenantOwned
{
    public const int SkuMaxLength = 64;

    public int TenantId { get; private set; }
    public string? Sku { get; private set; }
    public Money Price { get; private set; } = default!;
    public bool IsDefault { get; private set; }

    // سعر المقارنة (قبل الخصم) بعملة السعر نفسها — عمود مبلغ واحد، والعملة من السعر.
    private decimal? _compareAtAmount;
    public Money? CompareAtPrice => _compareAtAmount is decimal amount ? new Money(amount, Price.Currency) : null;
    public bool IsOnSale => _compareAtAmount is decimal amount && amount > Price.Amount;

    private ProductVariant() { }

    internal ProductVariant(bool isDefault, Money price, Money? compareAtPrice, string? sku)
    {
        IsDefault = isDefault;
        SetPricing(price, compareAtPrice, sku);
    }

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

    private static string? NormalizeSku(string? sku)
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
