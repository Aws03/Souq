using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence.Configurations;

// ============================================================================
// جداولُ فوترة التاجر ([ADR-0056](0056)).
//
// **بلا `ITenantOwned` وبلا مرشّح** — الشكل B في ADR-0047 §1: الفاتورةُ ليست بيانات التاجر بل
// دفترُ المنصّة عنه، ويجب أن تبقى مقروءةً بعد أرشفة متجرها. وثمنُ ذلك أنّ العزل يقوم على شرطٍ
// صريح في كل قراءة، وهو ما يحرسه `TenancyRuleTests` بإدراج القارئ في قائمته المراجَعة.
//
// **والمفاتيحُ إلى `Tenants` بـ `Restrict`:** متجرٌ عليه فاتورةٌ لا يُحذف من تحتها. وهو غيرُ ذي
// أثرٍ عملياً — لا حذفَ صلباً لمتجر في هذا المستودع، بل أرشفة — لكنّه يجعل القاعدة تقول ذلك.
// ============================================================================

internal sealed class PlatformBillingSettingsConfiguration : IEntityTypeConfiguration<PlatformBillingSettings>
{
    public void Configure(EntityTypeBuilder<PlatformBillingSettings> builder)
    {
        builder.ToTable("PlatformBillingSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Currency).HasMaxLength(3);
        builder.Property(s => s.IssuerName).HasMaxLength(PlatformBillingSettings.IssuerNameMaxLength);
        builder.Property(s => s.IssuerAddress).HasMaxLength(PlatformBillingSettings.IssuerAddressMaxLength);
        builder.Property(s => s.IssuerTaxNumber).HasMaxLength(PlatformBillingSettings.TaxNumberMaxLength);
        builder.Property(s => s.InvoiceNumberPrefix)
            .HasMaxLength(PlatformBillingSettings.NumberPrefixMaxLength).IsRequired();
        builder.Property(s => s.CreditNoteNumberPrefix)
            .HasMaxLength(PlatformBillingSettings.NumberPrefixMaxLength).IsRequired();
        builder.Property(s => s.PaymentInstructions)
            .HasMaxLength(PlatformBillingSettings.PaymentInstructionsMaxLength);

        // ملفُّ ضريبةِ المنصّة نفسها — لا ملفُّ متجر. `Restrict`: ملفٌّ تُفوتر سوق تحته لا يُحذف.
        builder.HasOne<TaxProfile>().WithMany()
            .HasForeignKey(s => s.TaxProfileId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlatformDocumentSequenceConfiguration : IEntityTypeConfiguration<PlatformDocumentSequence>
{
    public void Configure(EntityTypeBuilder<PlatformDocumentSequence> builder)
    {
        builder.ToTable("PlatformDocumentSequences");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Series).HasMaxLength(PlatformDocumentSequence.SeriesMaxLength).IsRequired();

        // صفٌّ واحد لكل سلسلة. الفهرسُ الفريد هو ما يحسم سباقَ إنشاءَين على أوّل استعمال —
        // النمطُ نفسه الذي يتبعه `OrderNumberSequences`.
        builder.HasIndex(s => s.Series).IsUnique();
    }
}

internal sealed class PlatformInvoiceConfiguration : IEntityTypeConfiguration<PlatformInvoice>
{
    public void Configure(EntityTypeBuilder<PlatformInvoice> builder)
    {
        builder.ToTable("PlatformInvoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Number).HasMaxLength(PlatformInvoice.NumberMaxLength);
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.IssuerName).HasMaxLength(PlatformInvoice.NameMaxLength);
        builder.Property(i => i.IssuerAddress).HasMaxLength(PlatformInvoice.AddressMaxLength);
        builder.Property(i => i.IssuerTaxNumber).HasMaxLength(PlatformInvoice.TaxNumberMaxLength);
        builder.Property(i => i.BilledToName).HasMaxLength(PlatformInvoice.NameMaxLength);
        builder.Property(i => i.BilledToTaxNumber).HasMaxLength(PlatformInvoice.TaxNumberMaxLength);
        builder.Property(i => i.PaymentInstructions)
            .HasMaxLength(PlatformBillingSettings.PaymentInstructionsMaxLength);
        builder.Property(i => i.Notes).HasMaxLength(PlatformInvoice.NotesMaxLength);

        builder.Property(i => i.TaxAmount).HasPrecision(18, 4);
        builder.Property(i => i.CreditedAmount).HasPrecision(18, 4);

        // لقطةُ الضريبة مستندٌ في عمود، بالمحوّل نفسه الذي يخدم `Orders.TaxSnapshot` — فلا
        // تسلسلان لشيءٍ واحد، ولا تنفرد الفاتورةُ بقواعدِ كتابةٍ تخالف الطلب.
        builder.Property(i => i.TaxSnapshot)
            .HasConversion(TaxSnapshotJson.Converter)
            .Metadata.SetValueComparer(TaxSnapshotJson.Comparer);

        // **رقمٌ فريدٌ عبر المنصّة كلّها** لا عبر متجرٍ واحد: السلسلةُ سلسلةُ سوق، ورقمان
        // متطابقان فيها يعنيان مستندَين يُشار إليهما بالاسم نفسه. مُرشَّحٌ على غير الفارغ لأنّ
        // المسوّدات كلّها بلا رقم.
        builder.HasIndex(i => i.Number).IsUnique().HasFilter("[Number] IS NOT NULL");

        // قائمةُ فواتير متجرٍ بترتيبها الزمنيّ — أكثرُ قراءةٍ تقع على هذا الجدول.
        builder.HasIndex(i => new { i.TenantId, i.IssuedAtUtc });

        // البحثُ عمّا استحقّ ولم يُسدَّد: مدخلُ شاشة المستحقّات اليوم، ومدخلُ سلّم المطالبة في C6.
        builder.HasIndex(i => new { i.Status, i.DueAtUtc });

        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Plan>().WithMany()
            .HasForeignKey(i => i.PlanId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BillingPeriod>().WithMany()
            .HasForeignKey(i => i.BillingPeriodId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(PlatformInvoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(i => i.Lines).WithOne()
            .HasForeignKey(l => l.PlatformInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PlatformInvoice.Payments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(i => i.Payments).WithOne()
            .HasForeignKey(p => p.PlatformInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlatformInvoiceLineConfiguration : IEntityTypeConfiguration<PlatformInvoiceLine>
{
    public void Configure(EntityTypeBuilder<PlatformInvoiceLine> builder)
    {
        builder.ToTable("PlatformInvoiceLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).HasMaxLength(PlatformInvoiceLine.DescriptionMaxLength).IsRequired();
        builder.Property(l => l.Currency).HasMaxLength(3).IsRequired();
        builder.Property(l => l.TaxCategory).HasMaxLength(TaxRate.CategoryMaxLength).IsRequired();

        // الكمّيةُ عشرية: وحدةُ مقياسٍ قد تكون جزءاً. وأربعُ خانات تكفي الدينارَ بثلاثٍ وتزيد.
        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitAmount).HasPrecision(18, 4);
    }
}

internal sealed class PlatformInvoicePaymentConfiguration : IEntityTypeConfiguration<PlatformInvoicePayment>
{
    public void Configure(EntityTypeBuilder<PlatformInvoicePayment> builder)
    {
        builder.ToTable("PlatformInvoicePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasPrecision(18, 4);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(PlatformInvoicePayment.ReferenceMaxLength);
        builder.Property(p => p.Note).HasMaxLength(PlatformInvoicePayment.NoteMaxLength);

        // مَن سجّل السداد. `Restrict`: حسابٌ سجّل إقراراً بوصول مالٍ لا يُحذف فيُفقَد مَن أقرّ.
        builder.HasOne<Souq.Domain.Identity.User>().WithMany()
            .HasForeignKey(p => p.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Number).HasMaxLength(CreditNote.NumberMaxLength);
        builder.Property(n => n.Currency).HasMaxLength(3).IsRequired();
        builder.Property(n => n.Reason).HasMaxLength(CreditNote.ReasonMaxLength);
        builder.Property(n => n.TaxAmount).HasPrecision(18, 4);

        builder.Property(n => n.TaxSnapshot)
            .HasConversion(TaxSnapshotJson.Converter)
            .Metadata.SetValueComparer(TaxSnapshotJson.Comparer);

        builder.HasIndex(n => n.Number).IsUnique().HasFilter("[Number] IS NOT NULL");
        builder.HasIndex(n => n.PlatformInvoiceId);

        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(n => n.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // إشعارٌ بلا فاتورته ليس مستنداً: `Restrict` تمنع فاتورةً تُحذف من تحت تصحيحها.
        builder.HasOne<PlatformInvoice>().WithMany()
            .HasForeignKey(n => n.PlatformInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(CreditNote.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(n => n.Lines).WithOne()
            .HasForeignKey(l => l.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CreditNoteLineConfiguration : IEntityTypeConfiguration<CreditNoteLine>
{
    public void Configure(EntityTypeBuilder<CreditNoteLine> builder)
    {
        builder.ToTable("CreditNoteLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).HasMaxLength(CreditNoteLine.DescriptionMaxLength).IsRequired();
        builder.Property(l => l.Currency).HasMaxLength(3).IsRequired();
        builder.Property(l => l.TaxCategory).HasMaxLength(TaxRate.CategoryMaxLength).IsRequired();
        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitAmount).HasPrecision(18, 4);
    }
}

internal sealed class BillingPeriodConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> builder)
    {
        builder.ToTable("BillingPeriods");
        builder.HasKey(p => p.Id);

        // فترةٌ واحدة لكل بداية لكل متجر: فترتان تبدآن في اللحظة نفسها تجعلان «أيُّ فترةٍ يلتحق
        // بها هذا الحدث؟» سؤالاً بجوابين.
        builder.HasIndex(p => new { p.TenantId, p.StartsAtUtc }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status });

        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BillableEventConfiguration : IEntityTypeConfiguration<BillableEvent>
{
    public void Configure(EntityTypeBuilder<BillableEvent> builder)
    {
        builder.ToTable("BillableEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Meter).HasMaxLength(BillableEvent.MeterMaxLength).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(BillableEvent.IdempotencyKeyMaxLength).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(BillableEvent.DescriptionMaxLength);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);

        // ============================================================================
        // **الفهرسُ الذي يمنع الفوترة مرّتين.** فحصٌ في الذاكرة قبل الإدراج لا يكفي: مُنادِيان
        // متزامنان يمرّان كلاهما. هذا القيد هو ما يحسمه فعلاً، والكودُ يلتقط خرقَه ويُعيد صفَّه
        // القائم — النمطُ نفسه الذي يتبعه عدّادُ أرقام الطلبات.
        // ============================================================================
        builder.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();

        // أحداثُ فترةٍ لم تُفوتَر بعد: قراءةُ صناعة الفاتورة.
        builder.HasIndex(e => new { e.BillingPeriodId, e.PlatformInvoiceId });

        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BillingPeriod>().WithMany()
            .HasForeignKey(e => e.BillingPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PlatformInvoice>().WithMany()
            .HasForeignKey(e => e.PlatformInvoiceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
