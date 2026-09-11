using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        // الرمز فريد داخل المتجر: SAVE10 في متجرين كوبونان مستقلّان.
        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        // استخدامات الكوبون تشير إليه داخل المتجر (مفتاح مركّب، المرحلة 10).
        builder.HasAlternateKey(c => new { c.TenantId, c.Id });
        builder.Property(c => c.Type).HasConversion<int>();
        // نسبة مئوية أو مبلغ ثابت — نفس دقّة المال كي لا يُقتطع مبلغ خصم ثابت بالفلس.
        builder.Property(c => c.Value).HasColumnType(PersistenceConventions.MoneyColumnType);

        // UsedCount (المرحلة 10): الاستخدامات المحجوزة والمؤكَّدة — يُزاد عند إنشاء الطلب ويُنقص بإلغائه. rowversion يسلسل
        // طلبات الكوبون نفسه: الخاسر في سباق يعيد القواعد على قراءة جديدة (ADR-0013، ADR-0030).
        builder.HasRowVersion();

        // اعتماد اختياري (owned optional): كوبون بلا حد أدنى للطلب لا يخزّن شيئاً هنا.
        builder.OwnsOne(c => c.MinOrderAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("MinOrderAmount").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("MinOrderCurrency").HasMaxLength(3);
        });
    }
}
