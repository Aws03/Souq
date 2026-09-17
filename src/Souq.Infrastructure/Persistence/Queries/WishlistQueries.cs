using Microsoft.EntityFrameworkCore;
using Souq.Application.Features.Wishlist;
using Souq.Domain.Entities;
using Souq.Domain.Enums;

namespace Souq.Infrastructure.Persistence.Queries;

// ============================================================================
// تنفيذ IWishlistQueries (المرحلة 13، ADR-0008): عناصر المفضّلة بأسعار الكتالوج الحيّة في استعلام واحد — المنتج الظاهر وحده
// (نشط وفئته مفعّلة، كقوائم الكتالوج)، والسعر من المتغيّر الافتراضي (D-21)، والمتاح من Inventory (الموجود − المحجوز). القائمة
// محدودة بسقف المفضّلة فلا ترقيم.
// ============================================================================
internal sealed class WishlistQueries : IWishlistQueries
{
    private readonly AppDbContext _db;
    public WishlistQueries(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<WishlistItemDto>> ListAsync(int customerId, string culture, CancellationToken ct)
    {
        // الظاهر كما في قوائم الكتالوج (CatalogQueries): نشط في فئة مفعّلة — بوّابة V2 المؤقّتة أُزيلت في V3 (ADR-0041).
        var visible = _db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Category!.IsActive);

        var rows = await _db.WishlistItems.AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .Join(visible, w => w.ProductId, p => p.Id, (w, p) => new { Item = w, Product = p })
            .OrderByDescending(x => x.Item.CreatedAt).ThenByDescending(x => x.Item.Id)
            .Select(x => new Row(
                x.Product.Id, x.Product.Slug,
                x.Product.Translations.Select(t => new TextRow(t.Culture, t.Name)).ToList(),
                // السعر كما في المتجر (V3): أرخص متغيّر يمكن شراؤه الآن، وإلا أرخص متغيّر نشط — لا المتغيّر الافتراضي.
                x.Product.Variants.Where(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
                    .OrderBy(v => v.Price.Amount).ThenBy(v => v.Id)
                    .Select(v => new PriceRow(v.Price.Amount, EF.Property<decimal?>(v, "_compareAtAmount"), v.Price.Currency)).FirstOrDefault(),
                x.Product.Variants.Where(v => v.IsActive).OrderBy(v => v.Price.Amount).ThenBy(v => v.Id)
                    .Select(v => new PriceRow(v.Price.Amount, EF.Property<decimal?>(v, "_compareAtAmount"), v.Price.Currency)).FirstOrDefault(),
                x.Product.Variants.Where(v => v.IsActive && _db.InventoryItems.Any(i => i.VariantId == v.Id && i.OnHand - i.Reserved > 0))
                    .Select(v => (decimal?)v.Price.Amount).Max(),
                x.Product.Variants.Count(v => v.IsActive),
                _db.InventoryItems.Where(s => s.ProductId == x.Product.Id && x.Product.Variants.Any(v => v.Id == s.VariantId && v.IsActive))
                    .Sum(s => (int?)(s.OnHand - s.Reserved)) ?? 0,
                x.Product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.Url).FirstOrDefault(),
                x.Item.CreatedAt))
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var names = r.Texts.ToDictionary(t => t.Culture, t => new WishlistText(t.Name));
            var name = names.TryGetValue(culture, out var main)
                ? main.Name
                : r.Texts.OrderBy(t => t.Culture, StringComparer.Ordinal).Select(t => t.Name).FirstOrDefault() ?? r.Slug;
            var price = r.Cheapest ?? r.CheapestActive;
            return new WishlistItemDto(r.Id, r.Slug, name, names, price?.Price ?? 0m, price?.CompareAtPrice, price?.Currency ?? "",
                Math.Max(r.Available, 0), r.ImageUrl, r.AddedAt,
                r.Cheapest is not null && r.HighestPurchasable > r.Cheapest.Price, r.ActiveVariants > 1);
        }).ToList();
    }

    private sealed record TextRow(string Culture, string Name);

    private sealed record Row(
        int Id, string Slug, List<TextRow> Texts, PriceRow? Cheapest, PriceRow? CheapestActive, decimal? HighestPurchasable,
        int ActiveVariants, int Available, string? ImageUrl, DateTime AddedAt);

    private sealed record PriceRow(decimal Price, decimal? CompareAtPrice, string Currency);
}
