using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Resp;

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
///   - call <see cref="DiscardCommitted"/> to indicate how much data is no longer needed
///  Emphasis: no concurrency! This is intended for a single worker acting as both producer and consumer.
/// </remarks>
internal struct CycleBuffer(MemoryPool<byte> pool, int pageSize = CycleBuffer.DefaultPageSize)
{
    private const int DefaultPageSize = 8 * 1024;
    private readonly int xorPageSize = pageSize ^ DefaultPageSize;
    public int PageSize => xorPageSize ^ DefaultPageSize; // branch-free default value
    public MemoryPool<byte> Pool => pool ?? MemoryPool<byte>.Shared; // in respect of default struct

    private Segment? startSegment, endSegment;

    private int endSegmentCommittedAndFirstTrimmedFlag, endSegmentLength;
    private const int MSB = 1 << 31;

    private int EndSegmentCommitted
    {
        get => endSegmentCommittedAndFirstTrimmedFlag & ~MSB;
        // set: preserve MSB
        set => endSegmentCommittedAndFirstTrimmedFlag = (value & ~MSB) | (endSegmentCommittedAndFirstTrimmedFlag & MSB);
    }

    private bool FirstSegmentTrimmed
    {
        get => (endSegmentCommittedAndFirstTrimmedFlag & MSB) != 0;
        set
        {
            if (value) endSegmentCommittedAndFirstTrimmedFlag |= MSB;
            else endSegmentCommittedAndFirstTrimmedFlag &= ~MSB;
        }
    }

    public bool TryGetCommitted(out ReadOnlySpan<byte> span)
    {
        DebugAssertValid();
        if (!ReferenceEquals(startSegment, endSegment))
        {
            span = default;
            return false;
        }

        span = startSegment is null ? default : startSegment.Memory.Span.Slice(start: 0, length: EndSegmentCommitted);
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

        var available = endSegmentLength - EndSegmentCommitted;
        if (count > available) Throw();
        EndSegmentCommitted += count;
        DebugAssertValid();

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    public bool CommittedIsEmpty => ReferenceEquals(startSegment, endSegment) & EndSegmentCommitted == 0;

    /// <summary>
    /// Marks committed data as fully consumed; it will no longer appear in later calls to <see cref="GetAllCommitted"/>.
    /// </summary>
    public void DiscardCommitted(int count)
    {
        DebugAssertValid();
        // optimize for most common case, where we consume everything
        if (ReferenceEquals(startSegment, endSegment)
            & count == EndSegmentCommitted
            & (endSegmentCommittedAndFirstTrimmedFlag & MSB) == 0) // checks sign *and* non-trimmed
        {
            // we are consuming all the data in the single segment; we can
            // just reset that segment back to full size and re-use as-is;
            // already checked MSB/trimmed, which means we don't need to do *anything*
            // except push this back to zero
            endSegmentCommittedAndFirstTrimmedFlag = 0; // = untrimmed and unused
            DebugAssertValid(0);
            DebugCounters.OnDiscardFull(count);
        }
        else
        {
            DiscardCommittedSlow(count);
        }
    }

    private void DiscardCommittedSlow(int count)
    {
        DebugCounters.OnDiscardPartial(count);
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
                if (count == endSegmentCommittedAndFirstTrimmedFlag) // note already checked sign, so: not trimmed
                {
                    endSegmentCommittedAndFirstTrimmedFlag = 0; // = untrimmed and unused
#if DEBUG
                    blame += ",full-final (u)";
#endif
                }
                else if (count == EndSegmentCommitted)
                {
                    endSegmentLength = startSegment!.Untrim();
                    endSegmentCommittedAndFirstTrimmedFlag = 0; // = untrimmed and unused
#if DEBUG
                    blame += ",full-final (t)";
#endif
                }
                else
                {
                    // discard from the start
                    segment.TrimStart(count);
                    endSegmentLength -= count;
                    EndSegmentCommitted -= count;
                    FirstSegmentTrimmed = true;
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
                segment.TrimStart(count);
                FirstSegmentTrimmed = true;
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
                FirstSegmentTrimmed = false;
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
                endSegmentLength == 0 & endSegmentCommittedAndFirstTrimmedFlag == 0,
                "un-init state should be zero");
            return;
        }

        Debug.Assert(endSegment is not null, "end segment must not be null if start segment exists");
        Debug.Assert(
            endSegmentLength == endSegment!.Length,
            $"end segment length is incorrect - expected {endSegmentLength}, got {endSegment.Length}");
        Debug.Assert(FirstSegmentTrimmed == startSegment.IsTrimmed(), "start segment trimmed is incorrect");
        Debug.Assert(EndSegmentCommitted <= endSegmentLength);
    }

    public long GetCommittedLength()
    {
        DebugAssertValid();
        return ReferenceEquals(startSegment, endSegment)
            ? EndSegmentCommitted
            : GetAllCommitted().Length;
    }

    public bool TryGetFirstCommittedSpan(bool fullOnly, out ReadOnlySpan<byte> span)
    {
        DebugAssertValid();
        if (TryGetFirstCommittedMemory(fullOnly, out var memory))
        {
            span = memory.Span;
            return true;
        }

        span = default;
        return false;
    }

    public bool TryGetFirstCommittedMemory(bool fullOnly, out ReadOnlyMemory<byte> memory)
    {
        DebugAssertValid();
        if (ReferenceEquals(startSegment, endSegment))
        {
            // single page
            if (EndSegmentCommitted == 0)
            {
                // empty (includes uninitialized)
                memory = default;
                return false;
            }

            memory = startSegment!.Memory.Slice(start: 0, length: EndSegmentCommitted);
            return !fullOnly | EndSegmentCommitted == endSegmentLength;
        }

        // multi-page
        memory = startSegment!.Memory;
        return true;
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
                : new ReadOnlySequence<byte>(startSegment.Memory.Slice(start: 0, length: EndSegmentCommitted));
        }

        Debug.Assert(startSegment is not null, "end segment is unexpectedly null");
        return new(startSegment, 0, endSegment!, EndSegmentCommitted);
    }

    private Segment GetNextSegment()
    {
        DebugAssertValid();
        var spare = endSegment?.Next;
        if (spare is not null) // we already have an unused segment; just update our state
        {
            endSegment!.TrimEnd(EndSegmentCommitted);
            endSegment = spare;
            EndSegmentCommitted = 0;
            endSegmentLength = spare.Length;
            DebugAssertValid();
            return spare;
        }

        Segment segment = Segment.Create(Pool.Rent(PageSize));
        if (endSegment is null)
        {
            Debug.Assert(startSegment is null & EndSegmentCommitted == 0, "invalid empty state");
            endSegmentLength = segment.Length;
            endSegment = startSegment = segment;
            DebugAssertValid();
            return segment;
        }

        endSegment.Append(EndSegmentCommitted, segment);
        EndSegmentCommitted = 0;
        endSegmentLength = segment.Length;
        endSegment = segment;
        DebugAssertValid();
        return segment;
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
            var endSegmentCommitted = EndSegmentCommitted;
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

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        private Segment() { }
        private IMemoryOwner<byte> _lease = NullLease.Instance;
        private static Segment? _spare;

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

        public void Append(int committedBytes, Segment next)
        {
            Debug.Assert(Next is null, "current segment already has a next");
            Debug.Assert(next.Next is null && next.RunningIndex == 0, "inbound next segment is in a chain");
            TrimEnd(committedBytes);
            Debug.Assert(Length == committedBytes, "should be the committed size");
            next.RunningIndex = this.RunningIndex + committedBytes;
            Next = next;
        }

        public void TrimEnd(int newLength)
        {
            var delta = Length - newLength;
            if (delta != 0)
            {
                // buffer wasn't fully used; trim
                Memory = Memory.Slice(0, newLength);

                // fix any trailing running-indices (otherwise length math is wrong)
                var next = Next;
                while (next is not null)
                {
                    next.RunningIndex -= delta;
                    next = next.Next;
                }
            }
        }

        public void TrimStart(int remove)
        {
            Memory = Memory.Slice(start: remove);
            RunningIndex += remove; // so that ROS length keeps working
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
            Memory = _lease.Memory; // reset, in case we trimmed it
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
            Interlocked.Exchange(ref _spare, this);
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
        public int Untrim() => (Memory = _lease.Memory).Length;

        public bool IsTrimmed() => Length < _lease.Memory.Length;

        public void AppendOrRecycle(Segment segment, int maxDepth)
        {
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
}
