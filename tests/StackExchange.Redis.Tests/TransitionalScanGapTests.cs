using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The cursor scans have <b>not</b> moved to the context surface, and SER352 cannot say so.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AutoDatabase]</c> skips every <c>IEnumerable&lt;T&gt;</c>/<c>IAsyncEnumerable&lt;T&gt;</c> member -
/// deferred execution does not fit capture-and-replay - so the scans are written by hand in
/// <c>TransitionalDatabase.Scans.cs</c>. Those hand-written members forward to the test fallback and throw
/// without one, exactly like the generated stubs, but the generator has no way to tell a real
/// implementation from a forwarding throw: to it they are simply "declared". So they are missing from the
/// count, and the SER352 number has been short by this many all along.
/// </para>
/// <para>
/// This is the tripwire in the one place that can see it. It sweeps by reflection rather than naming the
/// members, so a scan that <i>is</i> implemented later fails here and has to be accounted for - which is
/// the behaviour SER352 would have had if it could see them.
/// </para>
/// </remarks>
public class TransitionalScanGapTests
{
    private static IEnumerable<MethodInfo> ScanMembers() =>
        typeof(TransitionalDatabase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType)
            .Where(m => m.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                     || m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

    public static TheoryData<string> ScanNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ScanMembers().Select(Describe).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            data.Add(name);
        }
        return data;
    }

    private static string Describe(MethodInfo m)
        => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})";

    /// <summary>Every scan still throws, so none of them is quietly half-done.</summary>
    [Theory]
    [MemberData(nameof(ScanNames))]
    public void EveryScanStillThrows(string name)
    {
        var method = ScanMembers().Single(m => Describe(m) == name);
        var db = new TransitionalDatabase(new RespDatabaseContext(new RespContext()), null!, null);

        var args = method.GetParameters()
            .Select(p => p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(p.ParameterType))
            .ToArray();

        // the forwarder evaluates its fallback before building the iterator, so this throws on the call
        // rather than on the first MoveNext - which is what makes a lazy member testable at all
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(db, args));
        Assert.IsType<NotImplementedException>(ex.InnerException);
    }

    /// <summary>
    /// And the count, so the real size of the gap is written down somewhere that fails when it changes.
    /// </summary>
    /// <remarks>
    /// SER352 reports the generated stubs only. Add this to it for the honest total.
    /// </remarks>
    [Fact]
    public void TheScanGapIsThisBig()
        => Assert.Equal(13, ScanMembers().Count());
}
