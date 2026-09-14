using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A minimal RESP3 executor on its own connection, with <c>CLIENT TRACKING</c> enabled, that feeds
/// invalidation pushes straight into a <see cref="RespClientCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// The point is to prove the <b>delivery</b> path - that a real server's invalidation actually reaches
/// <c>OnInvalidate</c>, with key bytes that match what the cache recorded. Everything else about the cache
/// was already tested against fakes; this is the half that was resting on reasoning.
/// </para>
/// <para>
/// Deliberately NOT production plumbing. It owns one socket, has no reconnect, no pipelining beyond a FIFO
/// of pending replies, and no failover. Routing pushes through the real <c>PhysicalConnection</c> is
/// separate work - and doing it here first means that work starts from a known-good target rather than a
/// guess about the wire format. See design notes 6.13.
/// </para>
/// <para>
/// Mode is RESP3 + <c>BCAST</c>, per the decision in 6.13: invalidations arrive in-band on the same
/// connection, so there is no redirect race, and broadcasting needs no server-side per-client memory.
/// </para>
/// </remarks>
internal sealed class TrackingExecutor : IRespExecutor, IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly RespClientCache _cache;
    private readonly ConcurrentQueue<TaskCompletionSource<byte[]>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();

    private int _invalidations, _flushes, _keysInvalidated, _deliveries;

    /// <summary>Invalidation pushes received.</summary>
    internal int Invalidations => Volatile.Read(ref _invalidations);

    /// <summary>Keys named across all those pushes; a single push can carry several.</summary>
    internal int KeysInvalidated => Volatile.Read(ref _keysInvalidated);

    /// <summary>Flush pushes received (the null payload).</summary>
    internal int Flushes => Volatile.Read(ref _flushes);

    /// <summary>Pub/sub deliveries recognised as such, and therefore NOT mistaken for invalidations.</summary>
    internal int Deliveries => Volatile.Read(ref _deliveries);

    public int Database => 0;

    private TrackingExecutor(Socket socket, RespClientCache cache)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _cache = cache;
    }

    internal static async Task<TrackingExecutor> ConnectAsync(string host, int port, RespClientCache cache, params string[] prefixes)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port).ConfigureAwait(false);

        var executor = new TrackingExecutor(socket, cache);
        _ = Task.Run(executor.ReadLoopAsync);

        await executor.CommandAsync("HELLO", "3").ConfigureAwait(false);

        var tracking = new string[3 + (prefixes.Length * 2)];
        tracking[0] = "CLIENT";
        tracking[1] = "TRACKING";
        tracking[2] = "on";
        var next = 3;
        foreach (var prefix in prefixes)
        {
            tracking[next++] = "PREFIX";
            tracking[next++] = prefix;
        }

        // BCAST goes last so it follows any PREFIX arguments, which is how the server documents it
        var args = new string[tracking.Length + 1];
        Array.Copy(tracking, args, tracking.Length);
        args[args.Length - 1] = "BCAST";

        var reply = await executor.CommandAsync(args).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(reply);
        if (!text.StartsWith("+OK", StringComparison.Ordinal))
        {
            executor.Dispose();
            throw new InvalidOperationException("CLIENT TRACKING refused: " + text.Trim());
        }

        return executor;
    }

    /// <summary>Send an ad-hoc command, for setup and for the test's own writes.</summary>
    internal async Task<byte[]> CommandAsync(params string[] parts)
    {
        var payload = new StringBuilder().Append('*').Append(parts.Length).Append("\r\n");
        foreach (var part in parts)
        {
            payload.Append('$').Append(Encoding.UTF8.GetByteCount(part)).Append("\r\n").Append(part).Append("\r\n");
        }

        return await SendRawAsync(Encoding.UTF8.GetBytes(payload.ToString())).ConfigureAwait(false);
    }

    private async Task<byte[]> SendRawAsync(byte[] frame)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        // enqueue BEFORE writing: the reply can arrive before the write call returns
        _pending.Enqueue(completion);
        await _stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
        await _stream.FlushAsync().ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    public RespPayload Send(in RespRequest request)
        => throw new NotSupportedException("This harness is async-only.");

    public async ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
    {
        var reply = await SendRawAsync(request.Span.ToArray()).ConfigureAwait(false);
        return RespPayload.Create(reply);
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        var have = 0;

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (have == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);

                var read = await _stream.ReadAsync(buffer, have, buffer.Length - have).ConfigureAwait(false);
                if (read <= 0) break;
                have += read;

                // drain every complete frame currently buffered, then compact what is left
                var consumed = 0;
                while (consumed < have)
                {
                    var state = default(RespScanState);
                    if (!state.TryRead(buffer.AsSpan(consumed, have - consumed), out var frameLength)) break;

                    Dispatch(buffer.AsSpan(consumed, frameLength));
                    consumed += frameLength;
                }

                if (consumed > 0)
                {
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, have - consumed);
                    have -= consumed;
                }
            }
        }
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
        {
            while (_pending.TryDequeue(out var stranded)) stranded.TrySetException(ex);
        }
    }

    /// <summary>Route one frame: an invalidation push feeds the cache, anything else answers a request.</summary>
    /// <remarks>
    /// The first byte IS the prefix, so this needs no parsing to decide - the same reasoning
    /// <c>RespClientCache.IsCacheableReply</c> uses for errors. Attributes are the only construct that can
    /// precede a value, and a push is never behind one.
    /// </remarks>
    /// <summary>What a push frame turns out to be. Mirrors PhysicalConnection's OutOfBandResult.</summary>
    private enum PushClass
    {
        /// <summary>Unknown to us. DROP it - see <see cref="Dispatch"/>.</summary>
        Unrecognized,

        /// <summary>An invalidation; feed the cache.</summary>
        Invalidate,

        /// <summary>Out-of-band pub/sub delivery; belongs to no request.</summary>
        Delivery,

        /// <summary>A subscribe/unsubscribe confirmation, which <i>can be</i> the reply to a command.</summary>
        MatchToCommand,
    }

    private void Dispatch(ReadOnlySpan<byte> frame)
    {
        if (!frame.IsEmpty && (RespPrefix)frame[0] == RespPrefix.Push)
        {
            switch (Classify(frame))
            {
                case PushClass.Invalidate:
                    TryInvalidate(frame);
                    return;

                case PushClass.MatchToCommand:
                    break; // falls through to the pending queue below

                case PushClass.Delivery:
                    Interlocked.Increment(ref _deliveries);
                    return;

                default:
                    // Something we do not know. DROPPING the unknown is the important half:
                    // a RESP3 push is out-of-band by definition, so handing an unrecognised one to the
                    // pending queue would answer somebody's request with it and desynchronise every reply
                    // after it. PhysicalConnection.OnOutOfBand takes exactly this line.
                    return;
            }
        }

        if (_pending.TryDequeue(out var completion)) completion.TrySetResult(frame.ToArray());
    }

    /// <summary>
    /// Classify a push by its first element.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="PushClass.MatchToCommand"/> is <b>"can be"</b>, not "is": the same
    /// subscribe/unsubscribe shape also arrives unsolicited (<c>RESET</c>, server-side teardown, shard
    /// migration), and <c>UNSUBSCRIBE</c> with no arguments answers one command with <i>N</i> pushes - or
    /// none at all, if there were no subscriptions. So correlation is not one-to-one and cannot be decided
    /// from the frame. This harness gets away with the naive version because it issues exactly one
    /// SUBSCRIBE and awaits exactly one reply; the real connection tracks subscription state instead.
    /// </remarks>
    private static PushClass Classify(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        if (!reader.TryMoveNext(checkError: false) || !reader.TryMoveNext(false)) return PushClass.Unrecognized;

        if (reader.Is("invalidate"u8)) return PushClass.Invalidate;
        if (reader.Is("message"u8) || reader.Is("pmessage"u8) || reader.Is("smessage"u8)) return PushClass.Delivery;
        if (reader.Is("subscribe"u8) || reader.Is("unsubscribe"u8)
            || reader.Is("psubscribe"u8) || reader.Is("punsubscribe"u8)
            || reader.Is("ssubscribe"u8) || reader.Is("sunsubscribe"u8))
        {
            return PushClass.MatchToCommand;
        }

        return PushClass.Unrecognized;
    }

    private bool TryInvalidate(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        if (!reader.TryMoveNext(checkError: false) || reader.Prefix != RespPrefix.Push) return false;
        if (!reader.TryMoveNext(false) || !reader.Is("invalidate"u8)) return false;
        if (!reader.TryMoveNext(false)) return false;

        // a null payload is FLUSHALL/FLUSHDB - "everything you have is gone", not "nothing changed"
        if (reader.IsNull)
        {
            Interlocked.Increment(ref _invalidations);
            Interlocked.Increment(ref _flushes);
            _cache.OnFlush();
            return true;
        }

        // ONE push can name several keys: MSET a b c arrives as a single push with a 3-element array
        var count = reader.AggregateLength();
        Interlocked.Increment(ref _invalidations);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryMoveNext(false) || !reader.TryGetSpan(out var key)) break;
            Interlocked.Increment(ref _keysInvalidated);
            _cache.OnInvalidate(key); // allocation-free: the key never leaves this span
        }

        return true;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _stream.Dispose();
            _socket.Dispose();
        }
        catch
        {
            // a harness tearing down; nothing useful to do
        }

        _shutdown.Dispose();
    }
}
