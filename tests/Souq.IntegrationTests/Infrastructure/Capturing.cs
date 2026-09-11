using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Notifications;

namespace Souq.IntegrationTests.Infrastructure;

// ============================================================================
// بديل IEmailSender يحفظ الرسائل في الذاكرة بدل إرسالها — الرابط كما يصل للمستخدم تماماً (بمضيفه). المرحلة 14: الرسائل تُرسل
// من صندوق الصادر لا من الطلب، فالاختبار يشغّل دورة الإرسال (SouqApiFactory.DispatchNotificationsAsync) قبل قراءتها. ويمكن
// جعله يفشل (إعادة المحاولة) أو يتعطّل (إثبات أن الطلب لا ينتظر مزوّد البريد).
// ============================================================================
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private int _failuresLeft;
    private volatile TaskCompletionSource? _gate;

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (_gate is { } gate) await gate.Task.WaitAsync(ct);
        if (Interlocked.Decrement(ref _failuresLeft) >= 0)
            throw new EmailDeliveryException("فشل مزوّد مصطنع للاختبار");
        _sent.Enqueue(message);
    }

    // المحاولات القادمة تفشل بعدد times ثم تنجح.
    public void FailNext(int times) => Volatile.Write(ref _failuresLeft, times);

    // مزوّد معطّل: كل إرسال ينتظر حتى Release.
    public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release()
    {
        _gate?.TrySetResult();
        _gate = null;
    }

    public EmailMessage? LastTo(string email, EmailTemplate template) =>
        _sent.Reverse().FirstOrDefault(m => m.Kind == template.ToString() && m.To == email);

    public string LastInvitationLinkFor(string email) => Last(EmailTemplate.Invitation, email);

    public string LastInvitationTokenFor(string email) => TokenOf(Last(EmailTemplate.Invitation, email));

    public string LastResetLinkFor(string email) => Last(EmailTemplate.PasswordReset, email);

    public string LastResetTokenFor(string email) => TokenOf(Last(EmailTemplate.PasswordReset, email));

    public string LastVerificationTokenFor(string email) => TokenOf(Last(EmailTemplate.EmailVerification, email));

    private string Last(EmailTemplate template, string email) =>
        (LastTo(email, template) ?? throw new InvalidOperationException($"لم تُرسل رسالة {template} إلى {email}")).ActionUrl!;

    private static string TokenOf(string link) =>
        Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
}

// سجلّ واحد كما يراه أي مزوّد منظَّم: الرسالة المنسَّقة + الخصائص المسمّاة + قيم النطاق.
public sealed record CapturedLog(
    string Category, LogLevel Level, string Message,
    IReadOnlyDictionary<string, object?> Properties, IReadOnlyDictionary<string, object?> Scope);

// ============================================================================
// مزوّد سجلّ يجمع كل ما يُسجَّل — لإثبات أن الأسرار لا تصل للسجل أبداً، وأن السياق المنظَّم
// (CorrelationId, UserId, UseCase) يصل فعلاً. ISupportExternalScope: يرى النطاقات التي يفتحها
// الوسطاء والسلوكيات كما يراها مزوّد JSON في الإنتاج.
// ============================================================================
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLog> Entries => _entries.ToArray();

    // الرسالة المنسَّقة (+ الاستثناء إن وُجد) — للبحث النصّي عن أي تسريب.
    public IReadOnlyCollection<string> Messages => _entries.Select(e => e.Message).ToArray();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var (key, value) in pairs.Where(p => p.Key != "{OriginalFormat}"))
                    properties[key] = value;

            var scope = new Dictionary<string, object?>();
            owner._scopes.ForEachScope((current, collected) =>
            {
                if (current is IEnumerable<KeyValuePair<string, object?>> values)
                    foreach (var (key, value) in values) collected[key] = value;
            }, scope);

            var message = exception is null ? formatter(state, null) : $"{formatter(state, exception)} {exception}";
            owner._entries.Enqueue(new CapturedLog(category, logLevel, message, properties, scope));
        }
    }
}
