using System;
using System.Buffers;
using System.Collections.Generic;
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
    private readonly bool _splitReadAndParse;
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
        bool isEncrypted = false,
        bool splitReadAndParse = true)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _isEncrypted = isEncrypted;
        _splitReadAndParse = splitReadAndParse;
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
    internal override void Write(ReadOnlySpan<byte> payload) => _writer.Write(payload);

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
    /// <summary>Whether the writer is on a thread of its own rather than the thread-pool.</summary>
    /// <remarks>Can change after connect: a switchable writer may transition to async.</remarks>
    public bool IsSyncWriter => _writer.IsSync;

    /// <summary>Whether the reader is on a thread of its own rather than the thread-pool.</summary>
    /// <remarks>Decided once, when reading starts, from the writer's mode at that moment.</remarks>
    public bool IsSyncReader { get; private set; }

    public override void Start(TransportReceiver receiver)
    {
        if (receiver is null) throw new ArgumentNullException(nameof(receiver));

        // THE READER FOLLOWS THE WRITER. A sync-mode writer owns its thread so that this connection does not
        // depend on the thread-pool (the DedicatedThreads opt-in, for an application whose pool is saturated);
        // reading with ReadAsync would put half of the connection straight back on it - the half that has to
        // run for any reply to be processed. So a sync writer gets a reader of its own too, blocking in Read.
        if (_writer.IsSync)
        {
            IsSyncReader = true;
            var thread = new Thread(() => ReadLoopSync(receiver))
            {
                IsBackground = true,
                Name = "SE.Redis Sync Reader",
            };
            thread.Start();
        }
        else if (_splitReadAndParse)
        {
            _ = Task.Run(() => ReadAndParseSeparatelyAsync(receiver));
        }
        else
        {
            _ = Task.Run(() => ReadLoopAsync(receiver));
        }
    }

    // ---- the read/parse split (the intent of StackExchange.Redis PR #3251, re-implemented for v4) ----------------

    /// <summary>How many fills may be outstanding between the filler and the parser.</summary>
    /// <remarks>
    /// Bounded, so a peer that produces faster than this process parses is held back through the socket rather
    /// than through this process's memory. At 64KiB each that is 256KiB of pooled buffers per connection - the
    /// configuration that was measured.
    /// </remarks>
    private const int FillDepth = 4;

    /// <summary>How much one read may return, and so how many replies one hand-off carries.</summary>
    /// <remarks>
    /// The hand-off has a fixed cost per buffer, so what matters is how many replies it is amortised over: at
    /// 16KiB an 8KiB reply amortises it over about two. Measured on <c>get-8k-conc64</c>: 16KiB 331,966 ops/s,
    /// 64KiB 348,747, 256KiB 346,683 - it saturates at 64KiB.
    /// </remarks>
    private const int FillBufferBytes = 64 * 1024;

    /// <summary>How many times the parser polls for a fill before parking; zero parks at once.</summary>
    /// <remarks>
    /// The filler is usually only microseconds ahead, so parking costs more than the wait it replaces - and this
    /// is what the split gave back on shapes with nothing to parse. Measured on <c>incr-conc64</c>: no spin
    /// 1,150,757 ops/s at 10.19us CPU/op, 200 spins 1,200,150 at 9.77us; 1,000 to 16,000 are within noise of
    /// 200. CPU per op goes DOWN: it removes park/unpark work rather than trading CPU for latency.
    /// </remarks>
    private const int FillSpin = 200;

    /// <summary>One filled buffer, and how much of it the read returned.</summary>
    private readonly struct Fill(byte[] buffer, int length)
    {
        public readonly byte[] Buffer = buffer;
        public readonly int Length = length;
    }

    /// <summary>Read from the socket independently of parsing, so the two no longer take turns.</summary>
    /// <remarks>
    /// <para>
    /// <b>The single loop reads, parses, then reads again.</b> Nothing reads while the receiver is busy, so a parse
    /// pass only ever sees what one physical read returned, and the socket sits idle meanwhile: the ceiling
    /// StackExchange.Redis#3251 diagnosed in the 3.x reader, which this transport inherited. Here a filler does
    /// nothing but socket-to-queue, and a parser drains the queue.
    /// </para>
    /// <para>
    /// <b>Simpler than #3251, because of the receiver's contract.</b> A payload is transport-owned and valid only for
    /// the call - the receiver copies anything it keeps - so a buffer can be reused the moment
    /// <see cref="TransportReceiver.OnReceived"/> returns, and the halves need only a bounded FIFO between them. Order
    /// is preserved by construction: one filler, one parser.
    /// </para>
    /// <para>
    /// <b>The parser completes operations, and must never run their continuations</b> - a continuation that blocks
    /// on a reply would block the very thread that delivers it. That is guaranteed one layer down: operations
    /// always complete asynchronously except where a waiting caller has claimed them (see <c>RespMessageBase</c>).
    /// </para>
    /// </remarks>
    private async Task ReadAndParseSeparatelyAsync(TransportReceiver receiver)
    {
        var free = new Stack<byte[]>(FillDepth);
        var filled = new Queue<Fill>(FillDepth);
        var sync = new object();
        var hasFilled = new SingleWaiterSemaphore(0); // the parser waits on this, and the filler's final wake
        var hasFree = new SingleWaiterSemaphore(FillDepth); // the filler waits on this

        // ONE registration for the connection's life wakes both waiters at shutdown, where a token per wait was a
        // registration (and an allocation) on every hand-off; a cancelled semaphore answers false, now and after
        using var shutdownRegistration = _shutdown.Token.Register(
            static state =>
            {
                var (filled, freed) = ((SingleWaiterSemaphore, SingleWaiterSemaphore))state!;
                filled.Cancel();
                freed.Cancel();
            },
            (hasFilled, hasFree));

        // the filler's own stop, linked to shutdown: ending the parser must stop the filler without cancelling the
        // transport's token, which the writer also uses and DisposeAsync owns
        using var stopFiller = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var stop = stopFiller.Token;

        for (var i = 0; i < FillDepth; i++) free.Push(ArrayPool<byte>.Shared.Rent(FillBufferBytes));

        Exception? fault = null;
        var completed = false;

        var filler = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    if (!await hasFree.WaitAsync().ConfigureAwait(false)) break; // stopping

                    byte[] buffer;
                    lock (sync) buffer = free.Pop();

#if NET6_0_OR_GREATER
                    var read = await _stream.ReadAsync(buffer.AsMemory(), stop).ConfigureAwait(false);
#else
                    var read = await _stream.ReadAsync(buffer, 0, buffer.Length, stop).ConfigureAwait(false);
#endif
                    if (read <= 0)
                    {
                        lock (sync) free.Push(buffer);
                        break; // orderly close
                    }

                    lock (sync) filled.Enqueue(new Fill(buffer, read));
                    hasFilled.Release();
                }
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                fault = ex;
            }
            catch
            {
                // stopping: disposing the stream is how a pending read is released, and its exception is ours
            }
            finally
            {
                // wake the parser so it drains what is queued and then stops
                lock (sync) completed = true;
                hasFilled.Release();
            }
        });

        try
        {
            var done = false;
            while (!done)
            {
                var acquired = false;
                for (var i = 0; i < FillSpin && !acquired; i++)
                {
                    acquired = hasFilled.TryWait();
                    if (!acquired) Thread.SpinWait(20);
                }

                // false only at shutdown, which is what the cancelled wait used to throw for, and handled the same way
                if (!acquired && !await hasFilled.WaitAsync().ConfigureAwait(false)) throw new OperationCanceledException();

                // one permit, so one buffer
                Fill next;
                lock (sync)
                {
                    if (filled.Count == 0)
                    {
                        done = completed; // the filler's final wake, or nothing yet
                        continue;
                    }

                    next = filled.Dequeue();
                }

                try
                {
                    receiver.OnReceived(new ReadOnlySpan<byte>(next.Buffer, 0, next.Length));
                    receiver.OnBatchEnd();
                }
                finally
                {
                    lock (sync) free.Push(next.Buffer);
                    hasFree.Release();
                }
            }
        }
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
        {
            fault ??= ex;
        }
        catch
        {
            // shutting down
        }
        finally
        {
            // stop the filler before reclaiming anything it might still be reading into
            try
            {
                stopFiller.Cancel(); // a pending read
                hasFree.Cancel(); // and a filler waiting for a buffer
            }
            catch (ObjectDisposedException)
            {
                // already torn down
            }

            try
            {
                await filler.ConfigureAwait(false);
            }
            catch
            {
                // its fault, if any, is already recorded; this is only the join
            }

            lock (sync)
            {
                while (filled.Count != 0) ArrayPool<byte>.Shared.Return(filled.Dequeue().Buffer);
                while (free.Count != 0) ArrayPool<byte>.Shared.Return(free.Pop());
            }

            // exactly once, and after the buffers are back, as the single loop does
            receiver.OnClosed(fault);
        }
    }

    private void ReadLoopSync(TransportReceiver receiver)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        Exception? fault = null;
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var read = _stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break; // orderly close

                receiver.OnReceived(new ReadOnlySpan<byte>(buffer, 0, read));
                receiver.OnBatchEnd();
            }
        }
        catch (Exception ex) when (!_shutdown.IsCancellationRequested)
        {
            fault = ex;
        }
        catch
        {
            // shutting down: disposing the stream is how a blocking Read is released, and its exception is ours
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            receiver.OnClosed(fault); // exactly once, after the buffer is returned - as the async loop does
        }
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
