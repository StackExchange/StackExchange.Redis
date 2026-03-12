using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis.Tests.Helpers;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

public class BasicMultiGroupTests(ITestOutputHelper log)
{
    protected TextWriter Log { get; } = new TextWriterOutputHelper(log);

    [Fact]
    public async Task SelectByWeight()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(endpoint: germany);
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
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

        using var server0 = new InProcessTestServer(endpoint: germany);
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

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

        using var server0 = new InProcessTestServer(endpoint: germany);
        Assert.SkipUnless(server0.GetClientConfig().CommandMap.IsAvailable(RedisCommand.PUBLISH), "PUBLISH is not available");
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

        HashSet<string> seen = [];

        void Seen(string source, RedisChannel channel, RedisValue value)
        {
            string message = $"[{source}] {channel}: {value}";
            lock (seen)
            {
                seen.Add(message);
            }
        }

        void Reset()
        {
            lock (seen)
            {
                seen.Clear();
            }
        }
        RedisChannel channel = RedisChannel.Literal("chan");
        var pub0 = (await server0.ConnectAsync()).GetSubscriber();
        await pub0.SubscribeAsync(channel, (x, y) => Seen(nameof(pub0), x, y));
        var pub1 = (await server1.ConnectAsync()).GetSubscriber();
        await pub1.SubscribeAsync(channel, (x, y) => Seen(nameof(pub1), x, y));
        var pub2 = (await server2.ConnectAsync()).GetSubscriber();
        await pub2.SubscribeAsync(channel, (x, y) => Seen(nameof(pub2), x, y));

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var multi = conn.GetSubscriber();
        await multi.SubscribeAsync(channel, (x, y) => Seen(nameof(conn), x, y));

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // now publish via all 4 options, see what happens
        Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");
        await multi.PingAsync();

        // we're expecting just canada, so:
        Assert.Equal(6, seen.Count);
        Assert.Contains("[pub0] chan: abc", seen);
        Assert.Contains("[pub1] chan: def", seen);
        Assert.Contains("[pub2] chan: ghi", seen);
        Assert.Contains("[conn] chan: def", seen); // receives the message from pub1
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from itself
        Assert.Contains("[pub1] chan: jkl", seen); // received the message from the multi-group

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);

        Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");
        await multi.PingAsync();

        // now we're expecting just tokyo, so:
        Assert.Equal(6, seen.Count);
        Assert.Contains("[pub0] chan: abc", seen);
        Assert.Contains("[pub1] chan: def", seen);
        Assert.Contains("[pub2] chan: ghi", seen);
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from pub2
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from itself
        Assert.Contains("[pub2] chan: jkl", seen); // received the message from the multi-group
    }

    [Fact]
    public async Task PubSubOrderedRouted()
    {
        EndPoint germany = new DnsEndPoint("germany", 6379);
        EndPoint canada = new DnsEndPoint("canada", 6379);
        EndPoint tokyo = new DnsEndPoint("tokyo", 6379);

        using var server0 = new InProcessTestServer(endpoint: germany);
        Assert.SkipUnless(server0.GetClientConfig().CommandMap.IsAvailable(RedisCommand.PUBLISH), "PUBLISH is not available");
        using var server1 = new InProcessTestServer(endpoint: canada);
        using var server2 = new InProcessTestServer(endpoint: tokyo);

        HashSet<string> seen = [];

        void Seen(string source, RedisChannel channel, RedisValue value)
        {
            string message = $"[{source}] {channel}: {value}";
            lock (seen)
            {
                seen.Add(message);
            }
        }

        void Reset()
        {
            lock (seen)
            {
                seen.Clear();
            }
        }

        void WriteSeen(string source, ChannelMessageQueue queue)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var msg in queue)
                {
                    Seen(source, msg.Channel, msg.Message);
                }
            });
        }

        RedisChannel channel = RedisChannel.Literal("chan");
        var pub0 = (await server0.ConnectAsync()).GetSubscriber();
        WriteSeen(nameof(pub0), await pub0.SubscribeAsync(channel));
        var pub1 = (await server1.ConnectAsync()).GetSubscriber();
        WriteSeen(nameof(pub1), await pub1.SubscribeAsync(channel));
        var pub2 = (await server2.ConnectAsync()).GetSubscriber();
        WriteSeen(nameof(pub2), await pub2.SubscribeAsync(channel));

        ConnectionGroupMember[] members = [
            new(server0.GetClientConfig()) { Weight = 2 },
            new(server1.GetClientConfig()) { Weight = 9 },
            new(server2.GetClientConfig()) { Weight = 3 },
        ];
        await using var conn = await ConnectionMultiplexer.ConnectGroupAsync(members);
        Assert.True(conn.IsConnected);
        var typed = Assert.IsType<MultiGroupMultiplexer>(conn);
        var multi = conn.GetSubscriber();
        WriteSeen(nameof(conn), await multi.SubscribeAsync(channel));

        // (R.4.1) If multiple member databases are configured, then I want to failover to the one with the highest weight.
        var db = conn.GetDatabase();
        var ep = await db.IdentifyEndpointAsync();
        Assert.Equal(canada, ep);

        // now publish via all 4 options, see what happens
        Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");
        await multi.PingAsync();

        // we're expecting just canada, so:
        Assert.Equal(6, seen.Count);
        Assert.Contains("[pub0] chan: abc", seen);
        Assert.Contains("[pub1] chan: def", seen);
        Assert.Contains("[pub2] chan: ghi", seen);
        Assert.Contains("[conn] chan: def", seen); // receives the message from pub1
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from itself
        Assert.Contains("[pub1] chan: jkl", seen); // received the message from the multi-group

        // change weight and update
        members[1].Weight = 1;
        typed.SelectPreferredGroup();
        ep = await db.IdentifyEndpointAsync();
        Assert.Equal(tokyo, ep);

        Reset();
        await pub0.PublishAsync(channel, "abc");
        await pub1.PublishAsync(channel, "def");
        await pub2.PublishAsync(channel, "ghi");
        await multi.PublishAsync(channel, "jkl");
        await multi.PingAsync();

        // now we're expecting just tokyo, so:
        Assert.Equal(6, seen.Count);
        Assert.Contains("[pub0] chan: abc", seen);
        Assert.Contains("[pub1] chan: def", seen);
        Assert.Contains("[pub2] chan: ghi", seen);
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from pub2
        Assert.Contains("[conn] chan: jkl", seen); // receives the message from itself
        Assert.Contains("[pub2] chan: jkl", seen); // received the message from the multi-group
    }
}
