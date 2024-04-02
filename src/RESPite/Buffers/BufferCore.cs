using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using RESPite.Buffers;
using RESPite.Buffers.Internal;

namespace RESPite.Internal;

/// <summary>
/// Handles buffer management; intended for use as the private implementation layer of a transport
/// </summary>
public struct BufferCore<T>
    : IDisposable, IBufferWriter<T> // note mutable struct intended to encapsulate logic as a field inside a class instance
{
    private readonly SlabManager<T> _slabManager;
    private RefCountedSequenceSegment<T> _head, _tail;
    private readonly long _maxLength;
    private int _headOffset, _tailOffset, _tailSize;
    internal readonly long MaxLength => _maxLength;

    internal SlabManager<T> SlabManager => _slabManager;

    /// <summary>
    /// Initializes the instance
    /// </summary>
    public BufferCore(int maxLength = 0) : this(SlabManager<T>.Ambient, maxLength) { }
    internal BufferCore(SlabManager<T> slabManager, int maxLength = 0)
    {
        if (maxLength <= 0) maxLength = int.MaxValue;
        _maxLength = maxLength;
        _headOffset = _tailOffset = _tailSize = 0;
        _slabManager = slabManager;
        Expand();
    }

    /// <summary>
    /// The immediately available contiguous bytes in the current buffer (or next buffer, if none)
    /// </summary>
    public readonly int AvailableWriteBytes
    {
        get
        {
            var remaining = _tailSize - _tailOffset;
            return remaining == 0 ? _slabManager.ChunkSize : remaining;
        }
    }

    [MemberNotNull(nameof(_head))]
    [MemberNotNull(nameof(_tail))]
    private void Expand()
    {
        Debug.Assert(_tail is null || _tailOffset == _tail.Memory.Length, "tail page should be full");
        if (MaxLength > 0 && (GetBuffer().Length + _slabManager.ChunkSize) > MaxLength) ThrowQuota();

        var next = new RefCountedSequenceSegment<T>(_slabManager.GetChunk(out var chunk), chunk, _tail);
        _tail = next;
        _tailOffset = 0;
        _tailSize = next.Memory.Length;
        if (_head is null)
        {
            _head = next;
            _headOffset = 0;
        }

        static void ThrowQuota() => throw new InvalidOperationException("Buffer quota exceeded");
    }

    /// <summary>
    /// Attempt to get a span of the requested size; returns <c>false</c> if not possible
    /// </summary>
    public bool TryGetWritableSpan(int minSize, out Span<T> span)
    {
        if (minSize <= AvailableWriteBytes) // don't pay lookup cost if impossible
        {
            span = GetWritableTail().Span;
            return span.Length >= minSize;
        }
        span = default;
        return false;
    }

    /// <summary>
    /// Gets the region in which new data can be deposited
    /// </summary>
    public Memory<T> GetWritableTail()
    {
        if (_tailOffset == _tailSize)
        {
            Expand();
        }
        // definitely something available; return the gap
        return MemoryMarshal.AsMemory(_tail.Memory).Slice(_tailOffset);
    }

    /// <summary>
    /// Gets the committed bytes
    /// </summary>
    public readonly ReadOnlySequence<T> GetBuffer() => _head is null ? default : new(_head, _headOffset, _tail, _tailOffset);

    /// <summary>
    /// Commits data to the buffer
    /// </summary>
    internal void Commit(int bytes) // unlike Advance, this remains valid for data outside what has been written
    {
        if (bytes >= 0 && bytes <= _tailSize - _tailOffset)
        {
            _tailOffset += bytes;
        }
        else
        {
            CommitSlow(bytes);
        }
    }
    private void CommitSlow(int bytes) // multi-segment commits (valid even though it remains unwritten) and error-cases
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (bytes > 0)
        {
            var space = _tailSize - _tailOffset;
            if (bytes <= space)
            {
                _tailOffset += bytes;
            }
            else
            {
                _tailOffset += space;
                Expand(); // need more
            }
            bytes -= space;
        }
    }

    /// <summary>
    /// Drops data from the head of the buffer
    /// </summary>
    public void Advance(long bytes)
    {
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes < 0) Throw();
        while (bytes > 0 && _head is not null)
        {
            var mem = _head.Memory;
            var available = mem.Length - _headOffset;

            if (bytes < available) // fits entirely inside the current page; just increment a conter
            {
                _headOffset += (int)bytes;
                return;
            }

            // otherwise, we need to lose that page (accounting for the delta)
            bytes -= available;
            _head.Release();
            _head = _head.Next!;
            _headOffset = 0;
        }

        if (bytes != 0) Throw(); // and oops, we've destroyed everything

        if (_head is null) // did we throw away everything? if so: reset
        {
            _tail = null!;
            _tailOffset = _tailSize = 0;
            Expand();
        }
    }

    /*

    /// <summary>
    /// Detaches the entire committed chain to the caller without leaving things in a resumable state
    /// </summary>
    public ReadOnlySequence<T> Detach()
    {
        var all = GetBuffer();
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        return all;
    }

    /// <summary>
    /// Detaches the head portion of the committed chain, retaining the rest of the buffered data
    /// for additional use
    /// </summary>
    public ReadOnlySequence<T> DetachRotating(long bytes)
    {
        // semantically, we're going to AddRef on all the nodes in take, and then
        // drop (and Dispose()) all nodes that we no longer need; but this means
        // that the only shared segment is the first one (and only if there is data left),
        // so we can manually check that one segment, rather than walk two chains
        var all = GetBuffer();
        var take = all.Slice(0, bytes);

        var end = take.End;
        var endSegment = (RefCountedSequenceSegment<T>)end.GetObject()!;

        var bytesLeftLastPage = endSegment.Memory.Length - end.GetInteger();
        if (bytesLeftLastPage != 0 && (
            bytesLeftLastPage >= 64 // worth using for the next read, regardless
            || endSegment.Next is not null // we've already allocated another page, which means this page is full
            || _tailOffset != end.GetInteger() // (^^ final page) & we have additional read bytes
            ))
        {
            // keep sharing the last page of the outbound / first page of retained
            endSegment.AddRef();
            _head = endSegment;
            _headOffset = end.GetInteger();
        }
        else
        {
            // move to the next page
            _headOffset = 0;
            if (endSegment.Next is null)
            {
                // no next page buffered; reset completely
                Debug.Assert(ReferenceEquals(endSegment, _tail));
                _head = _tail = null!;
                Expand();
            }
            else
            {
                // start fresh from the next page
                var next = endSegment.Next;
                endSegment.Next = null; // walk never needed
                _head = next;
            }
        }
        return take;
    }
    */

    /// <summary>
    /// Gets the committed bytes and detach them from the payload
    /// </summary>
    public RefCountedBuffer<T> Detach()
    {
        var leased = RefCountedBuffer<T>.CreateValidated(GetBuffer());
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        return leased;
    }


    /// <see cref="IDisposable.Dispose"/>
    public void Dispose() => Detach().Release();

    void IBufferWriter<T>.Advance(int count) => Commit(count);
    Memory<T> IBufferWriter<T>.GetMemory(int sizeHint) => GetWritableTail();
    Span<T> IBufferWriter<T>.GetSpan(int sizeHint) => GetWritableTail().Span;
}
