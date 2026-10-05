using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Forwards a few chosen log events - diagnostics that explain a failure nothing else does - to the test's
/// output, and nothing else.
/// </summary>
/// <remarks>
/// Attached to every non-shared test multiplexer, so a rare failure on CI arrives with its explanation
/// rather than needing a reproduction. Kept to an allow-list because all-events logging made the CI summary
/// too large to upload.
/// </remarks>
internal sealed class DiagnosticEventLogger(TextWriter output, params int[] eventIds) : ILoggerFactory, ILogger
{
    public void AddProvider(ILoggerProvider provider) { }

    public ILogger CreateLogger(string categoryName) => this;

    public void Dispose() { }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Array.IndexOf(eventIds, eventId.Id) < 0) return;
        try
        {
            output.WriteLine($"[diagnostic {eventId.Id}] {formatter(state, exception)}");
        }
        catch
        {
            // the test may have finished, and its output with it
        }
    }
}
