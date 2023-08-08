using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)]
public class LoggerTests : TestBase
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;
    public LoggerTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task BasicLoggerConfig()
    {
        var traceLogger = new TestLogger(LogLevel.Trace, Writer);
        var debugLogger = new TestLogger(LogLevel.Debug, Writer);
        var infoLogger = new TestLogger(LogLevel.Information, Writer);
        var warningLogger = new TestLogger(LogLevel.Warning, Writer);
        var errorLogger = new TestLogger(LogLevel.Error, Writer);
        var criticalLogger = new TestLogger(LogLevel.Critical, Writer);

        var options = ConfigurationOptions.Parse(GetConfiguration());
        options.LoggerFactory = new TestWrapperLoggerFactory(new TestMultiLogger(traceLogger, debugLogger, infoLogger, warningLogger, errorLogger, criticalLogger));

        using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        // We expect more at the trace level: GET, ECHO, PING on commands
        Assert.True(traceLogger.CallCount > debugLogger.CallCount);
        // Many calls for all log lines - don't set exact here since every addition would break the test
        Assert.True(debugLogger.CallCount > 30);
        Assert.True(infoLogger.CallCount > 30);
        // No debug calls at this time
        // We expect no error/critical level calls to have happened here
        Assert.Equal(0, errorLogger.CallCount);
        Assert.Equal(0, criticalLogger.CallCount);
    }

    [Fact]
    public async Task WrappedLogger()
    {
        var options = ConfigurationOptions.Parse(GetConfiguration());
        var wrapped = new TestWrapperLoggerFactory(NullLogger.Instance);
        options.LoggerFactory = wrapped;

        using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        Assert.True(wrapped.Logger.LogCount > 0);
    }

    public class TestWrapperLoggerFactory : ILoggerFactory
    {
        public TestWrapperLogger Logger { get; }
        public TestWrapperLoggerFactory(ILogger logger) => Logger = new TestWrapperLogger(logger);

        public void AddProvider(ILoggerProvider provider) => throw new NotImplementedException();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void Dispose() { }
    }

    public class TestWrapperLogger : ILogger
    {
        public int LogCount = 0;
        public ILogger _inner { get; }

        public TestWrapperLogger(ILogger toWrap) => _inner = toWrap;

        public IDisposable BeginScope<TState>(TState state) => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Interlocked.Increment(ref LogCount);
            _inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    /// <summary>
    /// To save on test time, no reason to spin up n connections just to test n logging implementations...
    /// </summary>
    private class TestMultiLogger : ILogger
    {
        private readonly ILogger[] _loggers;
        public TestMultiLogger(params ILogger[] loggers) => _loggers = loggers;

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (var logger in _loggers)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    private class TestLogger : ILogger
    {
        private readonly StringBuilder sb = new StringBuilder();
        private long _callCount;
        private readonly LogLevel _logLevel;
        private readonly TextWriter _output;
        public TestLogger(LogLevel logLevel, TextWriter output) =>
            (_logLevel, _output) = (logLevel, output);

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _logLevel;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Interlocked.Increment(ref _callCount);
            var logLine = $"{_logLevel}> [LogLevel: {logLevel}, EventId: {eventId}]: {formatter?.Invoke(state, exception)}";
            sb.AppendLine(logLine);
            _output.WriteLine(logLine);
        }

        public long CallCount => Interlocked.Read(ref _callCount);
        public override string ToString() => sb.ToString();
    }
}
