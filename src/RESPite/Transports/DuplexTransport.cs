using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace RESPite.Transports;

/// <summary>
/// A duplex byte transport, deliberately NOT a <see cref="System.IO.Stream"/> and NOT a pipe: outbound
/// is staged-and-flushed (batching is an explicit contract point, not an implementation accident), and
/// inbound is PUSH — the transport delivers bytes to a <see cref="TransportReceiver"/> on the
/// transport's own schedule, in transport-owned memory.
///
/// The shape is derived from measured transport work rather than taste: any-thread copying writes with
/// an explicit flush (batching at the caller's natural boundaries was the largest single lever
/// measured), push delivery (pull adapters over a push transport measured 24-40% overhead), and a
/// batch-end notification (coalescing responses produced during a delivery burst into one flush
/// eliminated a measured 3x send amplification).
///
/// The transport IS the outbound <see cref="IBufferWriter{T}"/> — there is no separate output object.
/// Staging is callable from any thread (single logical writer at a time); bytes are owned by the
/// transport once <see cref="Advance"/> returns; <see cref="Flush"/> hands the staged bytes to the
/// wire. Passing the transport AS <see cref="IBufferWriter{T}"/> deliberately grants stage-only
/// access: the holder composes, the owner flushes at its batch boundary.
/// </summary>
[Experimental(Experiments.Transport, UrlFormat = Experiments.UrlFormat)]
public abstract class DuplexTransport : IBufferWriter<byte>, IAsyncDisposable
{
    /// <summary>Request writable space to stage outbound bytes (see <see cref="IBufferWriter{T}"/>).</summary>
    public abstract Memory<byte> GetMemory(int sizeHint = 0);

    /// <inheritdoc cref="GetMemory"/>
    /// <remarks>Defaults to <c>GetMemory(sizeHint).Span</c>; override when the transport has a cheaper
    /// span path than a <see cref="Memory{T}"/> round-trip.</remarks>
    public virtual Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    /// <summary>Commit <paramref name="count"/> bytes obtained via <see cref="GetMemory"/> or
    /// <see cref="GetSpan"/>; the transport owns them when this returns, so caller state need not
    /// survive it.</summary>
    public abstract void Advance(int count);

    /// <summary>Hand everything staged since the last flush to the wire, as one send where the
    /// transport allows. Returns false if the transport is closed (staged bytes are dropped).</summary>
    public abstract bool Flush();

    /// <summary>Begin inbound delivery. Exactly one receiver, set once, before any data is expected;
    /// delivery runs on the transport's schedule and threads.</summary>
    public abstract void Start(TransportReceiver receiver);

    /// <summary>
    /// Whether this transport's bytes are encrypted on the wire. Since the transport owns connect and
    /// TLS end-to-end, this is the only thing a consumer can ask; it is the transport's assertion, and
    /// the default is the safe answer (no). A transport that terminates TLS itself MUST override this,
    /// otherwise a configuration that demands TLS will refuse to use it.
    /// </summary>
    /// <remarks>Valid once the transport is connected; consumers check it before first use.</remarks>
    public virtual bool IsEncrypted => false;

    public abstract ValueTask DisposeAsync();
}

/// <summary>
/// The consumer half of <see cref="DuplexTransport"/>. Callbacks run on the transport's threads and
/// must be bounded and non-blocking; anything long-running belongs on the consumer's own scheduler.
/// </summary>
[Experimental(Experiments.Transport, UrlFormat = Experiments.UrlFormat)]
public abstract class TransportReceiver
{
    /// <summary>Bytes arrived. <paramref name="payload"/> is TRANSPORT-OWNED and valid only for the
    /// duration of the call — copy anything retained. Return false to request the transport close.</summary>
    public abstract bool OnReceived(ReadOnlySpan<byte> payload);

    /// <summary>A delivery burst has ended (for loop transports: the event batch is drained). Flush
    /// anything staged in response to the burst HERE, once, rather than per <see cref="OnReceived"/> —
    /// per-callback flushing measurably amplifies peer segmentation.</summary>
    public virtual void OnBatchEnd() { }

    /// <summary>The transport closed; fires exactly once. <paramref name="fault"/> is the failure when
    /// the transport can attribute one, else null (a clean or unattributed close).</summary>
    public virtual void OnClosed(Exception? fault) { }
}
