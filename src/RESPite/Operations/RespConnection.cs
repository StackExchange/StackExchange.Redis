using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Messages;
using RESPite.Transports;

namespace RESPite.Operations;

/// <summary>
/// One connection: a FIFO of operations over a <see cref="DuplexTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is where the operation type meets a wire. Requests go out in the order they were queued, replies
/// come back in that same order, and the <i>n</i>th reply completes the <i>n</i>th operation - which is
/// the whole of RESP's correlation model and the reason the queue is a queue rather than a dictionary.
/// </para>
/// <para>
/// <b>What it deliberately does not do:</b> reconnect, handshake, select a database, retry, or choose a
/// server. Those are the layers above (design notes section 3b), and keeping them out is what makes this
/// testable against a transport that is just two byte arrays.
/// </para>
/// </remarks>
/// <summary>One element of a run: the message, and the token of the life that was queued.</summary>
/// <remarks>
/// A run is composed before it is written - a batch, a transaction - and an element can complete in between
/// (cancelled through its token), be consumed, and have its pooled instance rented for a new life. Writing it by
/// its CURRENT token would write that stranger's request and enqueue the stranger here; the token captured when
/// it was queued makes it a stale element instead, which the write skips.
/// </remarks>
internal readonly struct RespRunEntry(IRespMessage message, short token)
{
    public readonly IRespMessage Message = message;
    public readonly short Token = token;

    /// <summary>Queue a message as it stands now: its current life.</summary>
    public static RespRunEntry Of(IRespMessage message) => new(message, message.Token);

    public void Deconstruct(out IRespMessage message, out short token)
    {
        message = Message;
        token = Token;
    }
}

internal class RespConnection : TransportReceiver, IAsyncDisposable
{
    private readonly DuplexTransport _transport;

    /// <summary>The transport this connection reads and writes through; for diagnostics such as threading mode.</summary>
    internal DuplexTransport Transport => _transport;

    /// <summary>Operations written and awaiting a reply, in wire order.</summary>
    private readonly ConcurrentQueue<PendingEntry> _pending = new();

    /// <summary>A queued operation, and WHICH life of it was written.</summary>
    /// <remarks>
    /// <b>The token is the point.</b> An operation that ends early - cancelled, timed out, faulted - keeps its
    /// slot in this queue to absorb the reply it no longer wants, and meanwhile its owner can consume it; one
    /// that ended definitely (a cancellation) is then recycled and rented again. Acting through the instance's
    /// CURRENT token would act on that later life: its late reply completing somebody else's command, the
    /// heartbeat timing out a pooled instance (whose next renter failed at once, "0ms elapsed"), push matching
    /// reading another command's name. Every action through the queue therefore uses the token captured when
    /// the life was written, and a stale entry does nothing but consume its frame.
    /// </remarks>
    private readonly struct PendingEntry(IRespMessage message, short token)
    {
        public readonly IRespMessage Message = message;
        public readonly short Token = token;

        public bool IsCurrent => Message.Token == Token;
    }

    /// <summary>
    /// Serialises writers.
    /// </summary>
    /// <remarks>
    /// The enqueue happens <b>inside</b> this lock, with the write. Not for the queue's sake - it is
    /// already concurrent - but because queue order has to equal wire order: two senders that enqueued
    /// outside the lock could interleave their writes and each would then be matched to the other's
    /// reply, silently.
    /// </remarks>
    private readonly object _writeLock = new();

    /// <summary>Where the reply to every prefix goes; see <see cref="IRespMessage.Prefix"/>.</summary>
    private readonly RespPrefixReplySink _prefixReplies = new();

    /// <summary>How many prefix replies were errors; see <see cref="IRespMessage.Prefix"/>.</summary>
    public long PrefixErrors => _prefixReplies.Errors;

    /// <summary>The most recent prefix error reply, as text, or null.</summary>
    public string? LastPrefixError => _prefixReplies.LastError;

    /// <summary>The pending-queue entry that stands in for a sequence check's reply; see <see cref="SequenceCheckHead"/>.</summary>
    private readonly RespSequenceCheckSink _sequenceChecks = new();

    private byte[]? _sequenceCheckHead;
    private uint _nextSequenceToken = 1; // issued under the write lock
    private uint _expectedSequenceToken = 1; // consumed on the reader
    private long _sequenceChecksPassed;

    // a reply held back until the check behind it validates; reader-loop state, published last and claimed
    // (by the reader delivering it, or by Close faulting it) through _parkedMessage
    private IRespMessage? _parkedMessage;
    private short _parkedToken;
    private RefCountedBuffer? _parkedBuffer;
    private int _parkedOffset, _parkedLength;

    /// <summary>
    /// The rendered head of the sequence-check command, or <see langword="null"/> (the default) for a connection
    /// that does not check reply sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The high-integrity mode of the shipped client, as a connection primitive.</b> When set, every request
    /// whose message says <see cref="IRespMessage.SequenceChecked"/> is followed - under the same write lock, so
    /// nothing can come between - by one more command that echoes a 4-byte token: this head (everything up to
    /// the token's bulk payload, e.g. <c>*2\r\n$4\r\nECHO\r\n$4\r\n</c>), then the token, little-endian,
    /// then CRLF. The token is a running counter, so the connection need store nothing per request: the
    /// <i>n</i>th check must answer with <i>n</i>, and a reply stream that has lost or gained a frame fails the
    /// very next check rather than quietly answering every later command with its predecessor's reply.
    /// </para>
    /// <para>
    /// <b>A checked reply is not delivered until its check validates.</b> The reply frame is held (a reservation
    /// on the receive buffer, released as soon as the check lands) and delivered, or faulted, by the check's
    /// outcome - so a caller never sees a value the next frame then proves was not theirs. On a failed check the
    /// held reply is faulted with <see cref="CreateSequenceCheckFault"/> and the connection closes with it,
    /// which fails everything else pending: there is no recovery for a desynchronised stream.
    /// </para>
    /// <para>
    /// The spelling of the command is the owner's (the shipped client renders it through its command map, so a
    /// renamed <c>ECHO</c> is honoured); this type only knows the shape of the reply it expects. Set before the
    /// connection is published to senders, on the thread that connects it.
    /// </para>
    /// </remarks>
    public byte[]? SequenceCheckHead
    {
        get => Volatile.Read(ref _sequenceCheckHead);
        set => Volatile.Write(ref _sequenceCheckHead, value);
    }

    /// <summary>How many sequence checks have validated on this connection; see <see cref="SequenceCheckHead"/>.</summary>
    public long SequenceChecksPassed => Volatile.Read(ref _sequenceChecksPassed);

    /// <summary>Stamp, enqueue and stage one reserved request - and its prefix in front of it, if it has one.</summary>
    /// <param name="message">The message being written.</param>
    /// <param name="token">The life of <paramref name="message"/> that was reserved.</param>
    /// <param name="payload">Its request bytes.</param>
    /// <param name="received">The receive counter, snapshotted by the caller.</param>
    /// <remarks>
    /// <b>The one place a request's bytes meet the pending queue</b>, so that a prefix is handled identically
    /// on every write path - single, pair, run, inbox drain. A prefix is one frame and earns one entry naming
    /// the sink, enqueued ahead of the message's own: the server answers in order, so the sink takes the
    /// prefix's reply and the message still gets the next one. The counters are snapshotted BEFORE this
    /// request's own bytes, prefix included: what a timeout wants to know is whether the connection moved at
    /// all while this operation waited, and including our own write in the baseline would make every
    /// command look like progress.
    /// </remarks>
    private void Stage(IRespMessage message, short token, ReadOnlySpan<byte> payload, long received)
    {
        message.OnEnqueued(this, _bytesSent, received);
        if (message.Prefix is { } prefix)
        {
            _pending.Enqueue(new(_prefixReplies, 0));
            Write(prefix);
            Volatile.Write(ref _bytesSent, _bytesSent + prefix.Length);
        }

        _pending.Enqueue(new(message, token));
        Write(payload);
        Volatile.Write(ref _bytesSent, _bytesSent + payload.Length);

        // and its check behind it, the mirror of the prefix in front: one more frame, one more entry naming the
        // sink, under the lock that staged the request, so the n'th check is the n'th thing the server echoes
        if (Volatile.Read(ref _sequenceCheckHead) is { } head && message.SequenceChecked)
        {
            var sequence = _nextSequenceToken;
            _nextSequenceToken = sequence == uint.MaxValue ? 1 : sequence + 1; // never zero, so a stale diagnostic reads as "none"
            message.OnSequenceCheck(sequence);

            Span<byte> tail = stackalloc byte[6];
            BinaryPrimitives.WriteUInt32LittleEndian(tail, sequence);
            tail[4] = (byte)'\r';
            tail[5] = (byte)'\n';

            _pending.Enqueue(new(_sequenceChecks, 0));
            Write(head);
            Write(tail);
            Volatile.Write(ref _bytesSent, _bytesSent + head.Length + tail.Length);
        }
    }

    /// <summary>Whether the next pending entry is a sequence check, i.e. the reply being matched now is a checked one.</summary>
    private bool NextIsSequenceCheck()
        => _pending.TryPeek(out var next) && ReferenceEquals(next.Message, _sequenceChecks);

    /// <summary>Hold a checked reply until its check validates; see <see cref="SequenceCheckHead"/>.</summary>
    private void Park(in PendingEntry entry, ReadOnlySpan<byte> frame, RefCountedBuffer buffer)
    {
        if (!buffer.TryReserve(frame, out var reservation))
        {
            // cannot hold the bytes (not our buffer, or it is already gone): deliver now rather than lose the
            // reply; the check behind it is still validated, so a desync still closes the connection
            entry.Message.TrySetResult(entry.Token, frame, buffer);
            return;
        }

        _parkedToken = entry.Token;
        _parkedBuffer = buffer;
        _parkedOffset = reservation.Offset;
        _parkedLength = reservation.Length;
        Volatile.Write(ref _parkedMessage, entry.Message); // last: publishes the fields above to Close

        // Close drains the queue, so the check entry that would have delivered this is gone; a close that
        // raced the park must not leave the reply held forever
        if (Volatile.Read(ref _closed) != 0) FaultParked(ClosedFault());
    }

    /// <summary>The reply to a sequence check is exactly this long: <c>$4\r\n</c>, four token bytes, <c>\r\n</c>.</summary>
    private const int SequenceCheckReplyLength = 10;

    /// <summary>Whether <paramref name="frame"/> is the reply to a check carrying <paramref name="expected"/>.</summary>
    private static bool IsSequenceCheckReply(ReadOnlySpan<byte> frame, uint expected)
        => frame.Length == SequenceCheckReplyLength
            && frame[0] == (byte)'$' && frame[1] == (byte)'4' && frame[2] == (byte)'\r' && frame[3] == (byte)'\n'
            && frame[8] == (byte)'\r' && frame[9] == (byte)'\n'
            && BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(4, 4)) == expected;

    /// <summary>A check validated: move the counter on.</summary>
    private void OnSequenceCheckPassed(uint expected)
    {
        _expectedSequenceToken = expected == uint.MaxValue ? 1 : expected + 1;
        Volatile.Write(ref _sequenceChecksPassed, _sequenceChecksPassed + 1);
    }

    /// <summary>Validate the frame that answers a sequence check, and release the reply it was guarding.</summary>
    /// <returns><see langword="false"/> if the check failed and the connection has been closed.</returns>
    private bool ValidateSequenceCheck(ReadOnlySpan<byte> frame)
    {
        var expected = _expectedSequenceToken;
        if (IsSequenceCheckReply(frame, expected))
        {
            OnSequenceCheckPassed(expected);
            DeliverParked();
            return true;
        }

        // the stream is not what we think it is; the held reply may be somebody else's, and so may every reply
        // after it - fault the one we hold, then close, which faults the rest
        var fault = CreateSequenceCheckFault(expected, frame);
        FaultParked(fault);
        Close(fault);
        return false;
    }

    private enum LookAheadResult
    {
        /// <summary>The check has not arrived, or something out-of-band sits in front of it: hold the reply.</summary>
        NotYet,

        /// <summary>The check was already in the buffer and validated; the reply has been delivered.</summary>
        Delivered,

        /// <summary>The check was already in the buffer and failed; the connection is closed.</summary>
        Closed,
    }

    /// <summary>
    /// Try to settle a checked reply without holding it: the server writes a reply and its check back to back, so
    /// the check is usually already in the buffer when the reply is matched.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists:</b> a held reply is a reservation on the receive buffer, and <see cref="EnsureSpace"/>
    /// cannot compact a buffer somebody holds, so it rolls to a fresh one - which, with some reply nearly always
    /// held under load, turned every receive into an allocation (measured: +48% CPU per op and peak RSS 81 to
    /// 224 MB at 64 callers, against +6% sequentially). Looking ahead costs a bounds check and ten byte compares,
    /// and leaves the hold for the case it was designed for: a check that genuinely straddles a read.
    /// Only the reader loop touches <c>_start</c>, so consuming the check here is the same as the scanner finding
    /// it next; the scan state is clean between frames, so nothing is double-counted.
    /// </remarks>
    private LookAheadResult LookAhead(in PendingEntry entry, ReadOnlySpan<byte> frame, RefCountedBuffer buffer)
    {
        if (_end - _start < SequenceCheckReplyLength) return LookAheadResult.NotYet;

        var next = buffer.GetSpan().Slice(_start, SequenceCheckReplyLength);

        // a push, or anything that is not a bulk string, is not the check: take the slow path, which skips pushes
        // and validates whatever in-band frame comes next (and so still catches a desync)
        if (next[0] != (byte)'$') return LookAheadResult.NotYet;

        var expected = _expectedSequenceToken;
        if (!IsSequenceCheckReply(next, expected))
        {
            // in-band, a bulk string, and not our token: this is the failure the mode exists to catch, whether the
            // frame is complete or not - the next in-band frame can only ever be a four-byte bulk here. Settle it
            // now rather than hold and re-discover it; the frame is described by what we can see of it.
            var fault = CreateSequenceCheckFault(expected, next);
            entry.Message.TrySetException(entry.Token, fault, definite: false);
            Close(fault);
            return LookAheadResult.Closed;
        }

        // consume the check as if the scanner had just found it, and dequeue the entry that stood for it
        _start += SequenceCheckReplyLength;
        Volatile.Write(ref _bytesLastResult, SequenceCheckReplyLength);
        _pending.TryDequeue(out _); // the sink, by NextIsSequenceCheck; only this loop dequeues
        OnSequenceCheckPassed(expected);

        entry.Message.TrySetResult(entry.Token, frame, buffer);
        return LookAheadResult.Delivered;
    }

    private void DeliverParked()
    {
        var message = Interlocked.Exchange(ref _parkedMessage, null);
        if (message is null) return; // handed off (a redirect, a queued receipt), or a stale life: nothing was held

        var buffer = _parkedBuffer!;
        _parkedBuffer = null;
        try
        {
            message.TrySetResult(_parkedToken, buffer.GetSpan().Slice(_parkedOffset, _parkedLength), buffer);
        }
        finally
        {
            buffer.Release(); // our reservation; the message took its own if it is keeping the bytes
        }
    }

    private void FaultParked(Exception fault)
    {
        var message = Interlocked.Exchange(ref _parkedMessage, null);
        if (message is null) return;

        var buffer = _parkedBuffer;
        _parkedBuffer = null;

        // INDEFINITE: the server answered, so the command may well have been applied; what cannot be trusted
        // is which answer was whose
        message.TrySetException(_parkedToken, fault, definite: false);
        buffer?.Release();
    }

    /// <summary>The fault for a sequence check that did not validate; see <see cref="SequenceCheckHead"/>.</summary>
    /// <param name="expected">The token the check should have carried.</param>
    /// <param name="frame">The frame that arrived instead.</param>
    /// <returns>The exception the held reply, and then every pending command, fails with.</returns>
    protected virtual Exception CreateSequenceCheckFault(uint expected, ReadOnlySpan<byte> frame)
        => new InvalidOperationException(
            $"Reply sequence check failed: expected check {expected}, received {DescribeFrame(frame)}; the reply stream is desynchronised.");

    /// <summary>A short description of a frame for a fault message: its kind and size, never its contents.</summary>
    protected static string DescribeFrame(ReadOnlySpan<byte> frame)
        => frame.IsEmpty ? "an empty frame" : $"a {(RespPrefix)frame[0]} frame of {frame.Length} bytes";

    /// <summary>
    /// Sends that found the write lock busy, waiting for the holder to write them; only used when
    /// <see cref="CombineWrites"/> is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Combining, not queueing behind.</b> With many more senders than cores, a lock taken once per command
    /// convoys: the holder is descheduled mid-write and everybody spins and parks behind it, and measured at
    /// 50 callers on 12 cores that was 59% of all thread time in <c>Monitor.Enter</c>. A sender that finds
    /// the lock taken instead leaves its operation here and returns, and whoever holds the lock writes it
    /// before letting go - so under contention one thread writes many commands per acquisition, and an
    /// uncontended send is exactly what it was.
    /// </para>
    /// <para>
    /// <b>Order is kept.</b> Every path that takes the lock drains this first, so an operation queued here is
    /// written before anything its caller sends afterwards; and the holder drains again before releasing and
    /// re-checks after, so one queued in the instant between its last drain and its release is not stranded.
    /// <b>Nothing here has been written</b>: on close these fail definitely (never sent), and the timeout
    /// sweep covers them as it covers <see cref="_pending"/>.
    /// </para>
    /// </remarks>
    private readonly ConcurrentQueue<PendingEntry> _inbox = new();

    /// <summary>Whether a send that finds the write lock busy hands its operation to the holder; see <see cref="_inbox"/>.</summary>
    internal bool CombineWrites { get; set; }

    /// <summary>Take the write lock, and write anything senders left for its holder first.</summary>
    private void EnterWrite()
    {
        Monitor.Enter(_writeLock);
        if (!_inbox.IsEmpty) DrainInboxLocked();
    }

    /// <summary>Write anything left for us, release the write lock, and make sure nothing was stranded.</summary>
    private void ExitWrite()
    {
        if (!_inbox.IsEmpty) DrainInboxLocked();
        Monitor.Exit(_writeLock);
        if (!_inbox.IsEmpty) FinishCombining();
    }

    /// <summary>
    /// A sender may have queued after the holder's last drain but before it released, and found the lock
    /// still held; whoever gets here takes the lock back for it if nobody else has.
    /// </summary>
    private void FinishCombining()
    {
        while (!_inbox.IsEmpty && Monitor.TryEnter(_writeLock))
        {
            try
            {
                DrainInboxLocked();
            }
            finally
            {
                Monitor.Exit(_writeLock);
            }
        }
    }

    /// <summary>Write, in order, everything senders left for the lock holder. Caller holds the write lock.</summary>
    private void DrainInboxLocked()
    {
        var closed = Volatile.Read(ref _closed) != 0;
        while (_inbox.TryDequeue(out var entry))
        {
            var message = entry.Message;
            if (closed)
            {
                // never written, so DEFINITELY not applied - unlike what Close faults out of _pending
                message.TrySetException(entry.Token, ClosedFault(), definite: true);
                continue;
            }

            if (!message.TryReserveRequest(entry.Token, out var payload)) continue; // completed meanwhile

            try
            {
                Stage(message, entry.Token, payload.Span, Volatile.Read(ref _bytesReceived));
            }
            finally
            {
                message.ReleaseRequest();
            }
        }
    }

    /// <summary>
    /// The receive buffer, reference-counted so a reply can be <b>retained rather than copied</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be a plain growable array that was compacted after every drain. That is fine until
    /// you want a payload to point <i>into</i> it: compacting, resizing, or reusing the array moves bytes
    /// somebody is still reading, so every reply had to be copied out first - measured at ~72 bytes an
    /// operation, and the one remaining copy on the receive path.
    /// </para>
    /// <para>
    /// Counted instead. A delivered frame can take a reservation against this buffer, and the buffer
    /// survives until the last reservation is released. What the connection gives up in exchange is the
    /// right to move bytes whenever it likes: it may only compact in place while it is the <i>sole</i>
    /// holder, and otherwise rolls forward to a fresh buffer, carrying the unconsumed tail.
    /// </para>
    /// <para>
    /// <b>One copy remains, and it is the transport contract that requires it.</b>
    /// <c>TransportReceiver.OnReceived</c> hands over bytes that are transport-owned and valid only for
    /// that call, so they must be taken somewhere before anything else happens - that is
    /// <see cref="Append"/>, and it costs one copy per byte received, once. What has gone is the
    /// <i>second</i> copy, per reply, out of here into a private array.
    /// </para>
    /// <para>
    /// <b>A frame split across several reads costs nothing extra.</b> It is concatenated here as part of
    /// that same single copy and comes out contiguous, so it can be reserved exactly like one that
    /// arrived whole - including a frame larger than the buffer, since growth is sized to fit it. Getting
    /// rid of the last copy would mean the transport handing over <i>ownership</i> rather than a
    /// borrowed span, which is a change to <c>TransportReceiver</c> rather than anything here.
    /// </para>
    /// </remarks>
    private RefCountedBuffer? _inbound;

    /// <summary>First unconsumed byte in <see cref="_inbound"/>.</summary>
    private int _start;

    // the scan of the frame at _start, carried across receives; its TotalBytes is how far into the frame it has read. See Drain
    private RespScanState _scan;

    /// <summary>First free byte in <see cref="_inbound"/>.</summary>
    private int _end;

    /// <summary>The size a receive buffer is rented at unless a single message needs more.</summary>
    private const int DefaultBufferSize = 16 * 1024;

    private long _bytesSent;
    private long _bytesReceived;
    private long _bytesLastResult;
    private Exception? _fault;
    private int _closed;

    /// <summary>Create a connection over a transport, and begin receiving.</summary>
    /// <param name="transport">The transport; this connection takes it over.</param>
    /// <param name="receiveBufferPool">
    /// Where inbound buffers are rented from; null for the default. A host that lets its users supply a
    /// pool has to pass it here, or the setting is accepted and silently ignored.
    /// </param>
    public RespConnection(DuplexTransport transport, MemoryPool<byte>? receiveBufferPool = null)
    {
        _receiveBufferPool = receiveBufferPool;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.Start(this);
    }

    /// <summary>Where inbound buffers are rented from; null for the default.</summary>
    /// <remarks>
    /// <b>Every reply this connection reads lands here</b>, and payloads retained from it keep it alive -
    /// so a caller who supplies a pool is asking to own the memory the replies live in, not merely some
    /// staging copy of them. Renting from the default instead honoured the setting nowhere that mattered.
    /// </remarks>
    private readonly MemoryPool<byte>? _receiveBufferPool;

    /// <summary>Why this connection ended, or null while it has not.</summary>
    /// <remarks>
    /// <b>The question a waiting command needs answered.</b> "No connection became available" is not a
    /// diagnosis by itself; what the caller wants to know is what happened to the one they had.
    /// </remarks>
    public Exception? Fault => Volatile.Read(ref _fault);

    /// <summary>Total bytes handed to the transport.</summary>
    public long BytesSent => Volatile.Read(ref _bytesSent);

    /// <summary>Total bytes taken from the transport.</summary>
    public long BytesReceived => Volatile.Read(ref _bytesReceived);

    /// <summary>Operations written and still awaiting a reply.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>The messages awaiting a reply, oldest first, as of now.</summary>
    /// <remarks>A <see cref="ConcurrentQueue{T}"/> enumerates a moment-in-time snapshot, so this is safe to walk while traffic continues.</remarks>
    internal IEnumerable<IRespMessage> PendingSnapshot
    {
        get
        {
            foreach (var entry in _pending)
            {
                if (entry.IsCurrent) yield return entry.Message;
            }
        }
    }

    /// <summary>Operations awaiting a reply that can still be finished by one.</summary>
    /// <remarks>
    /// <b>Not <see cref="PendingCount"/>, and the gap is the point.</b> A timed-out or otherwise
    /// already-ended operation keeps its place in the queue so that the reply it no longer wants is still
    /// matched to the right slot - see <see cref="ExpirePending"/> - so the queue's depth answers "how many
    /// replies are still owed", which is what a diagnostic wants, and not "how much work could still
    /// complete", which is what anybody waiting for quiet needs. Waiting on the first number waits out the
    /// full timeout for commands that have already given up.
    /// </remarks>
    internal int UnfinishedPendingCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _pending)
            {
                if (entry.IsCurrent && !entry.Message.IsFinished) count++;
            }

            return count;
        }
    }

    /// <summary>The size of the last complete reply frame read, in bytes.</summary>
    /// <remarks>
    /// <b>Diagnostics, and specifically timeout diagnostics.</b> "The last thing we read was 0 bytes"
    /// separates a connection that is idle from one that is mid-reply, which is the first question anybody
    /// asks of a command that did not come back. Callers have read this as <c>last-in</c> for years.
    /// </remarks>
    public long BytesLastResult => Volatile.Read(ref _bytesLastResult);

    /// <summary>Bytes read from the transport that have not yet formed a complete frame.</summary>
    /// <remarks>
    /// Reported as <c>cur-in</c>. Read without synchronisation on purpose: the two ends move only on the
    /// read loop, so the worst this can be is one frame out of date, and a diagnostic that took a lock on
    /// the IO path to be exact would be paying for precision nobody can use.
    /// </remarks>
    public long BytesInBuffer
    {
        get
        {
            var pending = Volatile.Read(ref _end) - Volatile.Read(ref _start);
            return pending > 0 ? pending : 0;
        }
    }

    /// <summary>Time out anything that has been awaiting a reply for too long.</summary>
    /// <param name="olderThan">How long an operation may wait before it is declared timed out.</param>
    /// <returns>How many were timed out by this call.</returns>
    /// <remarks>
    /// <b>Nothing is dequeued, and that is the whole subtlety.</b> Replies are matched to this queue
    /// POSITIONALLY - the head answers the next frame - so removing a timed-out operation would re-address
    /// every reply after it to the wrong command. It stays where it is, already completed, and its slot
    /// still consumes the frame that eventually arrives; <c>Drain</c> ignores the result of delivering to
    /// an operation that has already ended, which is what makes that safe.
    /// <para>
    /// A timeout is deliberately not a cancellation: the request may well have been written and executed,
    /// so this says only that we stopped waiting.
    /// </para>
    /// </remarks>
    internal int ExpirePending(TimeSpan olderThan)
    {
        if (olderThan <= TimeSpan.Zero || (_pending.IsEmpty && _inbox.IsEmpty)) return 0;

        var expired = 0;
        foreach (var entry in _pending)
        {
            if (entry.Message.TryTimeoutIfOlderThan(entry.Token, olderThan)) expired++;
        }

        // not yet written, but waiting all the same; a timed-out one is skipped when the holder reaches it
        foreach (var entry in _inbox)
        {
            if (entry.Message.TryTimeoutIfOlderThan(entry.Token, olderThan)) expired++;
        }

        return expired;
    }

    /// <summary>Whether the connection has closed, for any reason.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Write an operation and queue it for its reply.</summary>
    /// <param name="message">The operation; it completes itself when the reply lands.</param>
    /// <returns>Whether the operation was written.</returns>
    /// <remarks>
    /// <b>The enqueue precedes the write, and both are inside the lock.</b> A reply can arrive before
    /// <c>Flush</c> returns - on a loopback transport it routinely does - so an operation that is written
    /// before it is queued can have its reply arrive with nothing to match it against, which desynchronises
    /// every reply after it.
    /// </remarks>
    public bool Send(IRespMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        // NOT faulted here, deliberately: only the caller knows how to describe this failure - which
        // command, which flags, how far it got - and a generic exception invented at this layer would
        // win the outcome claim and lock the good one out. Close() still faults what it has already
        // queued, because by then nobody else can.
        if (Volatile.Read(ref _closed) != 0) return false;

        if (CombineWrites)
        {
            if (!Monitor.TryEnter(_writeLock))
            {
                // busy: leave it for the holder, and make sure there still is one (see _inbox)
                _inbox.Enqueue(new(message, message.Token));
                FinishCombining();
                _transport.Flush();
                return true;
            }

            if (!_inbox.IsEmpty) DrainInboxLocked(); // anything queued before us goes first
        }
        else
        {
            Monitor.Enter(_writeLock);
        }

        bool alone;
        try
        {
            if (!message.TryReserveRequest(message.Token, out var payload))
            {
                // the operation was completed or recycled before we got to it; not ours to write
                return false;
            }

            try
            {
                alone = _pending.IsEmpty; // nothing else in flight: the sequential shape; see FlushAlone
                Stage(message, message.Token, payload.Span, Volatile.Read(ref _bytesReceived));
            }
            finally
            {
                message.ReleaseRequest();
            }
        }
        finally
        {
            ExitWrite();
        }

        // OUTSIDE the lock. The bytes are already committed to the outbound buffer in queue order, so
        // signalling the writer is not order-sensitive; another sender flushing first simply carries ours
        // out with theirs, which is a win rather than a race. Keeping the critical section down to
        // "stamp, enqueue, memcpy" is the point of the whole design - the old core serialises every
        // argument of every command inside its write lock, because that is where WriteTo runs.
        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>
    /// Write one operation, giving the caller the chance - <b>inside the write lock</b> - to put a
    /// preamble immediately in front of it.
    /// </summary>
    /// <typeparam name="TState">Whatever the factory needs; a struct, so a static lambda allocates nothing.</typeparam>
    /// <param name="message">The operation to write.</param>
    /// <param name="state">Passed to the factory.</param>
    /// <param name="preamble">
    /// Asked once, holding the write lock: returns the operation to write first, or <see langword="null"/>
    /// for none. It may record that the preamble is being written - doing so here is what makes the claim
    /// and the write atomic.
    /// </param>
    /// <param name="wrotePreamble">What the factory produced, so the caller can track or discard it.</param>
    /// <returns>Whether <paramref name="message"/> was written.</returns>
    /// <remarks>
    /// <para>
    /// The difference from the two-operation form is <b>when the preamble is built</b>. There the caller
    /// has one ready and the lock only decides whether to use it; here the lock decides and the factory
    /// then builds one, so nothing is allocated for a preamble that turns out to be unnecessary.
    /// </para>
    /// <para>
    /// That matters for <c>SELECT</c>: the question "is this connection on the right database?" cannot be
    /// answered before taking the lock - another sender may move it - so every command would otherwise have
    /// to arrive with a speculative <c>SELECT</c> in hand, and throw it away in the common case where the
    /// database has not changed.
    /// </para>
    /// <para>
    /// The factory runs under the lock, so it must not block, take other locks in a different order, or
    /// call back into this connection.
    /// </para>
    /// </remarks>
    public bool Send<TState>(
        IRespMessage message,
        TState state,
        Func<TState, IRespMessage?> preamble,
        out IRespMessage? wrotePreamble)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        if (preamble is null) throw new ArgumentNullException(nameof(preamble));

        wrotePreamble = null;
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            var head = preamble(state);
            wrotePreamble = head;

            if (head is null)
            {
                if (!message.TryReserveRequest(message.Token, out var only)) return false;
                try
                {
                    Stage(message, message.Token, only.Span, Volatile.Read(ref _bytesReceived));
                }
                finally
                {
                    message.ReleaseRequest();
                }
            }
            else
            {
                if (!head.TryReserveRequest(head.Token, out var headPayload)) return false;
                try
                {
                    if (!message.TryReserveRequest(message.Token, out var bodyPayload)) return false;
                    try
                    {
                        var received = Volatile.Read(ref _bytesReceived);
                        Stage(head, head.Token, headPayload.Span, received);
                        Stage(message, message.Token, bodyPayload.Span, received);
                    }
                    finally
                    {
                        message.ReleaseRequest();
                    }
                }
                finally
                {
                    head.ReleaseRequest();
                }
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>
    /// Write two operations contiguously, deciding <b>inside the write lock</b> whether the first is
    /// needed at all.
    /// </summary>
    /// <typeparam name="TState">Whatever the predicate needs; a struct, so a static lambda allocates nothing.</typeparam>
    /// <param name="first">The preamble, written only if <paramref name="isFirstNeeded"/> says so.</param>
    /// <param name="second">The operation whose reply the caller wants.</param>
    /// <param name="state">Passed to the predicate.</param>
    /// <param name="isFirstNeeded">
    /// Asked once, holding the write lock. It may record that the preamble is being written - doing so
    /// here is what makes the claim and the write atomic.
    /// </param>
    /// <param name="wroteFirst">Whether the preamble was written; if not, the caller still owns it.</param>
    /// <returns>Whether <paramref name="second"/> was written.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why the predicate cannot be asked earlier.</b> Deciding before taking the lock and then writing
    /// is two steps that another sender can interleave: A asks "is a preamble needed?", is told yes and
    /// records it; B asks, sees A's record and is told no; B then wins the lock and writes its command
    /// ahead of A's preamble. B's command runs without it.
    /// </para>
    /// <para>
    /// For <c>SCRIPT LOAD</c> that race costs a redundant load or a <c>NOSCRIPT</c> retry, which is why it
    /// went unnoticed. For <c>SELECT</c> it is a command executed against the wrong database, silently -
    /// and connection state like <c>SELECT</c> is the reason this overload exists.
    /// </para>
    /// <para>
    /// The predicate runs under the lock, so it must not block, take other locks in a different order, or
    /// call back into this connection.
    /// </para>
    /// </remarks>
    public bool Send<TState>(
        IRespMessage first,
        IRespMessage second,
        TState state,
        Func<TState, bool> isFirstNeeded,
        out bool wroteFirst)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        if (isFirstNeeded is null) throw new ArgumentNullException(nameof(isFirstNeeded));

        wroteFirst = false;
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            if (!isFirstNeeded(state))
            {
                // the preamble is not wanted after all; this is an ordinary single write, and the caller
                // is told so that it can discard the operation it speculatively created
                if (!second.TryReserveRequest(second.Token, out var only)) return false;
                try
                {
                    Stage(second, second.Token, only.Span, Volatile.Read(ref _bytesReceived));
                }
                finally
                {
                    second.ReleaseRequest();
                }
            }
            else
            {
                if (!first.TryReserveRequest(first.Token, out var firstPayload)) return false;
                try
                {
                    if (!second.TryReserveRequest(second.Token, out var secondPayload)) return false;
                    try
                    {
                        var received = Volatile.Read(ref _bytesReceived);
                        Stage(first, first.Token, firstPayload.Span, received);
                        Stage(second, second.Token, secondPayload.Span, received);
                        wroteFirst = true;
                    }
                    finally
                    {
                        second.ReleaseRequest();
                    }
                }
                finally
                {
                    first.ReleaseRequest();
                }
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>
    /// As <see cref="Send{TState}(IRespMessage, IRespMessage, TState, Func{TState, bool}, out bool)"/>, with a
    /// connection-state preamble - a <c>SELECT</c> - decided and written ahead of both, under the same lock.
    /// </summary>
    /// <typeparam name="TState">Whatever the callbacks need; a struct, so static lambdas allocate nothing.</typeparam>
    /// <param name="first">The preamble, written only if <paramref name="isFirstNeeded"/> says so.</param>
    /// <param name="second">The operation whose reply the caller wants.</param>
    /// <param name="state">Passed to both callbacks.</param>
    /// <param name="select">Asked once, holding the write lock and BEFORE the gate; returns the operation to write
    /// ahead of everything, or null.</param>
    /// <param name="isFirstNeeded">Asked once, holding the write lock.</param>
    /// <param name="wroteSelect">What <paramref name="select"/> produced, so the caller can discard its reply.</param>
    /// <param name="wroteFirst">Whether the preamble was written; if not, the caller still owns it.</param>
    /// <returns>Whether <paramref name="second"/> was written.</returns>
    /// <remarks>
    /// <b>Why a pair needs its own <c>SELECT</c>.</b> A connection is shared by every database that reaches its
    /// endpoint, so the database it is on is whatever the last writer selected. The pair used to be written with
    /// neither, so a <c>HIMPORT PREPARE</c>/<c>SET</c> for the default database ran against whichever database a
    /// concurrent caller had just selected - succeeding there, and leaving the default database without the key.
    /// </remarks>
    public bool Send<TState>(
        IRespMessage first,
        IRespMessage second,
        TState state,
        Func<TState, IRespMessage?> select,
        Func<TState, bool> isFirstNeeded,
        out IRespMessage? wroteSelect,
        out bool wroteFirst)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        if (select is null) throw new ArgumentNullException(nameof(select));
        if (isFirstNeeded is null) throw new ArgumentNullException(nameof(isFirstNeeded));

        wroteSelect = null;
        wroteFirst = false;
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            if (select(state) is { } head)
            {
                wroteSelect = head;
                if (head.TryReserveRequest(head.Token, out var headPayload))
                {
                    try
                    {
                        Stage(head, head.Token, headPayload.Span, Volatile.Read(ref _bytesReceived));
                    }
                    finally
                    {
                        head.ReleaseRequest();
                    }
                }
            }

            var needed = isFirstNeeded(state);
            if (needed)
            {
                if (!first.TryReserveRequest(first.Token, out var firstPayload)) return false;
                try
                {
                    Stage(first, first.Token, firstPayload.Span, Volatile.Read(ref _bytesReceived));
                    wroteFirst = true;
                }
                finally
                {
                    first.ReleaseRequest();
                }
            }

            if (!second.TryReserveRequest(second.Token, out var secondPayload)) return false;
            try
            {
                Stage(second, second.Token, secondPayload.Span, Volatile.Read(ref _bytesReceived));
            }
            finally
            {
                second.ReleaseRequest();
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>Write two operations with nothing of anybody else's between them.</summary>
    /// <param name="first">The operation to write first; typically a preamble.</param>
    /// <param name="second">The operation whose reply the caller wants.</param>
    /// <returns>Whether both were written.</returns>
    /// <remarks>
    /// <b>Contiguity, not batching.</b> The motivating case is <c>ASKING</c> before a redirected command:
    /// the server applies it to the very next command on that connection, so anything interleaved between
    /// them would receive the <c>ASKING</c> instead. Two separate <see cref="Send(IRespMessage)"/> calls
    /// cannot promise that - another sender takes the lock in between - so the pair has to be one write.
    /// </remarks>
    public bool Send(IRespMessage first, IRespMessage second)
    {
        if (first is null) throw new ArgumentNullException(nameof(first));
        if (second is null) throw new ArgumentNullException(nameof(second));
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            if (!first.TryReserveRequest(first.Token, out var firstPayload)) return false;
            try
            {
                if (!second.TryReserveRequest(second.Token, out var secondPayload)) return false;
                try
                {
                    var received = Volatile.Read(ref _bytesReceived);
                    Stage(first, first.Token, firstPayload.Span, received);
                    Stage(second, second.Token, secondPayload.Span, received);
                }
                finally
                {
                    second.ReleaseRequest();
                }
            }
            finally
            {
                first.ReleaseRequest();
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>
    /// Write several operations contiguously, giving the caller the chance - <b>inside the write lock</b> -
    /// to put a preamble in front of the whole run.
    /// </summary>
    /// <typeparam name="TState">Whatever the factory needs; a struct, so a static lambda allocates nothing.</typeparam>
    /// <param name="operations">The operations, in the order they should reach the server.</param>
    /// <param name="count">How many of <paramref name="operations"/> to write.</param>
    /// <param name="state">Passed to the factory.</param>
    /// <param name="preamble">Asked once, holding the write lock; returns the operation to write first, or null.</param>
    /// <param name="wrotePreamble">What the factory produced, so the caller can track or discard it.</param>
    /// <returns>Whether the run was written.</returns>
    /// <remarks>
    /// <b>One preamble for the whole run, not one per element.</b> A run is composed through a single
    /// context and so belongs to a single database, and everything in it is written with nothing of
    /// anybody else's in between - so a <c>SELECT</c> at the front governs all of it, for exactly as long
    /// as it needs to.
    /// </remarks>
    public bool Send<TState>(
        RespRunEntry[] operations,
        int count,
        TState state,
        Func<TState, IRespMessage?> preamble,
        out IRespMessage? wrotePreamble)
    {
        if (operations is null) throw new ArgumentNullException(nameof(operations));
        if (preamble is null) throw new ArgumentNullException(nameof(preamble));

        wrotePreamble = null;
        if (count <= 0) return true;
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            var received = Volatile.Read(ref _bytesReceived);

            if (preamble(state) is { } head)
            {
                wrotePreamble = head;
                if (head.TryReserveRequest(head.Token, out var headPayload))
                {
                    try
                    {
                        Stage(head, head.Token, headPayload.Span, received);
                    }
                    finally
                    {
                        head.ReleaseRequest();
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                var (message, token) = operations[i];
                if (!message.TryReserveRequest(token, out var payload)) continue; // see the single-run overload

                try
                {
                    Stage(message, token, payload.Span, received);
                }
                finally
                {
                    message.ReleaseRequest();
                }
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    /// <summary>Write several operations with nothing of anybody else's between them.</summary>
    /// <param name="operations">The operations, in the order they should reach the server.</param>
    /// <param name="count">How many of <paramref name="operations"/> to write.</param>
    /// <returns>Whether they were written.</returns>
    /// <remarks>
    /// <para>
    /// The N-way form of the pair used for <c>ASKING</c>, and the whole of what a batch needs from a
    /// connection. Note what is <b>not</b> here: no result collection, no completion sources, no
    /// scattering replies back to callers. Each operation is its own completion, so writing them is the
    /// entire job and the replies find their own way home.
    /// </para>
    /// <para>
    /// <b>Queued before written, all of them, under one lock</b> - for the same reason a single send is:
    /// the first reply can arrive before the last write returns.
    /// </para>
    /// </remarks>
    public bool Send(RespRunEntry[] operations, int count)
    {
        if (operations is null) throw new ArgumentNullException(nameof(operations));
        if (count <= 0) return true;
        if (Volatile.Read(ref _closed) != 0) return false;

        EnterWrite();
        var alone = _pending.IsEmpty; // nothing else in flight: see DuplexTransport.FlushAlone
        try
        {
            var received = Volatile.Read(ref _bytesReceived);
            for (var i = 0; i < count; i++)
            {
                var (message, token) = operations[i];

                // a reserve failure means this one was already completed - cancelled, most likely - so it
                // is owed nothing and the rest of the run carries on without it. Failing the whole batch
                // because one element was cancelled would be the wrong trade. The token is the one captured
                // when the element was QUEUED, not the instance's current one: a cancelled element may have
                // been consumed and its instance rented again since, and its current token is a stranger's.
                // A successful reserve then holds this life, so the calls below cannot reach another.
                if (!message.TryReserveRequest(token, out var payload)) continue;

                try
                {
                    // enqueued before IT is written, which is all the ordering this needs: a reply to an
                    // earlier element can arrive while a later one is still being copied, but no reply can
                    // arrive for something not yet written
                    Stage(message, token, payload.Span, received);
                }
                finally
                {
                    message.ReleaseRequest();
                }
            }
        }
        finally
        {
            ExitWrite();
        }

        if (alone) _transport.FlushAlone();
        else _transport.Flush();
        return true;
    }

    private void Write(ReadOnlySpan<byte> payload)
    {
        // one call rather than a GetSpan/Advance loop: the transport can stage a whole payload in a single
        // hold of its own lock (which a large request against a block-backed transport still spans several of)
        _transport.Write(payload);
    }

    /// <inheritdoc/>
    public override bool OnReceived(ReadOnlySpan<byte> payload)
    {
        // TRANSPORT-OWNED and valid only for this call, so it is copied before anything else happens
        Volatile.Write(ref _bytesReceived, _bytesReceived + payload.Length);
        Append(payload);
        Drain();
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A close the transport reports without an exception is the peer ending the stream, which is a failure with a
    /// cause, not "the connection is closed": the pending commands should say so. Our own close (dispose) runs
    /// first and wins, so this only ever describes a close we did not ask for.
    /// </remarks>
    public override void OnClosed(Exception? fault) => Close(fault ?? CreateRemoteClosedFault());

    /// <summary>
    /// Offer a reply to somebody who may take the operation elsewhere instead of completing it here.
    /// </summary>
    /// <param name="frame">The complete reply frame.</param>
    /// <param name="message">The operation this reply was matched to.</param>
    /// <returns>
    /// Whether the operation has been taken over. <see langword="true"/> means it is <b>not</b> completed
    /// here - somebody else now owns finishing it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Deliberately generic: this layer knows nothing about why a reply might not be the answer. The
    /// motivating case is a cluster redirect - the slot moved, so the command has to be re-issued
    /// somewhere else rather than failed - but the connection's part is only "this did not finish it".
    /// </para>
    /// <para>
    /// <b>Called on the IO loop, in reply order, and that is the point.</b> Commands redirected together
    /// were sent to the wrong node in order and answered in that order; re-issuing them as the replies
    /// arrive puts them on the new connection in the caller's original order. Handing this to a worker
    /// thread would lose exactly that.
    /// </para>
    /// </remarks>
    protected virtual bool TryHandOff(ReadOnlySpan<byte> frame, IRespMessage message) => false;

    /// <summary>
    /// Handle a frame that belongs to nobody - a RESP3 push.
    /// </summary>
    /// <param name="frame">The complete frame.</param>
    /// <returns>Whether the frame was consumed; <see langword="false"/> matches it to the queue head.</returns>
    /// <remarks>
    /// <para>
    /// The default consumes <b>every</b> push, which is the safe direction: a push is out-of-band by
    /// definition, and handing an unrecognised one to the queue answers somebody's request with it and
    /// desynchronises every reply after. Dropping loses information; matching corrupts it.
    /// </para>
    /// <para>
    /// <b>The exception a subscriber has to override for:</b> subscribe/unsubscribe confirmations arrive
    /// as pushes and <i>can</i> be the reply to a command - though not one-for-one, since <c>UNSUBSCRIBE</c>
    /// with no arguments answers one command with <i>N</i> pushes, or none at all. That correlation cannot
    /// be decided from the frame, which is why it is a virtual here rather than a rule.
    /// </para>
    /// </remarks>
    protected virtual bool OnOutOfBand(ReadOnlySpan<byte> frame) => true;

    /// <summary>
    /// Whether this frame is out-of-band at all, before asking what to do with it.
    /// </summary>
    /// <param name="frame">The complete frame, including its prefix.</param>
    /// <remarks>
    /// <para>
    /// <b>A push frame is out-of-band by definition</b>, which is the default and is all RESP3 needs. RESP2
    /// has no push prefix: there, a delivery is an ordinary array, and the only thing distinguishing it
    /// from a reply is the state of the connection it arrived on. That is knowledge this type does not
    /// have and should not acquire - it is protocol plumbing, not a client - so recognition is a virtual
    /// and the client decides.
    /// </para>
    /// <para>
    /// Answering true here means the frame is never matched against a pending operation, so an
    /// over-eager implementation does not merely mislabel a frame, it strands whatever was waiting for
    /// one. A <c>PING</c> on a RESP2 subscriber connection is the case to watch: its reply is also an
    /// array.
    /// </para>
    /// </remarks>
    /// <summary>The operation the next reply will be matched to, without taking it.</summary>
    /// <param name="message">The head of the pending queue, when there is one.</param>
    /// <returns>Whether anything is pending.</returns>
    /// <remarks>
    /// For a subclass deciding whether an out-of-band frame that CLAIMS to be a reply really is one: a
    /// confirmation push matched to the wrong operation answers somebody's command with an unrelated frame,
    /// and every reply after it is then off by one. Only the reader loop dequeues, and this runs on it, so
    /// the head cannot change between the peek and the match.
    /// </remarks>
    protected bool TryPeekPending(out IRespMessage? message)
    {
        // a stale head - its life ended and the instance moved on - names nobody: its frame is owed, but what
        // the instance says about itself now describes a different command
        if (_pending.TryPeek(out var entry) && entry.IsCurrent)
        {
            message = entry.Message;
            return true;
        }

        message = null;
        return false;
    }

    protected virtual bool IsOutOfBand(ReadOnlySpan<byte> frame)
        => !frame.IsEmpty && (RespPrefix)frame[0] == RespPrefix.Push;

    private void Append(ReadOnlySpan<byte> payload)
    {
        EnsureSpace(payload.Length);
        payload.CopyTo(_inbound!.GetSpan().Slice(_end));
        _end += payload.Length;
    }

    /// <summary>Make room for <paramref name="incoming"/> more bytes, moving as little as possible.</summary>
    /// <remarks>
    /// Three cases, in the order that keeps the common one free: it already fits; we are the only holder,
    /// so the unconsumed tail can slide to the front in place; or somebody still holds a reservation
    /// against this buffer, so it must be left exactly where it is and we roll forward to a new one. The
    /// old buffer returns to the pool by itself when its last reader is done.
    /// </remarks>
    private void EnsureSpace(int incoming)
    {
        var pending = _end - _start;
        var buffer = _inbound;

        if (buffer is not null && _end + incoming <= buffer.GetSpan().Length) return;

        if (buffer is not null && pending + incoming <= buffer.GetSpan().Length && buffer.RefCount == 1)
        {
            // sole holder: nothing is reading this, so the tail can move. RefCount can only FALL from
            // another thread (a reader releasing), never rise - reservations are only taken here, on the
            // read loop - so observing 1 means 1.
            if (pending != 0) buffer.GetSpan().Slice(_start, pending).CopyTo(buffer.GetSpan());
            _start = 0;
            _end = pending;
            return;
        }

        // Sized for what is NEEDED, not doubled. Doubling on every roll was the first version, and it is
        // wrong here in a way it would not be for a growable list: a roll happens whenever somebody holds
        // a reservation, not because the buffer was too small, so the size ratcheted upward with traffic
        // and every roll rented a larger array than the last. Measured at 4KB replies it turned a saved
        // memcpy into a net allocation LOSS. Grow only when one message genuinely needs more room.
        var size = Math.Max(pending + incoming, DefaultBufferSize);
        var replacement = RefCountedBuffer.Rent(size, _receiveBufferPool);
        if (pending != 0) buffer!.GetSpan().Slice(_start, pending).CopyTo(replacement.GetSpan());

        buffer?.Release(); // our reference; any reservations keep it alive until they are done
        _inbound = replacement;
        _start = 0;
        _end = pending;
    }

    private void Drain()
    {
        var buffer = _inbound;
        if (buffer is null) return;

        while (_start < _end)
        {
            // Through RespFrameScanner, with the scan CARRIED across receives and only unseen bytes fed to it -
            // which is how the scanner is meant to be driven (LoggingTunnel does the same), and which also buys its
            // fast path for the commonest replies (+OK, :N, _), skipping the reader entirely.
            //
            // This used to build a fresh RespScanState per attempt and rescan an incomplete frame from its first
            // byte each time more arrived - quadratic in the frame: a large aggregate arriving a read at a time kept
            // the reader in RespScanState.ReadCore for seconds, not reading the socket, and every other command on
            // the connection stalled behind it. It was fresh for a real reason - an incomplete frame's bytes stay in
            // the buffer, and re-feeding them to a state that already counted them double-counts the aggregate
            // depth - but the answer was never to start again: the state's TotalBytes says exactly how far into
            // this frame it has read, so feeding from there re-feeds nothing. Offsets are relative to _start, so
            // the compaction in EnsureSpace, which moves [_start, _end) as a block, leaves them valid.
            var scanned = (int)_scan.TotalBytes;
            var unseen = buffer.GetSpan().Slice(_start + scanned, _end - _start - scanned);
            if (RespFrameScanner.Default.TryRead(ref _scan, unseen) != OperationStatus.Done) break;

            var length = (int)_scan.TotalBytes;
            _scan = default;

            var frame = buffer.GetSpan().Slice(_start, length);
            _start += length;
            Volatile.Write(ref _bytesLastResult, length);

            if (IsOutOfBand(frame) && OnOutOfBand(frame)) continue;

            if (_pending.TryDequeue(out var entry))
            {
                // the frame that answers a sequence check: validated here, never handed to anybody; a failure
                // has already closed the connection, so there is nothing left to drain
                if (ReferenceEquals(entry.Message, _sequenceChecks))
                {
                    if (!ValidateSequenceCheck(frame)) return;
                    continue;
                }

                // offered first: a redirect is an instruction, not an answer, and completing the
                // operation with it would report a failure for something routine. Only to the life that was
                // written: a stale entry's frame is simply consumed (see PendingEntry)
                if (!(entry.IsCurrent && TryHandOff(frame, entry.Message)))
                {
                    if (_sequenceCheckHead is not null && entry.IsCurrent && NextIsSequenceCheck())
                    {
                        // a checked reply waits for its check (see SequenceCheckHead); a stale life has nobody
                        // waiting, so its frame is simply consumed and only the check is validated. The mode
                        // test comes first so that a connection without checks pays nothing here but the read.
                        switch (LookAhead(in entry, frame, buffer))
                        {
                            case LookAheadResult.Closed: return;
                            case LookAheadResult.Delivered: break;
                            default:
                                Park(in entry, frame, buffer);
                                break;
                        }
                    }
                    else
                    {
                        // the buffer goes along with the bytes, so a message whose result IS the frame can
                        // retain it rather than copy it out
                        entry.Message.TrySetResult(entry.Token, frame, buffer);
                    }
                }
            }

            // a reply with nothing pending is a protocol break; there is no correct recovery, and
            // pretending otherwise would mis-address every reply after it
            else
            {
                Close(new InvalidOperationException("A reply arrived with no operation pending."));
                return;
            }
        }

        // NOTHING is rewound here, and that is the whole correctness argument for sharing frames.
        //
        // Consumed frames live in [0, _start). The scanner is finished with them; their READERS are not -
        // a delivered frame may be held by a reservation for as long as its payload lives. Rewinding to
        // zero because the scanner has caught up would let the next append write straight over them, and
        // the reader would find somebody else's reply where its own used to be. (It does: reply 0 came
        // back reading "+reply-484".)
        //
        // So the decision of when bytes may move belongs in one place, EnsureSpace, which asks whether
        // anyone else is holding the buffer before it moves anything.
    }

    private void Close(Exception? fault)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        Volatile.Write(ref _fault, fault);
        var reason = fault ?? ClosedFault();

        // our own reference on the receive buffer; outstanding payloads keep it alive past this
        var buffer = Interlocked.Exchange(ref _inbound, null);
        buffer?.Release();

        // INDEFINITE, and this is the case that distinction exists for: a write may have reached the
        // server and had its reply lost with the connection, so these operations are not provably
        // unapplied and their instances must not go back to a pool
        while (_pending.TryDequeue(out var entry))
        {
            entry.Message.TrySetException(entry.Token, reason, definite: false);
        }

        // a reply held for a check that will now never arrive is pending in the same sense: answered, unverified
        FaultParked(reason);

        // and what never reached the socket: DEFINITE, since none of it was written. A sender racing this
        // close can still enqueue after it; the holder's drain sees the connection closed and fails those too.
        while (_inbox.TryDequeue(out var entry))
        {
            entry.Message.TrySetException(entry.Token, reason, definite: true);
        }

        // and only then tell whoever owns us, so what they see is a connection whose commands have already been
        // failed - a close nobody heard about was a dead connection nothing noticed until its next send
        if (Closed is { } closed)
        {
            try
            {
                closed(this);
            }
            catch
            {
                // an owner's bookkeeping must not stop the close; the commands above are already failed
            }
        }
    }

    /// <summary>Called once, when this connection closes for any reason, after its pending commands are failed.</summary>
    /// <remarks>
    /// <b>The owner's only notice of a close it did not ask for.</b> A server that ends the stream - an output-buffer
    /// limit, <c>CLIENT KILL</c>, a restart - used to fail the pending commands and stop there: nothing was logged, no
    /// failure was raised, and nothing reconnected until a later command happened to try the dead connection. Set
    /// before the connection is published; a close that races that is caught by checking <see cref="IsClosed"/>.
    /// </remarks>
    internal Action<RespConnection>? Closed { get; set; }

    /// <summary>The fault for a close the transport reported without one: the peer ended the stream.</summary>
    /// <returns>The exception pending commands fail with.</returns>
    protected virtual Exception CreateRemoteClosedFault() => new System.IO.IOException("The connection was closed by the remote end.");

    private Exception ClosedFault()
        => _fault ?? new InvalidOperationException("The connection is closed.");

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Close(null);
        await _transport.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
