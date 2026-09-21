using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Entities;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// جداولُ الضريبة ([ADR-0055](0055)).
//
// **الملفّ وإصداراته ونسبُه جداولُ منصّة بلا `TenantId`** — كـ `Plan` تماماً، ولنفس السبب: قواعدُ
// اختصاصٍ تحفظها المنصّة مرّةً ويختارها أيُّ متجر. ولو حملت معرّف متجرٍ لعادت القيمُ تتشتّت متجراً
// متجراً، ولَما كان لتحقّقٍ واحدٍ معنى.
//
// **وإعدادُ ضريبةِ المتجر جدولٌ ملكٌ لمتجر** (`ITenantOwned`): مرشّحُ المستأجر ومفتاحُه إلى
// `Tenants` يضعهما الانعكاسُ في `AppDbContext`، لا هذا الملفّ.
// ============================================================================
internal sealed class TaxProfileConfiguration : IEntityTypeConfiguration<TaxProfile>
{
    public void Configure(EntityTypeBuilder<TaxProfile> builder)
    {
        builder.ToTable("TaxProfiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Jurisdiction).HasMaxLength(TaxProfile.JurisdictionMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(TaxProfile.NameMaxLength).IsRequired();

        // ملفٌّ واحد لكل اختصاص: ملفّان لبلدٍ واحد يجعلان «أيُّهما الصحيح؟» سؤالاً لا جواب له،
        // والتصحيحُ إصدارٌ داخل الملفّ لا ملفٌّ ثانٍ.
        builder.HasIndex(p => p.Jurisdiction).IsUnique();

        builder.Metadata.FindNavigation(nameof(TaxProfile.Versions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Versions).WithOne()
            .HasForeignKey(v => v.TaxProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaxProfileVersionConfiguration : IEntityTypeConfiguration<TaxProfileVersion>
{
    public void Configure(EntityTypeBuilder<TaxProfileVersion> builder)
    {
        builder.ToTable("TaxProfileVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Notes).HasMaxLength(4000);

        // حالةُ التحقّق قيمةٌ مملوكة: حالتُها ومَن تحقّق ومتى وملاحظتُه في أعمدة الإصدار نفسه —
        // فلا يُقرأ رقمٌ بلا حالةِ تحقّقه، ولا يُوصَل جدولٌ ثانٍ ليُعرَف أنّ الرقم غير مؤكَّد.
        builder.OwnsOne(v => v.Verification, verification =>
        {
            verification.Property(x => x.State).HasColumnName("VerificationState").IsRequired();
            verification.Property(x => x.By).HasColumnName("VerifiedBy").HasMaxLength(TaxVerification.ByMaxLength);
            verification.Property(x => x.At).HasColumnName("VerifiedAt");
            verification.Property(x => x.Note).HasColumnName("VerificationNote").HasMaxLength(TaxVerification.NoteMaxLength);
        });

        // عتبةُ التسجيل مبلغٌ بعملته — أو لا عتبةَ للاختصاص. تُعرَض ولا يُحتسب بها شيءٌ اليوم.
        builder.OwnsOne(v => v.RegistrationThreshold, threshold =>
        {
            threshold.Property(m => m.Amount).HasColumnName("RegistrationThresholdAmount").HasPrecision(18, 4);
            threshold.Property(m => m.Currency).HasColumnName("RegistrationThresholdCurrency").HasMaxLength(3);
        });

        // إصدارٌ واحد برقمه لكل ملفّ، والقراءةُ الحيّة بتاريخ النفاذ.
        builder.HasIndex(v => new { v.TaxProfileId, v.Version }).IsUnique();
        builder.HasIndex(v => new { v.TaxProfileId, v.EffectiveFrom });

        builder.Metadata.FindNavigation(nameof(TaxProfileVersion.Rates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(v => v.Rates).WithOne()
            .HasForeignKey(r => r.TaxProfileVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("TaxRates");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Code).HasMaxLength(TaxRate.CodeMaxLength).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(TaxRate.NameMaxLength).IsRequired();
        builder.Property(r => r.Category).HasMaxLength(TaxRate.CategoryMaxLength).IsRequired();

        builder.HasIndex(r => new { r.TaxProfileVersionId, r.Code }).IsUnique();
    }
}

internal sealed class StoreTaxSettingsConfiguration : IEntityTypeConfiguration<StoreTaxSettings>
{
    public void Configure(EntityTypeBuilder<StoreTaxSettings> builder)
    {
        builder.ToTable("StoreTaxSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.RegistrationNumber).HasMaxLength(StoreTaxSettings.RegistrationNumberMaxLength);

        // صفٌّ واحد لكل متجر: إعدادٌ لا سجلّ.
        builder.HasIndex(s => s.TenantId).IsUnique();

        // المفتاحُ إلى جدول منصّة بلا متجر — كـ `Subscription` إلى `Plan`. و`Restrict`: ملفٌّ
        // اختارَه متجرٌ لا يُحذف من تحته.
        builder.HasOne<TaxProfile>().WithMany()
            .HasForeignKey(s => s.TaxProfileId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
