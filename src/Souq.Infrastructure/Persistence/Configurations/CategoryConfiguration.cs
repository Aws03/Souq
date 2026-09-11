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
        builder.Property(c => c.Slug).HasMaxLength(Category.SlugMaxLength).IsRequired();
        builder.Ignore(c => c.Name);   // من الترجمات (D-10)

        // فريد داخل المتجر لا على المنصّة: "electronics" في متجرين فئتان مستقلّتان (ADR-0005).
        builder.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique();
        // عرض الشجرة: أبناء أب مرتّبون.
        builder.HasIndex(c => new { c.TenantId, c.ParentId, c.SortOrder });

        builder.HasMany(c => c.Translations).WithOne().HasForeignKey("CategoryId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Translations).UsePropertyAccessMode(PropertyAccessMode.Field);

        // علاقة ذاتية للفئة الأب. Restrict: لا تُحذف فئة لها أبناء (يحرسه المعالج برسالة واضحة، والقاعدة الحارس
        // الأخير). الحلقات والعمق يحرسهما الكيان (Category.MoveTo).
        builder.HasOne<Category>()
               .WithMany()
               .HasForeignKey(c => c.ParentId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
