using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
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
        var reader = new RespReader(frame);
#pragma warning disable CS0618
        if ((reader.TryReadNext() & reader.Prefix is RespPrefix.Array)
            && (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString))
#pragma warning restore CS0618
        {
            if (!reader.TryParseScalar(&KnownCommandsMetadata.TryParse, out KnownCommands command))
                command = KnownCommands.Unknown; // just to be explicit

            switch (command)
            {
                case KnownCommands.Unknown:
                    SendUnknownCommand(reader);
                    break;
#pragma warning disable CS0618
                case KnownCommands.Select when (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString) &&
                                               reader.TryReadInt32(out var db)
                                               && !reader.TryReadNext():
#pragma warning restore CS0618
                    if (OnSelect(db)) SendOK();
                    else SendRaw("-ERR invalid database\r\n"u8);
                    break;
                case KnownCommands.Select:
                    SendUnknownCommandUsage();
                    break;
                case KnownCommands.Auth:
                case KnownCommands.Hello:
                    // not yet implemented
                    SendUnknownCommand(reader);
                    break;
#pragma warning disable CS0618
                case KnownCommands.Ping when reader.TryReadNext():
                case KnownCommands.Echo when reader.TryReadNext():
#pragma warning restore CS0618
                    bool handled = false;
                    if (reader.Prefix is RespPrefix.BulkString)
                    {
                        byte[] pooled = [];
                        var chunk = reader.Buffer(ref pooled, stackalloc byte[32]);
#pragma warning disable CS0618
                        if (!reader.TryReadNext())
#pragma warning restore CS0618
                        {
                            SendBlob(chunk);
                            handled = true;
                        }

                        ArrayPool<byte>.Shared.Return(pooled);
                    }

                    if (!handled) SendUnknownCommandUsage();
                    break;
                case KnownCommands.Ping:
                    SendRaw("+PONG\r\n"u8);
                    break;
                case KnownCommands.Echo:
                    SendUnknownCommandUsage();
                    break;
#pragma warning disable CS0618
                case KnownCommands.Time when !reader.TryMoveNext():
                    var delta = DateTime.UtcNow - DateTime.UnixEpoch;
                    SendRaw("*2\r\n"u8, flush: false);
                    SendBulkStringInt64((long)delta.TotalSeconds, flush: false);
                    SendBulkStringInt64((delta.Milliseconds * 1000) + delta.Microseconds);
                    break;
#pragma warning restore CS0618
                case KnownCommands.Time:
                    SendUnknownCommandUsage();
                    break;
#pragma warning disable CS0618
                case KnownCommands.Client when (reader.TryReadNext() & reader.Prefix is RespPrefix.BulkString)
                                               && reader.TryParseScalar(
                                                   &KnownCommandsMetadata.TryParse,
                                                   out SubCommands subCommand)
                                               && subCommand is SubCommands.Id && !reader.TryReadNext():
#pragma warning restore CS0618
                    SendInt32(Id);
                    break;
                case KnownCommands.Client:
                    SendUnknownCommandUsage();
                    break;
                case KnownCommands.Reset:
                    // deliberately, we never pollute the connection, so: nothing to do
                    SendOK();
                    break;
                default:
                    // known, and no special rules; forward it
                    upstream.Send(_db, this, frame);
                    break;
            }
        }
        else
        {
            SendRaw("-ERR invalid request\r\n"u8);
        }
    }

    private void SendBlob(ReadOnlySpan<byte> value)
    {
        SendRaw(FormatInt32(RespPrefix.BulkString, value.Length, stackalloc byte[INT32_SCRATCH]), flush: false);
        SendRaw(value, flush: false);
        SendRaw("\r\n"u8);
    }

    private static ReadOnlySpan<byte> FormatInt32(RespPrefix prefix, int value, Span<byte> target)
    {
        target[0] = (byte)prefix;
        if (!Utf8Formatter.TryFormat(value, target.Slice(1), out var bytes))
            ThrowFormat();
        target[bytes + 1] = (byte)'\r';
        target[bytes + 2] = (byte)'\n';
        return target.Slice(0, bytes + 3);
    }

    private const int INT32_SCRATCH = 16;

    private void SendInt32(int value, bool flush = true)
        => SendRaw(FormatInt32(RespPrefix.Integer, value, stackalloc byte[INT32_SCRATCH]), flush);

    private void SendBulkStringInt64(long value, bool flush = true)
    {
        // use a single stackalloc for the 2 parts - payload first (we can't write in the correct place
        // without knowing the lengths first... which is doable, but let's keep it simple)
        Span<byte> scratch = stackalloc byte[32 + INT32_SCRATCH];
        if (!Utf8Formatter.TryFormat(value, scratch, out var bytes))
            ThrowFormat();
        scratch[bytes] = (byte)'\r';
        scratch[bytes + 1] = (byte)'\n';
        var lengthPrefix = FormatInt32(RespPrefix.BulkString, bytes, scratch.Slice(bytes + 2));
        SendRaw(lengthPrefix, flush: false);
        SendRaw(scratch.Slice(0, bytes + 2), flush: flush);
    }

    private static void ThrowFormat() => throw new FormatException();

    private void SendOK() => SendRaw("+OK\r\n"u8);

    private void SendUnknownCommand(in RespReader reader)
    {
        byte[] pooled = [];
        SendRaw("-ERR unknown command: "u8, flush: false);
        SendRaw(reader.Buffer(ref pooled, stackalloc byte[32]), flush: false);
        SendRaw("\r\n"u8);
        ArrayPool<byte>.Shared.Return(pooled);
    }

    private void SendUnknownCommandUsage() => SendRaw("-ERR unknown command usage\r\n"u8);

    public void SendRaw(ReadOnlySpan<byte> frame, bool flush = true)
    {
        outbound.Write(frame);
        if (flush)
        {
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
