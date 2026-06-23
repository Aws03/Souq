using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<int>();   // enum يُخزّن كرقم
        builder.Property(o => o.ShippingAddress).HasMaxLength(500);
        builder.HasIndex(o => o.CustomerId);

        // علاقة التجمّع: الطلب يملك أسطره. الوصول للأسطر عبر الحقل الخاص _items.
        builder.HasMany(o => o.Items)
               .WithOne()
               .HasForeignKey("OrderId")
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
