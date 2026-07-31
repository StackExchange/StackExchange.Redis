using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests;

public class CircuitBreakerServerTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task CircuitBreakerObservesMessageResults()
    {
        using var server = new InProcessTestServer();

        // take the template options from the server, and slot in our test breaker *before* connecting,
        // so every physical connection yanks an accumulator from it during init
        var config = server.GetClientConfig();
        var breaker = new CountingCircuitBreaker();
        config.CircuitBreaker = breaker;

        using var client = await ConnectionMultiplexer.ConnectAsync(config);
        var db = client.GetDatabase();

        // some successful operations (these, plus handshake traffic, count as non-fault observations)
        RedisKey key = Me();
        await db.StringSetAsync(key, "abc");
        Assert.Equal("abc", await db.StringGetAsync(key));
        await db.StringGetAsync(key);

        var successesAfterGetSets = breaker.Successes;

        // knock the server offline: it now replies LOADING to every command, which (unlike an
        // application-level "unknown command") is a genuine availability fault the breaker observes.
        // flip it straight back off so no background heartbeat can observe a second LOADING reply -
        // the fault for our command is recorded synchronously as it completes, before the await returns.
        server.IsLoading = true;
        var fault = await Assert.ThrowsAsync<RedisServerException>(() => db.StringGetAsync(key));
        server.IsLoading = false;
        Assert.Equal(RedisErrorKind.Loading, fault.Kind);
        Output.WriteLine($"loading fault: {fault.GetType().Name}: {fault.Message}");

        Output.WriteLine($"observed successes={breaker.Successes}, failures={breaker.Failures}, lastFault={breaker.LastFault?.GetType().Name}");

        // the get/sets were observed as successes
        Assert.True(successesAfterGetSets > 0, "expected the successful operations to be observed");

        // exactly one fault (the LOADING reply), captured as the clean server error. A healthy
        // breaker must NOT tear the connection down: a regression there shows up here as a
        // RedisConnectionException (and extra faults) rather than this RedisServerException.
        Assert.Equal(1, breaker.Failures);
        var serverFault = Assert.IsType<RedisServerException>(breaker.LastFault);
        Assert.Equal(RedisErrorKind.Loading, serverFault.Kind);
    }

    // a minimal breaker for tests: shares counters across all accumulators it creates, so we can
    // observe traffic across every physical connection; it never trips (always reports healthy)
    private sealed class CountingCircuitBreaker : CircuitBreaker
    {
        private int _successes, _failures;

        public int Successes => Volatile.Read(ref _successes);
        public int Failures => Volatile.Read(ref _failures);
        public Exception? LastFault { get; private set; }

        public override Accumulator CreateAccumulator() => new CountingAccumulator(this);

        private sealed class CountingAccumulator(CountingCircuitBreaker owner) : Accumulator
        {
            public override void ObserveResult(in FaultContext context)
            {
                if (context.IsFault)
                {
                    Interlocked.Increment(ref owner._failures);
                    owner.LastFault = context.Fault;
                }
                else
                {
                    Interlocked.Increment(ref owner._successes);
                }
            }

            public override bool IsHealthy() => true;

            public override void Reset() { }
        }
    }
}
