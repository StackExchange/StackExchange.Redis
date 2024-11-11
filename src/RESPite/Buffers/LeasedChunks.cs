using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RESPite.Buffers;

/// <summary>
/// Represents multiple <see cref="ReadOnlyMemory{T}"/> values that share the same underlying multi-segment buffer.
/// Individual values do not span multiple segments, but multiple values can share a single segment. A set
/// of integer lengths indicate how the values are distributed over the buffers: values are contiguous based
/// on the lengths, but if a length would cross a segment boundary it is assumed that the next value starts
/// at the beginning of the next segment.
/// </summary>
public readonly struct LeasedChunks<T> : IDisposable, IEnumerable<ReadOnlyMemory<T>>
{
    private readonly RefCountedBuffer<T> _buffer;
    private readonly int[] _lengthsPooled;
    private readonly int _count;

    private static readonly LeasedChunks<T> s_disposed = new(default, [], -2);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override string ToString() => _count switch
    {
        -2 => "(disposed)",
        -1 => "(null)",
        0 => "(empty)",
        1 => "1 chunk",
        _ => $"{_count} chunks",
    };

    /// <summary>
    /// Indicates whether this is a value with zero chunks.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Indicates whether this represents a null value.
    /// </summary>
    public bool IsNull => _count == -1;

    /// <summary>
    /// Indicates whether this represents a disposed value.
    /// </summary>
    internal bool IsDisposed => _count == -2;

    /// <summary>
    /// Gets the number of elements represented by this value.
    /// </summary>
    public int Length
    {
        get
        {
            ThrowIfDisposed();
            return Math.Max(0, _count);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_count == -2) Throw();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw() => throw new ObjectDisposedException(nameof(LeasedChunks<int>));
    }

    internal LeasedChunks(in RefCountedBuffer<T> buffer, int[] lengthsPooled, int count)
    {
        _lengthsPooled = lengthsPooled;
        _count = count;
        if (count > 0)
        {
            buffer.Retain();
            _buffer = buffer;
        }
        else
        {
            _buffer = default;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var buffer = _buffer;
        var arr = _lengthsPooled;
        Unsafe.AsRef(in this) = s_disposed;

        if (arr is not null)
        {
            ArrayPool<int>.Shared.Return(arr);
        }
        buffer.Release();
    }

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(in this);

    IEnumerator<ReadOnlyMemory<T>> IEnumerable<ReadOnlyMemory<T>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public struct Enumerator : IEnumerator<ReadOnlyMemory<T>>, IEnumerator
    {
        private readonly int[] _lengthsPooled;
        private readonly int _length;
        private int _index, _sourceOffset;
        private ReadOnlySequence<T>.Enumerator _source;

        internal Enumerator(in LeasedChunks<T> parent)
        {
            _length = parent.Length; // includes disposal/null handling
            _source = parent._buffer.GetEnumerator();
            _lengthsPooled = parent._lengthsPooled;
            _index = -1;
            _sourceOffset = 0;
        }

        /// <summary>
        /// Move to the next value, if possible.
        /// </summary>
        public bool MoveNext()
        {
            if (_index < _length)
            {
                int len = _lengthsPooled[++_index];

                var current = _source.Current;
                if (_sourceOffset + len >= current.Length)
                {
                    // keep consuming the current segment
                    Current = current.Slice(_sourceOffset, len);
                    _sourceOffset += len;
                    return true;
                }

                // new segment
                if (!_source.MoveNext())
            }
            Current = default;
            return false;
        }

        /// <summary>
        /// Gets the current iteration value.
        /// </summary>
        public ReadOnlyMemory<T> Current { get; private set; }

        object IEnumerator.Current => Current;


        void IEnumerator.Reset() => throw new NotSupportedException();
        void IDisposable.Dispose() { }
    }
}
