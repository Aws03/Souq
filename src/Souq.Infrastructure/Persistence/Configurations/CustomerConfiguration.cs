using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;
using Souq.Domain.Identity;

namespace Souq.Infrastructure.Persistence.Configurations;

// ملف الشراء (وحدة Customers). اعتماد الدخول انتقل إلى Users في المرحلة 3؛ هنا مرجع للحساب فقط.
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FullName).HasMaxLength(Customer.FullNameMaxLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();

        // ملف واحد لكل حساب في المتجر (القاعدة هي الحارس الأخير ضد تسجيلين متزامنين).
        builder.HasIndex(c => new { c.TenantId, c.UserId }).IsUnique();
        // بحث الإدارة عن العملاء ببريد التواصل (المرحلة 7) — ليس فريداً: هوية الدخول في Users.
        builder.HasIndex(c => new { c.TenantId, c.Email });

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
