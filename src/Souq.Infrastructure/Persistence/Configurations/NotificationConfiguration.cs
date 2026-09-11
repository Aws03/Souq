using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// إشعارات داخل التطبيق (المرحلة 14): فهرسا صندوق المستلم — عدّ غير المقروء للشارة، والقائمة الأحدث أولاً.
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(Notification.KindMaxLength).IsRequired();
        builder.Property(n => n.Data).HasMaxLength(Notification.DataMaxLength).IsRequired();
        builder.Ignore(n => n.IsRead);

        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.ReadAt });
        builder.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.CreatedAt });
    }
}
