using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// إعداد جدول حركات المخزون. فهرس على (ProductId, CreatedAt تنازلياً) لأننا نجلب
// تاريخ منتج مرتّباً بالأحدث دائماً — الاستعلام الوحيد على هذا الجدول.
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasConversion<int>();   // enum يُخزّن كرقم
        builder.Property(m => m.Note).HasMaxLength(300);

        // مرجع للمنتج. Restrict: لا يُحذف منتج له سجلّ حركة (والمنتجات تُعطَّل لا تُحذف).
        builder.HasOne<Product>()
               .WithMany()
               .HasForeignKey(m => m.ProductId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });
    }
}
