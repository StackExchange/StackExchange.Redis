#if SOCKETSET
using System.Net.Sockets;
using SocketSets;

namespace RESPite.Proxy;

/// <summary>
/// LEVEL 2: RESP framing driven directly off the transport's receive callback, on the IO loop thread.
///
/// WHAT THIS REMOVES, relative to <see cref="PipeProxyClient"/> (level 1). Level 1 goes
/// transport -> PipeIoBridge -> two Pipes -> PipeProxyClient -> <c>pipeReader.AsStream()</c> -> RespReader,
/// so EVERY REQUEST pays: a pipe write, a ThreadPool hop to wake the pipe reader, a Stream wrapper, the
/// reply into a second pipe, another hop, and a pump task to drain it. This class pays NONE of that: the
/// bytes arrive as a span on the loop thread and are framed in place; replies go straight out through
/// <see cref="Connection.Send"/>.
///
/// WHY THAT IS THE RIGHT SHAPE HERE AND NOT IN KESTREL. An inline reader is dangerous in ASP.NET because
/// the loop thread would end up running arbitrary user code -- which is why Kestrel keeps IO queues, and
/// why SocketSet's own <c>SS_PIPE_SCHED=inline-both</c> experiment measured WORSE than hopping (both
/// readers serialise on the loop). Here the handler is ours and is bounded, non-blocking RESP framing, so
/// the objection does not apply. The hazard that DOES remain is self-inflicted head-of-line blocking:
/// clients are round-robined onto a few STICKY upstream legs, so anything that blocks this thread stalls
/// that leg's whole cohort. Expect it to show in p99 before it shows in throughput.
///
/// WHY IT IS AIMED AT A MEASURED DEFECT, not a hypothesis. Against Envoy the level-1 proxy loses ~2x at
/// -P 1 and wins ~1.5x at -P 16: per-request overhead is our problem, parse throughput is not. Level 1's
/// pipes+hops are a fixed cost per request that amortises away under pipelining -- exactly that shape.
///
/// The framing itself is entirely RESPite's: <c>GetReceiveBuffer()</c> / <c>OnAfterReceive()</c> are the
/// same push seam <see cref="SocketProxyClient"/> drives from its SAEA completions, so partial frames
/// spanning reads, the CycleBuffer carry-over and OnReadFrame dispatch are all reused unchanged. This
/// class is a transport adapter, not a new parser.
/// </summary>
internal sealed class SocketSetProxyClient : ProxyClient
{
    private readonly Connection _conn;

    public SocketSetProxyClient(ProxyServer.InnerLeg upstream, Connection conn) : base(upstream)
    {
        _conn = conn;
        InitRead();
    }

    /// <summary>
    /// Feed bytes that just arrived, ON THE LOOP THREAD. Returns false once the client is torn down, so
    /// the caller can stop handing it data.
    ///
    /// <paramref name="data"/> is TRANSPORT-OWNED and valid only for the duration of the receive
    /// callback, so it is copied into the reader's own buffer rather than retained. The loop exists
    /// because the reader hands out whatever uncommitted space its current page has, which may be smaller
    /// than the payload -- a single copy-and-commit would silently drop the remainder.
    /// </summary>
    public bool Feed(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var dest = GetReceiveBuffer();
            if (dest.IsEmpty) return false; // reader is gone/closed; nothing can consume this
            int take = Math.Min(dest.Length, data.Length);
            data.Slice(0, take).CopyTo(dest.Span);
            // inline: true -- we ARE the IO thread here; this is not a pool callback.
            if (!OnAfterReceive(take, inline: true)) return false;
            data = data.Slice(take);
        }
        return true;
    }

    /// <summary>The peer went away (or we are tearing down): let the reader release its buffers and run
    /// the normal close bookkeeping, exactly as the SAEA client does on EOF.</summary>
    public void Close(SocketError error = SocketError.Success, Exception? fault = null)
        => OnReceiveCleanup(error, fault);

    /// <summary>
    /// Replies go straight out. Called under the base's write lock, which is what makes this safe against
    /// <see cref="Connection.Send"/>'s single-writer-until-Flush contract: local replies are produced on
    /// the loop thread and upstream replies on the leg's own thread, and the lock serialises the two.
    /// Send copies into library buffers, so <paramref name="frame"/> need not outlive the call.
    /// </summary>
    protected override void SendRawSynchronized(ReadOnlySpan<byte> frame)
    {
        DebugAssertLock();
        _conn.Send(frame);
    }
}
#endif
