using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The <c>SCRIPT LOAD</c> that rides in front of an <c>EVALSHA</c>, and the belief it leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// This pairing had no coverage, and the reason it went unnoticed is worth recording: dropping it is not
/// a <i>correctness</i> regression. With no hash to send, the message writes a plain <c>EVAL &lt;body&gt;</c>
/// instead, which the server runs and caches perfectly well - so every test still passes, and the only
/// loss is that the body goes on the wire on every single call, forever, because nothing ever learns the
/// hash. A mutation that removed the pairing survived the whole suite.
/// </para>
/// <para>
/// What is observable is the belief: the endpoint learns a script is loaded only from a <c>SCRIPT LOAD</c>
/// reply. So "did we pair?" is asked as "does the endpoint now know it?", which is also the state the
/// write-time skip consumes - making this the test that stops that optimisation from being built on sand.
/// </para>
/// </remarks>
public class ScriptLoadPairingTests(ITestOutputHelper output) : TestBase(output)
{
    private const string Script = "return 'hello from ScriptLoadPairingTests'";

    private static ServerEndPoint GetEndPoint(IInternalConnectionMultiplexer muxer, EndPoint endpoint)
        => muxer.GetServerEndPoint(endpoint);

    [Fact]
    public async Task EvaluatingAColdScriptTeachesTheEndpointItsHash()
    {
        await using var muxer = Create();
        var endpoint = muxer.GetEndPoints()[0];
        var server = GetEndPoint(muxer, endpoint);

        // start from "this endpoint knows nothing", without SCRIPT FLUSH: that is server-wide and would
        // race every other test using scripts on the same box
        server.FlushScriptCache();
        Assert.False(server.IsScriptLoaded(Script));

        await muxer.GetDatabase().ScriptEvaluateAsync(Script, flags: CommandFlags.None);

        Assert.True(
            server.IsScriptLoaded(Script),
            "the SCRIPT LOAD preamble was not paired with the EVALSHA - the body will now be re-sent on every call");
    }

    /// <summary>With the script believed loaded, the pair declines to expand and only EVALSHA is written.</summary>
    /// <remarks>
    /// Asserted through the belief rather than the wire, so it survives the frame path taking over: the
    /// contract is "a warm script needs no preamble", not "this many messages were written".
    /// </remarks>
    [Fact]
    public async Task ASecondEvaluationNeedsNoPreamble()
    {
        await using var muxer = Create();
        var endpoint = muxer.GetEndPoints()[0];
        var server = GetEndPoint(muxer, endpoint);

        server.FlushScriptCache();
        await muxer.GetDatabase().ScriptEvaluateAsync(Script, flags: CommandFlags.None);
        Assert.True(server.IsScriptLoaded(Script));

        // the second call must still work, and must not disturb the belief it is relying on
        var result = await muxer.GetDatabase().ScriptEvaluateAsync(Script, flags: CommandFlags.None);
        Assert.Equal("hello from ScriptLoadPairingTests", result.ToString());
        Assert.True(server.IsScriptLoaded(Script));
    }

    /// <summary>
    /// The same pairing on the <c>RespResult</c> path, which is a <b>different message type</b>.
    /// </summary>
    /// <remarks>
    /// Worth its own test rather than looking like duplication: <c>ScriptEvaluateResp</c> builds
    /// <c>ScriptEvalMessage</c> where <c>ScriptEvaluate</c> builds <c>ScriptEvaluateMessage</c>, and the two
    /// carry separate copies of this logic. Mutating one and not the other is invisible to every test that
    /// only exercises the other - which is exactly what happened when this file had only the first three.
    /// </remarks>
    [Fact]
    public async Task TheRespPathPairsToo()
    {
        await using var muxer = Create();
        var endpoint = muxer.GetEndPoints()[0];
        var server = GetEndPoint(muxer, endpoint);

        server.FlushScriptCache();
        Assert.False(server.IsScriptLoaded(Script));

        await muxer.GetDatabase().ScriptEvaluateRespAsync(Script, default, default, CommandFlags.None);

        Assert.True(
            server.IsScriptLoaded(Script),
            "the RespResult script path did not pair its SCRIPT LOAD - it has its own copy of this logic");
    }

    /// <summary>NoScriptCache opts out of the pairing entirely, and leaves no belief behind.</summary>
    [Fact]
    public async Task NoScriptCacheLearnsNothing()
    {
        await using var muxer = Create();
        var endpoint = muxer.GetEndPoints()[0];
        var server = GetEndPoint(muxer, endpoint);

        server.FlushScriptCache();
        await muxer.GetDatabase().ScriptEvaluateAsync(Script, flags: CommandFlags.NoScriptCache);

        // the server caches it regardless - nothing a client sends prevents that - but *we* must not
        // record it, or a later call would send EVALSHA for a script we never loaded durably
        Assert.False(server.IsScriptLoaded(Script));
    }
}
