using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Buffers;
using RESPite.Internal;
using RESPite.Messages;
using RESPite.Streams;

namespace StackExchange.Redis;

internal sealed partial class PhysicalConnection
{
    private long totalBytesReceived;

    // Guards every touch of _readBuffer once a background filler is in play (see ReadAllAsync): the filler
    // and the parse loop run as two independent tasks, and CycleBuffer itself has no internal thread-safety
    // (by design - see its own remarks), so external mutual exclusion is what makes that safe. Only ever held
    // for short, synchronous sections - never across an await (Monitor requires the same thread to exit that
    // entered, which an await's continuation cannot guarantee), and never across frame dispatch (see
    // ParseAvailableFrames for why that matters).
    private readonly object _readBufferLock = new();

    // Signals from the filler to the parse loop that there is new committed data (or that the filler has
    // stopped, successfully or not) worth waking up for. Bounded to a single pending signal - multiple
    // commits that land before the parser gets back around to waiting just coalesce into one wake, which
    // is fine: the parser always re-checks the buffer's actual state rather than trusting the signal count.
    private SemaphoreSlim? _fillSignal;
    private Exception? _fillerFault;

    // Filler lifecycle flags. Every *decision* based on them is taken under _readBufferLock (the filler's
    // lease/commit gates, and the stop handshake in OnParseLoopStopped/OnFillerStopped), which is what makes
    // "whichever of the two stops last recycles the buffer" race-free; volatile so that the cheap reads
    // outside the lock see a fresh value too.
    //
    // _fillerDone: the filler has genuinely, fully stopped - no read in flight, no lease it will still
    // commit. Set in the filler's own finally, so it is a stronger statement than "was asked to stop".
    private volatile bool _fillerDone;

    // _readLoopDoomed: the parse loop has given up, for any reason (parse fault, ForceReconnect, the
    // filler's own clean EOF or fault), and _readBuffer is on its way out. The filler checks this before
    // taking a lease and again before committing a completed read - once doomed, a read that completes is
    // simply dropped: the connection is being torn down, and nothing will ever parse it. The read itself is
    // left to complete naturally rather than raced/cancelled - it doesn't touch shared state until the commit
    // step this flag gates.
    private volatile bool _readLoopDoomed;

    // _fillerHandoff: ReadAllSync is transitioning to ReadAllAsync mid-flight and needs its filler thread to
    // stop *without losing anything*: a read that completes after this is set is still committed - the
    // connection is alive, and every byte of it is a reply somebody is waiting for - and only then does the
    // filler stop, leaving _readBuffer intact for ReadAllAsync's own filler to inherit. Dooming instead would
    // drop that read on the floor, and with it desynchronise every later reply on the connection.
    private volatile bool _fillerHandoff;

    internal static PhysicalConnection Dummy(Stream stream, BufferedStreamWriter.WriteMode writeMode = BufferedStreamWriter.WriteMode.Default)
        => new(ioStream: stream, writeMode: writeMode);

    private volatile ReadStatus _readStatus = ReadStatus.NotStarted;
    internal ReadStatus GetReadStatus() => _readStatus;

    private BufferedStreamWriter.WriteMode WriteMode { get; }

    /// <summary>
    /// Whether this connection is read by a thread of our own rather than by the thread-pool.
    /// </summary>
    /// <remarks>
    /// Deliberately mirrors the branching in <see cref="StartReading"/> rather than restating the rule: a
    /// transport-backed connection is in push mode and has no reader of either kind, and otherwise the reader
    /// follows the writer's mode.
    /// </remarks>
    internal bool IsSyncReader => _transport is null && _output is { IsSync: true };

    internal void StartReading(CancellationToken cancellation = default)
    {
        if (_transport is { } transport)
        {
            // push mode: no reader thread/Task -- the transport delivers into the parser directly
            StartTransportReading(transport);
            return;
        }

        if (cancellation.CanBeCanceled && cancellation != InputCancel)
        {
            cancellation.ThrowIfCancellationRequested();
            if (InputCancel.CanBeCanceled)
            {
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation, InputCancel).Token;
            }
        }
        else
        {
            cancellation = InputCancel;
        }

        if (_output is { IsSync: true })
        {
            StartReadingSync(this, cancellation);
            static void StartReadingSync(PhysicalConnection conn, CancellationToken cancellation)
            {
                // this method exists purely to limit capture context scope
                Thread thread = new Thread(() => conn.ReadAllSync(cancellation))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "SE.Redis Sync Reader",
                };
                thread.Start();
            }
        }
        else
        {
            StartReadAllAsync(cancellation);
        }
    }

    private void StartReadAllAsync(CancellationToken cancellationToken)
        => Task.Run(() => ReadAllAsync(cancellationToken)).RedisFireAndForget();

    private MemoryPool<byte>? ReaderBufferPool => BridgeCouldBeNull?.Multiplexer?.RawConfig?.ResponseBufferPool;

    private async Task ReadAllAsync(CancellationToken cancellationToken)
    {
        var tail = _ioStream ?? Stream.Null;
        bool transitioning = _readStatus is ReadStatus.TransitioningToAsync;
        if (!transitioning) // otherwise: preserve the state (and buffer contents) inherited from ReadAllSync
        {
            _readStatus = ReadStatus.Init;
            _readState = default;
            _readBuffer = CycleBuffer.Create(pool: ReaderBufferPool);
        }

        _fillerDone = false;
        _fillerFault = null;
        _readLoopDoomed = false;
        _fillerHandoff = false;

        // When inheriting a buffer from ReadAllSync, whatever its filler committed on the way out was signalled
        // to *that* loop's semaphore, not this one; start signalled so the first pass parses it (a no-op if
        // there was nothing) rather than sitting on it until the next reply happens to arrive.
        var fillSignal = _fillSignal = new SemaphoreSlim(transitioning ? 1 : 0, 1);

        // A background filler keeps reading into the shared buffer independently of the parse loop below,
        // so a connection that's busy parsing/dispatching still keeps accumulating data underneath it -
        // this is what lets a big batch build up per parse cycle under load, instead of every cycle
        // capping out at whatever one physical read returns (which is what a single, self-overlapping
        // read loop is still limited to: it can hide the *wait* for the next read behind parsing, but not
        // grow past one read's worth of buffer per cycle, because nothing keeps reading *while* parsing).
        // _readBufferLock is what makes sharing _readBuffer between the two tasks safe; see its remarks.
        var fillerTask = Task.Run(() => FillBufferAsync(tail, sync: false, cancellationToken), cancellationToken);
        try
        {
            while (true)
            {
                _readStatus = ReadStatus.ReadAsync;
                await fillSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

                _readStatus = ReadStatus.TryParseResult;
                ParseAvailableFrames();

                if (ForceReconnect) break;
                if (_fillerDone)
                {
                    if (_fillerFault is { } fault) ExceptionDispatchInfo.Capture(fault).Throw(); // keep the read site's trace
                    break; // clean EOF; the filler already established there is nothing more coming
                }
            }

            _readStatus = ReadStatus.ProcessBufferComplete;
            OnParseLoopStopped(); // stops the filler; recycles the buffer now, or leaves that to the filler
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (OperationCanceledException) when (_readStatus is ReadStatus.ReadAsync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        }
        finally
        {
            // Redundant with the clean-exit path above (harmless: it is idempotent), and the only exit for
            // every other path - keeps each of them self-contained rather than relying on that having run.
            OnParseLoopStopped();

            // the filler also observes the same cancellationToken (via the disposing/reconnecting
            // machinery that drives it elsewhere) and will wind itself down on its own; this just makes
            // sure a late fault doesn't surface as an unobserved task exception, matching the
            // fire-and-forget convention used elsewhere on this teardown path.
            fillerTask.RedisFireAndForget();
        }
    }

    /// <summary>
    /// Independently keeps <see cref="_readBuffer"/> full: reads, commits under <see cref="_readBufferLock"/>,
    /// signals <see cref="_fillSignal"/>, repeats - with no knowledge of (or dependency on) whether the parse
    /// loop has caught up. Never has more than one outstanding, uncommitted read at a time, which is what keeps
    /// this safe without CycleBuffer needing any concurrency-awareness of its own: the existing single-writer
    /// lease/commit bookkeeping (see CycleBuffer.Commit's remarks on CopyDueToDiscardDuringWrite) already
    /// tolerates a discard landing on the parse-loop side between this method's lease and its own commit of
    /// that same lease - that guarantee doesn't depend on which thread the discard happens on, only on nothing
    /// *else* taking a second, overlapping lease meanwhile, and nothing here ever does.
    /// </summary>
    /// <remarks>
    /// One body serves both readers. With <paramref name="sync"/> it runs on <see cref="ReadAllSync"/>'s
    /// dedicated filler thread and issues blocking reads; there is then no await on the path at all, so the
    /// whole loop runs to completion on that thread and the returned task is already complete. Without it, it
    /// is <see cref="ReadAllAsync"/>'s filler task. Everything else - the lease/commit gates, the stop
    /// handshake, the signalling - is identical, and deliberately exists exactly once.
    /// <para>
    /// A second thread/task is what makes this decoupling possible today: the parse loop's own thread is
    /// busy dispatching (result matching, TCS completion, user callbacks) while this one keeps reading, and
    /// that dispatch work is exactly what a single thread can't do *and* keep reading at the same time. If a
    /// future architecture (e.g. a v4 that moves parsing and dispatch out of the read loop entirely, onto
    /// whatever consumes the parsed results) shrinks the read loop's own per-cycle cost down to "read plus
    /// minimal bookkeeping," it may be worth re-testing whether a single thread can then sustain the same
    /// batching depth without this filler split at all - the mechanism that made the second thread necessary
    /// here is specifically the competition between dispatch and re-issuing reads on one thread; remove that
    /// competition and the calculus might change. Untested hypothesis, not a finding - flagged for whoever
    /// next revisits this once that groundwork exists.
    /// </para>
    /// </remarks>
    private async Task FillBufferAsync(Stream tail, bool sync, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                Memory<byte> buffer;
                lock (_readBufferLock)
                {
                    // Checked *inside* the same lock that guards every _readBuffer touch (here and in the
                    // parse loop's own recycle), not just before it: checking outside the lock leaves a
                    // window between "saw false" and "took the lock" for the parse loop to doom-and-recycle
                    // in between, so this thread would then touch a buffer that's gone - or, worse, still get
                    // a lease into a segment that's just been handed back to the shared spare pool for some
                    // *other* connection's CycleBuffer to reuse. Checking under the same lock makes the two
                    // mutually exclusive.
                    if (_readLoopDoomed | _fillerHandoff) return; // nothing in flight: stop before taking a lease
                    buffer = _readBuffer.GetUncommittedMemory();
                }

                int read;
                try
                {
                    if (sync)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
#if NET
                        read = tail.Read(buffer.Span);
#else
                        read = tail.Read(buffer);
#endif
                    }
                    else
                    {
                        read = await tail.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (EndOfStreamException)
                {
                    read = 0; // some streams throw rather than returning 0; treat identically
                }

                if (read <= 0)
                {
                    return; // clean EOF - _fillerDone is set in the finally block below
                }

                bool handoff;
                lock (_readBufferLock)
                {
                    // as above - checked again now the read has had time to run, under the same lock as
                    // the Commit() it guards
                    if (_readLoopDoomed) return; // torn down while the read was in flight; nothing will ever parse it
                    _readBuffer.Commit(read);

                    // counted here (rather than by the parse loop) because this is where the bytes arrive, and
                    // inside the lock so that the count is never behind a reply the parser has already handed
                    // out; read from arbitrary threads by the timeout/failure diagnostics (see GetBytes),
                    // hence interlocked rather than a plain add
                    Interlocked.Add(ref totalBytesReceived, read);
                    handoff = _fillerHandoff; // decided under the same lock as the commit it must follow
                }

                UpdateLastReadTime();
#if DEBUG
                if (sync)
                {
                    DebugCounters.OnSyncRead(read);
                }
                else
                {
                    DebugCounters.OnAsyncRead(read, inline: false);
                }
#endif
                SignalFillProgress();
                if (handoff) return; // committed, so nothing is lost; ReadAllAsync's filler takes over from here
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown path (connection tearing down) - not a fault worth reporting
        }
        catch (Exception ex)
        {
            _fillerFault = ex;
        }
        finally
        {
            OnFillerStopped(); // recycles the buffer if the parse loop has already gone; else leaves that to it
            SignalFillProgress(); // wake the parse loop even if nothing new arrived, so it notices completion
        }
    }

    // The parse loop and the filler stop independently, and _readBuffer must not be recycled (Release() hands
    // segments back to the shared static spare, and their leases back to the pool) while either could still
    // touch it: the filler may have a read in flight into a segment, and the parse loop may still be
    // dispatching frames out of one. So whichever of the two stops *last* is the one that recycles, decided
    // under _readBufferLock so that exactly one of them observes the other as already stopped. The filler
    // stopping is what makes this safe on the common teardown shape: on ForceReconnect it is nearly always
    // blocked in a read that only RecordConnectionFailed's socket shutdown will unblock - so the parse loop
    // dooms it, tears the socket down, and the filler recycles on its way out. Merely dropping the field
    // instead (as an earlier iteration did whenever the filler was still mid-read) leaks the leases of a
    // custom ConfigurationOptions.ResponseBufferPool on every reconnect.
    private void OnParseLoopStopped()
    {
        lock (_readBufferLock)
        {
            _readLoopDoomed = true;
            if (_fillerDone) ReleaseReadBufferInsideLock();
        }
    }

    private void OnFillerStopped()
    {
        lock (_readBufferLock)
        {
            _fillerDone = true;
            if (_readLoopDoomed) ReleaseReadBufferInsideLock();
        }
    }

    private void ReleaseReadBufferInsideLock()
    {
        Debug.Assert(Monitor.IsEntered(_readBufferLock), "must hold _readBufferLock");
        _readBuffer.Release(); // a no-op once already wiped, so running twice (clean exit + finally) is harmless
        _readBuffer = default;
    }

    // Only ever called from the filler's own single logical thread of control (FillBufferAsync's task, or
    // ReadAllSync's filler thread running that same method) - never concurrently with itself - which is what
    // makes the CurrentCount==0-then-Release() check below race-free. That's a property of the call sites,
    // not of this method, so a future second caller needs to be reasoned about afresh rather than assumed
    // safe. The semaphore is never disposed (it holds no unmanaged resources), just abandoned when the loop
    // it belongs to exits, so an old filler's last signal after a handoff simply lands nowhere.
    private void SignalFillProgress()
    {
        var sem = _fillSignal;
        if (sem is not null && sem.CurrentCount == 0)
        {
            try
            {
                sem.Release();
            }
            catch (SemaphoreFullException)
            {
                // would mean the single-caller invariant above was violated by a second, concurrent
                // caller racing the check-then-Release above; benign here (the semaphore ends up
                // signaled either way, which was the only goal), but Debug.Fail so a future violation
                // surfaces immediately in debug/test runs instead of silently vanishing.
                Debug.Fail("SignalFillProgress: concurrent Release race - single-caller invariant violated");
            }
        }
    }

    /// <summary>
    /// Sync-mode counterpart of the async filler/parser split in <see cref="ReadAllAsync"/>: a dedicated
    /// filler thread (running the same <see cref="FillBufferAsync"/> body, in blocking mode) keeps reading into
    /// <see cref="_readBuffer"/> independently of this (parser) thread, synchronized the same way
    /// (<see cref="_readBufferLock"/>, <see cref="_fillSignal"/>, <see cref="_readLoopDoomed"/>). This pays for
    /// the same batching-depth recovery with a second dedicated thread, on top of the reader thread that
    /// <see cref="StartReading"/> already spins up for sync/<see cref="ConnectionMultiplexer.DedicatedThreads"/>
    /// connections - a cost callers of that mode already accept in exchange for not risking ThreadPool starvation.
    /// </summary>
    /// <remarks>
    /// Unlike the earlier single-threaded version this replaced, transitioning to async mid-flight (see
    /// <see cref="ShouldTransitionToAsync"/>) needs the filler thread to have genuinely stopped touching
    /// <see cref="_readBuffer"/> before <see cref="ReadAllAsync"/> starts its own filler on the same buffer -
    /// two independent fillers racing the same <see cref="CycleBuffer"/> would violate the single-writer
    /// assumption both designs otherwise rely on. But it must stop *gracefully*: the connection is alive, so a
    /// read that is in flight at that moment is a real reply that must still be committed, not dropped - hence
    /// <see cref="_fillerHandoff"/> rather than <see cref="_readLoopDoomed"/> for that path, and the explicit
    /// <c>filler.Join()</c> to sequence the handoff after it. The stream stays open throughout, so a filler
    /// blocked in a read unblocks the normal way, on the next reply; this thread waits it out, which is the
    /// (rare, and accepted) cost of a transition on an idle connection.
    /// </remarks>
    private void ReadAllSync(CancellationToken cancellationToken)
    {
        _readStatus = ReadStatus.Init;
        _readState = default;
        _readBuffer = CycleBuffer.Create(pool: ReaderBufferPool);
        _fillerDone = false;
        _fillerFault = null;
        _readLoopDoomed = false;
        _fillerHandoff = false;
        var fillSignal = _fillSignal = new SemaphoreSlim(0, 1);

        var tail = _ioStream ?? Stream.Null;
        Thread filler = new Thread(() => FillBufferAsync(tail, sync: true, cancellationToken).GetAwaiter().GetResult())
        {
            IsBackground = true,

            // Deliberately Normal, not AboveNormal like the parser thread below: the parser does the
            // higher-value, latency-sensitive work (result matching, TCS completion, user callbacks), so
            // under CPU contention it should win any priority-based scheduling contest against this
            // thread, whose job - reading ahead - can afford to lose a beat without anyone waiting on it.
            Priority = ThreadPriority.Normal,
            Name = "SE.Redis Sync Filler",
        };
        filler.Start();

        try
        {
            while (true)
            {
                _readStatus = ReadStatus.ReadSync;
                fillSignal.Wait(cancellationToken);

                _readStatus = ReadStatus.TryParseResult;
                ParseAvailableFrames();

                if (ForceReconnect) break;
                if (ShouldTransitionToAsync()) return; // finally below hands off once the filler has stopped
                if (_fillerDone)
                {
                    if (_fillerFault is { } fault) ExceptionDispatchInfo.Capture(fault).Throw(); // keep the read site's trace
                    break; // clean EOF; the filler already established there is nothing more coming
                }
            }

            _readStatus = ReadStatus.ProcessBufferComplete;
            OnParseLoopStopped(); // stops the filler; recycles the buffer now, or leaves that to the filler
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (OperationCanceledException) when (_readStatus is ReadStatus.ReadSync)
        {
            _readStatus = ReadStatus.RanToCompletion;
            RecordConnectionFailed(ConnectionFailureType.SocketClosed);
        }
        catch (Exception ex)
        {
            _readStatus = ReadStatus.Faulted;
            RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        }
        finally
        {
            if (_readStatus is ReadStatus.TransitioningToAsync)
            {
                // hand off rather than tear down; see this method's remarks
                _fillerHandoff = true;
                filler.Join(); // fully stopped - not merely asked to - before another filler touches the buffer
                StartReadAllAsync(cancellationToken);
            }
            else
            {
                OnParseLoopStopped(); // as in ReadAllAsync: idempotent, and the only exit for the non-clean paths
            }
        }
    }

    private bool ShouldTransitionToAsync()
    {
        if (_output is { IsSync: false })
        {
            _readStatus = ReadStatus.TransitioningToAsync;
            return true;
        }
        return false;
    }

    private bool ForceReconnect => BridgeCouldBeNull?.NeedsReconnect == true;

    private static IMemoryOwner<byte>? SharedNoLease;

    private CycleBuffer _readBuffer;
    private RespScanState _readState = default;

    private long GetReadCommittedLength()
    {
        try
        {
            var len = _readBuffer.GetCommittedLength();
            return len < 0 ? -1 : len;
        }
        catch
        {
            return -1;
        }
    }

    // Push-mode (transport) entry point; the pull loops commit from their filler and parse from their own
    // loop instead. No filler exists in push mode, so no lock is needed around the commit here.
    private bool CommitAndParseFrames(int bytesRead)
    {
        if (bytesRead <= 0)
        {
            return false;
        }

        totalBytesReceived += bytesRead;
        Debug.Assert(
            bytesRead <= _readBuffer.UncommittedAvailable,
            $"Insufficient bytes in {nameof(CommitAndParseFrames)}; got {bytesRead}, Available={_readBuffer.UncommittedAvailable}");
        _readBuffer.Commit(bytesRead);

        ParseAvailableFrames();
        return true;
    }

    /// <summary>
    /// Parses and dispatches as many complete RESP frames as are currently committed in <see cref="_readBuffer"/>,
    /// discarding what it consumes. A no-op if there is nothing new since the last call (which can legitimately
    /// happen when called from the background-filler design in <see cref="ReadAllAsync"/> - e.g. a wake that
    /// turns out to carry no new data, such as the filler's final "I'm done" signal).
    /// </summary>
    private void ParseAvailableFrames()
    {
        ref RespScanState state = ref _readState; // avoid a ton of ldarg0

        // Snapshot the committed region under the lock, then parse and dispatch *outside* it. Holding the lock
        // across dispatch (result matching, TCS completion, user callbacks) would stall the filler's next
        // Commit - and so its next read - for the whole pass, capping read-ahead at one outstanding read and
        // leaving the kernel's socket buffer, not ours, in charge of batching: the opposite of the point of
        // the split. Parsing outside is safe because the filler only ever *appends*: committed bytes are never
        // moved or rewritten by it (a discard-during-write copy targets bytes already discarded), and the
        // sequence/span handed out here stays valid until the discard below - which, like every other touch
        // of the buffer's own bookkeeping, is back under the lock. The transport push path has no filler and
        // simply takes the lock uncontended.
        bool single;
        ReadOnlySpan<byte> fullSpan;
        ReadOnlySequence<byte> fullSequence = default;
        lock (_readBufferLock)
        {
            Debug.Assert(_readBuffer.GetCommittedLength() >= 0, "multi-segment running-indices are corrupt");
            single = _readBuffer.TryGetCommitted(out fullSpan);
            if (!single) fullSequence = _readBuffer.GetAllCommitted();
        }
#if PARSE_DETAIL
        string src = $"parse ({(single ? fullSpan.Length : fullSequence.Length)}-{state.TotalBytes})";
        try
#endif
        {
            var scanner = RespFrameScanner.Default;

            OperationStatus status = OperationStatus.NeedMoreData;
            if (single)
            {
                int fullyConsumed = 0;
                var toParse = fullSpan.Slice((int)state.TotalBytes); // skip what we've already parsed
                OnDetailLog($"parsing {toParse.Length} bytes, single buffer");

                if (toParse.IsEmpty) return; // nothing new since the last call
                while (true)
                {
#if PARSE_DETAIL
                    src += $",span {toParse.Length}";
#endif
                    int totalBytesBefore = (int)state.TotalBytes;
                    if (toParse.Length < RespScanState.MinBytes
                        || (status = scanner.TryRead(ref state, toParse)) != OperationStatus.Done)
                    {
                        break;
                    }

                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;
#if PARSE_DETAIL
                    src += $",frame {bytes}";
#endif
                    // send the frame somewhere (note this is the *full* frame, not just the bit we just parsed)
                    OnDetailLog($"found {state.Prefix} frame, {bytes} bytes");
                    OnResponseFrame(state.Prefix, fullSpan.Slice(fullyConsumed, bytes), ref SharedNoLease);
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }
                OnDetailLog($"discarding {fullyConsumed} bytes");
                lock (_readBufferLock)
                {
                    _readBuffer.DiscardCommitted(fullyConsumed);
                }
            }
            else // the same thing again, but this time with multi-segment sequence
            {
                if (fullSequence.IsEmpty) return; // nothing committed at all yet
                Debug.Assert(!fullSequence.IsSingleSegment, "non-trivial sequence expected");

                long fullyConsumed = 0;
                var toParse = fullSequence.Slice((int)state.TotalBytes); // skip what we've already parsed
                OnDetailLog($"parsing {toParse.Length} bytes, multi-buffer");

                if (toParse.IsEmpty) return; // nothing new since the last call
                while (true)
                {
#if PARSE_DETAIL
                    src += $",ros {toParse.Length}";
#endif
                    int totalBytesBefore = (int)state.TotalBytes;
                    if (toParse.Length < RespScanState.MinBytes
                        || (status = scanner.TryRead(ref state, toParse)) != OperationStatus.Done)
                    {
                        break;
                    }

                    Debug.Assert(
                        state is
                        {
                            IsComplete: true, TotalBytes: >= RespScanState.MinBytes, Prefix: not RespPrefix.None
                        },
                        "Invalid RESP read state");

                    // extract the frame
                    var bytes = (int)state.TotalBytes;
#if PARSE_DETAIL
                    src += $",frame {bytes}";
#endif
                    // send the frame somewhere (note this is the *full* frame, not just the bit we just parsed)
                    OnDetailLog($"found {state.Prefix} frame, {bytes} bytes");
                    OnResponseFrame(state.Prefix, fullSequence.Slice(fullyConsumed, bytes));
                    UpdateBufferStats(bytes, toParse.Length);

                    // update our buffers to the unread potions and reset for a new RESP frame
                    fullyConsumed += bytes;
                    toParse = toParse.Slice(bytes - totalBytesBefore); // move past the extra bytes we just read
                    state = default;
                    status = OperationStatus.NeedMoreData;
                }

                OnDetailLog($"discarding {fullyConsumed} bytes");
                lock (_readBufferLock)
                {
                    _readBuffer.DiscardCommitted(fullyConsumed);
                }
            }

            if (status != OperationStatus.NeedMoreData)
            {
                ThrowStatus(status);

                static void ThrowStatus(OperationStatus status) =>
                    throw new InvalidOperationException($"Unexpected operation status: {status}");
            }
        }
#if PARSE_DETAIL
        catch (Exception ex)
        {
            OnDetailLog($"{nameof(ParseAvailableFrames)}: {ex.Message}");
            OnDetailLog(src);
            if (Debugger.IsAttached) Debugger.Break();
            throw new InvalidOperationException($"{src} lead to {ex.Message}", ex);
        }
#endif
    }

    [Conditional("PARSE_DETAIL")]
    internal void OnDetailLog(string message)
    {
#if PARSE_DETAIL
        message = $"[{id}] {message}";
        lock (LogLock)
        {
            Console.WriteLine(message);
            Debug.WriteLine(message);
            File.AppendAllText("verbose.log", message + Environment.NewLine);
        }
#endif
    }

#if PARSE_DETAIL
    private static int s_id;
    private readonly int id = Interlocked.Increment(ref s_id);
    private static readonly object LogLock = new();
#endif

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySequence<byte> payload)
    {
        if (payload.IsSingleSegment)
        {
            OnResponseFrame(prefix, payload.FirstSpan, ref SharedNoLease);
        }
        else
        {
            var len = checked((int)payload.Length);
            var memoryPool = BridgeCouldBeNull?.Multiplexer.RawConfig.ResponseBufferPool ?? MemoryPool<byte>.Shared;
            var memoryOwner = memoryPool.Rent(len);
            Span<byte> oversized = memoryOwner.Memory.Span.Slice(0, len);

            payload.CopyTo(oversized);

            OnResponseFrame(prefix, oversized, ref memoryOwner);

            memoryOwner?.Dispose();
        }
    }

    private void UpdateBufferStats(int lastResult, long inBuffer)
    {
        // Track the last result size *after* processing for the *next* error message
        bytesInBuffer = inBuffer;
        bytesLastResult = lastResult;
    }

    private void OnResponseFrame(RespPrefix prefix, ReadOnlySpan<byte> frame, ref IMemoryOwner<byte>? memoryOwner)
    {
        DebugValidateSingleFrame(frame);
        _readStatus = ReadStatus.MatchResult;

        switch (prefix)
        {
            case RespPrefix.Push: // explicit RESP3 push message
            case RespPrefix.Array when (_protocol is RedisProtocol.Resp2 & connectionType is ConnectionType.Subscription)
                                       && !IsArrayPong(frame): // could be a RESP2 pub/sub payload
                // out-of-band; pub/sub etc
                switch (OnOutOfBand(frame, ref memoryOwner))
                {
                    case OutOfBandResult.Handled:
                        OnDetailLog($"out-of-band message, not dequeuing: {prefix}");
                        return;
                    case OutOfBandResult.NotRecognized when prefix is RespPrefix.Push:
                        // a RESP3 push frame is out-of-band *by definition*; if we don't recognize it
                        // (newer server, or a feature we don't implement) we drop it - matching it to a
                        // pending command would desynchronize the entire response stream
                        OnDetailLog($"unrecognized out-of-band message, dropped: {prefix}");
                        return;
                }
                break;
        }

        // request/response; match to inbound
        MatchNextResult(frame);

        static bool IsArrayPong(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= sizeof(ulong))
            {
                var hash = AsciiHash.HashCS(payload);
                switch (hash)
                {
                    case ArrayPong_LC_Bulk.HashCS when payload.StartsWith(ArrayPong_LC_Bulk.U8):
                    case ArrayPong_UC_Bulk.HashCS when payload.StartsWith(ArrayPong_UC_Bulk.U8):
                    case ArrayPong_LC_Simple.HashCS when payload.StartsWith(ArrayPong_LC_Simple.U8):
                    case ArrayPong_UC_Simple.HashCS when payload.StartsWith(ArrayPong_UC_Simple.U8):
                        var reader = new RespReader(payload); // no services needed: never leases
                        return reader.SafeTryMoveNext() // have root
                               && reader.Prefix == RespPrefix.Array // root is array
                               && reader.SafeTryMoveNext() // have first child
                               && (reader.IsInlneCpuUInt32(pong) || reader.IsInlneCpuUInt32(PONG)); // pong
                }
            }

            return false;
        }
    }

    internal enum PushKind
    {
        [AsciiHash("")]
        None,
        [AsciiHash("message")]
        Message,
        [AsciiHash("pmessage")]
        PMessage,
        [AsciiHash("smessage")]
        SMessage,
        [AsciiHash("subscribe")]
        Subscribe,
        [AsciiHash("psubscribe")]
        PSubscribe,
        [AsciiHash("ssubscribe")]
        SSubscribe,
        [AsciiHash("unsubscribe")]
        Unsubscribe,
        [AsciiHash("punsubscribe")]
        PUnsubscribe,
        [AsciiHash("sunsubscribe")]
        SUnsubscribe,

        // the maintenance-notification family; these are *not* pub/sub - element 1 is a sequence number
        // rather than a channel, so they must be dispatched before anything reads a channel name. The specs
        // write them uppercase while the pub/sub kinds above are lowercase, hence the case-insensitive match
        [AsciiHash("MOVING")]
        Moving,
        [AsciiHash("MIGRATING")]
        Migrating,
        [AsciiHash("MIGRATED")]
        Migrated,
        [AsciiHash("FAILING_OVER")]
        FailingOver,
        [AsciiHash("FAILED_OVER")]
        FailedOver,
        [AsciiHash("SMIGRATING")]
        SlotMigrating,
        [AsciiHash("SMIGRATED")]
        SlotMigrated,
    }

    internal static partial class PushKindMetadata
    {
        /// <summary>
        /// Identifies a push frame from its first element.
        /// </summary>
        /// <remarks>
        /// Case-insensitive: the pub/sub kinds are lowercase on the wire and the maintenance kinds are
        /// uppercase, and no specification anywhere is careful about it - so don't bake in an assumption.
        /// </remarks>
        [AsciiHash(CaseSensitive = false)]
        internal static partial bool TryParse(ReadOnlySpan<byte> value, out PushKind result);
    }

    /// <summary>
    /// The outcome of inspecting a possible out-of-band frame.
    /// </summary>
    private enum OutOfBandResult
    {
        /// <summary>
        /// We could not identify the frame; for a RESP3 push frame this means "drop it".
        /// </summary>
        NotRecognized,

        /// <summary>
        /// Consumed as an out-of-band message; nothing further to do.
        /// </summary>
        Handled,

        /// <summary>
        /// Recognized, but it is the reply to a command we sent (subscribe/unsubscribe confirmations
        /// arrive as push frames in RESP3), so normal request/response matching must complete it.
        /// </summary>
        MatchToCommand,
    }

    internal static ReadOnlySpan<byte> StackCopyLengthChecked(scoped in RespReader reader, Span<byte> buffer)
    {
        var len = reader.CopyTo(buffer);
        if (len == buffer.Length && reader.ScalarLength() > len) return default; // too small
        return buffer.Slice(0, len);
    }

    private OutOfBandResult OnOutOfBand(ReadOnlySpan<byte> payload, ref IMemoryOwner<byte>? memoryOwner)
    {
        var muxer = BridgeCouldBeNull?.Multiplexer;
        if (muxer is null) return OutOfBandResult.Handled; // consume it blindly

        var reader = new RespReader(payload, _readerServices);

        // read the message kind from the first element
        if (reader.SafeTryMoveNext() & reader.IsAggregate & !reader.IsStreaming
            && reader.AggregateLength() >= 2
            && (reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError))
        {
            PushKind kind;
            unsafe
            {
                if (!reader.TryParseScalar(&PushKindMetadata.TryParse, out kind)) kind = PushKind.None;
            }

            if (kind is >= PushKind.Moving and <= PushKind.SlotMigrated)
            {
                // not pub/sub: dispatch before anything tries to read element 1 as a channel name
                return OnMaintenanceNotification(muxer, kind, ref reader);
            }

            RedisChannel.RedisChannelOptions channelOptions = kind switch
            {
                PushKind.PMessage or PushKind.PSubscribe or PushKind.PUnsubscribe => RedisChannel.RedisChannelOptions.Pattern,
                PushKind.SMessage or PushKind.SSubscribe or PushKind.SUnsubscribe => RedisChannel.RedisChannelOptions.Sharded,
                _ => RedisChannel.RedisChannelOptions.None,
            };

            static bool TryMoveNextString(ref RespReader reader)
                => reader.SafeTryMoveNext() & reader.IsInlineScalar &
                   reader.Prefix is RespPrefix.BulkString or RespPrefix.SimpleString;

            if (kind is PushKind.None || !TryMoveNextString(ref reader)) return OutOfBandResult.NotRecognized;

            // the channel is always the second element
            var subscriptionChannel = AsRedisChannel(reader, channelOptions);

            switch (kind)
            {
                case (PushKind.Message or PushKind.SMessage) when reader.SafeTryMoveNext():
                    _readStatus = kind is PushKind.Message ? ReadStatus.PubSubMessage : ReadStatus.PubSubSMessage;

                    // special-case the configuration change broadcasts (we don't keep that in the usual pub/sub registry)
                    var configChanged = muxer.ConfigurationChangedChannel;
                    if (configChanged != null && reader.Prefix is RespPrefix.BulkString or RespPrefix.SimpleString && subscriptionChannel.Span.SequenceEqual(configChanged))
                    {
                        EndPoint? blame = null;
                        try
                        {
                            if (!reader.Is("*"u8))
                            {
                                // We don't want to fail here, just trying to identify
                                _ = Format.TryParseEndPoint(reader.ReadString(), out blame);
                            }
                        }
                        catch
                        {
                            /* no biggie */
                        }

                        Trace("Configuration changed: " + Format.ToString(blame));
                        _readStatus = ReadStatus.Reconfigure;
                        muxer.ReconfigureIfNeeded(blame, true, "broadcast");
                    }

                    // invoke the handlers
                    if (!subscriptionChannel.IsNull)
                    {
                        Trace($"{kind}: {subscriptionChannel}");
                        OnMessage(muxer, subscriptionChannel, subscriptionChannel, in reader);
                    }

                    return OutOfBandResult.Handled;
                case PushKind.PMessage when TryMoveNextString(ref reader):
                    _readStatus = ReadStatus.PubSubPMessage;

                    var messageChannel = AsRedisChannel(reader, RedisChannel.RedisChannelOptions.None);
                    if (!messageChannel.IsNull && reader.SafeTryMoveNext())
                    {
                        Trace($"{kind}: {messageChannel} via {subscriptionChannel}");
                        OnMessage(muxer, subscriptionChannel, messageChannel, in reader);
                    }

                    return OutOfBandResult.Handled;
                case PushKind.SUnsubscribe when !PeekChannelMessage(RedisCommand.SUNSUBSCRIBE, subscriptionChannel):
                    // then it was *unsolicited* - this probably means the slot was migrated
                    // (otherwise, we'll let the command-processor deal with it)
                    _readStatus = ReadStatus.PubSubUnsubscribe;
                    var server = BridgeCouldBeNull?.ServerEndPoint;
                    if (server is not null && muxer.TryGetSubscription(subscriptionChannel, out var subscription))
                    {
                        // wipe and reconnect; but: to where?
                        // counter-intuitively, the only server we *know* already knows the new route is:
                        // the outgoing server, since it had to change to MIGRATING etc; the new INCOMING server
                        // knows, but *we don't know who that is*, and other nodes: aren't guaranteed to know (yet)
                        muxer.DefaultSubscriber.ResubscribeToServer(subscription, subscriptionChannel, server, cause: "sunsubscribe");
                    }
                    return OutOfBandResult.Handled;

                case PushKind.Subscribe or PushKind.PSubscribe or PushKind.SSubscribe
                    or PushKind.Unsubscribe or PushKind.PUnsubscribe or PushKind.SUnsubscribe:
                    // recognized, but these are confirmations of commands *we* sent (in RESP3 they
                    // arrive as push frames), so the normal request/response matching completes them
                    return OutOfBandResult.MatchToCommand;
            }
        }
        return OutOfBandResult.NotRecognized;
    }

    private void OnMessage(
        ConnectionMultiplexer muxer,
        in RedisChannel subscriptionChannel,
        in RedisChannel messageChannel,
        in RespReader reader)
    {
        // note: this could be multi-message: https://github.com/StackExchange/StackExchange.Redis/issues/2507
        _readStatus = ReadStatus.InvokePubSub;
        switch (reader.Prefix)
        {
            case RespPrefix.BulkString:
            case RespPrefix.SimpleString:
                muxer.OnMessage(subscriptionChannel, messageChannel, reader.ReadRedisValue());
                break;
            case RespPrefix.Array:
                var iter = reader.AggregateChildren();
                while (iter.MoveNext())
                {
                    muxer.OnMessage(subscriptionChannel, messageChannel, iter.Value.ReadRedisValue());
                }

                break;
        }
    }

    private void MatchNextResult(ReadOnlySpan<byte> frame)
    {
        Trace("Matching result...");

        if (IsWriteFaulted)
        {
            // a write failed part-way and we could not complete teardown, so the queue may be out of
            // step with the wire; matching *anything* now risks handing a reply to the wrong caller.
            // bail out instead - the read loop records the failure, which fails everything queued.
            // the sentinel is pre-allocated: we may be here precisely because we are out of memory.
            _readStatus = ReadStatus.Faulted;
            throw WriteFaultedSentinel;
        }

        Message? msg = null;
        // check whether we're waiting for a high-integrity mode post-response checksum (using cheap null-check first)
        if (_awaitingToken is not null && (msg = Interlocked.Exchange(ref _awaitingToken, null)) is not null)
        {
            _readStatus = ReadStatus.ResponseSequenceCheck;
            if (!ProcessHighIntegrityResponseToken(msg, frame, this))
            {
                RecordConnectionFailed(ConnectionFailureType.ResponseIntegrityFailure, origin: nameof(ReadStatus.ResponseSequenceCheck));
            }
            return;
        }

        _readStatus = ReadStatus.DequeueResult;
        lock (_writtenAwaitingResponse)
        {
            if (msg is not null)
            {
                _awaitingToken = null;
            }

            if (!_writtenAwaitingResponse.TryDequeue(out msg))
            {
                // A failure can race bytes that were already in the socket or read buffer. Once shutdown has
                // started, no new command can enter this queue, so these are replies to commands the failure
                // path has already completed. Drop them with the dead connection instead of reporting a fresh
                // protocol failure.
                if (_isShutdown)
                {
                    return;
                }

                Throw(frame, connectionType, _protocol);

                [DoesNotReturn]
                static void Throw(ReadOnlySpan<byte> frame, ConnectionType connection, RedisProtocol protocol)
                {
                    var prefix = RespReaderInternalExtensions.GetRespPrefix(frame);
                    throw new InvalidOperationException($"Received {connection}/{protocol} response with no message waiting: " + prefix.ToString());
                }
            }
        }
        _activeMessage = msg;

        Trace("Response to: " + msg);
        _readStatus = ReadStatus.ComputeResult;
        var reader = new RespReader(frame, _readerServices);

        OnDetailLog($"computing result for {msg.CommandAndKey} ({RespReaderInternalExtensions.GetRespPrefix(frame)})");

        // need to capture HIT promptly, as -MOVED could cause a resend with a new high-integrity token
        // (a lazy approach would be to not rotate, but: we'd rather avoid that; the -MOVED case is rare)
        var highIntegrityToken = msg.HighIntegrityToken;
        bool computed = msg.ComputeResult(this, ref reader);
        if (computed)
        {
            OnDetailLog($"> complete: {msg.CommandAndKey}");
            _readStatus = msg.ResultBoxIsAsync ? ReadStatus.CompletePendingMessageAsync : ReadStatus.CompletePendingMessageSync;
            if (highIntegrityToken is 0)
            {
                // can't complete yet if needs checksum
                msg.Complete(this);
            }
        }
        else
        {
            OnDetailLog($"> incomplete: {msg.CommandAndKey}");
        }
        if (highIntegrityToken is not 0)
        {
            // stash this for the next non-OOB response, retaining the old HIT iff we had a -MOVED etc
            Volatile.Write(ref _awaitingToken, computed ? msg : new DummyHighIntegrityMessage(msg, highIntegrityToken));
        }

        _readStatus = ReadStatus.MatchResultComplete;
        _activeMessage = null;

        static bool ProcessHighIntegrityResponseToken(Message message, ReadOnlySpan<byte> frame, PhysicalConnection? connection)
        {
            bool isValid = false;
            var reader = new RespReader(frame); // no services needed: never leases
            if ((reader.SafeTryMoveNext() & reader.IsScalar)
                && reader.ScalarLength() is 4)
            {
                uint interpreted;
                if (reader.TryGetSpan(out var span))
                {
                    interpreted = BinaryPrimitives.ReadUInt32LittleEndian(span);
                }
                else
                {
                    Span<byte> tmp = stackalloc byte[4];
                    reader.CopyTo(tmp);
                    interpreted = BinaryPrimitives.ReadUInt32LittleEndian(tmp);
                }
                isValid = interpreted == message.HighIntegrityToken;
            }
            if (isValid)
            {
                message.Complete(connection);
                return true;
            }
            else
            {
                message.SetExceptionAndComplete(new InvalidOperationException("High-integrity mode detected possible protocol de-sync"), connection);
                return false;
            }
        }
    }

    private bool PeekChannelMessage(RedisCommand command, in RedisChannel channel)
    {
        Message? msg;
        bool haveMsg;
        lock (_writtenAwaitingResponse)
        {
            haveMsg = _writtenAwaitingResponse.TryPeek(out msg);
        }

        return haveMsg && msg is Message.CommandChannelBase typed
                       && typed.Command == command && typed.Channel == channel;
    }

    internal RedisChannel AsRedisChannel(in RespReader reader, RedisChannel.RedisChannelOptions options)
    {
        var channelPrefix = ChannelPrefix;
        if (channelPrefix is null)
        {
            // no channel-prefix enabled, just use as-is
            return new RedisChannel(reader.ReadByteArray(), options);
        }

        byte[] lease = [];
        var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(ref lease, stackalloc byte[256]);

        if (span.StartsWith(channelPrefix))
        {
            // we have a channel-prefix, and it matches; strip it
            span = span.Slice(channelPrefix.Length);
        }
        else if (IsServerDefinedChannel(span))
        {
            // Server-defined channels should ignore our channel-prefix rules.
            // we shouldn't get unexpected events, so to get here: we've received a notification
            // on a channel that doesn't match our prefix; this *should* be limited to
            // key notifications (see: IgnoreChannelPrefix), but: we need to be sure

            // leave alone
        }
        else
        {
            // no idea what this is
            span = default;
        }

        RedisChannel channel = span.IsEmpty ? default : new(span.ToArray(), options);
        if (lease.Length != 0) ArrayPool<byte>.Shared.Return(lease);
        return channel;
    }

    private static bool IsServerDefinedChannel(ReadOnlySpan<byte> span)
    {
        var hash = AsciiHash.HashCS(span);
        return hash switch
        {
            KeyspaceChannelPrefix.HashCS when span.StartsWith(KeyspaceChannelPrefix.U8) => true,
            KeyeventChannelPrefix.HashCS when span.StartsWith(KeyeventChannelPrefix.U8) => true,
            SubkeyspaceChannelPrefix.HashCS when span.StartsWith(SubkeyspaceChannelPrefix.U8) => true,
            SubkeyeventChannelPrefix.HashCS when span.StartsWith(SubkeyeventChannelPrefix.U8) => true,
            SubkeyspaceItemChannelPrefix.HashCS when span.StartsWith(SubkeyspaceItemChannelPrefix.U8) => true,
            SubkeyspaceEventChannelPrefix.HashCS when span.StartsWith(SubkeyspaceEventChannelPrefix.U8) => true,
            _ => false,
        };
    }

    [AsciiHash("__keyspace@")]
    private static partial class KeyspaceChannelPrefix { }
    [AsciiHash("__keyevent@")]
    private static partial class KeyeventChannelPrefix { }
    [AsciiHash("__subkeyspace@")]
    private static partial class SubkeyspaceChannelPrefix { }
    [AsciiHash("__subkeyevent@")]
    private static partial class SubkeyeventChannelPrefix { }
    [AsciiHash("__subkeyspaceitem@")]
    private static partial class SubkeyspaceItemChannelPrefix { }
    [AsciiHash("__subkeyspaceevent@")]
    private static partial class SubkeyspaceEventChannelPrefix { }

    [AsciiHash("*2\r\n$4\r\npong\r\n$")]
    private static partial class ArrayPong_LC_Bulk { }
    [AsciiHash("*2\r\n$4\r\nPONG\r\n$")]
    private static partial class ArrayPong_UC_Bulk { }
    [AsciiHash("*2\r\n+pong\r\n$")]
    private static partial class ArrayPong_LC_Simple { }
    [AsciiHash("*2\r\n+PONG\r\n$")]
    private static partial class ArrayPong_UC_Simple { }

    // ReSharper disable InconsistentNaming
    private static readonly uint
        pong = RespConstants.UnsafeCpuUInt32("pong"u8),
        PONG = RespConstants.UnsafeCpuUInt32("PONG"u8);

    // ReSharper restore InconsistentNaming
    [Conditional("DEBUG")]
    private static void DebugValidateSingleFrame(ReadOnlySpan<byte> payload)
    {
        var reader = new RespReader(payload); // debug validation only: never leases
        if (!reader.TryMoveNext(checkError: false))
        {
            throw new InvalidOperationException("No root RESP element");
        }
        reader.SkipChildren();

#pragma warning disable CS0618 // we don't expect *any* additional data, even attributes
        if (reader.TryReadNext())
#pragma warning restore CS0618
        {
            throw new InvalidOperationException($"Unexpected trailing {reader.Prefix}");
        }

        if (reader.ProtocolBytesRemaining != 0)
        {
            var copy = reader; // leave reader alone for inspection
            var prefix = copy.SafeTryMoveNext() ? copy.Prefix : RespPrefix.None;
            throw new InvalidOperationException(
                $"Unexpected additional {reader.ProtocolBytesRemaining} bytes remaining, {prefix}");
        }
    }

    internal sealed class DummyHighIntegrityMessage : Message
    {
        // note: we don't create this message very often - only when a HIT gets a -MOVED or similar
        public DummyHighIntegrityMessage(Message msg, uint highIntegrityToken) : base(msg.Db, msg.Flags, msg.Command)
        {
            WithHighIntegrity(highIntegrityToken);
        }

        public override int ArgCount => 0;

        protected override void WriteImpl(in MessageWriter writer)
            => throw new NotSupportedException("This message cannot be written; it is a place-holder for high-integrity scenarios.");
    }
}
