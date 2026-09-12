using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Notifications;
using Souq.Application.Common.Tenancy;
using Souq.Infrastructure.Services;
using Souq.Infrastructure.Tenancy;

namespace Souq.Infrastructure.Persistence.Outbox;

// دورة معالجة واحدة لما حان وقته — يستدعيها المُرسِل الخلفي كل بضع ثوانٍ، والاختبارات مباشرة (المُرسِل معطّل فيها).
public interface IOutboxProcessor
{
    Task<int> ProcessDueAsync(CancellationToken ct);

    Task<int> PurgeProcessedAsync(DateTime before, CancellationToken ct);
}

// ============================================================================
// OutboxProcessor (المرحلة 14، ADR-0034). لكل رسالة حان وقتها:
//   1. حجز بعقد إيجار (LockedUntil) بتحديث مشروط — نسختان من الخادم لا تعالجان الرسالة نفسها معاً، وعقد منتهٍ (عملية ماتت)
//      يُستعاد بعد دقيقتين.
//   2. معالجتها داخل نطاق متجرها (أو المنصّة) في نطاق خدمات جديد — خارج أي معاملة؛ المعالج يحفظ ما يلزمه ثم يتصل بالمزوّد.
//   3. نجاح ⇒ منجزة. فشل ⇒ المحاولة التالية بتباعد متصاعد (OutboxRetryPolicy)، وبعد آخرها "ميتة" تبقى للتشخيص. نوع مجهول ⇒
//      ميتة فوراً. السجلّ: معرّف الرسالة ونوعها ونوع الخطأ فقط — لا مستلم ولا رابط ولا رمز.
// التسليم "مرّة على الأقل": موت العملية بين الإرسال وتعليم الإنجاز يعيد الرسالة بعد انتهاء العقد.
// ============================================================================
internal sealed class OutboxProcessor : IOutboxProcessor
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly AppDbContext _db;
    private readonly ITenantDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IServiceProvider services, AppDbContext db, ITenantDirectory directory, TimeProvider clock, ILogger<OutboxProcessor> logger)
    {
        _services = services; _db = db; _directory = directory; _clock = clock; _logger = logger;
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var handled = 0;
        while (true)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var due = await _db.OutboxMessages.AsNoTracking()
                .Where(m => m.ProcessedAt == null && m.FailedAt == null && m.NextAttemptAt <= now
                            && (m.LockedUntil == null || m.LockedUntil < now))
                .OrderBy(m => m.Id)
                .Take(BatchSize)
                .Select(m => new DueMessage(m.Id, m.TenantId, m.Type, m.Payload, m.Attempts))
                .ToListAsync(ct);

            foreach (var message in due)
            {
                if (!await ClaimAsync(message.Id, now, ct)) continue;
                await ProcessAsync(message, ct);
                handled++;
            }

            if (due.Count < BatchSize) return handled;
        }
    }

    public Task<int> PurgeProcessedAsync(DateTime before, CancellationToken ct) =>
        _db.OutboxMessages.Where(m => m.ProcessedAt != null && m.ProcessedAt < before).ExecuteDeleteAsync(ct);

    private async Task<bool> ClaimAsync(long id, DateTime now, CancellationToken ct) =>
        await _db.OutboxMessages
            .Where(m => m.Id == id && m.ProcessedAt == null && m.FailedAt == null && (m.LockedUntil == null || m.LockedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedUntil, now + Lease), ct) == 1;

    private async Task ProcessAsync(DueMessage message, CancellationToken ct)
    {
        try
        {
            var payload = NotificationMessageTypes.Deserialize(message.Type, message.Payload)
                ?? throw new UnknownOutboxMessageException(message.Type);

            if (message.TenantId is int storeId)
            {
                var store = await _directory.FindByIdAsync(storeId, ct)
                    ?? throw new InvalidOperationException($"متجر رسالة الصادر {storeId} غير موجود");
                await TenantScopes.RunAsync(_services, store, scoped => NotificationMessageDispatch.DispatchAsync(scoped, payload, ct));
            }
            else
            {
                await TenantScopes.RunPlatformAsync(_services, scoped => NotificationMessageDispatch.DispatchAsync(scoped, payload, ct));
            }

            // CancellationToken.None عمداً كما في تسجيل الفشل: الرسالة غادرت النظام فعلاً (بريد أُرسل)، وتسجيل ذلك
            // عملٌ محاسبي لا يُلغى. بـ ct كانت إشارة الإيقاف بين الإرسال والتسجيل تُلغي الكتابة، فيبقى الصفّ غير
            // مُعالَج ومحجوزاً حتى تنتهي المهلة ثم يُرسَل ثانيةً — رسالة مكرّرة لكل رسالة طائرة عند كل نشر.
            var done = _clock.GetUtcNow().UtcDateTime;
            await _db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, done)
                .SetProperty(m => m.LockedUntil, (DateTime?)null)
                .SetProperty(m => m.LastError, (string?)null), CancellationToken.None);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            await RecordFailureAsync(message, ex);
        }
    }

    private async Task RecordFailureAsync(DueMessage message, Exception ex)
    {
        var failedAttempts = message.Attempts + 1;
        var now = _clock.GetUtcNow().UtcDateTime;
        var next = ex is UnknownOutboxMessageException ? null : OutboxRetryPolicy.NextAttemptAt(failedAttempts, now);
        DateTime? failedAt = next is null ? now : null;
        var error = LogRedaction.Truncate($"{ex.GetType().Name}: {ex.Message}", OutboxMessage.ErrorMaxLength);

        await _db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Attempts, failedAttempts)
            .SetProperty(m => m.NextAttemptAt, next ?? now)
            .SetProperty(m => m.FailedAt, failedAt)
            .SetProperty(m => m.LockedUntil, (DateTime?)null)
            .SetProperty(m => m.LastError, error), CancellationToken.None);

        if (next is null)
            _logger.LogError("Outbox message {MessageId} ({MessageType}) failed permanently after {Attempts} attempts: {ErrorType}",
                message.Id, message.Type, failedAttempts, ex.GetType().Name);
        else
            _logger.LogWarning("Outbox message {MessageId} ({MessageType}) failed on attempt {Attempts} ({ErrorType}); next attempt at {NextAttemptAt:o}",
                message.Id, message.Type, failedAttempts, ex.GetType().Name, next);
    }

    private sealed record DueMessage(long Id, int? TenantId, string Type, string Payload, int Attempts);

    private sealed class UnknownOutboxMessageException(string type) : Exception($"نوع رسالة صادر غير مسجّل: {type}");
}
