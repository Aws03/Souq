using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Interfaces;

namespace Souq.IntegrationTests.Infrastructure;

// بديل IEmailService يحفظ الرسائل في الذاكرة بدل إرسالها.
public sealed class CapturingEmailService : IEmailService
{
    private readonly ConcurrentQueue<(string To, string Token)> _resets = new();

    public Task SendOrderConfirmationAsync(string toEmail, int orderId, CancellationToken ct = default) => Task.CompletedTask;

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct = default)
    {
        _resets.Enqueue((toEmail, resetToken));
        return Task.CompletedTask;
    }

    public string LastResetTokenFor(string email) =>
        _resets.Reverse().First(r => r.To == email).Token;
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
