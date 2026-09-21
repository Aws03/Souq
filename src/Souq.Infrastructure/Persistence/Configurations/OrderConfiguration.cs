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
        // لقطة الشحن (المرحلة 12): الطريقة وتكلفتها بعملة الطلب ومدّتها ودولة العنوان وقالب رابط التتبّع.
        builder.Property(o => o.ShippingMethodName).HasMaxLength(Order.ShippingMethodMaxLength);
        builder.Property(o => o.ShippingAmount).HasColumnType(PersistenceConventions.MoneyColumnType);

        // الضريبة (ADR-0055): المبلغ عمودٌ تقرؤه القوائم بلا فكّ JSON، واللقطةُ مستندٌ يُقرأ كاملاً
        // مع طلبه — و`TaxAddedToTotal` مشتقّةٌ من عُرفِ اللقطة فلا تُخزَّن.
        builder.Property(o => o.TaxAmount).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Property(o => o.TaxSnapshot)
               .HasConversion(TaxSnapshotJson.Converter, TaxSnapshotJson.Comparer);
        builder.Ignore(o => o.Tax);
        builder.Ignore(o => o.TaxAddedToTotal);
        builder.Property(o => o.ShippingCountry).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(o => o.ShippingTrackingUrlTemplate).HasMaxLength(ShippingMethod.TrackingUrlMaxLength);
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        // قائمة الإدارة: طلبات متجر واحد، الأحدث أولاً — الفهرس يبدأ بالمستأجر.
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt });

        // المرحلة 9: رقم الطلب فريد داخل المتجر، ورمز التتبّع العام فريد (يُبحث به بلا مصادقة)، والفوترة لقطة كالشحن،
        // والإجماليات المثبَّتة أعمدة تقرؤها القوائم بلا جمع. مرشّح الحالة في قائمة الإدارة له فهرسه.
        builder.Property(o => o.BillingAddress).HasMaxLength(Order.ShippingAddressMaxLength).IsRequired();
        builder.Property(o => o.TrackingToken).HasMaxLength(Order.TrackingTokenLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(o => o.PlacedSubtotal).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Property(o => o.PlacedTotal).HasColumnType(PersistenceConventions.MoneyColumnType);
        builder.Ignore(o => o.IsPlaced);
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.TrackingToken }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt });

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
        // داخل المتجر (TenantId, CustomerId): فهرسه يخدم "طلباتي" أيضاً.
        builder.HasOne<Customer>()
               .WithMany()
               .HasForeignKey(o => new { o.TenantId, o.CustomerId })
               .HasPrincipalKey(c => new { c.TenantId, c.Id })
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
