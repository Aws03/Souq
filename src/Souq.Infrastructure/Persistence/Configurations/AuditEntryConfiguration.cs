using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Auditing;

namespace Souq.Infrastructure.Persistence.Configurations;

// سجلّ التدقيق (D-17): للإضافة فقط (حارس الكتابة). لا مفتاح أجنبي إلى Tenants عمداً — السطر شاهد تاريخي لا
// علاقة حيّة، ولا مرشّح مستأجر (يُقرأ من المنصّة بشرط صريح). الفهارس لأسئلة التدقيق الشائعة: بالزمن، بمتجر، بفاعل.
public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Area).HasMaxLength(10).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(a => a.ActorRole).HasMaxLength(AuditEntry.RoleMaxLength);
        builder.Property(a => a.TargetType).HasMaxLength(AuditEntry.TargetTypeMaxLength);
        builder.Property(a => a.TargetId).HasMaxLength(AuditEntry.TargetIdMaxLength);
        builder.Property(a => a.Metadata).HasMaxLength(AuditEntry.MetadataMaxLength);
        builder.Property(a => a.IpAddress).HasMaxLength(AuditEntry.IpAddressMaxLength);
        builder.Property(a => a.CorrelationId).HasMaxLength(AuditEntry.CorrelationIdMaxLength);

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
    }
}
