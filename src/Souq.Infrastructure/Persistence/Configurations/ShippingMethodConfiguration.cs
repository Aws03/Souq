using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// طرق الشحن (المرحلة 12، ADR-0032): السعر Money بعملته، حدّ المجانية بعملة السعر نفسها، والدول نصّ رموز ISO بفواصل. لا
// مفتاح من الطلبات إليها — الطلب يحمل لقطتها، فحذفها لا يمسّ التاريخ.
public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("ShippingMethods");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(ShippingMethod.NameMaxLength).IsRequired();
        builder.Property(m => m.FreeOverAmount).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Property(m => m.Carrier).HasMaxLength(ShippingMethod.CarrierMaxLength);
        builder.Property(m => m.TrackingUrlTemplate).HasMaxLength(ShippingMethod.TrackingUrlMaxLength);
        builder.Property(m => m.Countries).HasMaxLength(ShippingMethod.CountriesMaxLength).IsUnicode(false).IsRequired();

        builder.OwnsOne(m => m.Price, p =>
        {
            p.Property(x => x.Amount).HasColumnName("Price").HasColumnType(PersistenceConventions.MoneyColumnType);
            p.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(m => m.Price).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.IsActive, m.SortOrder });
    }
}
