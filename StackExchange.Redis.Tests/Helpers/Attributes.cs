using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests
{
    /// <summary>
    /// Override for <see cref="Xunit.FactAttribute"/> that truncates our DisplayName down.
    /// 
    /// Attribute that is applied to a method to indicate that it is a fact that should
    /// be run by the test runner. It can also be extended to support a customized definition
    /// of a test method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.FactDiscoverer", "StackExchange.Redis.Tests")]
    public class FactAttribute : Xunit.FactAttribute
    {
    }

    /// <summary>
    /// Override for <see cref="Xunit.TheoryAttribute"/> that truncates our DisplayName down.
    /// 
    /// Marks a test method as being a data theory. Data theories are tests which are
    /// fed various bits of data from a data source, mapping to parameters on the test
    /// method. If the data source contains multiple rows, then the test method is executed
    /// multiple times (once with each data row). Data is provided by attributes which
    /// derive from Xunit.Sdk.DataAttribute (notably, Xunit.InlineDataAttribute and Xunit.MemberDataAttribute).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("StackExchange.Redis.Tests.TheoryDiscoverer", "StackExchange.Redis.Tests")]
    public class TheoryAttribute : Xunit.TheoryAttribute
    {
    }

    public class FactDiscoverer : Xunit.Sdk.FactDiscoverer
    {
        public FactDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        public override IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
        {
            foreach (var test in base.Discover(discoveryOptions, testMethod, factAttribute))
            {
                yield return test.SetName(dn => dn?.Replace("StackExchange.Redis.Tests.", ""));
            }
        }
    }

    public class TheoryDiscoverer : Xunit.Sdk.TheoryDiscoverer
    {
        public TheoryDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        public override IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
        {
            foreach (var test in base.Discover(discoveryOptions, testMethod, theoryAttribute))
            {
                yield return test.SetName(dn => dn?.Replace("StackExchange.Redis.Tests.", ""));
            }
        }
    }

    internal static class XUnitExtensions
    {
        private static readonly PropertyInfo dnProtected =
            typeof(XunitTestCase).GetProperty(nameof(XunitTestCase.DisplayName), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        public static IXunitTestCase SetName(this IXunitTestCase testCase, Func<string, string> replace)
        {
            if (testCase is XunitTestCase xutc)
            {
                try
                {
                    dnProtected.SetValue(xutc, replace(xutc.DisplayName));
                }
                catch (Exception e)
                {
                    Trace.Write(e);
                }
            }
            return testCase;
        }
    }
}
