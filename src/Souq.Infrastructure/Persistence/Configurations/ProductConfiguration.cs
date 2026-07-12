using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// إعداد جدول المنتجات بـ Fluent API. لماذا هنا لا فوق الكيان (Data Annotations)؟
// حتى يبقى الكيان في Domain نقياً 100% من أي ارتباط بـ EF/SQL. لو بدّلنا ORM
// لاحقاً، نُعدّل هذا الملف فقط دون لمس الكيان. عزل التفاصيل التقنية مجدداً.
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

        // كائن القيمة Money يُخزّن كعمودين داخل جدول المنتج (Owned Type).
        builder.OwnsOne(p => p.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Price").HasColumnType("decimal(18,2)");
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        // Restrict لا Cascade (افتراض EF): حذف فئة لا يجوز أن يمحو منتجاتها —
        // المنتجات لها حذف منطقي، والفئة ذات المنتجات لا تُحذف أصلاً.
        builder.HasOne(p => p.Category)
               .WithMany()
               .HasForeignKey(p => p.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        // فهرس على CategoryId لأننا نبحث بالفئة كثيراً (مبدأ الأداء).
        builder.HasIndex(p => p.CategoryId);
    }
}
