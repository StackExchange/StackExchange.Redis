#if SOCKETSET
using System.IO.Pipelines;
using System.Net;
using SocketSets;

namespace RESPite.Proxy;

/// <summary>
/// Hosts the proxy on a <c>SocketSet</c> transport (io_uring / epoll / IOCP / RIO / managed) instead of
/// the hand-rolled <see cref="WorkerPool"/> + <see cref="WorkerSocketAsyncEventArgs"/> path.
///
/// WHY THIS EXISTS, AND WHAT IT IS AND IS NOT.
/// This is the LEVEL-1 integration: it reuses the EXISTING <c>ProxyServer.RunClientAsync(IDuplexPipe)</c>
/// seam — the one originally written for Kestrel's <c>ConnectionHandler</c> — so <see cref="ProxyClient"/>
/// and all the RESP framing/routing are byte-for-byte unchanged, and the only variable is the transport.
/// That makes it a legitimate A/B against <see cref="SocketProxyClient"/>.
///
/// It is deliberately NOT the fast path. Bridging through two <see cref="Pipe"/>s is exactly the shape
/// that costs 24-40% in the ASP.NET bridge (SocketSet's AspNetDemo/RESULTS.md), and the whole argument for
/// hosting a proxy on this transport is that we own the flow end to end and can therefore frame ON the
/// loop thread with no pipe and no hop (RespReader inline in OnReceive). That is the LEVEL-2 client, and
/// it is not this file. Measuring both is the point: level2 - level1 is the pipe-bridge tax measured
/// somewhere nobody can blame Kestrel for it.
///
/// KNOWN GAP vs the ASP.NET bridge, stated because it is worth a few percent and would otherwise look
/// like a transport result: that bridge defaults to a PINNED pipe pool (a measured win — unpinned cost
/// ~64 GCHandle pins per 256 KB response), and this uses the default pool. The pinned pool lives in the
/// SocketSet.AspNetCore package, not referenced here on purpose.
/// </summary>
/// <param name="level2">
/// false = LEVEL 1: bridge through two Pipes into the existing RunClientAsync(IDuplexPipe) seam. Same
/// application code as the Kestrel path, deliberately NOT the fast path -- it pays a pipe write, a
/// ThreadPool hop, a Stream wrapper, a second pipe and a pump task PER REQUEST.
/// true  = LEVEL 2: frame with RespReader directly off the receive callback on the loop thread; no pipe,
/// no pump, no hop. See <see cref="SocketSetProxyClient"/>.
/// The DIFFERENCE between the two is the pipe-bridge tax measured somewhere nobody can blame Kestrel.
/// </param>
internal sealed class SocketSetProxyServer(ProxyServer proxy, SocketSetOptions options, bool level2)
    : SocketSet(options)
{
    /// <summary>Something that staged output during the current receive callback and wants one flush at
    /// the END of it, instead of a flush per frame.</summary>
    internal interface IDeferredFlush
    {
        void FlushDeferred();
    }

    // CALLBACK-GRANULARITY FLUSHING. At -P 16 a single receive callback frames up to 16 commands, and a
    // per-frame stage+flush turns that into 16 sends on the loop thread -- measured as the level-2/3
    // collapse at depth (L3 1.12M vs L1 2.46M GET) while level 1's pumps coalesce. So while a callback is
    // running on THIS thread, writers stage and register here, and the callback flushes each registrant
    // once on the way out -- the same event-loop-iteration batching Envoy does. Thread-static is correct
    // because deferral is only ever claimed and drained by the loop thread the callback runs on; any
    // OTHER thread (a worker-upstream reply, for instance) sees Deferring==false and sends immediately,
    // exactly as before.
    [ThreadStatic]
    private static List<IDeferredFlush>? t_pending;
    [ThreadStatic]
    private static bool t_deferring;

    internal static bool Deferring => t_deferring;

    internal static void RegisterDeferred(IDeferredFlush target)
    {
        var list = t_pending ??= new List<IDeferredFlush>(8);
        // O(n) contains on a tiny list beats allocating a set; a callback touches a handful of targets.
        if (!list.Contains(target)) list.Add(target);
    }

    private static void DrainDeferred()
    {
        var list = t_pending;
        if (list is null || list.Count == 0) return;
        foreach (var target in list) target.FlushDeferred();
        list.Clear();
    }
    /// <summary>Per-connection state, reachable from the loop thread via <c>Connection.UserToken</c> —
    /// the same bookkeeping the ASP.NET bridge does, and for the same reason: <c>OnClosed</c> is handed a
    /// bare <see cref="Connection"/> and has to find the pipes belonging to it.</summary>
    private sealed class ClientState(Pipe inbound, Pipe outbound, Connection conn)
    {
        public Pipe Inbound { get; } = inbound;
        public Pipe Outbound { get; } = outbound;
        public Connection Conn { get; } = conn;

        private int _tornDown;
        public bool ClaimTeardown() => Interlocked.Exchange(ref _tornDown, 1) == 0;
    }

    private sealed class LegSlot(int index)
    {
        public int Index { get; } = index;
    }

    private CountdownEvent? _legGate;
    private bool _affine;

    /// <summary>
    /// Establish the upstream legs as SOCKETSET OUTBOUND connections, replacing the
    /// <see cref="WorkerNetworkStream"/> + blocking-reader-thread model. Replies are then framed on a
    /// transport loop thread (no park/pulse chain), and outbound pages go through
    /// <see cref="ConnectionStream"/> → <c>Connection.Send</c>. Blocks until every leg is connected,
    /// because <c>GetNextLeg</c> does not tolerate holes — call BEFORE <c>Listen</c>.
    /// </summary>
    public void ConnectUpstream(EndPoint upstream, TimeSpan timeout, bool affine = false)
    {
        int count = proxy.UpstreamLegCount;
        _affine = affine;
        _legGate = new CountdownEvent(count);
        for (int i = 0; i < count; i++)
        {
            // Affine: leg i is PLACED ON SHARD i (the caller sized the leg array to the shard count), so
            // OnAccept can route each client to the leg sharing its loop thread — the Envoy shape: forward
            // and reply both stay on one thread. Non-affine: ordinary placement, legs land wherever.
            if (affine) ConnectShard(i, upstream, new LegSlot(i));
            else Connect(upstream, new LegSlot(i));
        }
        if (!_legGate.Wait(timeout))
            throw new TimeoutException($"only {count - _legGate.CurrentCount}/{count} upstream legs connected within {timeout}");
    }

    protected override void OnConnect(ref ConnectContext ctx)
    {
        if (ctx.Connection.UserToken is LegSlot slot)
        {
            var leg = proxy.CreateLeg(new ConnectionStream(ctx.Connection));
            leg.InitTransportRead();
            // Swap the token BEFORE signalling: OnReceive routes on it, and upstream bytes can arrive the
            // moment the first command is forwarded.
            ctx.Connection.UserToken = leg;
            proxy.InstallLeg(slot.Index, leg);
            _legGate?.Signal();
        }
    }

    protected override void OnAccept(ref AcceptContext ctx)
    {
        if (level2)
        {
            // No pipes, no pump, no UsePipe: the connection stays on the callback path and we frame in
            // OnReceive. UserToken carries the client so the receive/close callbacks can find it.
            // Affine mode: OnAccept runs ON the owning shard's loop thread for the loop-driven backends,
            // so CurrentShardIndex names the shard this client lives on — route it to the leg on the SAME
            // shard. -1 means "no affinity available here" (callback-driven backend); fall back rather
            // than fail, but that path is round-robin and loses the whole point, so the banner says which.
            int shardIdx;
            ctx.Connection.UserToken = _affine && (shardIdx = SocketSetShard.CurrentShardIndex) >= 0
                ? proxy.RunClient(ctx.Connection, shardIdx)
                : proxy.RunClient(ctx.Connection);
            return;
        }

        // Two pipes, mirroring SocketSetConnection in the ASP.NET bridge:
        //   inbound  — the transport WRITES received bytes,  the proxy READS them
        //   outbound — the proxy WRITES replies,             the transport READS and sends them
        var inbound = new Pipe();
        var outbound = new Pipe();
        var state = new ClientState(inbound, outbound, ctx.Connection);
        ctx.Connection.UserToken = state;

        // The TRANSPORT side: the ends the transport itself drives. Getting these the wrong way round
        // deadlocks silently rather than failing, so they are passed by name.
        ctx.UsePipe(new DuplexPipe(input: outbound.Reader, output: inbound.Writer));

        // The APPLICATION side, which is what RunClientAsync expects (writes to .Output, reads from
        // .Input) — the same view Kestrel hands ProxyHandler.
        _ = RunAsync(new DuplexPipe(input: inbound.Reader, output: outbound.Writer), state);
    }

    /// <summary>
    /// The peer went away. THIS IS NOT OPTIONAL: <c>PipeIoBridge</c> does NOT complete the pipe for you —
    /// the ASP.NET bridge completes its inbound writer explicitly in its own <c>OnClosed</c>, and without
    /// the equivalent here the proxy's read loop never terminates, so every disconnected client leaks a
    /// task and two pipes. A keep-alive benchmark would never surface it; connection churn would.
    /// </summary>
    protected override void OnClosed(Connection connection)
    {
        switch (connection.UserToken)
        {
            case ClientState s: s.Inbound.Writer.Complete(); break;
            case SocketSetProxyClient c: c.Close(); break;
            // An upstream leg died. Per the design note on RunClientAsync, losing ~1/N of clients is
            // accepted; the leg cleanup fails its in-flight owners. It is NOT re-established (spike).
            case ProxyServer.InnerLeg leg: leg.CloseFromTransport(); break;
        }
    }

    /// <summary>Level 2 only: frame the bytes that just arrived, on this loop thread. Level 1 never gets
    /// here — its connection is in pipe mode, so the transport drives the pipe instead of this callback.
    /// </summary>
    protected override void OnReceive(ref ReceiveContext ctx)
    {
        // Everything staged during this callback -- replies from commands framed here, and the upstream
        // leg's forwarded requests -- is flushed ONCE on the way out. The finally matters: a torn-down
        // client mid-callback must not strand earlier clients' staged bytes.
        t_deferring = true;
        try
        {
            if (ctx.Connection.UserToken is ProxyServer.InnerLeg leg)
            {
                if (!leg.Feed(ctx.Payload)) ctx.Connection.Close();
                return;
            }
            if (ctx.Connection.UserToken is SocketSetProxyClient c && !c.Feed(ctx.Payload))
            {
                // The client is torn down (protocol fault or close). Abortive close is right: a faulted
                // RESP stream has no reply worth preserving, and leaving it open is what made a parse
                // error present as a client HANG rather than an error.
                ctx.Connection.Close();
            }
        }
        finally
        {
            t_deferring = false;
            DrainDeferred();
        }
    }

    private async Task RunAsync(IDuplexPipe app, ClientState state)
    {
        try
        {
            await proxy.RunClientAsync(app).ConfigureAwait(false);
            Teardown(state, fault: null);
        }
        catch (Exception ex)
        {
            // A protocol fault (e.g. an INLINE command, which RespReader does not accept — it rejects the
            // 'P' of a literal "PING\r\n") lands here. Before this teardown existed, the socket was simply
            // LEFT OPEN: the client waited forever for a reply, so a parse error presented as a client
            // HANG rather than an error, and every faulted connection leaked. Fail loudly and close.
            Console.Error.WriteLine($"[socketset-proxy] client faulted: {ex.GetType().Name}: {ex.Message}");
            Teardown(state, fault: ex);
        }
    }

    private static void Teardown(ClientState state, Exception? fault)
    {
        if (!state.ClaimTeardown()) return;

        // Complete the ends WE own: the proxy is finished reading, and finished producing replies.
        // Completing the outbound writer is what lets the transport's pump drain and finish.
        try { state.Outbound.Writer.Complete(fault); } catch { }
        try { state.Inbound.Reader.Complete(fault); } catch { }

        if (fault is not null)
        {
            // Abortive close is CORRECT here and only here. SocketSet's Close() cancels queued sends, so
            // calling it on a NORMAL exit can discard a reply that has not gone out yet — which is why the
            // ASP.NET bridge deliberately does not Close() on its pump's normal path and only does so from
            // Abort(). A protocol fault has no reply worth preserving.
            try { state.Inbound.Writer.Complete(fault); } catch { }
            try { state.Conn.Close(); } catch { }
        }
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }
}
#endif
