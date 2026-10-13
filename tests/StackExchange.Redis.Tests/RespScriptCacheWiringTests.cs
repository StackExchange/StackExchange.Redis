using System.Threading.Tasks;
using StackExchange.Redis.Server;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A context handed out by a real multiplexer actually carries a script cache.
/// </summary>
/// <remarks>
/// <para>
/// <c>RespSurfaceScriptsTests</c> proves what the cache <i>does</i>, but does it by constructing one and
/// attaching it by hand. Nothing proved that the library ever attaches one - and for a while it did not:
/// the only writer was a public <c>WithScriptCache</c> that no library code called, so every real
/// application took the "correct and wasteful" branch that re-renders <c>SCRIPT LOAD</c> per call. That
/// gap was invisible because the fallback is correct.
/// </para>
/// <para>
/// The scope is asserted as well as the presence. The entries are the bytes of <c>SCRIPT LOAD</c> plus a
/// SHA, both pure functions of the script and the command map, so every endpoint of one multiplexer would
/// render identical ones - an endpoint- or bridge-scoped cache would be N copies and N renders for no
/// gain. The per-endpoint half (does <i>this</i> server hold the script) is elsewhere, on
/// <c>ServerEndPoint</c>, which is the only part that can go stale.
/// </para>
/// </remarks>
public class RespScriptCacheWiringTests(ITestOutputHelper log)
{
    private async Task<(InProcessTestServer Server, ConnectionMultiplexer Muxer)> ConnectAsync()
    {
        var server = new InProcessTestServer(log);
        var muxer = await ConnectionMultiplexer.ConnectAsync(server.GetClientConfig(), new TextWriterOutputHelper(log));
        return (server, muxer);
    }

    [Fact]
    public async Task ADatabaseContextCarriesTheMultiplexersScriptCache()
    {
        var (server, muxer) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        Assert.Same(muxer.ScriptCache, muxer.GetDatabase().Raw.ScriptCache);
    }

    [Fact]
    public async Task AServerContextCarriesItToo()
    {
        // SCRIPT LOAD is a server command as much as a database one, and RedisServer builds its own context
        var (server, muxer) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        var endpoint = muxer.GetEndPoints()[0];
        Assert.Same(muxer.ScriptCache, muxer.GetServer(endpoint).Raw.ScriptCache);
    }

    [Fact]
    public async Task EveryDatabaseOfOneMultiplexerSharesOne()
    {
        // the rendering does not depend on the database, so a cache per database would be N copies of the
        // same arrays - this is what says the scope is the multiplexer and not something narrower
        var (server, muxer) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        Assert.Same(muxer.GetDatabase(0).Raw.ScriptCache, muxer.GetDatabase(3).Raw.ScriptCache);
    }

    [Fact]
    public async Task TwoMultiplexersDoNotShare()
    {
        // and not something broader: an entry is rendered through the command map, which belongs to the
        // multiplexer, so a process-wide cache keyed on script text alone could serve renamed bytes
        var (serverA, muxerA) = await ConnectAsync();
        using var _ = serverA;
        await using var __ = muxerA;
        var (serverB, muxerB) = await ConnectAsync();
        using var ___ = serverB;
        await using var ____ = muxerB;

        Assert.NotSame(muxerA.ScriptCache, muxerB.ScriptCache);
    }

    [Fact]
    public async Task ItStartsEmpty()
    {
        // unconditional, so it must cost nothing until a script is actually sent
        var (server, muxer) = await ConnectAsync();
        using var _ = server;
        await using var __ = muxer;

        Assert.Equal(0, muxer.ScriptCache.Count);
        Assert.Equal(0, muxer.ScriptCache.Rendered);
        Assert.Equal(0, muxer.ScriptCache.Bytes);
    }
}
