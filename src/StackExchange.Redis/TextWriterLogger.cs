using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal sealed class TextWriterLogger : ILogger
{
    private TextWriter? _writer;
    private readonly ILogger? _wrapped;

    internal static Action<string> NullWriter = _ => { };

    public TextWriterLogger(TextWriter writer, ILogger? wrapped)
    {
        _writer = writer;
        _wrapped = wrapped;
    }

    public IDisposable BeginScope<TState>(TState state) => NothingDisposable.Instance;
    public bool IsEnabled(LogLevel logLevel) => _writer is not null || _wrapped is not null;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _wrapped?.Log(logLevel, eventId, state, exception, formatter);
        if (_writer is TextWriter writer)
        {
            lock (writer)
            {
                // We check here again because it's possible we've released below, and never want to write past releasing.
                if (_writer is TextWriter innerWriter)
                {
                    innerWriter.Write($"{DateTime.UtcNow:HH:mm:ss.ffff}: ");
                    innerWriter.WriteLine(formatter(state, exception));
                }
            }
        }
    }

    public void Release()
    {
        // We lock here because we may have piled up on a lock above and still be writing.
        // We never want a write to go past the Release(), as many TextWriter implementations are not thread safe.
        if (_writer is TextWriter writer)
        {
            lock (writer)
            {
                _writer = null;
            }
        }
    }
}

internal static class TextWriterLoggerExtensions
{
    internal static ILogger? With(this ILogger? logger, TextWriter? writer) =>
        writer is not null ? new TextWriterLogger(writer, logger) : logger;
}

internal sealed class NothingDisposable : IDisposable
{
    public static readonly NothingDisposable Instance = new NothingDisposable();
    public void Dispose() { }
}
