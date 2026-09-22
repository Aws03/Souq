using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// الدفعات (المرحلة 11، ADR-0031): واحدة لكل طلب (فهرس فريد)، ومعرّف النيّة فريد في المتجر (الإشعار يصل به). لا عمود
// بطاقة — ولا يُضاف (اختبار معماري يحرس ذلك). rowversion: طلبا استرداد متزامنان يتعارضان على صفّ الدفعة.
// الاستردادات جزء من التجمّع بمفتاح مركّب داخل المتجر (Restrict: سجلّ مالي لا يُحذف).
// ============================================================================
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.HasAlternateKey(p => new { p.TenantId, p.Id });
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Gateway).HasMaxLength(Payment.GatewayMaxLength).IsUnicode(false).IsRequired();
        // هويّةُ الحساب الذي قبض (TD-50): اختياريّة — دفعاتٌ كُتبت قبلها، والبوّابة التجريبية بلا
        // مفتاح علنيّ. ASCII: المفاتيح العلنية للمزوّدين كلُّها كذلك.
        builder.Property(p => p.GatewayAccount).HasMaxLength(Payment.GatewayAccountMaxLength).IsUnicode(false);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(Payment.ProviderPaymentIdMaxLength).IsUnicode(false).IsRequired();
        builder.Property(p => p.RefundedAmount).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Property(p => p.PendingRefundAmount).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Ignore(p => p.Refundable);
        builder.Ignore(p => p.IsFullyRefunded);
        builder.HasRowVersion();

        builder.OwnsOne(p => p.Amount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("Amount").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(p => p.Amount).IsRequired();

        builder.HasOne<Order>().WithMany()
               .HasForeignKey(p => new { p.TenantId, p.OrderId })
               .HasPrincipalKey(o => new { o.TenantId, o.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Refunds).WithOne()
               .HasForeignKey(r => new { r.TenantId, r.PaymentId })
               .HasPrincipalKey(p => new { p.TenantId, p.Id })
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.TenantId, p.OrderId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.ProviderPaymentId }).IsUnique();
    }
}
