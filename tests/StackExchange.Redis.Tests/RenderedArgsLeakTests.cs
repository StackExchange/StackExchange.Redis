using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public class RenderedArgsLeakTests(ITestOutputHelper output) : TestBase(output)
{
    // end-to-end companion to the codec-level RenderedArgsTests: drives real ExecuteResp/ScriptEvaluateResp
    // traffic against a live server and confirms the rented request buffers actually come back - normal
    // completion, not just the isolated Recycle() calls the unit tests exercise directly.
    [Fact]
    public async Task ExecuteRespAndScriptEvaluateResp_DoNotLeakRentedBuffers()
    {
        const int Iterations = 500;
        var pool = new CountingPool();
        var options = new ConfigurationOptions
        {
            EndPoints = { TestConfig.Current.PrimaryServerAndPort },
            RequestBufferPool = pool,
            Protocol = TestContext.Current.GetProtocol(),
        };

        RedisKey key = Me();
        await using (var conn = await ConnectionMultiplexer.ConnectAsync(options))
        {
            var db = conn.GetDatabase();
            for (int i = 0; i < Iterations; i++)
            {
                using var setResult = await db.ExecuteRespAsync("SET", new RedisKeyOrValue[] { key, (RedisValue)i });
                using var getResult = await db.ScriptEvaluateRespAsync("return redis.call('get', KEYS[1])", new RedisKey[] { key }, default);
                Assert.Equal(i.ToString(), (string?)getResult.ReadScalar().ReadRedisValue());
            }
        }

        // after full teardown, every rented buffer should have come back - modulo a small, stable
        // residual for whatever the connection's own write/read buffering happened to be holding at the
        // moment of measurement, not a per-call leak (see PR #3211 discussion: ballpark 500 calls -> ~550
        // rents [500 for the request buffers, the rest for IO], with almost all of them returned).
        var outstanding = pool.Rented - pool.Returned;
        Output.WriteLine($"rented={pool.Rented}, returned={pool.Returned}, outstanding={outstanding}");
        Assert.True(pool.Rented >= Iterations * 2, $"expected at least one rent per call, got {pool.Rented} for {Iterations} iterations");
        Assert.True(outstanding <= 5, $"expected only a small residual, got {outstanding} outstanding (rented={pool.Rented}, returned={pool.Returned})");
    }

    // DIAGNOSTIC: same loop, but through the *old*, pre-RenderedArgs ScriptEvaluateAsync path, to determine
    // whether the "keys > args" corruption seen under full-suite load is specific to the new codec or a
    // pre-existing race in the write path that predates this PR entirely.
    [Fact]
    public async Task OldScriptEvaluateAsync_DoesNotCorruptUnderLoad()
    {
        const int Iterations = 500;
        RedisKey key = Me();
        await using var conn = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { TestConfig.Current.PrimaryServerAndPort },
            Protocol = TestContext.Current.GetProtocol(),
        });
        var db = conn.GetDatabase();
        for (int i = 0; i < Iterations; i++)
        {
            await db.StringSetAsync(key, i);
            var result = await db.ScriptEvaluateAsync("return redis.call('get', KEYS[1])", new RedisKey[] { key }, default);
            Assert.Equal(i.ToString(), (string?)result);
        }
    }

    private sealed class CountingPool : MemoryPool<byte>
    {
        private int _rented, _returned;
        public int Rented => _rented;
        public int Returned => _returned;
        public override int MaxBufferSize => Shared.MaxBufferSize;

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Interlocked.Increment(ref _rented);
            return new Owner(this, Shared.Rent(minBufferSize));
        }

        protected override void Dispose(bool disposing)
        {
        }

        private sealed class Owner(CountingPool pool, IMemoryOwner<byte> inner) : IMemoryOwner<byte>
        {
            public Memory<byte> Memory => inner.Memory;
            public void Dispose()
            {
                Interlocked.Increment(ref pool._returned);
                inner.Dispose();
            }
        }
    }
}
