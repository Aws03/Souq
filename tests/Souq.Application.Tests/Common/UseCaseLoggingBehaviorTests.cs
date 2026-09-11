using AwesomeAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Behaviors;

namespace Souq.Application.Tests.Common;

// نطاق "حالة الاستخدام" وتحذير البطء — بساعة متدرّجة (لا انتظار حقيقي) وسجلّ في الذاكرة.
public class UseCaseLoggingBehaviorTests
{
    private sealed record SampleCommand(string Password) : IRequest<string>;

    [Fact]
    public async Task حالة_استخدام_بطيئة_تُحذّر_باسمها_ومدّتها_ولا_تسجّل_محتوى_الطلب()
    {
        var logs = new ListLoggerFactory();
        var behavior = new UseCaseLoggingBehavior<SampleCommand, string>(logs, new SteppingClock(0, 700));

        await behavior.Handle(new SampleCommand("p@ss-SECRET"), () => Task.FromResult("ok"), CancellationToken.None);

        var warning = logs.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain("SampleCommand").And.Contain("700").And.NotContain("p@ss-SECRET");
    }

    [Fact]
    public async Task حالة_استخدام_سريعة_لا_تُسجّل_شيئاً()
    {
        var logs = new ListLoggerFactory();
        var behavior = new UseCaseLoggingBehavior<SampleCommand, string>(logs, new SteppingClock(0, 100));

        await behavior.Handle(new SampleCommand("x"), () => Task.FromResult("ok"), CancellationToken.None);

        logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task السجلات_داخل_حالة_الاستخدام_تحمل_اسمها_في_النطاق()
    {
        var logs = new ListLoggerFactory();
        var behavior = new UseCaseLoggingBehavior<SampleCommand, string>(logs, new SteppingClock(0, 1));
        var inner = logs.CreateLogger("handler");

        await behavior.Handle(new SampleCommand("x"), () =>
        {
            inner.LogInformation("inside the handler");
            return Task.FromResult("ok");
        }, CancellationToken.None);

        logs.Entries.Should().ContainSingle().Which.Scope["UseCase"].Should().Be(nameof(SampleCommand));
    }

    // ساعة تعيد طوابع زمنية محدّدة بالميلي ثانية: البداية ثم النهاية.
    private sealed class SteppingClock(params long[] timestampsMs) : TimeProvider
    {
        private int _next;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestampsMs[Math.Min(_next++, timestampsMs.Length - 1)];
    }

    private sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Scope);

    private sealed class ListLoggerFactory : ILoggerFactory
    {
        private readonly LoggerExternalScopeProvider _scopes = new();
        public List<Entry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(this);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class ListLogger(ListLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scope = new Dictionary<string, object?>();
                owner._scopes.ForEachScope((current, collected) =>
                {
                    if (current is IEnumerable<KeyValuePair<string, object?>> values)
                        foreach (var (key, value) in values) collected[key] = value;
                }, scope);
                owner.Entries.Add(new Entry(logLevel, formatter(state, exception), scope));
            }
        }
    }
}
