using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A server can forget a script at any time - SCRIPT FLUSH, a restart, a failover to a replica that never
/// had it - while the client still has the hash cached, so every EVALSHA path has to cope with NOSCRIPT by
/// re-issuing as EVAL. These pin that down for the RespResult-returning script APIs.
/// </summary>
public class ScriptEvalRespNoScriptTests(ITestOutputHelper output) : TestBase(output)
{
    private const string Script = "return 'hello from ScriptEvalRespNoScriptTests'";
    private const string Expected = "hello from ScriptEvalRespNoScriptTests";

    // A hash we hold that the server does not: exactly the state a SCRIPT FLUSH, a restart, or a
    // failover to a replica that never had the script would leave us in.
    //
    // Deliberately *not* done by calling SCRIPT FLUSH: that is server-wide, and would race every other
    // test using scripts on the same server - ScriptingTests.CheckLoads asserts ScriptExists straight
    // after caching, so a flush landing in that window fails it. Poisoning only this connection's own
    // client-side cache reproduces the same condition and touches nothing shared.
    private static readonly byte[] UnknownHash = Encoding.ASCII.GetBytes(new string('0', 40));

    private IInternalConnectionMultiplexer ConnectWithStaleHash()
    {
        var conn = Create(shared: false, allowAdmin: true);
        conn.GetServerSnapshot()[0].AddScript(Script, UnknownHash);
        return conn;
    }

    [Fact]
    public async Task ScriptEvaluateResp_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        using var result = conn.GetDatabase().ScriptEvaluateResp(Script, default, default);
        Assert.Equal(Expected, (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ScriptEvaluateRespAsync_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        using var result = await conn.GetDatabase().ScriptEvaluateRespAsync(Script, default, default);
        Assert.Equal(Expected, (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ScriptEvaluateReadOnlyResp_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        using var result = conn.GetDatabase().ScriptEvaluateReadOnlyResp(Script, default, default);
        Assert.Equal(Expected, (string?)result.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task ScriptEvaluateReadOnlyRespAsync_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        using var result = await conn.GetDatabase().ScriptEvaluateReadOnlyRespAsync(Script, default, default);
        Assert.Equal(Expected, (string?)result.ReadScalar().ReadRedisValue());
    }

    /// <summary>
    /// Control: the classic RedisResult-returning path already copes, so this shows the difference is the
    /// result processor rather than anything about the message or the test setup.
    /// </summary>
    [Fact]
    public async Task ScriptEvaluate_Classic_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        var result = conn.GetDatabase().ScriptEvaluate(Script);
        Assert.Equal(Expected, (string?)result);
    }
}
