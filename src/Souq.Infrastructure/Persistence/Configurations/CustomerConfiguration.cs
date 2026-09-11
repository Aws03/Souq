using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FullName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Role).HasMaxLength(20).IsRequired();
        // تجزئة SHA-256 بصيغة hex (64 حرفاً) — الرمز الخام لا يُخزَّن أبداً.
        builder.Property(c => c.PasswordResetTokenHash).HasMaxLength(64);

        // فهرس على التجزئة: GetByResetTokenAsync يُستدعى بكل زيارة لصفحة إعادة
        // التعيين — بلا فهرس يصبح فحصاً كاملاً للجدول عند كل محاولة.
        builder.HasIndex(c => c.PasswordResetTokenHash);

        // البريد هو هوية الدخول — فريد داخل المتجر على مستوى قاعدة البيانات (القاعدة النهائية
        // للحقيقة ضد الطلبات المتزامنة). الحسابات لكل متجر (D-06): البريد نفسه في متجرين مسموح.
        builder.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
    }
}
