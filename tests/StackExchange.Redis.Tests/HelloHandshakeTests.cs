using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// We issue <c>HELLO</c> whenever we expect the server to understand it, even when staying on RESP2:
/// the reply tells us the version/role/mode without needing <c>INFO</c> or <c>CONFIG</c>, both of
/// which are <c>@dangerous</c> and commonly restricted by ACLs; see issue #2968.
/// </summary>
public class HelloHandshakeTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(RedisProtocol.Resp2, "2")]
    [InlineData(RedisProtocol.Resp3, "3")]
    public async Task HelloIsIssuedForBothProtocols(RedisProtocol protocol, string expectedProtover)
    {
        using var server = new RecordingServer(log);
        var config = server.GetClientConfig();
        config.Protocol = protocol;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        var hellos = server.Recorded("HELLO");
        Assert.NotEmpty(hellos);
        Assert.All(hellos, args => Assert.Equal(expectedProtover, args.FirstOrDefault()));
        Assert.Equal(protocol, conn.GetServerSnapshot()[0].Protocol);
    }

    [Fact]
    public async Task NoHelloWhenDisabledViaCommandMap()
    {
        using var server = new RecordingServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        config.CommandMap = server.CreateCommandMap(except: "HELLO");

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Empty(server.Recorded("HELLO"));
        Assert.Equal(RedisProtocol.Resp2, conn.GetServerSnapshot()[0].Protocol);
    }

    [Fact]
    public async Task NoHelloWhenServerIsAssumedDownLevel()
    {
        using var server = new RecordingServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        config.DefaultVersion = new Version(5, 0, 0); // HELLO arrived in 6.0

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Empty(server.Recorded("HELLO"));
        Assert.Equal(RedisProtocol.Resp2, conn.GetServerSnapshot()[0].Protocol);
    }

    /// <summary>
    /// The point of issue #2968: with <c>INFO</c> and <c>CONFIG</c> unavailable we used to fall back to
    /// <c>SET {random-guid} replica-read-only PX 1 NX</c> to detect a read-only replica - a key write that
    /// cannot be allow-listed in an ACL. <c>HELLO</c> reports the role, so the probe isn't needed.
    /// </summary>
    [Theory]
    [InlineData(RedisProtocol.Resp2)]
    [InlineData(RedisProtocol.Resp3)]
    public async Task NoReplicaProbeWhenHelloTellsUsTheRole(RedisProtocol protocol)
    {
        using var server = new RecordingServer(log);
        var config = server.GetClientConfig();
        config.Protocol = protocol;
        config.CommandMap = server.CreateCommandMap(except: ["INFO", "CONFIG"]);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.NotEmpty(server.Recorded("HELLO"));
        Assert.Empty(server.Recorded("SET"));
        Assert.False(conn.GetServerSnapshot()[0].IsReplica); // from HELLO's "role"
    }

    /// <summary>
    /// The converse of <see cref="NoReplicaProbeWhenHelloTellsUsTheRole"/>: when HELLO isn't available
    /// either, the key-based probe is still the only signal we have, so it must still be issued.
    /// </summary>
    [Fact]
    public async Task ReplicaProbeStillUsedWhenHelloUnavailable()
    {
        using var server = new RecordingServer(log);
        var config = server.GetClientConfig();
        config.Protocol = RedisProtocol.Resp2;
        config.CommandMap = server.CreateCommandMap(except: ["INFO", "CONFIG", "HELLO"]);

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);

        Assert.Empty(server.Recorded("HELLO"));
        var sets = server.Recorded("SET");
        Assert.NotEmpty(sets);
        Assert.All(sets, args => Assert.Equal("replica-read-only", args.Skip(1).FirstOrDefault()));
    }

    private sealed class RecordingServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        private readonly ConcurrentQueue<(string Command, string[] Args)> _commands = new();

        public override TypedRedisValue Execute(RedisClient client, in RedisRequest request)
        {
            var args = new string[Math.Max(request.Count - 1, 0)];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = request.GetString(i + 1);
            }
            _commands.Enqueue((request.GetString(0).ToUpperInvariant(), args));
            return base.Execute(client, in request);
        }

        public List<string[]> Recorded(string command)
            => _commands.Where(x => x.Command == command).Select(x => x.Args).ToList();

        public CommandMap CreateCommandMap(params string[] except)
        {
            var commands = GetCommands();
            foreach (var command in except)
            {
                commands.Remove(command);
            }
            return CommandMap.Create(commands);
        }
    }
}
