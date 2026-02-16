using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Internal;

namespace RESPite.Buffers;

/// <summary>
/// Manages the state for a <see cref="ReadOnlySequence{T}"/> based IO buffer. Unlike <c>Pipe</c>,
/// it is <i>not</i> intended for a separate producer-consumer - there is no thread-safety, and no
/// activation; it just handles the buffers. It is intended to be used as a mutable (non-readonly)
/// field in a type that performs IO; the internal state mutates - it should not be passed around.
/// </summary>
/// <remarks>Notionally, there is an uncommitted area (write) and a committed area (read). Process:
/// - producer loop (*note no concurrency**)
///   - call <see cref="GetUncommittedSpan"/> to get a new scratch
///   - (write to that span)
///   - call <see cref="Commit"/> to mark complete portions
/// - consumer loop (*note no concurrency**)
///   - call <see cref="TryGetCommitted"/> to see if there is a single-span chunk; otherwise
///   - call <see cref="GetAllCommitted"/> to get the multi-span chunk
///   - (process none, some, or all of that data)
///   - call <see cref="DiscardCommitted(int)"/> to indicate how much data is no longer needed
///  Emphasis: no concurrency! This is intended for a single worker acting as both producer and consumer.
///
/// There is a *lot* of validation in debug mode; we want to be super sure that we don't corrupt buffer state.
/// </remarks>
public partial struct CycleBuffer
{
    // note: if someone uses an uninitialized CycleBuffer (via default): that's a skills issue; git gud
    public static CycleBuffer Create(MemoryPool<byte>? pool = null, int pageSize = DefaultPageSize)
    {
        pool ??= MemoryPool<byte>.Shared;
        if (pageSize <= 0) pageSize = DefaultPageSize;
        if (pageSize > pool.MaxBufferSize) pageSize = pool.MaxBufferSize;

        return new CycleBuffer(pool, pageSize);
    }

    private CycleBuffer(MemoryPool<byte> pool, int pageSize)
    {
        Pool = pool;
        PageSize = pageSize;
    }

    private const int DefaultPageSize = 8 * 1024;

    public int PageSize { get; }
    public MemoryPool<byte> Pool { get; }

    private Segment? startSegment, endSegment;

    private int endSegmentCommitted, endSegmentLength;

    public bool TryGetCommitted(out ReadOnlySpan<byte> span)
    {
        DebugAssertValid();
        if (!ReferenceEquals(startSegment, endSegment))
        {
            span = default;
            return false;
        }

        span = startSegment is null ? default : startSegment.Memory.Span.Slice(start: 0, length: endSegmentCommitted);
        return true;
    }

    /// <summary>
    /// Commits data written to buffers from  <see cref="GetUncommittedSpan"/>, making it available for consumption
    /// via <see cref="GetAllCommitted"/>. This compares to <see cref="IBufferWriter{T}.Advance(int)"/>.
    /// </summary>
    public void Commit(int count)
    {
        DebugAssertValid();
        if (count <= 0)
        {
            if (count < 0) Throw();
            return;
        }

        var available = endSegmentLength - endSegmentCommitted;
        if (count > available) Throw();
        endSegmentCommitted += count;
        DebugAssertValid();

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    public bool CommittedIsEmpty => ReferenceEquals(startSegment, endSegment) & endSegmentCommitted == 0;

    /// <summary>
    /// Marks committed data as fully consumed; it will no longer appear in later calls to <see cref="GetAllCommitted"/>.
    /// </summary>
    public void DiscardCommitted(int count)
    {
        DebugAssertValid();
        // optimize for most common case, where we consume everything
        if (ReferenceEquals(startSegment, endSegment)
            & count == endSegmentCommitted
            & count > 0)
        {
            /*
            we are consuming all the data in the single segment; we can
            just reset that segment back to full size and re-use as-is;
            note that we also know that there must *be* a segment
            for the count check to pass
            */
            endSegmentCommitted = 0;
            endSegmentLength = endSegment!.Untrim(expandBackwards: true);
            DebugAssertValid(0);
            DebugCounters.OnDiscardFull(count);
        }
        else if (count == 0)
        {
            // nothing to do
        }
        else
        {
            DiscardCommittedSlow(count);
        }
    }

    public void DiscardCommitted(long count)
    {
        DebugAssertValid();
        // optimize for most common case, where we consume everything
        if (ReferenceEquals(startSegment, endSegment)
            & count == endSegmentCommitted
            & count > 0) // checks sign *and* non-trimmed
        {
            // see <see cref="DiscardCommitted(int)"/> for logic
            endSegmentCommitted = 0;
            endSegmentLength = endSegment!.Untrim(expandBackwards: true);
            DebugAssertValid(0);
            DebugCounters.OnDiscardFull(count);
        }
        else if (count == 0)
        {
            // nothing to do
        }
        else
        {
            DiscardCommittedSlow(count);
        }
    }

    private void DiscardCommittedSlow(long count)
    {
        DebugCounters.OnDiscardPartial(count);
        DebugAssertValid();
#if DEBUG
        var originalLength = GetCommittedLength();
        var originalCount = count;
        var expectedLength = originalLength - originalCount;
        string blame = nameof(DiscardCommittedSlow);
#endif
        while (count > 0)
        {
            DebugAssertValid();
            var segment = startSegment;
            if (segment is null) break;
            if (ReferenceEquals(segment, endSegment))
            {
                // first==final==only segment
                if (count == endSegmentCommitted)
                {
                    endSegmentLength = startSegment!.Untrim();
                    endSegmentCommitted = 0; // = untrimmed and unused
#if DEBUG
                    blame += ",full-final (t)";
#endif
                }
                else
                {
                    // discard from the start
                    int count32 = checked((int)count);
                    segment.TrimStart(count32);
                    endSegmentLength -= count32;
                    endSegmentCommitted -= count32;
#if DEBUG
                    blame += ",partial-final";
#endif
                }

                count = 0;
                break;
            }
            else if (count < segment.Length)
            {
                // multiple, but can take some (not all) of the first buffer
#if DEBUG
                var len = segment.Length;
#endif
                segment.TrimStart((int)count);
                Debug.Assert(segment.Length > 0, "parial trim should have left non-empty segment");
#if DEBUG
                Debug.Assert(segment.Length == len - count, "trim failure");
                blame += ",partial-first";
#endif
                count = 0;
                break;
            }
            else
            {
                // multiple; discard the entire first segment
                count -= segment.Length;
                startSegment =
                    segment.ResetAndGetNext(); // we already did a ref-check, so we know this isn't going past endSegment
                endSegment!.AppendOrRecycle(segment, maxDepth: 2);
                DebugAssertValid();
#if DEBUG
                blame += ",full-first";
#endif
            }
        }

        if (count != 0) ThrowCount();
#if DEBUG
        DebugAssertValid(expectedLength, blame);
        _ = originalLength;
        _ = originalCount;
#endif

        [DoesNotReturn]
        static void ThrowCount() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    [Conditional("DEBUG")]
    private void DebugAssertValid(long expectedCommittedLength, [CallerMemberName] string caller = "")
    {
        DebugAssertValid();
        var actual = GetCommittedLength();
        Debug.Assert(
            expectedCommittedLength >= 0,
            $"Expected committed length is just... wrong: {expectedCommittedLength} (from {caller})");
        Debug.Assert(
            expectedCommittedLength == actual,
            $"Committed length mismatch: expected {expectedCommittedLength}, got {actual} (from {caller})");
    }

    [Conditional("DEBUG")]
    private void DebugAssertValid()
    {
        if (startSegment is null)
        {
            Debug.Assert(
                endSegmentLength == 0 & endSegmentCommitted == 0,
                "un-init state should be zero");
            return;
        }

        Debug.Assert(endSegment is not null, "end segment must not be null if start segment exists");
        Debug.Assert(
            endSegmentLength == endSegment!.Length,
            $"end segment length is incorrect - expected {endSegmentLength}, got {endSegment.Length}");
        Debug.Assert(endSegmentCommitted <= endSegmentLength, $"end segment is over-committed - {endSegmentCommitted} of {endSegmentLength}");

        // check running indices
        startSegment?.DebugAssertValidChain();
    }

    public long GetCommittedLength()
    {
        if (ReferenceEquals(startSegment, endSegment))
        {
            return endSegmentCommitted;
        }

        // note that the start-segment is pre-trimmed; we don't need to account for an offset on the left
        return (endSegment!.RunningIndex + endSegmentCommitted) - startSegment!.RunningIndex;
    }

    /// <summary>
    /// When used with <see cref="TryGetFirstCommittedSpan"/>, this means "any non-empty buffer".
    /// </summary>
    public const int GetAnything = 0;

    /// <summary>
    /// When used with <see cref="TryGetFirstCommittedSpan"/>, this means "any full buffer".
    /// </summary>
    public const int GetFullPagesOnly = -1;

    public bool TryGetFirstCommittedSpan(int minBytes, out ReadOnlySpan<byte> span)
    {
        DebugAssertValid();
        if (TryGetFirstCommittedMemory(minBytes, out var memory))
        {
            span = memory.Span;
            return true;
        }

        span = default;
        return false;
    }

    /// <summary>
    /// The minLength arg: -ve means "full segments only" (useful when buffering outbound network data to avoid
    /// packet fragmentation); otherwise, it is the minimum length we want.
    /// </summary>
    public bool TryGetFirstCommittedMemory(int minBytes, out ReadOnlyMemory<byte> memory)
    {
        if (minBytes == 0) minBytes = 1; // success always means "at least something"
        DebugAssertValid();
        if (ReferenceEquals(startSegment, endSegment))
        {
            // single page
            var available = endSegmentCommitted;
            if (available == 0)
            {
                // empty (includes uninitialized)
                memory = default;
                return false;
            }

            memory = startSegment!.Memory;
            var memLength = memory.Length;
            if (available == memLength)
            {
                // full segment; is it enough to make the caller happy?
                return available >= minBytes;
            }

            // partial segment (and we know it isn't empty)
            memory = memory.Slice(start: 0, length: available);
            return available >= minBytes & minBytes > 0; // last check here applies the -ve logic
        }

        // multi-page; hand out the first page (which is, by definition: full)
        memory = startSegment!.Memory;
        return memory.Length >= minBytes;
    }

    /// <summary>
    /// Note that this chain is invalidated by any other operations; no concurrency.
    /// </summary>
    public ReadOnlySequence<byte> GetAllCommitted()
    {
        if (ReferenceEquals(startSegment, endSegment))
        {
            // single segment, fine
            return startSegment is null
                ? default
                : new ReadOnlySequence<byte>(startSegment.Memory.Slice(start: 0, length: endSegmentCommitted));
        }

#if PARSE_DETAIL
        long length = GetCommittedLength();
#endif
        ReadOnlySequence<byte> ros = new(startSegment!, 0, endSegment!, endSegmentCommitted);
#if PARSE_DETAIL
        Debug.Assert(ros.Length == length, $"length mismatch: calculated {length}, actual {ros.Length}");
#endif
        return ros;
    }

    private Segment GetNextSegment()
    {
        DebugAssertValid();
        if (endSegment is not null)
        {
            endSegment.TrimEnd(endSegmentCommitted);
            Debug.Assert(endSegment.Length == endSegmentCommitted, "trim failure");
            endSegmentLength = endSegmentCommitted;
            DebugAssertValid();

            var spare = endSegment.Next;
            if (spare is not null)
            {
                // we already have a dangling segment; just update state
                endSegment.DebugAssertValidChain();
                endSegment = spare;
                endSegmentCommitted = 0;
                endSegmentLength = spare.Length;
                DebugAssertValid();
                return spare;
            }
        }

        Segment newSegment = Segment.Create(Pool.Rent(PageSize));
        if (endSegment is null)
        {
            // tabula rasa
            endSegmentLength = newSegment.Length;
            endSegment = startSegment = newSegment;
            DebugAssertValid();
            return newSegment;
        }

        endSegment.Append(newSegment);
        endSegmentCommitted = 0;
        endSegmentLength = newSegment.Length;
        endSegment = newSegment;
        DebugAssertValid();
        return newSegment;
    }

    /// <summary>
    /// Gets a scratch area for new data; this compares to <see cref="IBufferWriter{T}.GetSpan(int)"/>.
    /// </summary>
    public Span<byte> GetUncommittedSpan(int hint = 0)
        => GetUncommittedMemory(hint).Span;

    /// <summary>
    /// Gets a scratch area for new data; this compares to <see cref="IBufferWriter{T}.GetMemory(int)"/>.
    /// </summary>
    public Memory<byte> GetUncommittedMemory(int hint = 0)
    {
        DebugAssertValid();
        var segment = endSegment;
        if (segment is not null)
        {
            var memory = segment.Memory;
            if (endSegmentCommitted != 0) memory = memory.Slice(start: endSegmentCommitted);
            if (hint <= 0) // allow anything non-empty
            {
                if (!memory.IsEmpty) return MemoryMarshal.AsMemory(memory);
            }
            else if (memory.Length >= Math.Min(hint, PageSize >> 2)) // respect the hint up to 1/4 of the page size
            {
                return MemoryMarshal.AsMemory(memory);
            }
        }

        // new segment, will always be entire
        return MemoryMarshal.AsMemory(GetNextSegment().Memory);
    }

    /// <summary>
    /// This is the available unused buffer space, commonly used as the IO read-buffer to avoid
    /// additional buffer-copy operations.
    /// </summary>
    public int UncommittedAvailable
    {
        get
        {
            DebugAssertValid();
            return endSegmentLength - endSegmentCommitted;
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        private Segment() { }
        private IMemoryOwner<byte> _lease = NullLease.Instance;
        private static Segment? _spare;
        private Flags _flags;

        [Flags]
        private enum Flags
        {
            None = 0,
            StartTrim = 1 << 0,
            EndTrim = 1 << 2,
        }

        public static Segment Create(IMemoryOwner<byte> lease)
        {
            Debug.Assert(lease is not null, "null lease");
            var memory = lease!.Memory;
            if (memory.IsEmpty) ThrowEmpty();

            var obj = Interlocked.Exchange(ref _spare, null) ?? new();
            return obj.Init(lease, memory);
            static void ThrowEmpty() => throw new InvalidOperationException("leased segment is empty");
        }

        private Segment Init(IMemoryOwner<byte> lease, Memory<byte> memory)
        {
            _lease = lease;
            Memory = memory;
            return this;
        }

        public int Length => Memory.Length;

        public void Append(Segment next)
        {
            Debug.Assert(Next is null, "current segment already has a next");
            Debug.Assert(next.Next is null && next.RunningIndex == 0, "inbound next segment is already in a chain");
            next.RunningIndex = RunningIndex + Length;
            Next = next;
            DebugAssertValidChain();
        }

        private void ApplyChainDelta(int delta)
        {
            if (delta != 0)
            {
                var node = Next;
                while (node is not null)
                {
                    node.RunningIndex += delta;
                    node = node.Next;
                }
            }
        }

        public void TrimEnd(int newLength)
        {
            var delta = Length - newLength;
            if (delta != 0)
            {
                // buffer wasn't fully used; trim
                _flags |= Flags.EndTrim;
                Memory = Memory.Slice(0, newLength);
                ApplyChainDelta(-delta);
                DebugAssertValidChain();
            }
        }

        public void TrimStart(int remove)
        {
            if (remove != 0)
            {
                _flags |= Flags.StartTrim;
                Memory = Memory.Slice(start: remove);
                RunningIndex += remove; // so that ROS length keeps working; note we *don't* need to adjust the chain
                DebugAssertValidChain();
            }
        }

        public new Segment? Next
        {
            get => (Segment?)base.Next;
            private set => base.Next = value;
        }

        public Segment? ResetAndGetNext()
        {
            var next = Next;
            Next = null;
            RunningIndex = 0;
            _flags = Flags.None;
            Memory = _lease.Memory; // reset, in case we trimmed it
            DebugAssertValidChain();
            return next;
        }

        public void Recycle()
        {
            var lease = _lease;
            _lease = NullLease.Instance;
            lease.Dispose();
            Next = null;
            Memory = default;
            RunningIndex = 0;
            _flags = Flags.None;
            Interlocked.Exchange(ref _spare, this);
            DebugAssertValidChain();
        }

        private sealed class NullLease : IMemoryOwner<byte>
        {
            private NullLease() { }
            public static readonly NullLease Instance = new NullLease();
            public void Dispose() { }

            public Memory<byte> Memory => default;
        }

        /// <summary>
        /// Undo any trimming, returning the new full capacity.
        /// </summary>
        public int Untrim(bool expandBackwards = false)
        {
            var fullMemory = _lease.Memory;
            var fullLength = fullMemory.Length;
            var delta = fullLength - Length;
            if (delta != 0)
            {
                _flags &= ~(Flags.StartTrim | Flags.EndTrim);
                Memory = fullMemory;
                if (expandBackwards & RunningIndex >= delta)
                {
                    // push our origin earlier; only valid if
                    // we're the first segment, otherwise
                    // we break someone-else's chain
                    RunningIndex -= delta;
                }
                else
                {
                    // push everyone else later
                    ApplyChainDelta(delta);
                }

                DebugAssertValidChain();
            }
            return fullLength;
        }

        public bool StartTrimmed => (_flags & Flags.StartTrim) != 0;
        public bool EndTrimmed => (_flags & Flags.EndTrim) != 0;

        [Conditional("DEBUG")]
        public void DebugAssertValidChain([CallerMemberName] string blame = "")
        {
            var node = this;
            var runningIndex = RunningIndex;
            int index = 0;
            while (node.Next is { } next)
            {
                index++;
                var nextRunningIndex = runningIndex + node.Length;
                if (nextRunningIndex != next.RunningIndex) ThrowRunningIndex(blame, index);
                node = next;
                runningIndex = nextRunningIndex;
                static void ThrowRunningIndex(string blame, int index) => throw new InvalidOperationException(
                    $"Critical running index corruption in dangling chain, from '{blame}', segment {index}");
            }
        }

        public void AppendOrRecycle(Segment segment, int maxDepth)
        {
            segment.Memory.DebugScramble();
            var node = this;
            while (maxDepth-- > 0 && node is not null)
            {
                if (node.Next is null) // found somewhere to attach it
                {
                    if (segment.Untrim() == 0) break; // turned out to be useless
                    segment.RunningIndex = node.RunningIndex + node.Length;
                    node.Next = segment;
                    return;
                }

                node = node.Next;
            }

            segment.Recycle();
        }
    }

    /// <summary>
    /// Discard all data and buffers.
    /// </summary>
    public void Release()
    {
        var node = startSegment;
        startSegment = endSegment = null;
        endSegmentCommitted = endSegmentLength = 0;
        while (node is not null)
        {
            var next = node.Next;
            node.Recycle();
            node = next;
        }
    }

    /// <summary>
    /// Writes a value to the buffer; comparable to <see cref="BuffersExtensions.Write{T}(IBufferWriter{T}, ReadOnlySpan{T})"/>.
    /// </summary>
    public void Write(ReadOnlySpan<byte> value)
    {
        int srcLength = value.Length;
        while (srcLength != 0)
        {
            var target = GetUncommittedSpan(hint: srcLength);
            var tgtLength = target.Length;
            if (tgtLength >= srcLength)
            {
                value.CopyTo(target);
                Commit(srcLength);
                return;
            }

            value.Slice(0, tgtLength).CopyTo(target);
            Commit(tgtLength);
            value = value.Slice(tgtLength);
            srcLength -= tgtLength;
        }
    }

    /// <summary>
    /// Writes a value to the buffer; comparable to <see cref="BuffersExtensions.Write{T}(IBufferWriter{T}, ReadOnlySpan{T})"/>.
    /// </summary>
    public void Write(in ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment)
        {
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1
            Write(value.FirstSpan);
#else
            Write(value.First.Span);
#endif
        }
        else
        {
            WriteMultiSegment(ref this, in value);
        }

        static void WriteMultiSegment(ref CycleBuffer @this, in ReadOnlySequence<byte> value)
        {
            foreach (var segment in value)
            {
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1
                @this.Write(value.FirstSpan);
#else
                @this.Write(value.First.Span);
#endif
            }
        }
    }
}
