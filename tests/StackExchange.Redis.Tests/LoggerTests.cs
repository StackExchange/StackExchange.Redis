using GitHubActionsTestLogger;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.CodeDom;
using System.Collections.Generic;
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
        options.Logger = new TestMultiLogger(traceLogger, debugLogger, infoLogger, warningLogger, errorLogger, criticalLogger);

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
