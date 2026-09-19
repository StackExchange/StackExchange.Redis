using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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

    private byte[] _inbound = [];
    private int _inboundLength;

    private long _bytesSent;
    private long _bytesReceived;
    private Exception? _fault;
    private int _closed;

    /// <summary>Create a connection over a transport, and begin receiving.</summary>
    /// <param name="transport">The transport; this connection takes it over.</param>
    public RespConnection(DuplexTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.Start(this);
    }

    /// <summary>Total bytes handed to the transport.</summary>
    public long BytesSent => Volatile.Read(ref _bytesSent);

    /// <summary>Total bytes taken from the transport.</summary>
    public long BytesReceived => Volatile.Read(ref _bytesReceived);

    /// <summary>Operations written and still awaiting a reply.</summary>
    public int PendingCount => _pending.Count;

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
                _transport.Flush();
                return true;
            }
            finally
            {
                message.ReleaseRequest();
            }
        }
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

    private void Append(ReadOnlySpan<byte> payload)
    {
        var required = _inboundLength + payload.Length;
        if (required > _inbound.Length)
        {
            var size = Math.Max(required, Math.Max(_inbound.Length * 2, 1024));
            Array.Resize(ref _inbound, size);
        }

        payload.CopyTo(_inbound.AsSpan(_inboundLength));
        _inboundLength = required;
    }

    private void Drain()
    {
        var consumed = 0;
        while (consumed < _inboundLength)
        {
            // A FRESH state each attempt, deliberately. RespScanState CAN carry across calls, and the
            // first version of this held one in a field on exactly that reasoning - which was wrong, and
            // wrong in a way that passed every test at the time. Bytes are only consumed here when a
            // frame COMPLETES, so an incomplete frame leaves its bytes in the buffer; re-feeding those
            // same bytes to a state that already counted them double-counts the aggregate depth. Carrying
            // the state is for a reader that does not retain what it has scanned. This one does.
            var scan = default(RespScanState);
            if (!scan.TryRead(_inbound.AsSpan(consumed, _inboundLength - consumed), out var length)) break;

            var frame = _inbound.AsSpan(consumed, length);
            consumed += length;

            if (!frame.IsEmpty && (RespPrefix)frame[0] == RespPrefix.Push && OnOutOfBand(frame)) continue;

            if (_pending.TryDequeue(out var message))
            {
                message.TrySetResult(message.Token, frame);
            }

            // a reply with nothing pending is a protocol break; there is no correct recovery, and
            // pretending otherwise would mis-address every reply after it
            else
            {
                Close(new InvalidOperationException("A reply arrived with no operation pending."));
                return;
            }
        }

        if (consumed > 0)
        {
            Buffer.BlockCopy(_inbound, consumed, _inbound, 0, _inboundLength - consumed);
            _inboundLength -= consumed;
        }
    }

    private void Close(Exception? fault)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;

        _fault = fault;
        var reason = fault ?? ClosedFault();

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
