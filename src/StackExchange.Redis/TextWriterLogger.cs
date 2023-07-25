using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal sealed class TextWriterLogger : ILogger, IDisposable
{
    private TextWriter? _writer;
    private ILogger? _wrapped;

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
                writer.Write($"{DateTime.UtcNow:HH:mm:ss.ffff}: ");
                writer.WriteLine(formatter(state, exception));
            }
        }
    }

    public void Dispose()
    {
        _writer = null;
        _wrapped = null;
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
