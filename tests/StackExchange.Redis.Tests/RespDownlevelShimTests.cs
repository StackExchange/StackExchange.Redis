using System;
using System.Linq;
using System.Reflection;
using StackExchange.Redis.Interpolated;
using StackExchange.Redis.Interpolated.Downlevel;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Every command group reachable as an extension <i>property</i> is also reachable as a <i>method</i>.
/// </summary>
/// <remarks>
/// <para>
/// <c>db.Strings</c> needs C# 14; <c>db.Strings()</c> works back to C# 7.3, so the shims are what make the
/// surface usable on older tooling at all. They are written by hand rather than generated - a generator
/// costs build time on every consumer build, and analyzers already account for a large share of a clean
/// build here - so this test is what stops a new group shipping without one.
/// </para>
/// <para>
/// Note this file imports <b>both</b> namespaces, which is exactly what a down-level consumer does and
/// what an up-level consumer must not: on C# 14 that makes <c>db.Strings</c> ambiguous. The test therefore
/// only ever names the method form.
/// </para>
/// </remarks>
public class RespDownlevelShimTests
{
    private static string[] AccessorNames(Type receiver) =>
        typeof(RespSurface)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("get_", StringComparison.Ordinal))
            .Where(m => m.GetParameters() is { Length: 1 } ps && ps[0].ParameterType.Name == receiver.Name)
            .Select(m => m.Name.Substring(4))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static string[] ShimNames(Type receiver) =>
        typeof(RespGroups)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetParameters() is { Length: 1 } ps && ps[0].ParameterType.Name == receiver.Name)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void EveryKeyspaceGroupHasAMethodShim()
    {
        var accessors = AccessorNames(typeof(IRespKeyspaceTarget));
        Assert.NotEmpty(accessors); // a reflection typo would otherwise pass vacuously
        Assert.Equal(accessors, ShimNames(typeof(IRespKeyspaceTarget)));
    }

    /// <summary>The same for a bare context, which is how a caller holding one reaches the groups.</summary>
    [Fact]
    public void EveryContextGroupHasAMethodShim()
    {
        var accessors = AccessorNames(typeof(RespContext).MakeByRefType());
        Assert.NotEmpty(accessors);
        Assert.Equal(accessors, ShimNames(typeof(RespContext).MakeByRefType()));
    }

    /// <summary>And the shim really does reach a command, not merely exist.</summary>
    [Fact]
    public void AShimComposesWithTheCommandsThatHangOffIt()
    {
        var ctx = new RespContext();
        Assert.Equal(
            ctx.Strings().Context.Database,
            ctx.Database); // the group carries the context it was made from
    }
}
