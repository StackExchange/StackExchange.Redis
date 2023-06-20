using GitHubActionsTestLogger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        TestLoggerProvider traceLoggerProvider = new TestLoggerProvider(LogLevel.Trace, Writer),
                           debugLoggerProvider = new TestLoggerProvider(LogLevel.Debug, Writer),
                           infoLoggerProvider = new TestLoggerProvider(LogLevel.Information, Writer),
                           warningLoggerProvider = new TestLoggerProvider(LogLevel.Warning, Writer),
                           errorLoggerProvider = new TestLoggerProvider(LogLevel.Error, Writer),
                           criticalLoggerProvider = new TestLoggerProvider(LogLevel.Critical, Writer);

        TestLogger traceLogger = (TestLogger)traceLoggerProvider.CreateLogger(""),
            debugLogger = (TestLogger)debugLoggerProvider.CreateLogger(""),
            infoLogger = (TestLogger)infoLoggerProvider.CreateLogger(""),
            warningLogger = (TestLogger)warningLoggerProvider.CreateLogger(""),
            errorLogger = (TestLogger)errorLoggerProvider.CreateLogger(""),
            criticalLogger = (TestLogger)criticalLoggerProvider.CreateLogger("");

        var options = ConfigurationOptions.Parse(GetConfiguration());

        options.LoggerFactory = new LoggerFactory(new[] { traceLoggerProvider, debugLoggerProvider, infoLoggerProvider, warningLoggerProvider, errorLoggerProvider, criticalLoggerProvider });

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

    private class TestLoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public TestLoggerProvider(LogLevel logLevel, TextWriter output) => _logger = new TestLogger(logLevel, output);

        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
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
