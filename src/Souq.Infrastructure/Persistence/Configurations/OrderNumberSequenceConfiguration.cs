using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// عدّاد أرقام الطلبات (المرحلة 9): صفّ واحد لكل متجر (فهرس فريد) — OrderNumbers يزيده ذرّياً داخل معاملة الطلب.
public class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("OrderNumberSequences");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
