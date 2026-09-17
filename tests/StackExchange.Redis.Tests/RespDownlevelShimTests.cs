using System;
using System.Linq;
using System.Reflection;
using StackExchange.Redis.Downlevel;
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
    /// <summary>
    /// Where a group accessor can live - <b>two places, while the surface is being split up</b>.
    /// </summary>
    /// <remarks>
    /// Groups are moving out of the staging <c>Interpolated</c> namespace one at a time, each taking its
    /// accessor from <c>RespSurface</c> to <see cref="RespDatabaseExtensions"/> as it goes. Looking in
    /// both is what lets that happen a group at a time instead of in one sweep; when the last group moves,
    /// <c>RespSurface</c> drops off this list and then out of the library.
    /// </remarks>
    private static readonly Type[] AccessorHosts = [typeof(RespSurface), typeof(RespDatabaseExtensions)];

    /// <summary>
    /// Does this single-parameter method take <paramref name="receiver"/>, directly or as a constraint?
    /// </summary>
    /// <remarks>
    /// The accessors are <b>generic over the target</b> - <c>extension&lt;TTarget&gt;(TTarget target) where
    /// TTarget : IRespKeyspaceTarget</c> - so that a struct context reaches its groups through a
    /// constrained call rather than being boxed on every <c>db.Strings</c>. That makes the parameter type a
    /// generic parameter rather than the interface, so matching on the parameter type alone silently finds
    /// nothing; the constraint is where the receiver is now named.
    /// </remarks>
    private static bool Takes(MethodInfo method, Type receiver)
    {
        if (method.GetParameters() is not { Length: 1 } ps) return false;
        var type = ps[0].ParameterType;
        if (type.Name == receiver.Name) return true;

        return type.IsGenericParameter
            && Array.Exists(type.GetGenericParameterConstraints(), c => c.Name == receiver.Name);
    }

    private static string[] AccessorNames(Type receiver) =>
        AccessorHosts
            .SelectMany(host => host.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.StartsWith("get_", StringComparison.Ordinal))
            .Where(m => Takes(m, receiver))
            .Select(m => m.Name.Substring(4))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static string[] ShimNames(Type receiver) =>
        typeof(RespGroups)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => Takes(m, receiver))
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
        var ctx = new RespDatabaseContext(new RespContext());
        Assert.Equal(
            ctx.Strings().Raw.Database,
            ctx.Database); // the group carries the context it was made from
    }
}
