using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using RESPite.Buffers;
using RESPite.Messages;
using RESPite.Streams;
using StateFlags = RESPite.Streams.CycleBufferStreamWriter.StateFlags;

namespace RESPite.Proxy;

internal sealed class SocketProxyClient : ProxyClient, ICycleBufferCallback
{
    private CycleBuffer _receiveBuffer;
    private StateFlags _writeFlags;
    private readonly Socket _client;
    private readonly WorkerSocketAsyncEventArgs _readArgs, _writeArgs;

    public SocketProxyClient(ProxyServer.InnerLeg upstream, Socket client) : base(upstream)
    {
        _receiveBuffer = CycleBuffer.Create(pool: upstream.BufferPool, callback: this);
        _readArgs = new() { Inline = true };
        _readArgs.Init(this, WorkerStep.SocketProxyClientReadCallback);
        _writeArgs = new();
        _writeArgs.Init(this, WorkerStep.SocketProxyClientWriteCallback);
        _client = client;
    }

    public void StartReading()
    {
        InitRead();
        _readArgs.Pool.Enqueue(this, WorkerStep.SocketProxyClientRead);
    }

    protected override void SendRawSynchronized(ReadOnlySpan<byte> frame)
    {
        bool lockTaken = false;
        try
        {
            TakeWriteLock(ref lockTaken);
            if ((_writeFlags & StateFlags.Closed) != 0) return; // torn down; nothing left to write to
            _receiveBuffer.Write(frame);
            ActivateWriterInsideLock(StateFlags.Flush);
        }
        finally
        {
            ReleaseWriteLock(ref lockTaken);
        }
    }

    void ICycleBufferCallback.PageComplete() => OnActivate(StateFlags.None);

    public void Flush() => OnActivate(StateFlags.Flush);

    private void OnActivate(StateFlags newFlags)
    {
        bool lockTaken = false;
        try
        {
            TakeWriteLock(ref lockTaken);
            ActivateWriterInsideLock(newFlags);
        }
        finally
        {
            ReleaseWriteLock(ref lockTaken);
        }
    }

    internal void WorkerWriteCallback()
    {
        if (CheckSend()) WorkerWrite(); // try to do more
    }

    private bool CheckSend()
    {
        if (_writeArgs.SocketError is not SocketError.Success)
        {
            CloseWriter();
            return false;
        }
        // a stream socket may legally report a partial send; we only discard what actually went out,
        // and the remainder stays committed for the next TryGetFirstCommittedMemory, so this is safe.
        Debug.Assert(_writeArgs.BytesTransferred <= _writeArgs.MemoryBuffer.Length, "over-send?!");

        // we're done with the bytes that made it onto the wire
        bool lockTaken = false;
        try
        {
            TakeWriteLock(ref lockTaken);
            if ((_writeFlags & StateFlags.Closed) != 0) return false; // torn down; buffer already released
            _receiveBuffer.DiscardCommitted(_writeArgs.BytesTransferred);
        }
        finally
        {
            ReleaseWriteLock(ref lockTaken);
        }
        return true;
    }

    private void CloseWriter()
    {
        bool lockTaken = false;
        try
        {
            TakeWriteLock(ref lockTaken);
            _writeFlags = (_writeFlags | StateFlags.Closed) & ~StateFlags.ActiveWriter;
        }
        finally
        {
            ReleaseWriteLock(ref lockTaken);
        }
        _writeArgs.Dispose();
    }

    protected override void ReleaseResources()
    {
        // dispose the socket first: this aborts any in-flight send/receive so the SAEAs are no longer
        // in use by the time we dispose them (a stray completion just lands as OperationAborted).
        try { _client.Dispose(); }
        catch { /* already gone */ }
    }

    internal void WorkerRead()
    {
        try
        {
            const uint MAX_LOOP = 5;
            uint loop = 0; // uint so we're not too concerned about wrap-around
            while (true)
            {
                _readArgs.SetBuffer(GetReceiveBuffer());
                if (_client.ReceiveAsync(_readArgs))
                    return; // gone async, gets reactivated via pool

                if (!CheckReceive(inline: true))
                    break; // validation failed

                if (++loop > MAX_LOOP && _readArgs.Pool.HasWork)
                {
                    // yield to pool and come back for more
                    _readArgs.Pool.Enqueue(this, WorkerStep.SocketProxyClientRead);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            // socket torn down during shutdown/cancellation: treat as a normal close, not a fault
            OnReceiveCleanup(SocketError.ConnectionAborted);
        }
        catch (Exception ex)
        {
            OnReceiveCleanup(SocketError.Fault, ex);
        }
    }

    private protected override void OnReceiveCleanup(SocketError error, Exception? fault = null)
    {
        base.OnReceiveCleanup(error, fault);
        _readArgs.Dispose();
        _receiveBuffer.Release();
    }

    private bool CheckReceive(bool inline)
    {
        var err = _readArgs.SocketError;
        if (err is not SocketError.Success)
        {
            OnReceiveCleanup(err);
            return false;
        }

        if (_readArgs.BytesTransferred == 0)
        {
            // EOF
            OnReceiveCleanup(SocketError.Success);
            return false;
        }

        return OnAfterReceive(_readArgs.BytesTransferred, inline);
    }

    public void WorkerReadCallback()
    {
        if (CheckReceive(inline: false)) WorkerRead(); // try to do more
    }

    internal void WorkerWrite()
    {
        bool lockTaken = false;
        try
        {
            const uint MAX_LOOP = 5;
            uint loop = 0;
            while (true)
            {
                TakeWriteLock(ref lockTaken);
                if ((_writeFlags & StateFlags.Closed) != 0)
                {
                    _writeFlags &= ~StateFlags.ActiveWriter;
                    break; // torn down (buffer released); stop pumping
                }
                var minBytes = (_writeFlags & StateFlags.Flush) == 0 ? -1 : 1;
                var success = _receiveBuffer.TryGetFirstCommittedMemory(minBytes, out var memory);
                if (!success)
                {
                    _writeFlags &= ~StateFlags.ActiveWriter;
                    break;
                }

                ReleaseWriteLock(ref lockTaken);

                _writeArgs.SetBuffer(memory);
                if (_client.SendAsync(_writeArgs))
                    break; // gone async, gets reactivated via pool

                if (!CheckSend())
                    break; // validation failed

                if (++loop > MAX_LOOP && _writeArgs.Pool.HasWork)
                {
                    // yield to pool and come back for more
                    _writeArgs.Pool.Enqueue(this, WorkerStep.SocketProxyClientWrite);
                    break;
                }
            }
        }
        finally
        {
            ReleaseWriteLock(ref lockTaken);
        }
    }

    private void ActivateWriterInsideLock(StateFlags newFlags)
    {
        Debug.Assert(Monitor.IsEntered(_writeLock), $"{nameof(ActivateWriterInsideLock)} must be called while holding the writer lock.");

        var state = _writeFlags;
        if ((state & StateFlags.Closed) != 0) return;
        state |= newFlags & ~StateFlags.ActiveWriter;
        if ((state & StateFlags.ActiveWriter) == 0)
        {
            state |= StateFlags.ActiveWriter;
            _writeFlags = state;
            _writeArgs.Pool.Enqueue(this, WorkerStep.SocketProxyClientWrite);
        }
        else
        {
            _writeFlags = state;
        }
    }

    // guards _buffer/_writeFlags on the write side; a dedicated object (rather than 'this') so no
    // external code can contend the lock, and reentrancy via ICycleBufferCallback.PageComplete is
    // still safe (Monitor is reentrant on the same thread).
    private readonly object _writeLock = new();

    private void TakeWriteLock(ref bool lockTaken)
    {
        if (!lockTaken)
        {
            Monitor.TryEnter(_writeLock, 10_000, ref lockTaken);
            if (!lockTaken) Throw();
        }
        static void Throw() => throw new TimeoutException("Unable to acquire writer lock");
    }

    private void ReleaseWriteLock(ref bool lockTaken)
    {
        if (lockTaken)
        {
            Monitor.Exit(_writeLock);
            lockTaken = false;
        }
    }
}

internal sealed class PipeProxyClient(ProxyServer.InnerLeg upstream, PipeWriter outbound) : ProxyClient(upstream)
{
    protected override void SendRawSynchronized(ReadOnlySpan<byte> frame)
    {
        DebugAssertLock();
        outbound.Write(frame);

        var vt = outbound.FlushAsync(Lifetime);
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

internal abstract class ProxyClient(ProxyServer.InnerLeg upstream) : RespStream
{
    protected CancellationToken Lifetime => upstream.Lifetime;
    public int Id { get; set; }
    public int Database => _db;
    public ulong OpCount => Interlocked.Read(ref _opCount);

    private int _db;
    private TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ulong _opCount;
    private bool OnSelect(int db)
    {
        if (db < 0 | db > 999_999_999) return false;
        _db = db;
        return true;
    }

    public Task ExecuteAsync(PipeReader source)
    {
        StartReading(source.AsStream(), sync: false, cancellationToken: upstream.Lifetime);
        return _completionSource.Task;
    }

    private int _closed; // 0 = open, 1 = closed; guards teardown so it runs exactly once

    private protected override void RecordConnectionFailed(StreamFailureKind kind, Exception? fault = null)
    {
        // teardown can be reached from the read pump *and*, once the worker is multi-threaded, from a
        // write-side fault on another thread; make it idempotent so we don't double-remove/double-dispose
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        if (fault is null)
        {
            _completionSource.TrySetResult();
        }
        else
        {
            _completionSource.TrySetException(fault);
        }

        upstream.Remove(this);
        ReleaseResources();
    }

    /// <summary>
    /// Releases connection-scoped resources (sockets, buffers, ...). Invoked exactly once, after the
    /// connection has been removed from the pool.
    /// </summary>
    protected virtual void ReleaseResources() { }

    protected override unsafe void OnReadFrame(
        RespPrefix prefix,
        ReadOnlySpan<byte> frame,
        ref IMemoryOwner<byte>? memoryOwner)
    {
        _opCount++;

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
        return oversized.Slice(0, prefixLen + len + 2);
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

    protected abstract void SendRawSynchronized(ReadOnlySpan<byte> frame);

    [Conditional("DEBUG")]
    private protected void DebugAssertLock()
    {
        Debug.Assert(Monitor.IsEntered(_pending), "should hold lock");
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
