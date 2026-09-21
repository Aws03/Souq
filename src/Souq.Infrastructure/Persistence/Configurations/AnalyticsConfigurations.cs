using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// جداولُ الالتقاط السلوكي ([ADR-0050](0050) §7).
//
// `TenantId` ومرشّحُه ومفتاحُه الأجنبي إلى `Tenants` **لا تُضبَط هنا**: حلقةُ الانعكاس في
// `AppDbContext` تفعلها لكل `ITenantOwned`.
//
// **فهرسان من اليوم الأول، لا أكثر — كما أثبت `SearchQueryLogs` قبله:** واحدٌ لشكل القراءة وواحدٌ
// للمسح. وكلُّ فهرسٍ ثالث يُدفع ثمنُه تضخيماً في الكتابة على مسارٍ حجمُه أكبر بمرتبة من كل ما
// سبقه. ويومَ تُؤلم لوحاتُ التاجر، الخطوةُ التالية عمودٌ صفّيّ (columnstore) غيرُ مجمَّع فوق
// **الجدول نفسه** — لا مخزنٌ ثانٍ.
//
// ولا مفتاحَ أجنبيّ من الحدث إلى `Product`: الحدثُ واقعةٌ تاريخية بلقطتها، ومنتجٌ يُؤرشَف لا يُبطل
// حدثاً وقع. أمّا التجميعات فتحمل مفاتيحها المركّبة: هي قراءةٌ مباشرة للتاجر عن منتجٍ قائم.
// ============================================================================
internal sealed class BehaviouralEventConfiguration : IEntityTypeConfiguration<BehaviouralEvent>
{
    public void Configure(EntityTypeBuilder<BehaviouralEvent> builder)
    {
        builder.ToTable("BehaviouralEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(BehaviouralEvent.NameMaxLength).IsRequired();
        builder.Property(e => e.Surface).HasMaxLength(BehaviouralEvent.SurfaceMaxLength).IsRequired();
        builder.Property(e => e.Culture).HasMaxLength(BehaviouralEvent.CultureMaxLength).IsRequired();
        builder.Property(e => e.Payload).HasMaxLength(BehaviouralEvent.PayloadMaxLength).IsRequired();
        builder.Property(e => e.VisitorId).HasMaxLength(BehaviouralEvent.IdentifierMaxLength);
        builder.Property(e => e.SessionId).HasMaxLength(BehaviouralEvent.IdentifierMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(BehaviouralEvent.IdentifierMaxLength);

        // معرّف الحدث يُصكّ عند الالتقاط: فريدٌ لكل متجر، فإعادةُ إرسالٍ لا تصنع صفّين.
        builder.HasIndex(e => new { e.TenantId, e.EventId }).IsUnique();

        // شكلُ القراءة: أحداثُ متجرٍ في مدّة، بنوعها. والتجميعُ يمرّ بالمدّة نفسها.
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt, e.Name });
    }
}

internal sealed class VisitorIdentityLinkConfiguration : IEntityTypeConfiguration<VisitorIdentityLink>
{
    public void Configure(EntityTypeBuilder<VisitorIdentityLink> builder)
    {
        builder.ToTable("VisitorIdentityLinks");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.VisitorId).HasMaxLength(BehaviouralEvent.IdentifierMaxLength).IsRequired();

        // المفتاح المركّب: كل مفتاح أجنبي بين بيانات المتاجر يحمل المتجر (TenancyRuleTests).
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(l => new { l.TenantId, l.CustomerId })
            .HasPrincipalKey(c => new { c.TenantId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // ربطٌ واحد لكل (زائر، عميل): الزائر نفسه قد يدخل بحسابَين، والعميل نفسه من جهازَين.
        builder.HasIndex(l => new { l.TenantId, l.VisitorId, l.CustomerId }).IsUnique();

        // ومسارُ المحو: كلُّ روابط عميلٍ بعينه، دفعةً واحدة.
        builder.HasIndex(l => new { l.TenantId, l.CustomerId });
    }
}

internal sealed class ProductEngagementDailyConfiguration : IEntityTypeConfiguration<ProductEngagementDaily>
{
    public void Configure(EntityTypeBuilder<ProductEngagementDaily> builder)
    {
        builder.ToTable("ProductEngagementDailies");
        builder.HasKey(r => r.Id);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.Day, r.ProductId }).IsUnique();
    }
}

internal sealed class ProductPairDailyConfiguration : IEntityTypeConfiguration<ProductPairDaily>
{
    public void Configure(EntityTypeBuilder<ProductPairDaily> builder)
    {
        builder.ToTable("ProductPairDailies");
        builder.HasKey(r => r.Id);

        builder.HasOne<Product>().WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ProductIdLow })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ProductIdHigh })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.TenantId, r.Day, r.ProductIdLow, r.ProductIdHigh }).IsUnique();
    }
}

internal sealed class AnalyticsRollupStateConfiguration : IEntityTypeConfiguration<AnalyticsRollupState>
{
    public void Configure(EntityTypeBuilder<AnalyticsRollupState> builder)
    {
        builder.ToTable("AnalyticsRollupStates");
        builder.HasKey(s => s.Id);

        // صفٌّ واحد لكل متجر: العلامة حالةٌ لا سجلّ.
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
