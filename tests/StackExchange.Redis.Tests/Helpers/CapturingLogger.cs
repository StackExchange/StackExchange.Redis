using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis.Tests;

/// <summary>
/// An <see cref="ILoggerFactory"/> that records every formatted message, so a test can assert on what
/// was (or was not) logged.
/// </summary>
internal sealed class CapturingLogger : ILoggerFactory, ILogger
{
    private readonly List<string> _messages = [];

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider) { }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages) _messages.Add(formatter(state, exception));
    }

    public List<string> Matching(string fragment)
    {
        lock (_messages) return _messages.FindAll(x => x.Contains(fragment, StringComparison.Ordinal));
    }

    public string All
    {
        get { lock (_messages) return string.Join("\n", _messages); }
    }

    public void Dispose() { }
}
