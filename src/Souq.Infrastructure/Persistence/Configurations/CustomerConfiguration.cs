using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;
using Souq.Domain.Identity;
using Souq.Domain.ValueObjects;

namespace Souq.Infrastructure.Persistence.Configurations;

// ملف الشراء (وحدة Customers). اعتماد الدخول انتقل إلى Users في المرحلة 3؛ هنا مرجع للحساب فقط. المرحلة 7: الهاتف،
// الحالة التجارية، ختما الحظر والمحو، ودفتر العناوين جدولاً ابناً بمفتاح ظلّ CustomerId.
public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FullName).HasMaxLength(Customer.FullNameMaxLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(PostalAddress.PhoneMaxLength);
        builder.Property(c => c.Status).HasConversion<int>();
        builder.Ignore(c => c.IsBlocked);
        builder.Ignore(c => c.IsErased);
        builder.Ignore(c => c.DefaultShippingAddress);

        builder.HasMany(c => c.Addresses).WithOne().HasForeignKey("CustomerId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);

        // ملف واحد لكل حساب في المتجر (القاعدة هي الحارس الأخير ضد تسجيلين متزامنين).
        builder.HasIndex(c => new { c.TenantId, c.UserId }).IsUnique();
        // بحث الإدارة عن العملاء ببريد التواصل — ليس فريداً: هوية الدخول في Users. والتصفية بالحالة.
        builder.HasIndex(c => new { c.TenantId, c.Email });
        builder.HasIndex(c => new { c.TenantId, c.Status });

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

// عناوين العميل: ابن تجمّع (مفتاح ظلّ إلى العميل، يُحذف معه ويُحذف يتيماً حين يزيله العميل). الطلبات تحفظ لقطتها النصّية
// فحذف عنوان لا يمسّ طلباً سابقاً.
public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Label).HasMaxLength(CustomerAddress.LabelMaxLength);
        builder.Property(a => a.RecipientName).HasMaxLength(PostalAddress.NameMaxLength).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(PostalAddress.PhoneMaxLength).IsRequired();
        builder.Property(a => a.Country).HasMaxLength(2).IsRequired();
        builder.Property(a => a.City).HasMaxLength(PostalAddress.CityMaxLength).IsRequired();
        builder.Property(a => a.Region).HasMaxLength(PostalAddress.RegionMaxLength);
        builder.Property(a => a.Line1).HasMaxLength(PostalAddress.LineMaxLength).IsRequired();
        builder.Property(a => a.Line2).HasMaxLength(PostalAddress.LineMaxLength);
        builder.Property(a => a.PostalCode).HasMaxLength(PostalAddress.PostalCodeMaxLength);
        builder.HasIndex("CustomerId");
    }
}
