using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Comment).HasMaxLength(Review.CommentMaxLength).IsRequired();

        // الإشراف (المرحلة 13): الحالة رقماً، والملاحظة للإدارة. من أشرف معرّف فقط — سجلّ التدقيق هو الأثر الكامل.
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ModerationNote).HasMaxLength(Review.ModerationNoteMaxLength);
        builder.Ignore(r => r.IsPublished);

        // عميل واحد لا يُقيّم نفس المنتج مرتين (يحمي القاعدة على مستوى القاعدة
        // أيضاً، لا فقط في Application — دفاع في العمق).
        builder.HasIndex(r => new { r.CustomerId, r.ProductId }).IsUnique();

        // العرض العام (معتمد منتج واحد) وطابور المشرف (حالة، الأحدث أولاً).
        builder.HasIndex(r => new { r.TenantId, r.ProductId, r.Status });
        builder.HasIndex(r => new { r.TenantId, r.Status, r.CreatedAt });

        // Restrict على الجميع: تقييم يبقى سجلّاً تاريخياً، لا يُحذف بحذف مرجعه. كل مرجع داخل المتجر.
        builder.HasOne<Product>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.ProductId }).HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.CustomerId }).HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.OrderId }).HasPrincipalKey(o => new { o.TenantId, o.Id })
               .OnDelete(DeleteBehavior.Restrict);
    }
}
