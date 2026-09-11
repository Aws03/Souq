using Souq.Application.Common.Notifications;

namespace Souq.Infrastructure.Persistence.Outbox;

// ============================================================================
// صفّ في صندوق الصادر (المرحلة 14، ADR-0034) — كتلة بناء تقنية لا كيان مجال: يُكتب في وحدة عمل التغيير نفسها ويعالجه المُرسِل
// الخلفي. TenantId اختياري بلا مرشّح مستأجر: المُرسِل يمرّ على كل المتاجر ويشغّل كل رسالة داخل نطاق متجرها (أو المنصّة). الحمولة
// مراجع صغيرة بلا أسرار ولا بيانات شخصية. التحديثات (حجز، إنجاز، فشل) جماعية مشروطة في OutboxProcessor — آمنة بين نسختين.
// ============================================================================
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 100;
    public const int PayloadMaxLength = 4000;
    public const int ErrorMaxLength = 500;

    public long Id { get; private set; }
    public int? TenantId { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage For(object message, int? tenantId, DateTime now)
    {
        var payload = NotificationMessageTypes.Serialize(message);
        if (payload.Length > PayloadMaxLength)
            throw new InvalidOperationException($"حمولة رسالة الصادر {message.GetType().Name} أكبر من {PayloadMaxLength} حرف");
        return new OutboxMessage
        {
            TenantId = tenantId,
            Type = NotificationMessageTypes.NameOf(message.GetType()),
            Payload = payload,
            OccurredAt = now,
            NextAttemptAt = now,
        };
    }
}
