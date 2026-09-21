using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// عدّاد حصص المتجر (C2، ADR-0049): صفّ واحد لكل (متجر، اسم حدّ). الفهرس الفريد هو ما يحسم
// سباق إنشاءين على أوّل استعمال — TenantQuotaGuard يلتقط خرقه ويُعيد، كما يفعل OrderNumbers.
// TenantId والمرشّح يُضافان انعكاسياً لكل ITenantOwned في AppDbContext، فلا يُعلَنان هنا.
public class TenantUsageCounterConfiguration : IEntityTypeConfiguration<TenantUsageCounter>
{
    public void Configure(EntityTypeBuilder<TenantUsageCounter> builder)
    {
        builder.ToTable("TenantUsageCounters");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(Limit.NameMaxLength).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();
    }
}
