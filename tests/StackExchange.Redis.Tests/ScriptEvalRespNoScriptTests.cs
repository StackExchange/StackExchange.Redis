using System;
using System.Text;
using System.Threading;
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
    private const string ArgScript = "return ARGV[1] .. '|' .. ARGV[2]";
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
        conn.GetServerSnapshot()[0].AddScript(ArgScript, UnknownHash);
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
    /// <summary>
    /// The retry re-issues the *same message instance*, so anything the message released on completion has
    /// to still be there for the second write. With no keys or values there is nothing to release and
    /// nothing to notice - which is why the cases above missed this - so this one carries arguments.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoversFromNoScript_WithArguments(bool readOnly)
    {
        await using var conn = ConnectWithStaleHash();
        var db = conn.GetDatabase();
        RedisKey[] keys = [Me()];
        RedisValue[] values = ["alpha", "beta"];

        using var result = readOnly
            ? await db.ScriptEvaluateReadOnlyRespAsync(ArgScript, keys, values)
            : await db.ScriptEvaluateRespAsync(ArgScript, keys, values);

        Assert.Equal("alpha|beta", (string?)result.ReadScalar().ReadRedisValue());
    }

    /// <summary>
    /// A NOSCRIPT means "not final, a retry is coming", but that is a fact about the reply, not about the
    /// message: the flag it sets is never cleared, so if the retry then fails for some *other* reason, a
    /// check against the message would still read as NOSCRIPT and the request buffer would never come back.
    /// </summary>
    [Fact]
    public async Task RetryFailingForADifferentReasonStillReleasesItsBuffer()
    {
        const string Broken = "this is not lua";
        var pool = new CountingPool();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { TestConfig.Current.PrimaryServerAndPort },
            RequestBufferPool = pool,
            Protocol = TestContext.Current.GetProtocol(),
        });

        const int Iterations = 20;
        for (int i = 0; i < Iterations; i++)
        {
            // stale hash each time, so every attempt is a NOSCRIPT followed by a real EVAL - which then
            // fails to compile, i.e. an error that is emphatically not a NOSCRIPT
            conn.GetServerSnapshot()[0].AddScript(Broken, UnknownHash);
            await Assert.ThrowsAsync<RedisServerException>(
                async () => await conn.GetDatabase().ScriptEvaluateRespAsync(Broken, default, new RedisValue[] { "x" }));
        }

        await Task.Delay(100);

        // the residual is whatever the still-open connection's own IO buffering is holding; what matters
        // is that it does not grow with the number of calls, which is what a per-call leak looks like
        var outstanding = pool.Rented - pool.Returned;
        Output.WriteLine($"rented={pool.Rented} returned={pool.Returned} outstanding={outstanding}");
        Assert.True(pool.Rented >= Iterations, $"expected a rent per call, got {pool.Rented} for {Iterations}");
        Assert.True(outstanding <= 5, $"buffers are not coming back: {outstanding} outstanding after {Iterations} calls");
    }

    private sealed class CountingPool : System.Buffers.MemoryPool<byte>
    {
        private int _rented, _returned;
        public int Rented => Volatile.Read(ref _rented);
        public int Returned => Volatile.Read(ref _returned);
        public override int MaxBufferSize => Shared.MaxBufferSize;
        public override System.Buffers.IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Interlocked.Increment(ref _rented);
            return new Owner(this, Shared.Rent(minBufferSize));
        }
        protected override void Dispose(bool disposing) { }
        private sealed class Owner(CountingPool pool, System.Buffers.IMemoryOwner<byte> inner) : System.Buffers.IMemoryOwner<byte>
        {
            private int _disposed;
            public Memory<byte> Memory => inner.Memory;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Increment(ref pool._returned);
                inner.Dispose();
            }
        }
    }

    [Fact]
    public async Task ScriptEvaluate_Classic_RecoversFromNoScript()
    {
        await using var conn = ConnectWithStaleHash();
        var result = conn.GetDatabase().ScriptEvaluate(Script);
        Assert.Equal(Expected, (string?)result);
    }
}
