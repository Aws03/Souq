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
        builder.Property(i => i.ProductName).HasMaxLength(OrderItem.ProductNameMaxLength);
        builder.Property(i => i.VariantLabel).HasMaxLength(OrderItem.VariantLabelMaxLength);
        builder.Property(i => i.Sku).HasMaxLength(ProductVariant.SkuMaxLength);
        builder.OwnsOne(i => i.UnitPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("UnitPrice").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Ignore(i => i.LineTotal);   // محسوبة، لا تُخزّن

        // مرجع للمنتج الأصلي (رغم تجميد الاسم والسعر هنا). Restrict يمنع حذف منتج
        // له مبيعات تاريخية — وهذا سبب إضافي لاعتماد الحذف المنطقي في Product. داخل المتجر فقط.
        builder.HasOne<Product>()
               .WithMany()
               .HasForeignKey(i => new { i.TenantId, i.ProductId })
               .HasPrincipalKey(p => new { p.TenantId, p.Id })
               .OnDelete(DeleteBehavior.Restrict);

        // المتغيّر المشترى من المتجر نفسه، وRestrict للسبب ذاته: المتغيّر يُعطَّل ولا يُحذف.
        builder.HasOne<ProductVariant>()
               .WithMany()
               .HasForeignKey(i => new { i.TenantId, i.VariantId })
               .HasPrincipalKey(v => new { v.TenantId, v.Id })
               .OnDelete(DeleteBehavior.Restrict);

        // سطر واحد لكل متغيّر في الطلب (Order.AddItem يدمج) — القاعدة تضمنه حتى لو أُضيف سطر بطريق آخر.
        builder.HasIndex("OrderId", nameof(OrderItem.VariantId)).IsUnique();
    }
}
