using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Following a redirect rather than reporting it. See design notes 7h: the re-issue happens on the IO
/// loop precisely so that commands redirected together keep their order.
/// </summary>
public class RespRedirectFollowingTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal string Written
        {
            get { lock (_sync) return Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|"); }
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            lock (_sync)
            {
                if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 1024);
                return _out.AsMemory(_length);
            }
        }

        public override void Advance(int count)
        {
            lock (_sync) _length += count;
        }

        public override bool Flush() => true;

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        public override ValueTask DisposeAsync() => default;
    }

    /// <summary>One endpoint: a transport, a redirect-aware connection, and an executor over it.</summary>
    private sealed class Node
    {
        internal readonly FakeTransport Transport = new();

        internal RespEndpointExecutor Executor = null!;

        internal string Written => Transport.Written;

        internal static Node Create(RespRedirectRouter router, EndPoint endpoint)
        {
            var node = new Node();
            node.Executor = new RespEndpointExecutor(
                _ => Task.FromResult<RespConnection>(new RespRedirectingConnection(node.Transport, router)),
                endpoint: endpoint);
            return node;
        }
    }

    [Fact]
    public async Task AMovedIsFollowedAndTheAnswerComesFromTheNewNode()
    {
        var owner = new IPEndPoint(IPAddress.Loopback, 7001);
        RespMultiplexerExecutor muxer = null!;
        var moves = new List<(int Slot, EndPoint Endpoint)>();

        var wrongNode = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), new IPEndPoint(IPAddress.Loopback, 7000));
        var rightNode = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), owner);

        var topology = new RespTopology(RespClusterState.Yes);
        muxer = new RespMultiplexerExecutor(
            topology,
            (_, _, _) => wrongNode.Executor,           // our map is stale: it says "the wrong node"
            (_, _) => wrongNode.Executor,
            forEndpoint: ep => Equals(ep, owner) ? rightNode.Executor : null,
            onSlotMoved: (slot, ep) => moves.Add((slot, ep)));

        var context = new RespDatabaseContext(new RespContext(serverType: ServerType.Cluster).WithTopology(topology).WithExecutor(muxer));

        var pending = context.Strings.GetAsync("user:1");
        await WaitFor(() => wrongNode.Written.Length > 0);

        // the wrong node says where it went; the right node then answers for real
        wrongNode.Transport.Reply($"-MOVED {ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1")} 127.0.0.1:7001\r\n");
        await WaitFor(() => rightNode.Written.Length > 0);
        rightNode.Transport.Reply("$4\r\nmine\r\n");

        Assert.Equal("mine", (string?)await pending);

        // the command really was re-issued, not re-rendered by the caller
        Assert.Equal("*2|$3|GET|$6|user:1|", rightNode.Written);

        // and MOVED updated the map, which is the half that distinguishes it from ASK
        var move = Assert.Single(moves);
        Assert.Equal(owner, move.Endpoint);
    }

    [Fact]
    public async Task AnAskDoesNotUpdateTheMapAndSendsASKINGFirst()
    {
        var owner = new IPEndPoint(IPAddress.Loopback, 7001);
        RespMultiplexerExecutor muxer = null!;
        var moves = 0;

        var wrongNode = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), new IPEndPoint(IPAddress.Loopback, 7000));
        var rightNode = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), owner);

        var topology = new RespTopology(RespClusterState.Yes);
        muxer = new RespMultiplexerExecutor(
            topology,
            (_, _, _) => wrongNode.Executor,
            (_, _) => wrongNode.Executor,
            forEndpoint: ep => Equals(ep, owner) ? rightNode.Executor : null,
            onSlotMoved: (_, _) => moves++);

        var context = new RespDatabaseContext(new RespContext(serverType: ServerType.Cluster).WithTopology(topology).WithExecutor(muxer));

        var pending = context.Strings.GetAsync("user:1");
        await WaitFor(() => wrongNode.Written.Length > 0);

        // ASKING needs a LIVE connection - it is declined rather than backlogged, because a backlog
        // drains one at a time and would lose the adjacency that is its whole point. So warm the target
        // with an ordinary command first, exactly as real traffic would have.
        var warm = new RespDatabaseContext(new RespContext().WithExecutor(rightNode.Executor));
        var warming = warm.Strings.GetAsync("warm");
        await WaitFor(() => rightNode.Written.Length > 0);
        rightNode.Transport.Reply("$4\r\nwarm\r\n");
        await warming;
        Assert.True(rightNode.Executor.IsConnectedNow);
        var alreadyWritten = rightNode.Written.Length;

        wrongNode.Transport.Reply($"-ASK {ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1")} 127.0.0.1:7001\r\n");
        await WaitFor(() => rightNode.Written.Length > alreadyWritten
            && rightNode.Written.Contains("ASKING", StringComparison.Ordinal));

        // ASKING immediately precedes the command, with nothing between: the server applies it to the
        // very next command on the connection
        Assert.Contains("*1|$6|ASKING|*2|$3|GET|$6|user:1|", rightNode.Written);

        rightNode.Transport.Reply("+OK\r\n$4\r\nmine\r\n");
        Assert.Equal("mine", (string?)await pending);

        Assert.Equal(0, moves); // ASK says THIS KEY is migrating, not that the slot has moved
    }

    [Fact]
    public async Task ASecondRedirectIsNotFollowed()
    {
        // once is enough: a second redirect for the same command is pathological - two nodes that
        // disagree, or a topology changing faster than commands complete - so it short-circuits and the
        // server's own error is what the caller sees
        var owner = new IPEndPoint(IPAddress.Loopback, 7001);
        RespMultiplexerExecutor muxer = null!;

        var first = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), new IPEndPoint(IPAddress.Loopback, 7000));
        var second = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), owner);

        var topology = new RespTopology(RespClusterState.Yes);
        muxer = new RespMultiplexerExecutor(
            topology, (_, _, _) => first.Executor, (_, _) => first.Executor,
            forEndpoint: ep => Equals(ep, owner) ? second.Executor : first.Executor);

        var context = new RespDatabaseContext(new RespContext(serverType: ServerType.Cluster).WithTopology(topology).WithExecutor(muxer));

        var pending = context.Strings.GetAsync("user:1");
        await WaitFor(() => first.Written.Length > 0);

        first.Transport.Reply("-MOVED 1 127.0.0.1:7001\r\n");
        await WaitFor(() => second.Written.Length > 0);

        // the second node redirects it straight back; this one is NOT followed
        second.Transport.Reply("-MOVED 1 127.0.0.1:7000\r\n");

        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await pending);
        Assert.StartsWith("MOVED", ex.Message);
    }

    [Fact]
    public async Task AnUnroutableRedirectAsksForATopologyRefreshAndStillFails()
    {
        // "?" means the server does not know where the slot went either; there is nowhere to send this,
        // so the error stands - but it IS a signal that our view of the cluster is wrong
        RespMultiplexerExecutor muxer = null!;
        var suspect = 0;

        var node = Node.Create((in RespRedirect r, RespPayloadOperation op) => muxer.TryFollowRedirect(in r, op), new IPEndPoint(IPAddress.Loopback, 7000));

        var topology = new RespTopology(RespClusterState.Yes);
        muxer = new RespMultiplexerExecutor(
            topology, (_, _, _) => node.Executor, (_, _) => node.Executor,
            forEndpoint: _ => null,
            onTopologySuspect: () => suspect++);

        var context = new RespDatabaseContext(new RespContext(serverType: ServerType.Cluster).WithTopology(topology).WithExecutor(muxer));

        var pending = context.Strings.GetAsync("user:1");
        await WaitFor(() => node.Written.Length > 0);

        node.Transport.Reply("-MOVED 1 ?:6379\r\n");

        var ex = await Assert.ThrowsAsync<RedisServerException>(async () => await pending);
        Assert.StartsWith("MOVED", ex.Message);
        Assert.Equal(1, suspect);
    }

    private static async Task WaitFor(Func<bool> condition, int millis = 5000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > millis) Assert.Fail("condition not met within the time allowed");
            await Task.Delay(5);
        }
    }
}
