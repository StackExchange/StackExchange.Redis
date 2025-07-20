using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1502 // Element should not be on a single line
#pragma warning disable SA1649 // File name should match first type name
#pragma warning disable IDE0130 // Namespace does not match folder structure
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
[XunitTestCaseDiscoverer(typeof(FactDiscoverer))]
public class FactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1) : Xunit.FactAttribute(sourceFilePath, sourceLineNumber) { }

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
[XunitTestCaseDiscoverer(typeof(TheoryDiscoverer))]
public class TheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1) : Xunit.TheoryAttribute(sourceFilePath, sourceLineNumber) { }

public class FactDiscoverer : Xunit.v3.FactDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute)
        => base.Discover(discoveryOptions, testMethod, factAttribute).ExpandAsync();
}

public class TheoryDiscoverer : Xunit.v3.TheoryDiscoverer
{
    protected override ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, ITheoryAttribute theoryAttribute, ITheoryDataRow dataRow, object?[] testMethodArguments)
        => base.CreateTestCasesForDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow, testMethodArguments).ExpandAsync();

    protected override ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, ITheoryAttribute theoryAttribute)
        => base.CreateTestCasesForTheory(discoveryOptions, testMethod, theoryAttribute).ExpandAsync();
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RunPerProtocol() : Attribute { }

public interface IProtocolTestCase
{
    RedisProtocol Protocol { get; }
}

public class ProtocolTestCase : XunitTestCase, IProtocolTestCase
{
    public RedisProtocol Protocol { get; private set; }

    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ProtocolTestCase() { }

    public ProtocolTestCase(XunitTestCase testCase, RedisProtocol protocol) : base(
        testMethod: testCase.TestMethod,
        testCaseDisplayName: $"{testCase.TestCaseDisplayName.Replace("StackExchange.Redis.Tests.", "")} ({protocol.GetString()})",
        uniqueID: testCase.UniqueID + protocol.GetString(),
        @explicit: testCase.Explicit,
        skipExceptions: testCase.SkipExceptions,
        skipReason: testCase.SkipReason,
        skipType: testCase.SkipType,
        skipUnless: testCase.SkipUnless,
        skipWhen: testCase.SkipWhen,
        traits: testCase.TestMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
        testMethodArguments: testCase.TestMethodArguments,
        sourceFilePath: testCase.SourceFilePath,
        sourceLineNumber: testCase.SourceLineNumber,
        timeout: testCase.Timeout)
    => Protocol = protocol;

    protected override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue("resp", (int)Protocol);
    }

    protected override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        Protocol = (RedisProtocol)data.GetValue<int>("resp");
    }
}

public class ProtocolDelayEnumeratedTestCase : XunitDelayEnumeratedTheoryTestCase, IProtocolTestCase
{
    public RedisProtocol Protocol { get; private set; }

    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ProtocolDelayEnumeratedTestCase() { }

    public ProtocolDelayEnumeratedTestCase(XunitDelayEnumeratedTheoryTestCase testCase, RedisProtocol protocol) : base(
        testMethod: testCase.TestMethod,
        testCaseDisplayName: $"{testCase.TestCaseDisplayName.Replace("StackExchange.Redis.Tests.", "")} ({protocol.GetString()})",
        uniqueID: testCase.UniqueID + protocol.GetString(),
        @explicit: testCase.Explicit,
        skipTestWithoutData: testCase.SkipTestWithoutData,
        skipExceptions: testCase.SkipExceptions,
        skipReason: testCase.SkipReason,
        skipType: testCase.SkipType,
        skipUnless: testCase.SkipUnless,
        skipWhen: testCase.SkipWhen,
        traits: testCase.TestMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
        sourceFilePath: testCase.SourceFilePath,
        sourceLineNumber: testCase.SourceLineNumber,
        timeout: testCase.Timeout)
    => Protocol = protocol;

    protected override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue("resp", (int)Protocol);
    }

    protected override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        Protocol = (RedisProtocol)data.GetValue<int>("resp");
    }
}

internal static class XUnitExtensions
{
    public static async ValueTask<IReadOnlyCollection<IXunitTestCase>> ExpandAsync(this ValueTask<IReadOnlyCollection<IXunitTestCase>> discovery)
    {
        static IXunitTestCase CreateTestCase(XunitTestCase tc, RedisProtocol protocol) => tc switch
        {
            XunitDelayEnumeratedTheoryTestCase delayed => new ProtocolDelayEnumeratedTestCase(delayed, protocol),
            _ => new ProtocolTestCase(tc, protocol),
        };
        var testCases = await discovery;
        List<IXunitTestCase> result = [];
        foreach (var testCase in testCases.OfType<XunitTestCase>())
        {
            var testMethod = testCase.TestMethod;

            if ((testMethod.Method.GetCustomAttributes(typeof(RunPerProtocol)).FirstOrDefault()
                 ?? testMethod.TestClass.Class.GetCustomAttributes(typeof(RunPerProtocol)).FirstOrDefault()) is RunPerProtocol)
            {
                result.Add(CreateTestCase(testCase, RedisProtocol.Resp2));
                result.Add(CreateTestCase(testCase, RedisProtocol.Resp3));
            }
            else
            {
                // Default to RESP2 everywhere else
                result.Add(CreateTestCase(testCase, RedisProtocol.Resp2));
            }
        }
        return result;
    }
}

/// <summary>
/// Supports changing culture for the duration of a single test.
/// <see cref="Thread.CurrentThread" /> and <see cref="CultureInfo.CurrentCulture" /> with another culture.
/// </summary>
/// <remarks>
/// Based on: https://bartwullems.blogspot.com/2022/03/xunit-change-culture-during-your-test.html.
/// Replaces the culture and UI culture of the current thread with <paramref name="culture" />.
/// </remarks>
/// <param name="culture">The name of the culture.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class TestCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    private readonly CultureInfo culture = new CultureInfo(culture, false);
    private CultureInfo? originalCulture;

    /// <summary>
    /// Stores the current <see cref="Thread.CurrentPrincipal" /> and <see cref="CultureInfo.CurrentCulture" />
    /// and replaces them with the new cultures defined in the constructor.
    /// </summary>
    /// <param name="methodUnderTest">The method under test.</param>
    /// <param name="test">The current <see cref="ITest"/>.</param>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        originalCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = culture;
        CultureInfo.CurrentCulture.ClearCachedData();
    }

    /// <summary>
    /// Restores the original <see cref="CultureInfo.CurrentCulture" /> to <see cref="Thread.CurrentPrincipal" />.
    /// </summary>
    /// <param name="methodUnderTest">The method under test.</param>
    /// <param name="test">The current <see cref="ITest"/>.</param>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (originalCulture is not null)
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            CultureInfo.CurrentCulture.ClearCachedData();
        }
    }
}
