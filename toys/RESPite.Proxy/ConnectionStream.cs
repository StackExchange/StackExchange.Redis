#if SOCKETSET
using SocketSets;

namespace RESPite.Proxy;

/// <summary>
/// A WRITE-ONLY <see cref="Stream"/> over a SocketSet <see cref="Connection"/>, so an
/// <c>InnerLeg</c>'s <c>BufferedStreamWriter</c> can drain to a SocketSet upstream connection instead of
/// a <see cref="WorkerNetworkStream"/>.
///
/// Why write-only is correct: on the SocketSet upstream path the leg's READS are push-fed from
/// <c>OnReceive</c> (the same seam the level-2 client uses), so nothing ever calls Read here — the old
/// pull loop (<c>StartReading(stream, ...)</c>) is simply not started. Implementing Read as
/// NotSupported makes any accidental revival of the pull loop fail loudly instead of hanging.
///
/// Threading: the only writer is the CycleBufferStreamWriter's drain (single active writer by its own
/// state machine), and <see cref="Connection.Send(ReadOnlySpan{byte})"/> is documented callable from any
/// thread and COPIES into library buffers — so the drain's page need not survive the call, and the
/// connection's single-writer-until-Flush contract is satisfied. Each drained page becomes one send;
/// coalescing of many RESP commands into a page has already happened upstream of this class, which is
/// exactly the batching that made fewer legs measure faster.
/// </summary>
internal sealed class ConnectionStream(Connection connection) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Send == stage + flush as ONE operation. Returns false only when the connection is closed; throw
        // so the drain records a faulted write rather than silently discarding the tail of a command
        // stream — a half-written RESP frame would desynchronise every later reply on this leg.
        if (!connection.Send(buffer)) throw new IOException("upstream SocketSet connection is closed");
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { } // every Write is already a flushed send
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(
        "reads are push-fed from OnReceive; the pull loop must not be started on this leg");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) connection.Close();
        base.Dispose(disposing);
    }
}
#endif
