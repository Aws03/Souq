using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// سجلّ حركات المخزون: يُقرأ دائماً لمنتج مرتّباً بالأحدث (فهرس ProductId, CreatedAt)، ويُطابَق مع مخزونه بمجموع حركاته
// (فهرس InventoryItemId, CreatedAt). مرجعان داخل المتجر (مفتاحان مركّبان)، Restrict: السجلّ لا يُحذف ولا ما يشير إليه.
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasConversion<int>();   // enum يُخزّن كرقم
        builder.Property(m => m.Note).HasMaxLength(StockMovement.NoteMaxLength);

        builder.HasOne<Product>()
               .WithMany()
               .HasForeignKey(m => new { m.TenantId, m.ProductId })
               .HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>()
               .WithMany()
               .HasForeignKey(m => new { m.TenantId, m.InventoryItemId })
               .HasPrincipalKey(i => new { i.TenantId, i.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });
        builder.HasIndex(m => new { m.InventoryItemId, m.CreatedAt });
    }
}
