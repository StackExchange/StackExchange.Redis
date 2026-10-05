using System;
using System.Buffers;
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
internal class RespConnection : TransportReceiver, IAsyncDisposable
{
    private readonly DuplexTransport _transport;

    /// <summary>Operations written and awaiting a reply, in wire order.</summary>
    private readonly ConcurrentQueue<IRespMessage> _pending = new();

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
    internal IEnumerable<IRespMessage> PendingSnapshot => _pending;

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
            foreach (var message in _pending)
            {
                if (!message.IsFinished) count++;
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
        if (olderThan <= TimeSpan.Zero || _pending.IsEmpty) return 0;

        var expired = 0;
        foreach (var message in _pending)
        {
            if (message.TryTimeoutIfOlderThan(olderThan)) expired++;
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

        lock (_writeLock)
        {
            if (!message.TryReserveRequest(message.Token, out var payload))
            {
                // the operation was completed or recycled before we got to it; not ours to write
                return false;
            }

            try
            {
                // the counters are snapshotted BEFORE this request's own bytes: what a timeout wants to
                // know is whether the connection moved at all while this operation waited, and including
                // our own write in the baseline would make every command look like progress
                message.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));

                _pending.Enqueue(message);
                Write(payload.Span);
                Volatile.Write(ref _bytesSent, _bytesSent + payload.Length);
            }
            finally
            {
                message.ReleaseRequest();
            }
        }

        // OUTSIDE the lock. The bytes are already committed to the outbound buffer in queue order, so
        // signalling the writer is not order-sensitive; another sender flushing first simply carries ours
        // out with theirs, which is a win rather than a race. Keeping the critical section down to
        // "stamp, enqueue, memcpy" is the point of the whole design - the old core serialises every
        // argument of every command inside its write lock, because that is where WriteTo runs.
        _transport.Flush();
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

        lock (_writeLock)
        {
            var head = preamble(state);
            wrotePreamble = head;

            if (head is null)
            {
                if (!message.TryReserveRequest(message.Token, out var only)) return false;
                try
                {
                    message.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));
                    _pending.Enqueue(message);
                    Write(only.Span);
                    Volatile.Write(ref _bytesSent, _bytesSent + only.Length);
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
                        var bytes = headPayload.Length + bodyPayload.Length;
                        head.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));
                        message.OnEnqueued(this, _bytesSent + headPayload.Length, Volatile.Read(ref _bytesReceived));

                        _pending.Enqueue(head);
                        _pending.Enqueue(message);
                        Write(headPayload.Span);
                        Write(bodyPayload.Span);
                        Volatile.Write(ref _bytesSent, _bytesSent + bytes);
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

        _transport.Flush();
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

        lock (_writeLock)
        {
            if (!isFirstNeeded(state))
            {
                // the preamble is not wanted after all; this is an ordinary single write, and the caller
                // is told so that it can discard the operation it speculatively created
                if (!second.TryReserveRequest(second.Token, out var only)) return false;
                try
                {
                    second.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));
                    _pending.Enqueue(second);
                    Write(only.Span);
                    Volatile.Write(ref _bytesSent, _bytesSent + only.Length);
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
                        var bytes = firstPayload.Length + secondPayload.Length;
                        first.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));
                        second.OnEnqueued(this, _bytesSent + firstPayload.Length, Volatile.Read(ref _bytesReceived));

                        _pending.Enqueue(first);
                        _pending.Enqueue(second);
                        Write(firstPayload.Span);
                        Write(secondPayload.Span);
                        Volatile.Write(ref _bytesSent, _bytesSent + bytes);
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

        _transport.Flush();
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

        lock (_writeLock)
        {
            if (!first.TryReserveRequest(first.Token, out var firstPayload)) return false;
            try
            {
                if (!second.TryReserveRequest(second.Token, out var secondPayload)) return false;
                try
                {
                    var bytes = firstPayload.Length + secondPayload.Length;
                    first.OnEnqueued(this, _bytesSent, Volatile.Read(ref _bytesReceived));
                    second.OnEnqueued(this, _bytesSent + firstPayload.Length, Volatile.Read(ref _bytesReceived));

                    _pending.Enqueue(first);
                    _pending.Enqueue(second);
                    Write(firstPayload.Span);
                    Write(secondPayload.Span);
                    Volatile.Write(ref _bytesSent, _bytesSent + bytes);
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

        _transport.Flush();
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
        IRespMessage[] operations,
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

        lock (_writeLock)
        {
            var received = Volatile.Read(ref _bytesReceived);

            if (preamble(state) is { } head)
            {
                wrotePreamble = head;
                if (head.TryReserveRequest(head.Token, out var headPayload))
                {
                    try
                    {
                        head.OnEnqueued(this, _bytesSent, received);
                        _pending.Enqueue(head);
                        Write(headPayload.Span);
                        Volatile.Write(ref _bytesSent, _bytesSent + headPayload.Length);
                    }
                    finally
                    {
                        head.ReleaseRequest();
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                var message = operations[i];
                if (!message.TryReserveRequest(message.Token, out var payload)) continue;

                try
                {
                    message.OnEnqueued(this, _bytesSent, received);
                    _pending.Enqueue(message);
                    Write(payload.Span);
                    Volatile.Write(ref _bytesSent, _bytesSent + payload.Length);
                }
                finally
                {
                    message.ReleaseRequest();
                }
            }
        }

        _transport.Flush();
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
    public bool Send(IRespMessage[] operations, int count)
    {
        if (operations is null) throw new ArgumentNullException(nameof(operations));
        if (count <= 0) return true;
        if (Volatile.Read(ref _closed) != 0) return false;

        lock (_writeLock)
        {
            var received = Volatile.Read(ref _bytesReceived);
            for (var i = 0; i < count; i++)
            {
                var message = operations[i];

                // a reserve failure means this one was already completed - cancelled, most likely - so it
                // is owed nothing and the rest of the run carries on without it. Failing the whole batch
                // because one element was cancelled would be the wrong trade.
                if (!message.TryReserveRequest(message.Token, out var payload)) continue;

                try
                {
                    // enqueued before IT is written, which is all the ordering this needs: a reply to an
                    // earlier element can arrive while a later one is still being copied, but no reply can
                    // arrive for something not yet written
                    message.OnEnqueued(this, _bytesSent, received);
                    _pending.Enqueue(message);
                    Write(payload.Span);
                    Volatile.Write(ref _bytesSent, _bytesSent + payload.Length);
                }
                finally
                {
                    message.ReleaseRequest();
                }
            }
        }

        _transport.Flush();
        return true;
    }

    private void Write(ReadOnlySpan<byte> payload)
    {
        // loop rather than assume one span is enough: IBufferWriter only promises *at least* the hint,
        // and a large request against a block-backed transport will span several
        while (!payload.IsEmpty)
        {
            var destination = _transport.GetSpan(payload.Length);
            if (destination.IsEmpty) throw new InvalidOperationException("The transport offered no space.");

            var take = Math.Min(destination.Length, payload.Length);
            payload.Slice(0, take).CopyTo(destination);
            _transport.Advance(take);
            payload = payload.Slice(take);
        }
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
    public override void OnClosed(Exception? fault) => Close(fault);

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
    protected bool TryPeekPending(out IRespMessage? message) => _pending.TryPeek(out message);

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
            // A FRESH state each attempt, deliberately. RespScanState CAN carry across calls, and the
            // first version of this held one in a field on exactly that reasoning - which was wrong, and
            // wrong in a way that passed every test at the time. Bytes are only consumed here when a
            // frame COMPLETES, so an incomplete frame leaves its bytes in the buffer; re-feeding those
            // same bytes to a state that already counted them double-counts the aggregate depth. Carrying
            // the state is for a reader that does not retain what it has scanned. This one does.
            var scan = default(RespScanState);
            if (!scan.TryRead(buffer.GetSpan().Slice(_start, _end - _start), out var length)) break;

            var frame = buffer.GetSpan().Slice(_start, length);
            _start += length;
            Volatile.Write(ref _bytesLastResult, length);

            if (IsOutOfBand(frame) && OnOutOfBand(frame)) continue;

            if (_pending.TryDequeue(out var message))
            {
                // offered first: a redirect is an instruction, not an answer, and completing the
                // operation with it would report a failure for something routine
                if (!TryHandOff(frame, message))
                {
                    // the buffer goes along with the bytes, so a message whose result IS the frame can
                    // retain it rather than copy it out
                    message.TrySetResult(message.Token, frame, buffer);
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
        while (_pending.TryDequeue(out var message))
        {
            message.TrySetException(message.Token, reason, definite: false);
        }
    }

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
