using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistories");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Status).HasConversion<int>();   // enum يُخزّن كرقم
        builder.Property(h => h.Note).HasMaxLength(300);
        // من غيّر الحالة (المرحلة 9): النوع رقماً، ومعرّف الحساب بلا مفتاح أجنبي — سجلّ لا يُكسر بإيقاف حساب.
        builder.Property(h => h.ChangedBy).HasConversion<int>();
    }
}
