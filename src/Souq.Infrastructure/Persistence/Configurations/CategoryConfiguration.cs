using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(100);
        // فريد داخل المتجر لا على المنصّة: "electronics" في متجرين فئتان مستقلّتان (ADR-0005).
        builder.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique();

        // علاقة ذاتية للفئة الأب — لم تكن مُعرَّفة إطلاقاً في نموذج EF، فكان ParentId رقماً
        // بلا قيد يقبل أباً غير موجود. Restrict: لا تُحذف فئة لها أبناء (يحرسه المعالج
        // برسالة واضحة، والقاعدة هي الحارس الأخير).
        builder.HasOne<Category>()
               .WithMany()
               .HasForeignKey(c => c.ParentId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
