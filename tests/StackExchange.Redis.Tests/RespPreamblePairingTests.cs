using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Operations;
using StackExchange.Redis.Protocol;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Writing a preamble and its request as one contiguous pair, on the new core.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design notes 7s recorded this as unreachable</b>, because <c>IRespPreambleGate</c> took a
/// <c>PhysicalConnection</c> - an old-core type - so nothing on the new core could implement the
/// capability, and <c>AwaitPair</c> fell back to sending the two in sequence. That cost a round trip and,
/// worse, skipped the gate entirely, so an <c>EVALSHA</c> carried a <c>SCRIPT LOAD</c> it did not need on
/// every single call.
/// </para>
/// <para>
/// It later turned out to be a correctness question too: injecting a <c>SELECT</c> before a command for
/// another database is the same capability, which is what would let one connection serve several
/// databases again (see 7x).
/// </para>
/// </remarks>
public class RespPreamblePairingTests
{
    private sealed class FakeTransport : DuplexTransport
    {
        private readonly object _sync = new();
        private byte[] _out = new byte[4096];
        private int _length;
        private TransportReceiver? _receiver;

        internal int Flushes;

        internal string Written
        {
            get { lock (_sync) return Encoding.UTF8.GetString(_out, 0, _length).Replace("\r\n", "|"); }
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            lock (_sync)
            {
                if (_length + Math.Max(sizeHint, 1) > _out.Length) Array.Resize(ref _out, _length + sizeHint + 4096);
                return _out.AsMemory(_length);
            }
        }

        public override void Advance(int count)
        {
            lock (_sync) _length += count;
        }

        public override bool Flush()
        {
            Interlocked.Increment(ref Flushes);
            return true;
        }

        public override void Start(TransportReceiver receiver) => _receiver = receiver;

        internal void Reply(string text) => _receiver!.OnReceived(Encoding.UTF8.GetBytes(text));

        public override ValueTask DisposeAsync() => default;
    }

    private static async Task<(RespEndpointExecutor Executor, FakeTransport Transport, RespDatabaseContext Context)> ConnectedAsync()
    {
        var transport = new FakeTransport();
        var executor = new RespEndpointExecutor(
            _ => Task.FromResult<RespConnection>(new RespClientConnection(transport, static (in RespRedirect _, RespPayloadOperation _) => false)),
            endpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379));

        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        // force the connection up, so later assertions see only what the pair wrote
        var warming = context.Strings.GetAsync("warm");
        await WaitFor(() => transport.Written.Length > 0);
        transport.Reply("$4\r\nwarm\r\n");
        await warming;
        return (executor, transport, context);
    }

    [Fact]
    public async Task ThePairIsOneContiguousWrite()
    {
        var (executor, transport, context) = await ConnectedAsync();
        Assert.True(executor.CanWritePreamble, "the endpoint executor owns a connection, so it can pair");

        var before = transport.Written;
        var flushes = transport.Flushes;

        _ = context.Scripts.EvaluateAsync("return 1", default, default);
        await WaitFor(() => transport.Written.Length > before.Length);

        // one flush, and nothing between them: another sender taking the write lock in the middle would
        // put its command between the LOAD and the EVALSHA, which is the whole point of pairing
        Assert.Equal(1, transport.Flushes - flushes);
        Assert.Equal(
            "*3|$6|SCRIPT|$4|LOAD|$8|return 1|*3|$7|EVALSHA|$40|e0e1f9fabfc9d4800c877a703b823ac0578ff8db|$1|0|",
            transport.Written.Substring(before.Length));
    }

    /// <summary>
    /// A gate whose belief is <b>connection-scoped</b>: the first send claims, later sends on that same
    /// connection see the claim and collapse the pair to a single command.
    /// </summary>
    /// <remarks>
    /// Deliberately not the script gate, which is endpoint-scoped and answers "send it" whenever there is no
    /// <c>ServerEndPoint</c> to ask - correct, but it means a connection-level fake can never observe the
    /// collapse. <c>SELECT</c> is the case this shape is really for: which database a connection sits on is
    /// a fact about that socket and nothing else, and it is what would let one connection serve several
    /// databases again.
    /// </remarks>
    [Fact]
    public async Task AGateThatSaysNoTurnsThePairIntoOneCommand()
    {
        var (_, transport, context) = await ConnectedAsync();
        var gate = new ClaimingGate();

        for (var i = 0; i < 2; i++)
        {
            var before = transport.Written;
            var preamble = context.Raw.Render($"{RedisCommand.SELECT}{(RedisValue)3}");
            var request = context.Raw.Render($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
            ValueTask<bool> pending;
            try
            {
                pending = context.Raw.SendWithPreambleAsync(
                    ref preamble, ref request, CommandFlags.None, RespHandlers.Boolean, gate);
            }
            finally
            {
                preamble.Dispose();
                request.Dispose();
            }

            await WaitFor(() => transport.Written.Length > before.Length);
            var sent = transport.Written.Substring(before.Length);

            if (i == 0)
            {
                Assert.Equal("*2|$6|SELECT|$1|3|*3|$3|SET|$1|k|$1|v|", sent);
                transport.Reply("+OK\r\n+OK\r\n"); // the SELECT's reply, then the SET's
            }
            else
            {
                // the whole point: no SELECT at all, because this connection is already on database 3
                Assert.Equal("*3|$3|SET|$1|k|$1|v|", sent);
                transport.Reply("+OK\r\n");
            }

            Assert.True(await pending);
        }

        Assert.Equal(1, gate.Claims);
    }

    private sealed class ClaimingGate : IRespPreambleGate
    {
        private const long Id = 12345;
        internal int Claims;

        // claim inside the write, not on the reply: everything issued before the first reply lands would
        // otherwise still read as "not yet done" and each would carry its own preamble. See the field-set
        // probe, which measured exactly that.
        public bool IsNeeded(IRespPreambleTarget connection)
        {
            if (!connection.TryClaim(Id)) return false;
            Interlocked.Increment(ref Claims);
            return true;
        }

        public void OnEstablished(IRespPreambleTarget connection) { }
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
