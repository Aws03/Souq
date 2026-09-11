using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// السلال (المرحلة 8، ADR-0028): مالك واحد بالضبط — عميل أو بصمة رمز زائر (قيد فحص) — وسلة واحدة لكل مالك في المتجر
// (فهرسان فريدان مرشَّحان). بلا rowversion: الأخير يكسب في السلة، والدفع يعيد التحقّق من كل شيء. فهرس الانتهاء لمنسّق
// الحذف. الأسطر جزء من التجمّع (حذف متتالٍ)، ومنتجها ومتغيّرها من المتجر نفسه (مفتاحان مركّبان).
// ============================================================================
public class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.ToTable("Baskets", table => table.HasCheckConstraint("CK_Baskets_Owner",
            "([CustomerId] IS NOT NULL AND [GuestTokenHash] IS NULL) OR ([CustomerId] IS NULL AND [GuestTokenHash] IS NOT NULL)"));
        builder.HasKey(b => b.Id);
        builder.Property(b => b.GuestTokenHash).HasMaxLength(Basket.GuestTokenHashLength).IsFixedLength().IsUnicode(false);
        builder.Ignore(b => b.IsGuest);

        builder.HasMany(b => b.Lines).WithOne().HasForeignKey("BasketId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Restrict: ملف العميل لا يُحذف (المحو يجرّده ويحذف سلته، ADR-0027).
        builder.HasOne<Customer>().WithMany()
               .HasForeignKey(b => new { b.TenantId, b.CustomerId })
               .HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.TenantId, b.CustomerId }).IsUnique().HasFilter("[CustomerId] IS NOT NULL");
        builder.HasIndex(b => new { b.TenantId, b.GuestTokenHash }).IsUnique().HasFilter("[GuestTokenHash] IS NOT NULL");
        builder.HasIndex(b => new { b.TenantId, b.ExpiresAt });
    }
}

public class BasketLineConfiguration : IEntityTypeConfiguration<BasketLine>
{
    public void Configure(EntityTypeBuilder<BasketLine> builder)
    {
        builder.ToTable("BasketLines", table => table.HasCheckConstraint("CK_BasketLines_Quantity",
            $"[Quantity] BETWEEN 1 AND {Basket.MaxQuantityPerLine}"));
        builder.HasKey(l => l.Id);

        // Restrict: المنتجات تُؤرشف ولا تُحذف (DeleteProductHandler)، والسطر المؤرشف يظهر "غير متاح" في السلة.
        builder.HasOne<Product>().WithMany()
               .HasForeignKey(l => new { l.TenantId, l.ProductId })
               .HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany()
               .HasForeignKey(l => new { l.TenantId, l.VariantId })
               .HasPrincipalKey(v => new { v.TenantId, v.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex("BasketId", nameof(BasketLine.VariantId)).IsUnique();
    }
}
