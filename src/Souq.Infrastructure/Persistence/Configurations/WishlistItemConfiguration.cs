using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// مفضّلة العملاء (المرحلة 13). Restrict على المرجعين: ملف العميل لا يُحذف (المحو يحذف مفضّلته صراحةً، ADR-0027)، والمنتجات
// تُؤرشف ولا تُحذف (DeleteProductHandler) — المؤرشف يختفي من العرض فقط.
public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("WishlistItems");
        builder.HasKey(w => w.Id);

        builder.HasOne<Customer>().WithMany()
               .HasForeignKey(w => new { w.TenantId, w.CustomerId }).HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany()
               .HasForeignKey(w => new { w.TenantId, w.ProductId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);

        // منتج مرة واحدة لكل عميل: الحارس الأخير لنقرتين متزامنتين على القلب (409 من وحدة العمل).
        builder.HasIndex(w => new { w.TenantId, w.CustomerId, w.ProductId }).IsUnique();
    }
}
