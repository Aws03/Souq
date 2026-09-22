using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Interfaces;
using Souq.Domain.Platform;
using Souq.Domain.ValueObjects;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// المطالبة الآلية وتصعيدُها إلى تعليق، على SQL Server حقيقيّ (C6، [ADR-0058](0058)).
//
// **وما يُقاس هنا هو الفعلُ نفسه لا القرار**: القرارُ دالّةٌ نقيّة مُختبَرة بجدول حالاتٍ في
// `DunningPolicyTests`، وما يبقى — وهو ما لا يراه أيُّ اختبار وحدة — أنّ تنفيذ ذلك القرار يُغيّر
// حالةَ المتجر فعلاً، ويضع رسالةً في الصندوق بمتجرها الصحيح، ويُعلّم الفاتورةَ كي لا يُعاد.
//
// **والمتجر يعود فعّالاً في `finally` دائماً**: متاجرُ هذه الاختبارات تُنشأ لها وحدها، لكنّ
// تركَ متجرٍ معلَّقاً يُفشل قراءاتٍ لاحقة بأعراضٍ تُقرأ كعيوبٍ في ميزاتٍ أخرى.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class DunningTests
{
    private readonly SouqApiFactory _factory;

    public DunningTests(SouqApiFactory factory) => _factory = factory;

    // إعدادُ فوترةٍ مضبوط: المطالبةُ مفعّلةٌ بمهلةٍ ومُهَلِ تذكيرٍ يختارها الاختبار.
    private async Task ConfigureAsync(bool dunningEnabled, int grace, int maxReminders)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.PlatformBillingSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = PlatformBillingSettings.Empty();
            db.PlatformBillingSettings.Add(settings);
        }
        settings.SetCurrency("JOD");
        settings.SetIssuer("سوق (اختبار)", null, null);
        settings.SetTerms(30, grace);
        settings.SetDunning(dunningEnabled, 7, maxReminders);
        await db.SaveChangesAsync();
    }

    // فاتورةٌ صادرةٌ استحقّت قبل `daysOverdue` يوماً، لمتجرٍ خاصٍّ بهذا الاختبار.
    private async Task<(int InvoiceId, int TenantId)> OverdueInvoiceAsync(
        TestStore store, int daysOverdue, int remindersSent = 0)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var now = clock.GetUtcNow().UtcDateTime;
        var issued = now.AddDays(-(daysOverdue + 30));
        var due = now.AddDays(-daysOverdue);

        var invoice = new PlatformInvoice(
            store.Tenant.Id, "JOD", issued, issued.AddMonths(1));
        invoice.AddLine("اشتراك (مطالبة)", 1, new Money(100, "JOD"));
        invoice.Issue($"DUN{Guid.NewGuid():N}"[..16], issued, due, Money.Zero("JOD"), null,
            "سوق (اختبار)", null, null, store.Tenant.Name, null, null);

        for (var i = 0; i < remindersSent; i++) invoice.RecordReminder(due.AddDays(1));

        db.PlatformInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (invoice.Id, store.Tenant.Id);
    }

    private async Task SweepAsync()
    {
        // المنسّق نفسه يُستدعى لا يُحاكى: ما يُقاس هو ما يعمل في الإنتاج، لا نسخةٌ منه في اختبار.
        await using var scope = _factory.Services.CreateAsyncScope();
        var sweep = scope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<Microsoft.Extensions.Hosting.BackgroundService>()
            .Single(s => s.GetType().Name == "DunningService");
        await InvokeSweepAsync(sweep);
    }

    // `SweepAsync` خاصّة بالمنسّق: تُستدعى بالانعكاس كي لا يُفتح سطحُه العامّ لأجل اختبار.
    private static Task InvokeSweepAsync(object service) =>
        (Task)service.GetType()
            .GetMethod("SweepAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(service, [CancellationToken.None])!;

    private async Task<TenantStatus> StatusOfAsync(int tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync();
    }

    private async Task<PlatformInvoice> InvoiceAsync(int invoiceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PlatformInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
    }

    private async Task<int> OutboxCountAsync(int tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.CountAsync(m => m.TenantId == tenantId && m.Type == "InvoiceOverdueReminder");
    }

    private async Task RestoreAsync(int tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var store = await tenants.GetByIdAsync(tenantId);
        if (store is { Status: TenantStatus.Suspended }) store.Activate();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    // ========================================================================
    // **المطالبةُ معطّلةٌ ⇒ لا شيء يقع، مهما تأخّرت الفاتورة.**
    //
    // وهذا أهمُّ اختبارٍ سلبيّ في الملفّ: نشرُ هذه الشريحة على منصّةٍ قائمة يجب ألّا يُعلّق متجراً
    // واحداً يوم النشر.
    // ========================================================================
    [Fact]
    public async Task المطالبة_معطّلة_فلا_تعليق_ولا_تذكير()
    {
        var store = await _factory.CreateStoreAsync();
        await ConfigureAsync(dunningEnabled: false, grace: 1, maxReminders: 0);
        var (invoiceId, tenantId) = await OverdueInvoiceAsync(store, daysOverdue: 400);

        await SweepAsync();

        (await StatusOfAsync(tenantId)).Should().Be(TenantStatus.Active);
        (await InvoiceAsync(invoiceId)).RemindersSent.Should().Be(0);
        (await OutboxCountAsync(tenantId)).Should().Be(0);
    }

    // ========================================================================
    // التذكير: يُسجَّل على الفاتورة **وتُوضع رسالتُه في الصندوق بمتجرها** — والثاني هو ما لا يراه
    // اختبارُ وحدة. ومنسّقٌ يعمل بنطاق المنصّة يكتب رسالةً «بلا متجر» لو أُخذ المتجر من السياق.
    // ========================================================================
    [Fact]
    public async Task التذكير_يُسجَّل_ويُوضَع_في_الصندوق_بمتجره()
    {
        var store = await _factory.CreateStoreAsync();
        await ConfigureAsync(dunningEnabled: true, grace: 30, maxReminders: 3);
        var (invoiceId, tenantId) = await OverdueInvoiceAsync(store, daysOverdue: 1);

        try
        {
            await SweepAsync();

            (await InvoiceAsync(invoiceId)).RemindersSent.Should().Be(1);
            (await OutboxCountAsync(tenantId)).Should().Be(1);
            (await StatusOfAsync(tenantId)).Should().Be(TenantStatus.Active, "التذكير لا يُعلّق");

            // ودورةٌ ثانيةٌ فوراً لا تُذكّر مرّةً أخرى: الفاصلُ لم يمرّ.
            await SweepAsync();
            (await InvoiceAsync(invoiceId)).RemindersSent.Should().Be(1);
            (await OutboxCountAsync(tenantId)).Should().Be(1);
        }
        finally
        {
            await RestoreAsync(tenantId);
        }
    }

    // ========================================================================
    // **التصعيد**: مهلةٌ مستنفَدة وتذكيراتٌ مستنفَدة ⇒ المتجر يُعلَّق فعلاً، والفاتورةُ تُعلَّم.
    //
    // وهذا هو الفعلُ الذي وُجدت C6 لأجله، وهو الوحيد في المنتج الذي يُغلق متجرَ عميلٍ يدفع بلا
    // إنسانٍ في الحلقة.
    // ========================================================================
    [Fact]
    public async Task التصعيد_يُعلّق_المتجر_ويُعلّم_الفاتورة_ولا_يتكرّر()
    {
        var store = await _factory.CreateStoreAsync();
        await ConfigureAsync(dunningEnabled: true, grace: 7, maxReminders: 1);
        var (invoiceId, tenantId) = await OverdueInvoiceAsync(store, daysOverdue: 30, remindersSent: 1);

        try
        {
            await SweepAsync();

            (await StatusOfAsync(tenantId)).Should().Be(TenantStatus.Suspended);
            var escalated = await InvoiceAsync(invoiceId);
            escalated.EscalatedAtUtc.Should().NotBeNull();

            // **ولا يتكرّر**: دورةٌ ثانية لا تُعلّق ما هو معلَّق ولا تكتب علامةً ثانية.
            var firstEscalation = escalated.EscalatedAtUtc;
            await SweepAsync();
            (await InvoiceAsync(invoiceId)).EscalatedAtUtc.Should().Be(firstEscalation);
        }
        finally
        {
            await RestoreAsync(tenantId);
        }
    }

    // ========================================================================
    // **السدادُ يوقف السلّم فوراً.** فاتورةٌ سُدّدت بين دورتين لا تُذكّر ولا تُعلّق — والحالةُ
    // تصير `Settled` بالبناء، فلا تظهر في قراءة المتأخّرات أصلاً.
    // ========================================================================
    [Fact]
    public async Task السداد_يُخرج_الفاتورة_من_السلّم()
    {
        var store = await _factory.CreateStoreAsync();
        await ConfigureAsync(dunningEnabled: true, grace: 7, maxReminders: 0);
        var (invoiceId, tenantId) = await OverdueInvoiceAsync(store, daysOverdue: 30);

        try
        {
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var invoice = await db.PlatformInvoices
                    .Include(i => i.Lines).Include(i => i.Payments).SingleAsync(i => i.Id == invoiceId);
                var owner = await db.Users.IgnoreQueryFilters().Select(u => u.Id).FirstAsync();
                invoice.RecordPayment(new Money(100, "JOD"), PlatformPaymentMethod.BankTransfer,
                    DateTime.UtcNow, owner, "QA", null);
                await db.SaveChangesAsync();
            }

            await SweepAsync();

            (await StatusOfAsync(tenantId)).Should().Be(TenantStatus.Active);
            (await InvoiceAsync(invoiceId)).EscalatedAtUtc.Should().BeNull();
        }
        finally
        {
            await RestoreAsync(tenantId);
        }
    }
}
