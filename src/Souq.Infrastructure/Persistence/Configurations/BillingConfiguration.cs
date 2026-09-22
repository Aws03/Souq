using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// جداول مستوى التحكّم التجاري (C1، وحدة Billing، ADR-0047 §1). لا مرشّح مستأجر على أيٍّ منها:
//   • الشكل C (Plans وأبناؤها) — عالمية بلا متجر أصلاً.
//   • الشكل B (Subscriptions، EntitlementOverrides) — تخصّ متجراً وتحمل TenantId **عموداً عادياً**
//     بمفتاح أجنبي، كما يفعل TenantDomains اليوم. لا حارس كتابة يختمها ولا مرشّح يحميها،
//     فالعزل شرطٌ صريح في كل قراءة — ويحصر مَن يقرؤها أصلاً
//     `قراءة_جداول_المنصّة_بمفتاح_متجر_محصورة_في_مسارها_المراجَع` في TenancyRuleTests.
// ============================================================================

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(Plan.CodeMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();

        // الهوية الحقيقية: المعرّف + الإصدار. إصداران بالرقم نفسه لخطة واحدة يعنيان شرطين لعقد واحد.
        builder.HasIndex(p => new { p.Code, p.Version }).IsUnique();

        builder.Ignore(p => p.Grants);

        // سعرُ الإصدار (C5، ADR-0056): مبلغٌ بعملته — أو **لا سعر**، وهو حالُ الخطة التأسيسية
        // وكلِّ خطةٍ لم يُسعّرها المشغّل بعد. العمودان قابلان للفراغ معاً، ولا افتراضَ لأيٍّ منهما.
        builder.OwnsOne(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("PriceAmount").HasPrecision(18, 4);
            price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });

        builder.HasMany(p => p.Entitlements)
               .WithOne()
               .HasForeignKey("PlanId")
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Limits)
               .WithOne()
               .HasForeignKey("PlanId")
               .IsRequired()
               .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Limits).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PlanEntitlementConfiguration : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.ToTable("PlanEntitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Entitlement).HasMaxLength(64).IsRequired();
        builder.HasIndex("PlanId", nameof(PlanEntitlement.Entitlement)).IsUnique();
    }
}

public class PlanLimitConfiguration : IEntityTypeConfiguration<PlanLimit>
{
    public void Configure(EntityTypeBuilder<PlanLimit> builder)
    {
        builder.ToTable("PlanLimits");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(Limit.NameMaxLength).IsRequired();
        builder.HasIndex("PlanId", nameof(PlanLimit.Name)).IsUnique();
    }
}

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<int>();

        // صفّ واحد لكل متجر: "ما خطة هذا المتجر؟" سؤال له جواب واحد، والقاعدة هي الحارس الأخير.
        builder.HasIndex(s => s.TenantId).IsUnique();

        // Restrict لا Cascade — خلافاً لـ TenantDomains: سجلّ المنصّة **عن** المتجر يجب أن يبقى مقروءاً
        // بعد انتهاء العلاقة. المتاجر تُؤرشف ولا تُحذف (TenantRepository.Remove يرمي)، فالقيد إعلانُ نيّة
        // لا سلوكٌ يُنتظر وقوعه — وهو ما يمنع حذفاً مستقبلياً من أن يمحو دفاتر C5 معه بصمت.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId)
               .IsRequired().OnDelete(DeleteBehavior.Restrict);

        // الخطة لا تُحذف من تحت مشترك: القيد يمنع حذف إصدار ما زال يسري على متجر.
        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId)
               .IsRequired().OnDelete(DeleteBehavior.Restrict);

        // تغيير الخطة وإلغاؤها قد يقعان معاً من المنصّة: تعارض متفائل كسائر جداول المتجر.
        builder.HasRowVersion();
    }
}

public class EntitlementOverrideConfiguration : IEntityTypeConfiguration<EntitlementOverride>
{
    public void Configure(EntityTypeBuilder<EntitlementOverride> builder)
    {
        builder.ToTable("EntitlementOverrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Entitlement).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Reason).HasMaxLength(EntitlementOverride.ReasonMaxLength).IsRequired();

        // شكل القراءة الوحيد: استثناءات هذا المتجر السارية الآن. الصفوف تُحفظ بعد انتهائها — أثرُ
        // "من منح ماذا ولماذا" هو نصف قيمة الاستثناء، فلا يُحذف بانتهائه.
        builder.HasIndex(o => new { o.TenantId, o.ExpiresAtUtc });

        builder.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId)
               .IsRequired().OnDelete(DeleteBehavior.Restrict);
    }
}
