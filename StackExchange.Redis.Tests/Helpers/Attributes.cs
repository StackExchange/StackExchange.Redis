using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests
{
    /// <summary>
    /// <para>Override for <see cref="Xunit.FactAttribute"/> that truncates our DisplayName down.</para>
    /// <para>
    /// Attribute that is applied to a method to indicate that it is a fact that should
    /// be run by the test runner. It can also be extended to support a customized definition
    /// of a test method.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.FactDiscoverer", "StackExchange.Redis.Tests")]
    public class FactAttribute : Xunit.FactAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class FactLongRunningAttribute : FactAttribute
    {
        public override string Skip
        {
            get => TestConfig.Current.RunLongRunning ? base.Skip : "Config.RunLongRunning is false - skipping long test.";
            set => base.Skip = value;
        }
    }

    /// <summary>
    /// <para>Override for <see cref="Xunit.TheoryAttribute"/> that truncates our DisplayName down.</para>
    /// <para>
    /// Marks a test method as being a data theory. Data theories are tests which are
    /// fed various bits of data from a data source, mapping to parameters on the test
    /// method. If the data source contains multiple rows, then the test method is executed
    /// multiple times (once with each data row). Data is provided by attributes which
    /// derive from Xunit.Sdk.DataAttribute (notably, Xunit.InlineDataAttribute and Xunit.MemberDataAttribute).
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.TheoryDiscoverer", "StackExchange.Redis.Tests")]
    public class TheoryAttribute : Xunit.TheoryAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TheoryLongRunningAttribute : Xunit.TheoryAttribute
    {
        public override string Skip
        {
            get => TestConfig.Current.RunLongRunning ? base.Skip : "Config.RunLongRunning is false - skipping long test.";
            set => base.Skip = value;
        }
    }

    public class FactDiscoverer : Xunit.Sdk.FactDiscoverer
    {
        public FactDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
            => new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
    }

    public class TheoryDiscoverer : Xunit.Sdk.TheoryDiscoverer
    {
        public TheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow)
            => new[] { new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkip(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, string skipReason)
            => new[] { new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
            => new[] { new SkippableTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod) };

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkippedDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow, string skipReason)
            => new[] { new NamedSkippedDataRowTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, skipReason, dataRow) };
    }

    public class SkippableTestCase : XunitTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SkippableTestCase() { }

        public SkippableTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[] testMethodArguments = null)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        {
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var skipMessageBus = new SkippableMessageBus(messageBus);
            var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ForAwait();
            return result.Update(skipMessageBus);
        }
    }

    public class SkippableTheoryTestCase : XunitTheoryTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public SkippableTheoryTestCase() { }

        public SkippableTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod) { }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            var skipMessageBus = new SkippableMessageBus(messageBus);
            var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ForAwait();
            return result.Update(skipMessageBus);
        }
    }

    public class NamedSkippedDataRowTestCase : XunitSkippedDataRowTestCase
    {
        protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
            base.GetDisplayName(factAttribute, displayName).StripName();

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public NamedSkippedDataRowTestCase() { }

        public NamedSkippedDataRowTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, string skipReason, object[] testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, skipReason, testMethodArguments) { }
    }

    public class SkippableMessageBus : IMessageBus
    {
        private readonly IMessageBus InnerBus;
        public SkippableMessageBus(IMessageBus innerBus) => InnerBus = innerBus;

        public int DynamicallySkippedTestCount { get; private set; }

        public void Dispose() { }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed testFailed)
            {
                var exceptionType = testFailed.ExceptionTypes.FirstOrDefault();
                if (exceptionType == typeof(SkipTestException).FullName)
                {
                    DynamicallySkippedTestCount++;
                    return InnerBus.QueueMessage(new TestSkipped(testFailed.Test, testFailed.Messages.FirstOrDefault()));
                }
            }
            return InnerBus.QueueMessage(message);
        }
    }

    internal static class XUnitExtensions
    {
        internal static string StripName(this string name) =>
            name.Replace("StackExchange.Redis.Tests.", "");

        public static RunSummary Update(this RunSummary summary, SkippableMessageBus bus)
        {
            if (bus.DynamicallySkippedTestCount > 0)
            {
                summary.Failed -= bus.DynamicallySkippedTestCount;
                summary.Skipped += bus.DynamicallySkippedTestCount;
            }
            return summary;
        }
    }
}
