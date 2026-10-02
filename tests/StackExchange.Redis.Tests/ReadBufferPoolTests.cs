using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The reader's <c>CycleBuffer</c> rents its segments from <see cref="ConfigurationOptions.ResponseBufferPool"/>;
/// this checks that they go back when a connection is torn down, whichever of the parse loop and the
/// background filler happens to stop last (see <c>PhysicalConnection.OnParseLoopStopped</c>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class ReadBufferPoolTests(ITestOutputHelper output) : TestBase(output)
{
    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort + "," + TestConfig.Current.ReplicaServerAndPort;

    [Fact]
    [Trait(TestCategories.Category, TestCategories.SimulatedConnectionFailure)]
    public async Task ReadBuffersAreReturnedOnReconnectAndDispose()
    {
        var pool = new TrackingMemoryPool();
        var config = ConfigurationOptions.Parse(GetConfiguration());
        config.ResponseBufferPool = pool;
        config.AllowAdmin = true;
        config.AllowSimulateConnectionFailure = true;
        config.KeepAlive = 1;
        config.ReconnectRetryPolicy = new LinearRetry(200);

        try
        {
            var conn = await ConnectionMultiplexer.ConnectAsync(config, Writer);
            await using (conn)
            {
                var db = conn.GetDatabase();
                await db.PingAsync();
                var steady = pool.Outstanding;
                Log($"outstanding after connect: {steady} (rented {pool.Rented})");
                Assert.True(steady > 0, "the reader should be renting from the configured pool");

                var server = conn.GetServer(conn.GetEndPoints()[0]);
                Assert.SkipUnless(server.CanSimulateConnectionFailure(), "server cannot simulate connection failure");

                // tear the connection down under the reader: its buffers must come back, and the replacement
                // connection's rentals must not stack on top of leaked ones
                server.SimulateConnectionFailure(SimulatedFailureType.All);
                await UntilConditionAsync(TimeSpan.FromSeconds(5), () => server.IsConnected).ForAwait();
                Assert.True(server.IsConnected, "expected reconnect");
                await db.PingAsync();

                await UntilConditionAsync(TimeSpan.FromSeconds(5), () => pool.Outstanding <= steady).ForAwait();
                Log($"outstanding after reconnect: {pool.Outstanding} (rented {pool.Rented})");
                Assert.True(pool.Outstanding <= steady, $"read buffers leaked across reconnect: {steady} -> {pool.Outstanding}");
            }

            // and once everything is disposed, nothing should still be out
            await UntilConditionAsync(TimeSpan.FromSeconds(5), () => pool.Outstanding == 0).ForAwait();
            Log($"outstanding after dispose: {pool.Outstanding} (rented {pool.Rented})");
            Assert.Equal(0, pool.Outstanding);
        }
        finally
        {
            ClearAmbientFailures();
        }
    }

    private sealed class TrackingMemoryPool : MemoryPool<byte>
    {
        private int _rented, _outstanding;
        public int Rented => Volatile.Read(ref _rented);
        public int Outstanding => Volatile.Read(ref _outstanding);
        public override int MaxBufferSize => Shared.MaxBufferSize;
        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Interlocked.Increment(ref _rented);
            Interlocked.Increment(ref _outstanding);
            return new Lease(this, Shared.Rent(minBufferSize));
        }
        protected override void Dispose(bool disposing) { }

        private sealed class Lease(TrackingMemoryPool owner, IMemoryOwner<byte> inner) : IMemoryOwner<byte>
        {
            private IMemoryOwner<byte>? _inner = inner;
            public Memory<byte> Memory => _inner?.Memory ?? default;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _inner, null) is { } tmp)
                {
                    tmp.Dispose();
                    Interlocked.Decrement(ref owner._outstanding);
                }
            }
        }
    }
}
