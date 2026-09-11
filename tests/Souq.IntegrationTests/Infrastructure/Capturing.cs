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

// مزوّد سجلّ يجمع كل الرسائل المنسَّقة — لإثبات أن الأسرار لا تصل للسجل أبداً.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => sink.Enqueue($"{formatter(state, exception)} {exception}");
    }
}
