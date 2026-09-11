using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// مخزون المتغيّرات (المرحلة 6، ADR-0026): صفّ لكل متغيّر (فريد داخل المتجر)، rowversion لأنه أسخن صفّ في النظام
// (كل حجز ودفع وإلغاء يمرّ عليه)، ومفتاح بديل (TenantId, Id) تشير إليه الحجوزات والحركات داخل المتجر. قيود فحص في
// القاعدة خطّ دفاع أخير لقواعد الكيان: الموجود والمحجوز غير سالبين، والمحجوز لا يتجاوز الموجود.
// ============================================================================
public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", table =>
        {
            table.HasCheckConstraint("CK_InventoryItems_Quantities", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
            table.HasCheckConstraint("CK_InventoryItems_Threshold", "[LowStockThreshold] >= 0");
        });
        builder.HasKey(i => i.Id);
        builder.HasAlternateKey(i => new { i.TenantId, i.Id });
        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);
        builder.HasRowVersion();

        // المتغيّر والمنتج من المتجر نفسه (مفتاحان مركّبان). Restrict: المنتجات تُؤرشف ولا تُحذف.
        builder.HasOne<ProductVariant>().WithMany()
               .HasForeignKey(i => new { i.TenantId, i.VariantId })
               .HasPrincipalKey(v => new { v.TenantId, v.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany()
               .HasForeignKey(i => new { i.TenantId, i.ProductId })
               .HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.TenantId, i.VariantId }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.ProductId });
    }
}

// حجوزات المخزون: عمليات الطلب تبحث بالمرجع، والمنسّق يبحث عن النشط المنتهي (فهرس مرشَّح على النشط وحده يبقى صغيراً).
public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", table => table.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reference).HasMaxLength(StockReservation.ReferenceMaxLength).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Ignore(r => r.IsActive);

        builder.HasOne<InventoryItem>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.InventoryItemId })
               .HasPrincipalKey(i => new { i.TenantId, i.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.Reference });
        builder.HasIndex(r => new { r.TenantId, r.ExpiresAt }).HasFilter("[Status] = 0");
    }
}
