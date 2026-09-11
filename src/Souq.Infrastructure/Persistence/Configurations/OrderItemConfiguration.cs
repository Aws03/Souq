using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ProductName).HasMaxLength(200);
        builder.OwnsOne(i => i.UnitPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("UnitPrice").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Ignore(i => i.LineTotal);   // محسوبة، لا تُخزّن

        // مرجع للمنتج الأصلي (رغم تجميد الاسم والسعر هنا). Restrict يمنع حذف منتج
        // له مبيعات تاريخية — وهذا سبب إضافي لاعتماد الحذف المنطقي في Product.
        builder.HasOne<Product>()
               .WithMany()
               .HasForeignKey(i => i.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
