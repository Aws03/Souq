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
        builder.Property(r => r.Comment).HasMaxLength(1000).IsRequired();

        // عميل واحد لا يُقيّم نفس المنتج مرتين (يحمي القاعدة على مستوى القاعدة
        // أيضاً، لا فقط في Application — دفاع في العمق).
        builder.HasIndex(r => new { r.CustomerId, r.ProductId }).IsUnique();

        // Restrict على الجميع: تقييم يبقى سجلّاً تاريخياً، لا يُحذف بحذف مرجعه.
        builder.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
