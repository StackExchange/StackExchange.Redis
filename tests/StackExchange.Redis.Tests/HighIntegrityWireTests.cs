using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// High-integrity mode on the wire: the <c>ECHO</c> check behind each command, where it is and is not written,
/// and what a failed check does. The connection primitive itself is covered in <c>RESPite.Tests.RespSequenceCheckTests</c>.
/// </summary>
public class HighIntegrityWireTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[1024];
        private int _length;
        private TransportReceiver? _receiver;

        internal byte[] Written
        {
            get { lock (_sync) return _out.AsSpan(0, _length).ToArray(); }
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

        internal void Reply(byte[] bytes) => _receiver!.OnReceived(bytes);

        public override ValueTask DisposeAsync() => default;
    }

    private static readonly byte[] EchoHead = Encoding.ASCII.GetBytes("*2\r\n$4\r\nECHO\r\n$4\r\n");

    private static byte[] CheckReply(uint token)
    {
        var reply = new byte[10];
        "$4\r\n"u8.CopyTo(reply);
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(4), token);
        "\r\n"u8.CopyTo(reply.AsSpan(8));
        return reply;
    }

    private static byte[] Check(uint token)
    {
        var frame = new byte[EchoHead.Length + 6];
        EchoHead.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(EchoHead.Length), token);
        "\r\n"u8.CopyTo(frame.AsSpan(EchoHead.Length + 4));
        return frame;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts) length += part.Length;
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static async Task<(RespEndpointExecutor Endpoint, FakeTransport Transport)> ConnectedAsync()
    {
        var transport = new FakeTransport();
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)
            {
                SequenceCheckHead = EchoHead, // what the connection manager does for an interactive connection under HighIntegrity
            }),
            endpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379));

        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));
        var warming = context.Strings.GetAsync("warm");
        await WaitFor(() => transport.Written.Length > 0);
        transport.Reply("$4\r\nwarm\r\n");
        transport.Reply(CheckReply(1));
        await warming;
        return (executor, transport);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition());
    }

    [Fact]
    public async Task ACommandIsFollowedByItsCheckAndCompletesOnlyWhenItValidates()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var context = new RespDatabaseContext(new RespContext().WithExecutor(endpoint));

        var pending = context.Strings.GetAsync("a");
        var written = transport.Written.AsSpan(before).ToArray();
        Assert.Equal(Concat(Ascii("*2\r\n$3\r\nGET\r\n$1\r\na\r\n"), Check(2)), written);

        transport.Reply("$1\r\nA\r\n");
        Assert.False(pending.IsCompleted);
        transport.Reply(CheckReply(2));
        Assert.Equal("A", (string?)await pending);
        Assert.Equal(2, endpoint.ConnectionForDiagnostics!.SequenceChecksPassed);
    }

    [Fact]
    public async Task ATransactionChecksOnlyItsExec()
    {
        // MULTI opens the span and the queued commands are answered +QUEUED, so a check inside would be queued
        // too; EXEC closes the span and its check validates all of it - the v3 bridge's rule
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var tran = new RespTransactionExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(tran));

        var get = context.Strings.GetAsync("a");
        var incr = context.Strings.IncrementAsync("b");
        var executing = tran.ExecuteAsync();

        var written = transport.Written.AsSpan(before).ToArray();
        Assert.Equal(
            Concat(Ascii("*1\r\n$5\r\nMULTI\r\n*2\r\n$3\r\nGET\r\n$1\r\na\r\n*2\r\n$4\r\nINCR\r\n$1\r\nb\r\n*1\r\n$4\r\nEXEC\r\n"), Check(2)),
            written);

        transport.Reply("+OK\r\n+QUEUED\r\n+QUEUED\r\n*2\r\n$1\r\nA\r\n:7\r\n");
        Assert.False(executing.IsCompleted); // EXEC's array is held until its check lands
        transport.Reply(CheckReply(2));
        Assert.True(await executing);
        Assert.Equal("A", (string?)await get);
        Assert.Equal(7, await incr);
    }

    [Fact]
    public async Task AFailedCheckFailsTheCommandWithResponseIntegrityFailureAndClosesTheConnection()
    {
        var (endpoint, transport) = await ConnectedAsync();
        var context = new RespDatabaseContext(new RespContext().WithExecutor(endpoint));
        var connection = endpoint.ConnectionForDiagnostics!;

        var first = context.Strings.GetAsync("a");
        var second = context.Strings.GetAsync("b");
        transport.Reply("$1\r\nA\r\n");
        transport.Reply(CheckReply(9)); // the stream is not what we think it is

        var ex = await Assert.ThrowsAsync<RedisConnectionException>(async () => await first);
        Assert.Equal(ConnectionFailureType.ResponseIntegrityFailure, ex.FailureType);
        Assert.Contains("High-integrity mode detected possible protocol de-sync", ex.Message);
        Assert.Contains("expected sequence check 2", ex.Message);

        // the rest of the stream is untrusted too: the connection is gone, and what was pending fails with it
        Assert.True(connection.IsClosed);
        var other = await Assert.ThrowsAsync<RedisConnectionException>(async () => await second);
        Assert.Equal(ConnectionFailureType.ResponseIntegrityFailure, other.FailureType);
    }

    [Fact]
    public async Task ABatchChecksEveryMember()
    {
        // a batch is a run of ordinary commands: each has its own reply, so each has its own check
        var (endpoint, transport) = await ConnectedAsync();
        var before = transport.Written.Length;
        var batch = new RespOperationBatchExecutor(endpoint);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(batch));

        var a = context.Strings.GetAsync("a");
        var b = context.Strings.GetAsync("b");
        await batch.ExecuteAsync();

        var written = transport.Written.AsSpan(before).ToArray();
        Assert.Equal(
            Concat(Ascii("*2\r\n$3\r\nGET\r\n$1\r\na\r\n"), Check(2), Ascii("*2\r\n$3\r\nGET\r\n$1\r\nb\r\n"), Check(3)),
            written);

        transport.Reply("$1\r\nA\r\n");
        transport.Reply(CheckReply(2));
        transport.Reply("$1\r\nB\r\n");
        transport.Reply(CheckReply(3));
        Assert.Equal("A", (string?)await a);
        Assert.Equal("B", (string?)await b);
    }
}
