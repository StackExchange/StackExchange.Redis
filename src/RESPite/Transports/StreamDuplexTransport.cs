using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Streams;

namespace RESPite.Transports;

/// <summary>
/// A <see cref="DuplexTransport"/> over any <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// One adapter covers TCP, TLS and unix sockets, because in .NET all three are a <see cref="Stream"/> -
/// which is why this is the shape rather than a socket-specific transport.
/// </para>
/// <para>
/// <b>The outbound half is not reimplemented.</b> It delegates to <see cref="BufferedStreamWriter"/>, the
/// existing writer with the synchronous flush and the sync/async transition. That piece is deliberately
/// kept: a transport that buffered for itself would be a second, worse copy of it, and would lose the
/// latency mode.
/// </para>
/// <para>
/// The inbound half is a read loop that hands each read straight to the receiver. Buffers are
/// <b>transport-owned and valid only for the call</b>, as the base class requires - the reader is reused
/// on the next pass, so anything that must outlive the call has to be copied by the receiver.
/// </para>
/// </remarks>
internal sealed class StreamDuplexTransport : DuplexTransport
{
    private readonly Stream _stream;
    private readonly BufferedStreamWriter _writer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly bool _isEncrypted;
    private int _disposed;

    /// <summary>Create a transport over a stream, which it takes ownership of.</summary>
    /// <param name="stream">The stream; disposed with this transport.</param>
    /// <param name="mode">How the outbound half should write.</param>
    /// <param name="bufferPool">The pool to stage outbound bytes in.</param>
    /// <param name="isEncrypted">Whether the stream provides its own encryption.</param>
    public StreamDuplexTransport(
        Stream stream,
        BufferedStreamWriter.WriteMode mode = BufferedStreamWriter.WriteMode.Default,
        MemoryPool<byte>? bufferPool = null,
        bool isEncrypted = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _isEncrypted = isEncrypted;
        _writer = BufferedStreamWriter.Create(mode, stream, bufferPool, _shutdown.Token);
    }

    /// <inheritdoc/>
    public override bool IsEncrypted => _isEncrypted;

    /// <inheritdoc/>
    public override Memory<byte> GetMemory(int sizeHint = 0) => _writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public override Span<byte> GetSpan(int sizeHint = 0) => _writer.GetSpan(sizeHint);

    /// <inheritdoc/>
    public override void Advance(int count) => _writer.Advance(count);

    /// <inheritdoc/>
    public override bool Flush()
    {
        _writer.Flush();
        return true;
    }

    /// <summary>Move the outbound half to asynchronous writing.</summary>
    /// <remarks>
    /// Exposed because the decision is the caller's: synchronous writing targets latency and is right
    /// while a connection is answering one command at a time, and wrong once it is carrying a backlog.
    /// </remarks>
    public bool TransitionToAsync() => _writer.TransitionToAsync();

    /// <inheritdoc/>
    public override void Start(TransportReceiver receiver)
    {
        if (receiver is null) throw new ArgumentNullException(nameof(receiver));
        _ = Task.Run(() => ReadLoopAsync(receiver));
    }

    private async Task ReadLoopAsync(TransportReceiver receiver)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        Exception? fault = null;
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
#if NET6_0_OR_GREATER
                var read = await _stream.ReadAsync(buffer.AsMemory(), _shutdown.Token).ConfigureAwait(false);
#else
                var read = await _stream.ReadAsync(buffer, 0, buffer.Length, _shutdown.Token).ConfigureAwait(false);
#endif
                if (read <= 0) break; // orderly close

                receiver.OnReceived(new ReadOnlySpan<byte>(buffer, 0, read));
                receiver.OnBatchEnd();
            }
        }
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
        {
            fault = ex;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            // exactly once, as the base class promises, and AFTER the buffer is returned so a receiver
            // that faults its outstanding work cannot be handed a buffer we are about to recycle
            receiver.OnClosed(fault);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

#if NET8_0_OR_GREATER
        await _shutdown.CancelAsync().ConfigureAwait(false);
#else
        _shutdown.Cancel();
#endif
        _writer.Complete();
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        await _stream.DisposeAsync().ConfigureAwait(false);
#else
        _stream.Dispose();
#endif
        _shutdown.Dispose();
    }
}
