using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Tests
{
    public static class Skip
    {
        public static void Inconclusive(string message) => throw new SkipTestException(message);
        public static void NotSupported(string feature) => throw new SkipTestException(feature + " is not supported on this server");
    }

    [XunitTestCaseDiscoverer("Dapper.Tests.SkippableFactDiscoverer", "Dapper.Tests.Contrib")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class SkippableFactAttribute : FactAttribute
    {
    }

    public class SkipTestException : Exception
    {
        public SkipTestException(string reason) : base(reason)
        {
        }
    }

    // Most of the below is a direct copy & port from the wonderful examples by Brad Wilson at
    // https://github.com/xunit/samples.xunit/tree/master/DynamicSkipExample
    public class SkippableFactDiscoverer : IXunitTestCaseDiscoverer
    {
        private readonly IMessageSink _diagnosticMessageSink;

        public SkippableFactDiscoverer(IMessageSink diagnosticMessageSink)
        {
            _diagnosticMessageSink = diagnosticMessageSink;
        }

        public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
        {
            yield return new SkippableFactTestCase(_diagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), testMethod);
        }
    }

    public class SkippableFactTestCase : XunitTestCase
    {
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SkippableFactTestCase()
        {
        }

        public SkippableFactTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, ITestMethod testMethod, object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, testMethod, testMethodArguments)
        {
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var skipMessageBus = new SkippableFactMessageBus(messageBus);
            var result = await base.RunAsync(
                diagnosticMessageSink,
                skipMessageBus,
                constructorArguments,
                aggregator,
                cancellationTokenSource).ConfigureAwait(false);
            if (skipMessageBus.DynamicallySkippedTestCount > 0)
            {
                result.Failed -= skipMessageBus.DynamicallySkippedTestCount;
                result.Skipped += skipMessageBus.DynamicallySkippedTestCount;
            }

            return result;
        }
    }

    public class SkippableFactMessageBus : IMessageBus
    {
        private readonly IMessageBus _innerBus;
        public SkippableFactMessageBus(IMessageBus innerBus)
        {
            _innerBus = innerBus;
        }

        public int DynamicallySkippedTestCount { get; private set; }

        public void Dispose()
        {
        }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed testFailed)
            {
                var exceptionType = testFailed.ExceptionTypes.FirstOrDefault();
                if (exceptionType == typeof(SkipTestException).FullName)
                {
                    DynamicallySkippedTestCount++;
                    return _innerBus.QueueMessage(new TestSkipped(testFailed.Test, testFailed.Messages.FirstOrDefault()));
                }
            }
            return _innerBus.QueueMessage(message);
        }
    }
}
