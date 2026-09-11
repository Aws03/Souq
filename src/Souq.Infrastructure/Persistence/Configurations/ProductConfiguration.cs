using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// إعداد جدول المنتجات بـ Fluent API — لا Data Annotations على الكيان كي يبقى Domain
// نقياً 100% من EF/SQL. لو بدّلنا ORM لاحقاً، نُعدّل هذا الملف فقط.
// ============================================================================
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(p => p.NameEn).HasMaxLength(200).IsRequired();
        builder.Ignore(p => p.Name); // خاصية محسوبة للتوافق الخلفي — لا عمود لها
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.ImageUrl).HasMaxLength(500);
        builder.Property(p => p.VideoUrl).HasMaxLength(500);

        // المخزون يُعدَّل بالتزامن (شراءان معاً، أو شراء مع تعديل الإدارة) ⇒ rowversion
        // يمنع البيع الزائد والتحديث الضائع (Phase 0 C1/C4).
        builder.HasRowVersion();

        // كائن القيمة Money يُخزّن كعمودين داخل جدول المنتج (Owned Type).
        builder.OwnsOne(p => p.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Price").HasColumnType(PersistenceConventions.MoneyColumnType);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        // Restrict لا Cascade: حذف فئة لا يجوز أن يمحو منتجاتها.
        builder.HasOne(p => p.Category)
               .WithMany()
               .HasForeignKey(p => p.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.CategoryId);
    }
}
