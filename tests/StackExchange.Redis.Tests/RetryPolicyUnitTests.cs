using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class RetryPolicyUnitTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestExponentialRetry(bool rejectConnection)
    {
        using var server = new NonResponsiveServer(log);
        var options = server.GetClientConfig(withPubSub: false);
        var policy = new CountingRetryPolicy();
        options.ConnectRetry = 5;
        options.SyncTimeout = options.AsyncTimeout = options.ConnectTimeout = 1_000;
        options.ReconnectRetryPolicy = policy;

        // connect while the server is stable
        await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
        var db = conn.GetDatabase();
        db.Ping();
        Assert.Equal(0, policy.Clear());

        // now tell the server to become non-responsive to the next 2, and kill the current
        server.IgnoreNext(2, rejectConnection);
        server.ForAllClients(x => x.Kill());

        for (int i = 0; i < 10; i++)
        {
            try
            {
                await db.PingAsync();
                break;
            }
            catch (Exception ex)
            {
                log.WriteLine($"{nameof(db.PingAsync)} attempt {i}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        var counts = policy.GetRetryCounts();
        Assert.Equal("0,1", string.Join(",", counts));
    }

    private sealed class CountingRetryPolicy : IReconnectRetryPolicy
    {
        private readonly struct RetryRequest(int currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            public int CurrentRetryCount { get; } = currentRetryCount;
            public int TimeElapsedMillisecondsSinceLastRetry { get; } = timeElapsedMillisecondsSinceLastRetry;
        }
        private readonly List<RetryRequest> retryCounts = [];

        public int Clear()
        {
            lock (retryCounts)
            {
                int count = retryCounts.Count;
                retryCounts.Clear();
                return count;
            }
        }

        public int[] GetRetryCounts()
        {
            lock (retryCounts)
            {
                return retryCounts.Select(x => x.CurrentRetryCount).ToArray();
            }
        }

        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            lock (retryCounts)
            {
                retryCounts.Add(new(checked((int)currentRetryCount), timeElapsedMillisecondsSinceLastRetry));
            }
            return true;
        }
    }

    private sealed class NonResponsiveServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _ignoreNext;
        private bool _rejectConnection;

        public void IgnoreNext(int count, bool rejectConnection)
        {
            _ignoreNext = count;
            _rejectConnection = rejectConnection;
        }

        protected override void OnAcceptClient(EndPoint endpoint)
        {
            if (_rejectConnection && ShouldIgnoreClient())
            {
                Log($"(rejecting connection to {endpoint})");
                throw new SocketException((int)SocketError.ConnectionRefused);
            }
            base.OnAcceptClient(endpoint);
        }

        private bool ShouldIgnoreClient()
        {
            while (true)
            {
                var oldValue = Volatile.Read(ref _ignoreNext);
                if (oldValue <= 0) return false;
                var newValue = oldValue - 1;
                if (Interlocked.CompareExchange(ref _ignoreNext, newValue, oldValue) == oldValue) return true;
            }
        }

        public override RedisClient CreateClient(Node node)
        {
            var client = base.CreateClient(node);
            if (!_rejectConnection && ShouldIgnoreClient())
            {
                Log($"(accepting non-responsive connection to {node.Host}:{node.Port})");
                client.SkipAllReplies();
            }
            else
            {
                Log($"(accepting responsive connection to {node.Host}:{node.Port})");
            }
            return client;
        }
    }
}
