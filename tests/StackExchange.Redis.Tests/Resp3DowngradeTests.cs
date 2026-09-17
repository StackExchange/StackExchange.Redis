using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A connection can negotiate RESP3 and then, on a later reconnect, fail to (the HELLO times out, a token
/// expires, the endpoint moves to a down-level server, ...). We then downgrade to RESP2, which needs pub/sub on
/// its own connection - but subscriptions queued while we still expected RESP3 are sitting in the *interactive*
/// bridge's backlog. Writing those to the interactive connection puts it into subscriber mode, which breaks
/// every normal command on it; see issue #3154.
/// </summary>
public class Resp3DowngradeTests(ITestOutputHelper log)
{
    [Fact]
    public async Task SubscribeQueuedBeforeDowngradeDoesNotPoisonInteractive()
    {
        using var server = new DowngradeServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp3;
        config.AllowSimulateConnectionFailure = true;
        config.AbortOnConnectFail = false;
        config.ConnectTimeout = 5000;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = conn.GetServerSnapshot()[0];
        Assert.Equal(RedisProtocol.Resp3, endpoint.Protocol);

        var sub = conn.GetSubscriber();
        var db = conn.GetDatabase();
        RedisChannel channel = RedisChannel.Literal("resp3-downgrade");
        ConcurrentBag<string> received = [];
        await sub.SubscribeAsync(channel, (_, value) => received.Add(value!));

        // from here on the server no longer understands HELLO, so the next handshake lands on RESP2; and hold
        // the door shut so that we get a window where we are disconnected but still expecting RESP3
        server.HelloSupported = false;
        server.BlockAccepts();
        Assert.SkipUnless(endpoint.CanSimulateConnectionFailure, "Cannot simulate connection failure");
        endpoint.SimulateConnectionFailure(SimulatedFailureType.All);
        await server.WaitForBlockedAcceptAsync();

        // queue a subscribe while disconnected: this goes to the backlog of whichever bridge we *expect* to
        // need, and we still expect RESP3 - i.e. the interactive bridge
        await sub.SubscribeAsync(channel, (_, value) => received.Add(value!), CommandFlags.FireAndForget);

        // now let it reconnect (as RESP2) and drain that backlog
        server.ReleaseAccepts();
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => endpoint.Protocol == RedisProtocol.Resp2);
        Assert.Equal(RedisProtocol.Resp2, endpoint.Protocol);

        // the interactive connection must still be usable for normal commands
        RedisKey key = "resp3-downgrade-key";
        await db.StringSetAsync(key, "value");
        Assert.Equal("value", await db.StringGetAsync(key));

        // ...and pub/sub must still work, on the subscription connection now
        await sub.PublishAsync(channel, "payload");
        await UntilConditionAsync(TimeSpan.FromSeconds(10), () => !received.IsEmpty);
        Assert.Contains("payload", received);

        // finally, the invariant that matters (and that the assertions above only observe indirectly): under
        // RESP2 no single connection may carry both subscriptions and ordinary commands
        var mixed = server.ConnectionsMixingSubscriptionsAndCommands(RedisProtocol.Resp2);
        Assert.Empty(mixed);
    }

    private static async Task UntilConditionAsync(TimeSpan timeout, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private sealed class DowngradeServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private static readonly HashSet<string> SubscriptionCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "SUBSCRIBE", "UNSUBSCRIBE", "PSUBSCRIBE", "PUNSUBSCRIBE", "SSUBSCRIBE", "SUNSUBSCRIBE",
        };

        // commands that only ever make sense on a connection serving ordinary traffic
        private static readonly HashSet<string> InteractiveCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "SET", "DEL", "EXISTS", "INFO", "CONFIG", "CLUSTER", "PUBLISH",
        };

        private readonly ConcurrentDictionary<long, ConcurrentQueue<string>> _byClient = new();
        private TaskCompletionSource<bool>? _gate, _blocked;

        public bool HelloSupported { get; set; } = true;

        public void BlockAccepts()
        {
            // everything recorded from here on belongs to the post-downgrade connections; the RESP3 connection
            // that preceded them legitimately mixed subscriptions and ordinary commands
            _byClient.Clear();
            _blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockedAcceptAsync() => _blocked?.Task ?? Task.CompletedTask;

        public void ReleaseAccepts() => _gate?.TrySetResult(true);

        protected override async ValueTask OnAcceptClientAsync(EndPoint endpoint)
        {
            if (_gate is { } gate)
            {
                _blocked?.TrySetResult(true);
                await gate.Task;
            }
        }

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            _byClient.GetOrAdd(client.Id, _ => new()).Enqueue(request.GetString(0).ToUpperInvariant());
            return base.Execute(client, in request);
        }

        protected override TypedRedisValue Hello(RedisClient client, in RedisRequest request)
            => HelloSupported ? base.Hello(client, in request) : request.CommandNotFound();

        /// <summary>
        /// Connections that carried both subscription and ordinary commands; legitimate under RESP3 (one
        /// connection does everything), fatal under RESP2 (subscriber mode rejects ordinary commands).
        /// </summary>
        public List<string> ConnectionsMixingSubscriptionsAndCommands(RedisProtocol protocol) =>
            [.. _byClient
                .Where(pair => pair.Value.Any(SubscriptionCommands.Contains) && pair.Value.Any(InteractiveCommands.Contains))
                .Select(pair => $"[{protocol}] client {pair.Key}: {string.Join(",", pair.Value)}")];
    }
}
