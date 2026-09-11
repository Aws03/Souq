using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// استخدامات الكوبونات (المرحلة 10، ADR-0030): واحد لكل طلب (فهرس فريد)، ومفاتيح مركّبة للكوبون والطلب والعميل داخل المتجر
// نفسه (Restrict: سجلّ تقارير لا يُحذف مع ما يشير إليه). فهرس (الكوبون، العميل، الحالة) يخدم عدّ حدّ العميل.
// ============================================================================
public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("CouponRedemptions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Ignore(r => r.IsActive);

        builder.OwnsOne(r => r.Discount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("DiscountAmount").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(r => r.Discount).IsRequired();

        builder.HasOne<Coupon>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.CouponId })
               .HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.OrderId })
               .HasPrincipalKey(o => new { o.TenantId, o.Id })
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany()
               .HasForeignKey(r => new { r.TenantId, r.CustomerId })
               .HasPrincipalKey(c => new { c.TenantId, c.Id })
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.OrderId }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.CouponId, r.CustomerId, r.Status });
    }
}
