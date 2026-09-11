using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Infrastructure.Persistence.Outbox;

namespace Souq.Infrastructure.Persistence.Configurations;

// صندوق الصادر (المرحلة 14): بلا مرشّح مستأجر ولا مفتاح أجنبي — كتلة بناء يقرؤها المُرسِل عبر المتاجر ويحذف المُنجز منها بعد
// مدّة الاحتفاظ. فهرسان مُرشَّحان: ما ينتظر الإرسال (لا ينمو بالمُنجز)، والمُنجز (للحذف الدوري).
public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength).IsRequired();
        builder.Property(m => m.Payload).HasMaxLength(OutboxMessage.PayloadMaxLength).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.ErrorMaxLength);

        builder.HasIndex(m => m.NextAttemptAt).HasFilter("[ProcessedAt] IS NULL AND [FailedAt] IS NULL");
        builder.HasIndex(m => m.ProcessedAt).HasFilter("[ProcessedAt] IS NOT NULL");
    }
}
