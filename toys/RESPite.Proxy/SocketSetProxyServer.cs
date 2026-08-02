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
/// loop thread with no pipe and no hop. That is the LEVEL-2 client, and it is not this file.
///
/// So the intended use is to measure BOTH: level 1 gives the honest "same seam, different transport"
/// comparison, and level 2 minus level 1 is the pipe-bridge tax measured somewhere nobody can blame
/// Kestrel for it. Do not quote level 1 as what this transport can do.
///
/// KNOWN GAP vs the ASP.NET bridge, stated because it is worth a few percent and would otherwise look
/// like a transport result: that bridge defaults to a PINNED pipe pool (a measured win — unpinned cost
/// ~64 GCHandle pins per 256 KB response), and this uses <see cref="MemoryPool{T}.Shared"/>. The pinned
/// pool lives in the SocketSet.AspNetCore package, which is not referenced here on purpose (it would drag
/// in ASP.NET for a proxy that does not use it). Revisit before quoting any number.
/// </summary>
internal sealed class SocketSetProxyServer(ProxyServer proxy, SocketSetOptions options) : SocketSet(options)
{
    protected override void OnAccept(ref AcceptContext ctx)
    {
        // Two pipes, mirroring SocketSetConnection in the ASP.NET bridge:
        //   inbound  — the transport WRITES received bytes,  the proxy READS them
        //   outbound — the proxy WRITES replies,             the transport READS and sends them
        var inbound = new Pipe();
        var outbound = new Pipe();

        // The TRANSPORT side: we hand the transport the ends IT drives. Getting these two the wrong way
        // round deadlocks silently rather than failing, so they are named rather than positional.
        ctx.UsePipe(new DuplexPipe(input: outbound.Reader, output: inbound.Writer));

        // The APPLICATION side, which is what RunClientAsync expects (it writes to .Output and reads from
        // .Input) — the same view Kestrel hands ProxyHandler.
        var app = new DuplexPipe(input: inbound.Reader, output: outbound.Writer);

        // Fire and forget: ProxyClient owns its own lifetime and teardown (RecordConnectionFailed is
        // idempotent). We still observe the task, because an unobserved faulted Task here would surface
        // as a process-level UnobservedTaskException far from the cause.
        _ = RunAsync(app);
    }

    private async Task RunAsync(IDuplexPipe app)
    {
        try
        {
            await proxy.RunClientAsync(app).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[socketset-proxy] client faulted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }
}
#endif
