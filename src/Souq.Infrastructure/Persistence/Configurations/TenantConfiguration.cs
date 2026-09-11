using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// جداول المنصّة (وحدة Platform): لا TenantId مُرشَّح عليها — هي ما يُعرِّف المستأجر. قابلة للفصل عن
// جداول المتاجر لو انتقل متجر لقاعدة مستقلّة (MultiTenancy.md §6).
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(Tenant.SlugMaxLength).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Status).HasConversion<int>();
        builder.Property(t => t.DefaultCulture).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.TimeZone).HasMaxLength(Tenant.TimeZoneMaxLength).IsRequired();

        // إعدادات الواجهة (D-12): مستند JSON يُقرأ ويُكتب كاملاً مع المتجر ولا يُستعلم بداخله. NULL = متجر سابق
        // للمرحلة 4 (يُعرض الافتراضي حتى يُضبط — البذر يضبط مظهر المتجر الافتراضي، DbSeeder).
        builder.Property<StoreSettings?>("_settings")
               .HasColumnName("Settings")
               .HasConversion(StoreSettingsJson.Converter, StoreSettingsJson.Comparer);

        // الوحدات المفعّلة (D-11). الافتراضي للصفوف القائمة: كل الوحدات — الترقية لا تُفقد متجراً كوبوناته.
        builder.Property<string>("_modules")
               .HasColumnName("EnabledModules")
               .HasMaxLength(200)
               .IsRequired()
               .HasDefaultValue(StoreModules.Format(StoreModules.All));

        // سياسة نشر التقييمات (المرحلة 13): العمود يُضاف false، ثم تعيده هجرة Phase13 true للمتاجر القائمة (كانت تنشر فوراً).
        builder.Property(t => t.ReviewsAutoApprove);

        builder.Ignore(t => t.Settings);
        builder.Ignore(t => t.HasCustomSettings);
        builder.Ignore(t => t.Modules);
        builder.Ignore(t => t.PrimaryDomain);

        // مالك المنصّة وأكثر من مدير قد يعدّلون المتجر نفسه معاً (حالة، نطاقات، عملة، إعدادات).
        builder.HasRowVersion();

        builder.HasMany(t => t.Domains)
               .WithOne()
               .HasForeignKey(d => d.TenantId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Domains).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("TenantDomains");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Host).HasMaxLength(TenantDomain.HostMaxLength).IsRequired();

        // المضيف يحدّد المتجر لكل طلب ⇒ فريد على مستوى المنصّة كلها (القاعدة هي الحارس الأخير).
        builder.HasIndex(d => d.Host).IsUnique();
    }
}
