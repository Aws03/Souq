using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<int>();   // enum يُخزّن كرقم
        builder.Property(o => o.ShippingAddress).HasMaxLength(500);
        builder.Property(o => o.CouponCode).HasMaxLength(50);
        builder.Property(o => o.PaymentIntentId).HasMaxLength(100);
        builder.Property(o => o.TrackingNumber).HasMaxLength(100);
        builder.Property(o => o.ShippingCarrier).HasMaxLength(100);
        builder.HasIndex(o => o.CustomerId);

        // الحالة يغيّرها أكثر من طرف بنفس اللحظة (تأكيد العميل ضد Webhook، الإدارة ضد
        // الدفع) ⇒ rowversion يجعل الثاني يكتشف السباق بدل آثار مكرّرة (ADR-0013).
        builder.HasRowVersion();

        // خصم الكوبون المطبَّق (اختياري — owned optional، لا شيء يُخزَّن بلا كوبون).
        builder.OwnsOne(o => o.DiscountAmount, m =>
        {
            m.Property(x => x.Amount).HasColumnName("DiscountAmount").HasColumnType(PersistenceConventions.MoneyColumnType);
            m.Property(x => x.Currency).HasColumnName("DiscountCurrency").HasMaxLength(3);
        });

        // مفتاح أجنبي للعميل دون خاصية تنقّل في الكيان. Restrict: التاريخ المالي لا يُمحى.
        builder.HasOne<Customer>()
               .WithMany()
               .HasForeignKey(o => o.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);

        // علاقة التجمّع: الطلب يملك أسطره. IsRequired: سطر بلا طلب لا معنى له — كان
        // العمود يقبل NULL (مفتاح ظلّ على علاقة اختيارية) فيسمح بأسطر يتيمة.
        builder.HasMany(o => o.Items)
               .WithOne()
               .HasForeignKey("OrderId")
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // علاقة تجمّع ثانية: سجلّ تاريخ الحالة — نفس النمط والإلزام.
        builder.HasMany(o => o.StatusHistory)
               .WithOne()
               .HasForeignKey("OrderId")
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
