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

// منتج في المتجر (قائمة أو تفاصيل). Images: كل الصور المرتّبة في التفاصيل، وnull في القوائم (الرئيسية تكفي).
public sealed record ProductDto(
    int Id, string Slug, string Name, string? Description, IReadOnlyDictionary<string, CatalogTextDto> Translations,
    decimal Price, decimal? CompareAtPrice, string Currency, int StockQuantity,
    string? ImageUrl, IReadOnlyList<string>? Images, string? VideoUrl,
    int CategoryId, string? CategoryName, string? Brand);

public sealed record ProductImageDto(int Id, string Url, int SortOrder);

// سطر في جدول منتجات الإدارة — كل الحالات. Available = المتاح للبيع من وحدة Inventory (المرحلة 6).
public sealed record AdminProductListItemDto(
    int Id, string Slug, string Name, string Status, string? Sku, decimal Price, decimal? CompareAtPrice, string Currency,
    int Available, int LowStockThreshold, string? ImageUrl, int CategoryId, string? CategoryName, DateTime CreatedAt);

// نموذج تعديل منتج كاملاً (كل اللغات، كل الصور بمعرّفاتها). المخزون للعرض فقط: تعديله تصحيحات في وحدة Inventory.
public sealed record AdminProductDto(
    int Id, string Slug, string Status, IReadOnlyDictionary<string, CatalogTextDto> Translations,
    string? Sku, decimal Price, decimal? CompareAtPrice, string Currency,
    int OnHand, int Reserved, int Available, int LowStockThreshold,
    IReadOnlyList<ProductImageDto> Images, string? VideoUrl, int CategoryId, string? Brand,
    DateTime CreatedAt, DateTime? UpdatedAt);
