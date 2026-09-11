using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// جداول المنصّة (وحدة Platform): لا TenantId مُرشَّح عليها — هي ما يُعرِّف المستأجر. قابلة للفصل عن
// جداول المتاجر لو انتقل متجر لقاعدة مستقلّة (MultiTenancy.md §6).
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(Tenant.SlugMaxLength).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Status).HasConversion<int>();
        builder.Property(t => t.DefaultCulture).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.TimeZone).HasMaxLength(Tenant.TimeZoneMaxLength).IsRequired();

        // مالك المنصّة وأكثر من مدير قد يعدّلون المتجر نفسه معاً (حالة، نطاقات، عملة).
        builder.HasRowVersion();

        builder.HasMany(t => t.Domains)
               .WithOne()
               .HasForeignKey(d => d.TenantId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Domains).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("TenantDomains");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Host).HasMaxLength(TenantDomain.HostMaxLength).IsRequired();

        // المضيف يحدّد المتجر لكل طلب ⇒ فريد على مستوى المنصّة كلها (القاعدة هي الحارس الأخير).
        builder.HasIndex(d => d.Host).IsUnique();
    }
}
