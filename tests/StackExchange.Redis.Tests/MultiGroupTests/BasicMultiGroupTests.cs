using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

[RunPerProtocol]
public class BasicMultiGroupTests(ITestOutputHelper log)
{
    private sealed class Capture(ITestOutputHelper log)
    {
        private readonly List<string> _seen = [];

        public void Seen(string source, RedisChannel channel, RedisValue value)
        {
            string message = $"[{source}] {channel}: {value}";
            lock (_seen)
            {
                _seen.Add(message);
            }
        }

        public void Reset()
        {
            lock (_seen)
            {
                _seen.Clear();
            }
        }

        public void AssertTakeAny(string value)
        {
            lock (_seen)
            {
                Assert.True(_seen.Remove(value), $"Expected to find '{value}', but did not");
            }
        }

        public void AssertTakeFirst(string value)
        {
            lock (_seen)
            {
                Assert.NotEmpty(_seen);
                Assert.Equal(value, _seen[0]);
                _seen.RemoveAt(0); // not concerned by perf here
            }
        }

        public async ValueTask AwaitAsync(ISubscriber sub, int expected)
        {
            for (int i = 0; i < 10; i++)
            {
                lock (_seen)
                {
                    if (_seen.Count >= expected)
                    {
                        Assert.Equal(expected, _seen.Count);
                        log.WriteLine("Messages:");
                        foreach (var item in _seen)
                        {
                            log.WriteLine(item);
                        }
                        return;
                    }
                }
                log.WriteLine($"Waiting for {expected} messages, got {i}, pausing...");
                await Task.Delay(10, TestContext.Current.CancellationToken);
                await sub.PingAsync();
            }

            int actual;
            lock (_seen)
            {
                actual = _seen.Count;
            }
            throw new TimeoutException($"Timed out waiting for {expected} messages, got {actual}");
        }

        public Task WriteSeen(string source, ChannelMessageQueue queue) =>
            Task.Run(async () =>
            {
                await foreach (var msg in queue)
                {
                    Seen(source, msg.Channel, msg.Message);
                }
            });
    }
    protected TextWriter Log { get; } = new TextWriterOutputHelper(log);

    public enum InbuiltProbe
    {
        IsConnected,
        Ping,
        StringSet,
    }

    [Theory]
    [InlineData(InbuiltProbe.IsConnected, ServerType.Standalone)]
    [InlineData(InbuiltProbe.Ping, ServerType.Standalone)]
    [InlineData(InbuiltProbe.StringSet, ServerType.Standalone)]
    [InlineData(InbuiltProbe.IsConnected, ServerType.Cluster)]
    [InlineData(InbuiltProbe.Ping, ServerType.Cluster)]
    [InlineData(InbuiltProbe.StringSet, ServerType.Cluster)]
    public async Task SelectByWeight(InbuiltProbe probe, ServerType serverType)
    {
        var healthCheck = new HealthCheck
        {
            Probe = probe switch
            {
                InbuiltProbe.IsConnected => HealthCheck.HealthCheckProbe.IsConnected,
                InbuiltProbe.Ping => HealthCheck.HealthCheckProbe.Ping,
                InbuiltProbe.StringSet => HealthCheck.HealthCheckProbe.StringSet,
                _ => throw new ArgumentOutOfRangeException(nameof(probe)),
            },
        };

        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(log, endpoint: germany) { ServerType = serverType };
        using var server1 = new InProcessTestServer(log, endpoint: canada) { ServerType = serverType };
        using var server2 = new InProcessTestServer(log, endpoint: tokyo) { ServerType = serverType };

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        var options = new MultiGroupOptions { HealthCheck = healthCheck };
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members, options);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);

        WriteLatency(conn);
    }

    private void WriteLatency(IConnectionGroup conn)
    {
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        foreach (var member in conn.GetMembers())
        {
            log.WriteLine($"{member.Name}: {member.Latency.TotalMilliseconds}us");
        }
        log.WriteLine($"Active: {typed.Active}");
    }

    [Fact]
    public async Task SelectByLatency()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(log, endpoint: germany);
        using var server1 = new InProcessTestServer(log, endpoint: canada);
        using var server2 = new InProcessTestServer(log, endpoint: tokyo);

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()),
            new(server1.GetClientConfig()),
            new(server2.GetClientConfig()),
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        conn.ConnectionChanged += (_, args) => log.WriteLine($"Connection changed: {args.Type}, from {args.PreviousGroup?.Name ?? "(nil)"} to {args.Group.Name}");

        Assert.True(conn.IsConnected);
        server0.SetLatency(TimeSpan.FromMilliseconds(10));
        server1.SetLatency(TimeSpan.Zero);
        server2.SetLatency(TimeSpan.FromMilliseconds(15));
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        typed.OnHeartbeat(); // update latencies
        await Task.Delay(100); // allow time to settle
        typed.SelectPreferredGroup();
        WriteLatency(typed);

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // change latency and update
        server0.SetLatency(TimeSpan.FromMilliseconds(10));
        server1.SetLatency(TimeSpan.FromMilliseconds(10));
        server2.SetLatency(TimeSpan.Zero);
        typed.OnHeartbeat(); // update latencies
        await Task.Delay(100); // allow time to settle
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        WriteLatency(typed);
        Assert.Equal(tokyo, ep);
    }

    [Fact]
    public async Task PubSubRouted()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(log, endpoint: germany);
        Assert.SkipUnless(server0.GetClientConfig().CommandMap.IsAvailable(RedisCommand.PUBLISH), "PUBLISH is not available");
        using var server1 = new InProcessTestServer(log, endpoint: canada);
        using var server2 = new InProcessTestServer(log, endpoint: tokyo);

        Capture capture = new(log);

        RedisChannel channel = RedisChannel.Literal("chan");
        var pub0 = (await server0.ConnectAsync()).GetSubscriber();
        await pub0.SubscribeAsync(channel, (x, y) => capture.Seen(nameof(pub0), x, y));
        var pub1 = (await server1.ConnectAsync()).GetSubscriber();
        await pub1.SubscribeAsync(channel, (x, y) => capture.Seen(nameof(pub1), x, y));
        var pub2 = (await server2.ConnectAsync()).GetSubscriber();
        await pub2.SubscribeAsync(channel, (x, y) => capture.Seen(nameof(pub2), x, y));

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var multi = conn.GetSubscriber();
        await multi.SubscribeAsync(channel, (x, y) => capture.Seen(nameof(conn), x, y));

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // now publish via all 4 options, see what happens
        capture.Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");

        // we're expecting just canada, so:
        await capture.AwaitAsync(multi, 6);
        capture.AssertTakeAny("[pub0] chan: abc");
        capture.AssertTakeAny("[pub1] chan: def");
        capture.AssertTakeAny("[pub2] chan: ghi");
        capture.AssertTakeAny("[conn] chan: def"); // receives the message from pub1
        capture.AssertTakeAny("[conn] chan: jkl"); // receives the message from itself
        capture.AssertTakeAny("[pub1] chan: jkl"); // received the message from the multi-group

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);

        capture.Reset();
        log.WriteLine("Publishing...");
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");

        // now we're expecting just tokyo, so:
        await capture.AwaitAsync(multi, 6);
        capture.AssertTakeAny("[pub0] chan: abc");
        capture.AssertTakeAny("[pub1] chan: def");
        capture.AssertTakeAny("[pub2] chan: ghi");
        capture.AssertTakeAny("[conn] chan: ghi"); // receives the message from pub2
        capture.AssertTakeAny("[conn] chan: jkl"); // receives the message from itself
        capture.AssertTakeAny("[pub2] chan: jkl"); // received the message from the multi-group
    }

    [Fact]
    public async Task PubSubOrderedRouted()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(log, endpoint: germany);
        Assert.SkipUnless(
            server0.GetClientConfig().CommandMap.IsAvailable(RedisCommand.PUBLISH),
            "PUBLISH is not available");
        using var server1 = new InProcessTestServer(log, endpoint: canada);
        using var server2 = new InProcessTestServer(log, endpoint: tokyo);

        Capture capture = new(log);

        RedisChannel channel = RedisChannel.Literal("chan");
        var pub0 = (await server0.ConnectAsync()).GetSubscriber();
        _ = capture.WriteSeen(nameof(pub0), await pub0.SubscribeAsync(channel));
        var pub1 = (await server1.ConnectAsync()).GetSubscriber();
        _ = capture.WriteSeen(nameof(pub1), await pub1.SubscribeAsync(channel));
        var pub2 = (await server2.ConnectAsync()).GetSubscriber();
        _ = capture.WriteSeen(nameof(pub2), await pub2.SubscribeAsync(channel));

        ConnectionGroupMember[] members =
        [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var multi = conn.GetSubscriber();
        _ = capture.WriteSeen(nameof(conn), await multi.SubscribeAsync(channel));

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // now publish via all 4 options, see what happens
        capture.Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        for (int i = 0; i < 5; i++)
        {
            await multi.PublishAsync(channel, $"jkl{i}");
        }

        // we're expecting just canada, so:
        await capture.AwaitAsync(multi, 14);
        capture.AssertTakeAny("[pub0] chan: abc");
        capture.AssertTakeAny("[pub1] chan: def");
        capture.AssertTakeAny("[pub2] chan: ghi");
        for (int i = 0; i < 5; i++)
        {
            capture.AssertTakeAny($"[pub1] chan: jkl{i}"); // received the message from the multi-group
        }
        // these should be ordered
        capture.AssertTakeFirst("[conn] chan: def"); // receives the message from pub1
        for (int i = 0; i < 5; i++)
        {
            capture.AssertTakeFirst($"[conn] chan: jkl{i}"); // receives the message from itself
        }

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);

        capture.Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        for (int i = 0; i < 5; i++)
        {
            await multi.PublishAsync(channel, $"jkl{i}");
        }

        // now we're expecting just tokyo, so:
        await capture.AwaitAsync(multi, 14);
        capture.AssertTakeAny("[pub0] chan: abc");
        capture.AssertTakeAny("[pub1] chan: def");
        capture.AssertTakeAny("[pub2] chan: ghi");
        for (int i = 0; i < 5; i++)
        {
            capture.AssertTakeAny($"[pub2] chan: jkl{i}"); // received the message from the multi-group
        }

        // these should be ordered
        capture.AssertTakeFirst("[conn] chan: ghi"); // receives the message from pub2
        for (int i = 0; i < 5; i++)
        {
            capture.AssertTakeFirst($"[conn] chan: jkl{i}"); // receives the message from itself
        }
    }
}
