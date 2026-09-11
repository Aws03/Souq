namespace Souq.Application.Tests.TestDoubles;

// ساعة ثابتة قابلة للتقديم يدوياً — بديل TimeProvider في الاختبارات (لا انتظار ولا Reflection).
public sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now;

    public FixedClock(DateTimeOffset? now = null) => _now = now ?? DefaultNow;

    public DateTime UtcNow => _now.UtcDateTime;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
