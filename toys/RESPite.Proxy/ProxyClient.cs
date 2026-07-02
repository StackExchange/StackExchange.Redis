using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Messages;
using RESPite.Streams;

namespace RESPite.Proxy;

internal sealed class ProxyClient(ProxyServer.InnerLeg upstream, Stream inbound, PipeWriter outbound)
    : RespStream(inbound)
{
    public int Id { get; set; }
    public int Database => _db;
    private int _db;
    private TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool OnSelect(int db)
    {
        if (db < 0 | db > 999_999_999) return false;
        _db = db;
        return true;
    }

    public Task ExecuteAsync()
    {
        StartReading(sync: false, cancellationToken: upstream.Lifetime);
        return _completionSource.Task;
    }

    private protected override void RecordConnectionFailed(StreamFailureKind kind, Exception? fault = null)
    {
        if (fault is null)
        {
            _completionSource.TrySetResult();
        }
        else
        {
            _completionSource.TrySetException(fault);
        }

        upstream.Remove(this);
    }

    protected override unsafe void OnReadFrame(
        RespPrefix prefix,
        ReadOnlySpan<byte> frame,
        ref IMemoryOwner<byte>? memoryOwner)
    {
        ReadOnlyMemory<byte> localResponse = default;
        IDisposable? lease = null;
        KnownCommands command = KnownCommands.Unknown;

        var reader = new RespReader(frame);
#pragma warning disable CS0618
        if ((reader.TryReadNext() & reader.Prefix is RespPrefix.Array)
            && (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString))
#pragma warning restore CS0618
        {
            if (!reader.TryParseScalar(&KnownCommandsMetadata.TryParse, out command))
                command = KnownCommands.Unknown; // just to be explicit

            switch (command)
            {
                case KnownCommands.Unknown:
                    localResponse = CreateUnknownCommandResponse(reader, out lease);
                    break;
#pragma warning disable CS0618
                case KnownCommands.Select when (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString) &&
                                               reader.TryReadInt32(out var db)
                                               && !reader.TryReadNext():
#pragma warning restore CS0618
                    localResponse = OnSelect(db) ? CannedResponses.OK : CannedResponses.InvalidDatabase;
                    break;
                case KnownCommands.Select:
                    localResponse = CannedResponses.UnknownCommandUsage;
                    break;
                case KnownCommands.Auth:
                case KnownCommands.Hello:
                    // not yet implemented
                    command = KnownCommands.Unknown;
                    break;
#pragma warning disable CS0618
                case KnownCommands.Ping when reader.TryReadNext():
                case KnownCommands.Echo when reader.TryReadNext():
#pragma warning restore CS0618
                    if (reader.Prefix is RespPrefix.BulkString)
                    {
                        localResponse = CreateEchoResponse(reader, out lease);
                        if (reader.TryMoveNext())
                        {
                            // well that isn't right!
                            lease?.Dispose();
                            localResponse = default;
                        }
                    }

                    if (localResponse.IsEmpty) localResponse = CannedResponses.UnknownCommandUsage;
                    break;
                case KnownCommands.Ping:
                    localResponse = CannedResponses.Pong;
                    break;
                case KnownCommands.Echo:
                    localResponse = CannedResponses.UnknownCommandUsage;
                    break;
#pragma warning disable CS0618
                case KnownCommands.Time when !reader.TryMoveNext():
                    localResponse = CreateTimeResponse(out lease);
                    break;
#pragma warning restore CS0618
                case KnownCommands.Time:
                    localResponse = CannedResponses.UnknownCommandUsage;
                    break;
#pragma warning disable CS0618
                case KnownCommands.Client when (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString)
                                               && reader.TryParseScalar(
                                                   &KnownCommandsMetadata.TryParse,
                                                   out SubCommands subCommand)
                                               && subCommand is SubCommands.Id && !reader.TryReadNext():
#pragma warning restore CS0618
                    localResponse = CreateInt32Response(Id, out lease);
                    break;
                case KnownCommands.Client:
                    localResponse = CannedResponses.UnknownCommandUsage;
                    break;
                case KnownCommands.Reset:
                    // deliberately, we never pollute the connection, so: nothing to do
                    localResponse = CannedResponses.OK;
                    break;
            }
        }
        else
        {
            localResponse = CannedResponses.InvalidRequest;
        }

        if (command is KnownCommands.Unknown & localResponse.IsEmpty)
        {
            localResponse = CannedResponses.UnknownCommand;
        }

        lock (_pending)
        {
            if (!localResponse.IsEmpty && _pending.Count is 0)
            {
                // pure local and nothing else in play; no need to enqueue etc
                SendRawSynchronized(localResponse.Span);
            }
            else
            {
                _pending.Enqueue(new(command, localResponse, lease));
            }
        }

        if (localResponse.IsEmpty)
        {
            upstream.Send(_db, this, frame);
        }
    }

    private ReadOnlyMemory<byte> CreateTimeResponse(out IDisposable? lease)
    {
        var delta = DateTime.UtcNow - DateTime.UnixEpoch;
        var unixTime = (long)delta.TotalSeconds;
        var micros = (delta.Milliseconds * 1000) + delta.Microseconds;

        var oversized = Rent(64, out lease);
        var span = oversized.Span;
        "*2\r\n"u8.CopyTo(span);
        int offset = 4;
        offset += FormatBulkStringInt64(unixTime, span.Slice(offset));
        offset += FormatBulkStringInt64(micros, span.Slice(offset));
        return oversized.Slice(0, offset);
    }

    private ReadOnlyMemory<byte> CreateEchoResponse(RespReader reader, out IDisposable? lease)
    {
        var len = reader.ScalarLength();
        var oversized = Rent(32 + len, out lease);
        var span = oversized.Span;
        var prefixLen = FormatInt32(RespPrefix.BulkString, len, span);
        reader.CopyTo(span.Slice(prefixLen));
        "\r\n"u8.CopyTo(span.Slice(prefixLen + len));
        return oversized.Slice(prefixLen + len + 2);
    }

    private readonly struct PendingMessage(
        KnownCommands command,
        ReadOnlyMemory<byte> localResponse,
        IDisposable? lease)
    {
        public KnownCommands Command => command;
        public ReadOnlyMemory<byte> LocalResponse => localResponse;
        public bool IsRemote => localResponse.IsEmpty;
        public void Recycle() => lease?.Dispose();
    }

    private readonly Queue<PendingMessage> _pending = new();

    public void ForwardResponse(ReadOnlySpan<byte> response)
    {
        PendingMessage next;
        lock (_pending)
        {
            if (!_pending.TryDequeue(out next)) return; // unexpected! OOB?
            SendRawSynchronized(response);
        }

        next.Recycle();

        // flush any locally generated queued responses
        while (true)
        {
            lock (_pending)
            {
                if (!_pending.TryPeek(out next) | next.IsRemote) break;
                _ = _pending.Dequeue();

                var resp = next.LocalResponse;
                SendRawSynchronized(resp.Span);
                next.Recycle();
            }
        }
    }

    private static int FormatInt32(RespPrefix prefix, int value, Span<byte> target)
    {
        target[0] = (byte)prefix;
        if (!Utf8Formatter.TryFormat(value, target.Slice(1), out var bytes))
            ThrowFormat();
        target[bytes + 1] = (byte)'\r';
        target[bytes + 2] = (byte)'\n';
        return bytes + 3;
    }

    private const int INT32_SCRATCH = 16;

    private ReadOnlyMemory<byte> CreateInt32Response(int value, out IDisposable? lease)
    {
        var oversized = Rent(INT32_SCRATCH, out lease);
        var len = FormatInt32(RespPrefix.Integer, value, oversized.Span);
        return oversized.Slice(0, len);
    }

    private int FormatBulkStringInt64(long value, Span<byte> target)
    {
        // use a single stackalloc for the 2 parts - payload first (we can't write in the correct place
        // without knowing the lengths first... which is doable, but let's keep it simple)
        Span<byte> scratch = stackalloc byte[INT32_SCRATCH];
        if (!Utf8Formatter.TryFormat(value, scratch, out var payloadLen))
            ThrowFormat();

        var prefixLen = FormatInt32(RespPrefix.BulkString, payloadLen, target);
        scratch.Slice(0, payloadLen).CopyTo(target.Slice(prefixLen));
        "\r\n"u8.CopyTo(target.Slice(prefixLen + payloadLen));
        return prefixLen + payloadLen + 2;
    }

    private static void ThrowFormat() => throw new FormatException();

    private static class CannedResponses
    {
        public static readonly ReadOnlyMemory<byte> OK = "+OK\r\n"u8.ToArray();
        public static readonly ReadOnlyMemory<byte> Pong = "+PONG\r\n"u8.ToArray();
        public static readonly ReadOnlyMemory<byte> InvalidDatabase = "-ERR invalid database\r\n"u8.ToArray();
        public static readonly ReadOnlyMemory<byte> InvalidRequest = "-ERR invalid request\r\n"u8.ToArray();
        public static readonly ReadOnlyMemory<byte> UnknownCommand = "-ERR unknown command\r\n"u8.ToArray();
        public static readonly ReadOnlyMemory<byte> UnknownCommandUsage = "-ERR unknown command usage\r\n"u8.ToArray();
    }

    private Memory<byte> Rent(int minSize, out IDisposable? lease)
    {
        if (minSize is 0)
        {
            lease = null;
            return default;
        }

        var src = MemoryPool<byte>.Shared.Rent(minSize);
        lease = src;
        return src.Memory;
    }

    private ReadOnlyMemory<byte> CreateUnknownCommandResponse(in RespReader reader, out IDisposable? lease)
    {
        ReadOnlySpan<byte> preamble = "-ERR unknown command: "u8;
        var commandLength = reader.ScalarLength();
        var oversized = Rent(preamble.Length + commandLength + 2, out lease);
        var span = oversized.Span;
        preamble.CopyTo(span);
        int copied = reader.CopyTo(span.Slice(preamble.Length));
        Debug.Assert(copied == commandLength);
        "\r\n"u8.CopyTo(span.Slice(preamble.Length + commandLength));
        return oversized.Slice(0, preamble.Length + commandLength + 2);
    }

    private void SendRawSynchronized(ReadOnlySpan<byte> frame)
    {
        Debug.Assert(Monitor.IsEntered(_pending), "should hold lock");
        outbound.Write(frame);

        var vt = outbound.FlushAsync(upstream.Lifetime);
        if (vt.IsCompletedSuccessfully)
        {
            _ = vt.Result;
        }
        else
        {
            vt.AsTask().Wait(); // for test only
        }
    }
}

internal static partial class KnownCommandsMetadata
{
    [AsciiHash(CaseSensitive = false)]
    public static partial bool TryParse(ReadOnlySpan<byte> data, out KnownCommands command);

    [AsciiHash(CaseSensitive = false)]
    public static partial bool TryParse(ReadOnlySpan<byte> data, out SubCommands command);
}

internal enum SubCommands
{
    Unknown,
    Id,
}

internal enum KnownCommands
{
    Unknown = 0,

    // handled by the proxy
    Select,
    Auth,
    Hello,
    Ping,
    Echo,
    Time,
    Client,
    Reset,

    // upstreamed but need attention on cluster
    DbSize, // needs attention on cluster
    RandomKey, // this is a tricky one,

    // upstreamed, string
    Append,
    Decr,
    DecrBy,
    DelEx,
    Digest,
    Get,
    GetDel,
    GetEx,
    GetRange,
    GetSet,
    Incr,
    IncrBy,
    IncrByFloat,
    IncrEx,
    Lcs,
    MGet,
    MSet,
    MSetEx,
    MSetNx,
    PSetEx,
    Set,
    SetEx,
    SetNx,
    SetRange,
    StrLen,
    SubStr,

    // upstreamed, generic
    Copy,
    Del,
    Dump,
    Exists,
    Expire,
    ExpireAt,
    ExpireTime,
    Keys,
    Migrate,
    Move,
    Object,
    Persist,
    PExpire,
    PExpireAt,
    PExpireTime,
    PTtl,
    Rename,
    RenameNx,
    Restore,
    Scan,
    Sort,
    [AsciiHash("SORT_RO")]
    Sort_RO,
    Touch,
    Ttl,
    Type,
    Unlink,
    // Wait: nopedy nope nope
    // WaitAof: nopedy nope nope

    // upstreamed, server; very limited
    Info,
    Role,
}
