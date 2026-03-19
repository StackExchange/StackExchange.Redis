using System;
using System.Buffers;
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
    [InlineData(FailureMode.Success)]
    [InlineData(FailureMode.ConnectionRefused)]
    [InlineData(FailureMode.SlowNonConnect)]
    [InlineData(FailureMode.NoResponses)]
    [InlineData(FailureMode.GarbageResponses)]
    public async Task RetryPolicyFailureCases(FailureMode failureMode)
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
        server.FailNext(2, failureMode);
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
        if (failureMode is FailureMode.Success)
        {
            Assert.Empty(counts);
        }
        else
        {
            Assert.Equal("0,1", string.Join(",", counts));
        }
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

    public enum FailureMode
    {
        Success,
        SlowNonConnect,
        ConnectionRefused,
        NoResponses,
        GarbageResponses,
    }
    private sealed class NonResponsiveServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private int _failNext;
        private FailureMode _failureMode;

        public void FailNext(int count, FailureMode failureMode)
        {
            _failNext = count;
            _failureMode = failureMode;
        }

        protected override ValueTask OnAcceptClientAsync(EndPoint endpoint)
        {
            switch (_failureMode)
            {
                case FailureMode.SlowNonConnect when ShouldIgnoreClient():
                    Log($"(leaving pending connect to {endpoint})");
                    return TimeoutEventually();
                case FailureMode.ConnectionRefused when ShouldIgnoreClient():
                    Log($"(rejecting connection to {endpoint})");
                    throw new SocketException((int)SocketError.ConnectionRefused);
                default:
                    return base.OnAcceptClientAsync(endpoint);
            }

            static async ValueTask TimeoutEventually()
            {
                await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                throw new TimeoutException();
            }
        }

        private bool ShouldIgnoreClient()
        {
            while (true)
            {
                var oldValue = Volatile.Read(ref _failNext);
                if (oldValue <= 0) return false;
                var newValue = oldValue - 1;
                if (Interlocked.CompareExchange(ref _failNext, newValue, oldValue) == oldValue) return true;
            }
        }

        private sealed class GarbageClient(Node node) : RedisClient(node)
        {
            protected override void WriteResponse(
                IBufferWriter<byte> output,
                TypedRedisValue value,
                RedisProtocol protocol)
            {
#if NET
                var rand = Random.Shared;
#else
                var rand = new Random();
#endif
                var len = rand.Next(1, 1024);
                var buffer = ArrayPool<byte>.Shared.Rent(len);
                var span = buffer.AsSpan(0, len);
                try
                {
#if NET
                    rand.NextBytes(span);
#else
                    rand.NextBytes(buffer);
#endif
                    output.Write(span);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public override RedisClient CreateClient(Node node)
        {
            RedisClient client;
            if (_failureMode is FailureMode.GarbageResponses && ShouldIgnoreClient())
            {
                client = new GarbageClient(node);
                Log($"(accepting garbage-responsive connection to {node.Host}:{node.Port})");
                return client;
            }
            client = base.CreateClient(node);
            if (_failureMode is FailureMode.NoResponses && ShouldIgnoreClient())
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
