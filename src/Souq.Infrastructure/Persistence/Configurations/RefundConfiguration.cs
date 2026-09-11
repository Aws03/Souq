using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// الاستردادات (المرحلة 11): العلاقة بالدفعة معرَّفة من جهة Payment (تجمّعها). معرّف الاسترداد لدى البوّابة للمطابقة.
public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.Reason).HasMaxLength(Refund.ReasonMaxLength);
        builder.Property(r => r.FailureReason).HasMaxLength(Refund.ReasonMaxLength);
        builder.Property(r => r.ProviderRefundId).HasMaxLength(Refund.ProviderRefundIdMaxLength).IsUnicode(false);

        builder.OwnsOne(r => r.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(r => r.Amount).IsRequired();
    }
}
