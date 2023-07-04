using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal sealed class TextWriterLogger : ILogger
{
    private readonly TextWriter _writer;
    private readonly ILogger? _wrapped;

    internal static Action<string> NullWriter = _ => { };

    public TextWriterLogger(TextWriter writer, ILogger? wrapped)
    {
        _writer = writer;
        _wrapped = wrapped;
    }

    public IDisposable BeginScope<TState>(TState state) => new NothingDisposable();
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _wrapped?.Log(logLevel, eventId, state, exception, formatter);
        lock (_writer)
        {
            _writer.Write($"{DateTime.UtcNow:HH:mm:ss.ffff}: ");
            _writer.WriteLine(formatter(state, exception));
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
    public void Dispose() { }
}
