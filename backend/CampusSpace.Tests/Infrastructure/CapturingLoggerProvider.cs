using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>Keeps every rendered log message, for tests that check what is (and isn't) logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(CapturingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((category, formatter(state, exception)));
    }
}
