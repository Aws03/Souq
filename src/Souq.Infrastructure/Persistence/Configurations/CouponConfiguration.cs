using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Type).HasConversion<int>();
        builder.Property(c => c.Value).HasColumnType("decimal(18,2)");

        // اعتماد اختياري (owned optional): كوبون بلا حد أدنى للطلب لا يخزّن شيئاً هنا.
        builder.OwnsOne(c => c.MinOrderAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("MinOrderAmount").HasColumnType("decimal(18,2)");
            m.Property(x => x.Currency).HasColumnName("MinOrderCurrency").HasMaxLength(3);
        });
    }
}
