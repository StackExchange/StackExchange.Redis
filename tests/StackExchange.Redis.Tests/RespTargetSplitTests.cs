using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Which targets offer which command groups.
/// </summary>
/// <remarks>
/// <para>
/// The groups used to bind to <see cref="IRespTarget"/>, which <c>IRedis</c> carried, so every one of
/// <c>IDatabase</c>, <c>IServer</c> and <c>ISubscriber</c> offered <c>Strings</c>, <c>Hashes</c>,
/// <c>Keys</c> and <c>Scripts</c>. <c>server.Strings.GetAsync(key)</c> compiled - discoverability pointing
/// straight at a cliff, since an <c>IServer</c> is pinned to one endpoint and has no business doing
/// key-routed work.
/// </para>
/// <para>
/// Most of this is asserted by the fact that the test project compiles at all: the commented lines below
/// are the real assertions, and the reflection ones exist so that a regression reports itself as a failing
/// test rather than as something nobody notices is now possible.
/// </para>
/// </remarks>
public class RespTargetSplitTests
{
    [Fact]
    public void ADatabaseIsAKeyspaceTarget()
    {
        Assert.True(typeof(IRespKeyspaceTarget).IsAssignableFrom(typeof(IDatabase)));

        // and therefore this compiles - the line that matters, checked by the compiler and not by xunit
        static RespStrings Compiles(IDatabase db) => db.Strings;
        Assert.NotNull((object?)nameof(Compiles));
    }

    [Fact]
    public void AServerIsNotAKeyspaceTarget()
    {
        Assert.False(
            typeof(IRespKeyspaceTarget).IsAssignableFrom(typeof(IServer)),
            "IServer must not offer key-routed groups: 'server.Strings.GetAsync(key)' should not compile");

        // it keeps a context, though - server-scoped groups will bind to IRespServerTarget
        Assert.True(typeof(IRespServerTarget).IsAssignableFrom(typeof(IServer)));
        Assert.True(typeof(IRespTarget).IsAssignableFrom(typeof(IServer)));
    }

    [Fact]
    public void ASubscriberKeepsAContextButNoKeyspaceGroups()
    {
        Assert.True(typeof(IRespTarget).IsAssignableFrom(typeof(ISubscriber)));
        Assert.False(typeof(IRespKeyspaceTarget).IsAssignableFrom(typeof(ISubscriber)));
    }

    /// <summary>
    /// Every command group hangs off a target narrower than <see cref="IRespTarget"/>.
    /// </summary>
    /// <remarks>
    /// The rule this file exists to keep: binding a new group to <see cref="IRespTarget"/> would quietly
    /// put it back on <c>IServer</c> and <c>ISubscriber</c>, which is the exact regression being prevented
    /// and is a one-word mistake to make.
    /// </remarks>
    [Fact]
    public void NoGroupBindsToTheBareTarget()
    {
        var offenders = typeof(RespSurface)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.StartsWith("get_"))
            // not a list pattern: System.Index does not exist on net481, which only the full multi-TFM
            // build catches - a filtered 'dotnet test -f net10.0' compiles it happily
            .Where(m => m.GetParameters() is { Length: 1 } ps && ps[0].ParameterType == typeof(IRespTarget))
            .Select(m => m.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "these groups bind to IRespTarget, so IServer and ISubscriber offer them: " + string.Join(", ", offenders));
    }
}
