using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests;

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
public class FactAttribute : Xunit.FactAttribute { }

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

    public override IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        if (testMethod.Method.GetParameters().Any())
        {
            return new[] { new ExecutionErrorTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, "[Fact] methods are not allowed to have parameters. Did you mean to use [Theory]?") };
        }
        else if (testMethod.Method.IsGenericMethodDefinition)
        {
            return new[] { new ExecutionErrorTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, "[Fact] methods are not allowed to be generic.") };
        }
        else
        {
            return testMethod.Expand(protocol => new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, protocol: protocol));
        }
    }
}

public class TheoryDiscoverer : Xunit.Sdk.TheoryDiscoverer
{
    public TheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow)
        => testMethod.Expand(protocol => new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow, protocol: protocol));

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkip(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, string skipReason)
        => testMethod.Expand(protocol => new SkippableTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, protocol: protocol));

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
        => testMethod.Expand(protocol => new SkippableTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, protocol: protocol));

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForSkippedDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow, string skipReason)
        => new[] { new NamedSkippedDataRowTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, skipReason, dataRow) };
}

public class SkippableTestCase : XunitTestCase, IRedisTest
{
    public RedisProtocol Protocol { get; set; }
    public string ProtocolString => Protocol switch
    {
        RedisProtocol.Resp2 => "RESP2",
        RedisProtocol.Resp3 => "RESP3",
        _ => "UnknownProtocolFixMeeeeee"
    };

    protected override string GetUniqueID() => base.GetUniqueID() + ProtocolString;

    protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
        base.GetDisplayName(factAttribute, displayName).StripName() + "(" + ProtocolString + ")";

    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public SkippableTestCase() { }

    public SkippableTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null, RedisProtocol? protocol = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
        // TODO: Default RESP2 somewhere cleaner
        Protocol = protocol ?? RedisProtocol.Resp2;
    }

    public override void Serialize(IXunitSerializationInfo data)
    {
        data.AddValue(nameof(Protocol), (int)Protocol);
        base.Serialize(data);
    }

    public override void Deserialize(IXunitSerializationInfo data)
    {
        Protocol = (RedisProtocol)data.GetValue<int>(nameof(Protocol));
        base.Deserialize(data);
    }

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var skipMessageBus = new SkippableMessageBus(messageBus);
        TestBase.SetContext(new TestContext(this));
        var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ForAwait();
        return result.Update(skipMessageBus);
    }
}

public class SkippableTheoryTestCase : XunitTheoryTestCase, IRedisTest
{
    public RedisProtocol Protocol { get; set; }

    protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
        base.GetDisplayName(factAttribute, displayName).StripName();

    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public SkippableTheoryTestCase() { }

    public SkippableTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, RedisProtocol? protocol = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
        // TODO: Default RESP2 somewhere cleaner
        Protocol = protocol ?? RedisProtocol.Resp2;
    }

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var skipMessageBus = new SkippableMessageBus(messageBus);
        TestBase.SetContext(new TestContext(this));
        var result = await base.RunAsync(diagnosticMessageSink, skipMessageBus, constructorArguments, aggregator, cancellationTokenSource).ForAwait();
        return result.Update(skipMessageBus);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RunPerProtocol : Attribute
{
    public static RedisProtocol[] AllProtocols { get; } = new[] { RedisProtocol.Resp2, RedisProtocol.Resp3 };

    public RedisProtocol[] Protocols { get; }
    public RunPerProtocol(params RedisProtocol[] procotols) => Protocols = procotols ?? AllProtocols;
}

public class NamedSkippedDataRowTestCase : XunitSkippedDataRowTestCase
{
    protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName) =>
        base.GetDisplayName(factAttribute, displayName).StripName();

    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public NamedSkippedDataRowTestCase() { }

    public NamedSkippedDataRowTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, string skipReason, object[]? testMethodArguments = null)
    : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, skipReason, testMethodArguments) { }
}

public class SkippableMessageBus : IMessageBus
{
    private readonly IMessageBus InnerBus;
    public SkippableMessageBus(IMessageBus innerBus) => InnerBus = innerBus;

    public int DynamicallySkippedTestCount { get; private set; }

    public void Dispose()
    {
        InnerBus.Dispose();
        GC.SuppressFinalize(this);
    }

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

    public static IEnumerable<IXunitTestCase> Expand(this ITestMethod testMethod, Func<RedisProtocol, IXunitTestCase> generator)
    {
        if ((testMethod.Method.GetCustomAttributes(typeof(RunPerProtocol)).FirstOrDefault()
                          ?? testMethod.TestClass.Class.GetCustomAttributes(typeof(RunPerProtocol)).FirstOrDefault()) is IAttributeInfo attr)
        {
            // params means not null but default empty
            var protocols = attr.GetNamedArgument<RedisProtocol[]>(nameof(RunPerProtocol.Protocols));
            if (protocols.Length == 0)
            {
                protocols = RunPerProtocol.AllProtocols;
            }
            var results = new List<IXunitTestCase>();
            foreach (var protocol in protocols)
            {
                results.Add(generator(protocol));
            }
            return results;
        }
        else
        {
            return new[] { generator(RedisProtocol.Resp2) };
        }
    }
}

/// <summary>
/// Supports changing culture for the duration of a single test.
/// <see cref="Thread.CurrentThread" /> and <see cref="CultureInfo.CurrentCulture" /> with another culture.
/// </summary>
/// <remarks>
/// Based on: https://bartwullems.blogspot.com/2022/03/xunit-change-culture-during-your-test.html
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class TestCultureAttribute : BeforeAfterTestAttribute
{
    private readonly CultureInfo culture;
    private CultureInfo? originalCulture;

    /// <summary>
    /// Replaces the culture and UI culture of the current thread with <paramref name="culture" />.
    /// </summary>
    /// <param name="culture">The name of the culture.</param>
    public TestCultureAttribute(string culture) => this.culture = new CultureInfo(culture, false);

    /// <summary>
    /// Stores the current <see cref="Thread.CurrentPrincipal" /> and <see cref="CultureInfo.CurrentCulture" />
    /// and replaces them with the new cultures defined in the constructor.
    /// </summary>
    /// <param name="methodUnderTest">The method under test</param>
    public override void Before(MethodInfo methodUnderTest)
    {
        originalCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = culture;
        CultureInfo.CurrentCulture.ClearCachedData();
    }

    /// <summary>
    /// Restores the original <see cref="CultureInfo.CurrentCulture" /> to <see cref="Thread.CurrentPrincipal" />.
    /// </summary>
    /// <param name="methodUnderTest">The method under test</param>
    public override void After(MethodInfo methodUnderTest)
    {
        if (originalCulture is not null)
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            CultureInfo.CurrentCulture.ClearCachedData();
        }
    }
}
