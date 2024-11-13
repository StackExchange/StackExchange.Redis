using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RESPite.Internal.Buffers;

/// <summary>
/// Represents multiple values that partition data over the same underlying multi-segment buffer.
/// A set of integer lengths indicate how the values are distributed over the buffers.
/// </summary>
internal readonly struct RefCountedBuffers<T> : IDisposable, IEnumerable<ReadOnlySequence<T>>
{
    private readonly RefCountedBuffer<T> _buffer;
    private readonly int[] _lengthsPooled;
    private readonly int _length;

    /// <summary>
    /// Indicates an explicitly null payload.
    /// </summary>
    public static RefCountedBuffers<T> Null { get; } = new(default, [], -1);

    /// <summary>
    /// Indicates an empty payload.
    /// </summary>
    public static RefCountedBuffers<T> Empty { get; } = default;

    private static readonly RefCountedBuffers<T> __disposed = new(default, [], -2);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override string ToString() => _length switch
    {
        -2 => "(disposed)",
        -1 => "(null)",
        0 => "(empty)",
        1 => "1 chunk",
        _ => $"{_length} chunks",
    };

    /// <summary>
    /// Indicates whether this is a value with zero chunks.
    /// </summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// Indicates whether this represents a null value.
    /// </summary>
    public bool IsNull => _length == -1;

    /// <summary>
    /// Indicates whether this represents a disposed value.
    /// </summary>
    internal bool IsDisposed => _length == -2;

    /// <summary>
    /// Gets the number of elements represented by this value.
    /// </summary>
    public int Length
    {
        get
        {
            ThrowIfDisposed();
            return Math.Max(0, _length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_length == -2) Throw();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw() => throw new ObjectDisposedException(nameof(RefCountedBuffers<int>));
    }

    /// <summary>
    /// Allows construction of a <see cref="RefCountedBuffers{T}"/> value.
    /// </summary>
    public readonly struct Builder
    {
        private readonly int _length;
        private readonly int[] _lengthsPooled;

        /// <summary>
        /// Create a new instance with capacity for <paramref name="length"/> values.
        /// All lengths should be assigned via <see cref="SetLength(int, int)"/>.
        /// If <paramref name="clear"/> is disabled, all lengths <b>MUST</b> be
        /// assigned, or the behavior is undefined.
        /// </summary>
        public Builder(int length, bool clear = true)
        {
            _length = length;
            _lengthsPooled = length <= 0 ? [] : ArrayPool<int>.Shared.Rent(length);
            if (length > 0 && clear)
            {
                new Span<int>(_lengthsPooled, 0, length).Clear();
            }
        }

        /// <summary>
        /// Specify the <paramref name="length"/> of the element at position <paramref name="index"/>.
        /// </summary>
        public void SetLength(int index, int length) => _lengthsPooled[index] = length;

        /// <summary>
        /// Create the <see cref="RefCountedBuffers{T}"/> value over the designated <paramref name="buffer"/>,
        /// using the lengths previously specified via <see cref="SetLength(int, int)"/>.
        /// </summary>
        public RefCountedBuffers<T> Create(in RefCountedBuffer<T> buffer)
            => new(in buffer, _lengthsPooled, _length);
    }

    internal RefCountedBuffers(in RefCountedBuffer<T> buffer, int[] lengthsPooled, int count)
    {
        _lengthsPooled = lengthsPooled;
        _length = count;
        if (count > 0)
        {
            buffer.Retain();
            _buffer = buffer;
        }
        else
        {
            Debug.Assert(ReferenceEquals(lengthsPooled, Array.Empty<int>()), "non-content: should be shared empty array");
            _buffer = default;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_length > 0)
        {
            var buffer = _buffer;
            var arr = _lengthsPooled;
            Unsafe.AsRef(in this) = __disposed;

            if (arr is not null)
            {
                ArrayPool<int>.Shared.Return(arr);
            }
            buffer.Release();
        }
    }

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(in this);

    IEnumerator<ReadOnlySequence<T>> IEnumerable<ReadOnlySequence<T>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Used by efficiently perform per-item actions.
    /// </summary>
    public delegate bool LeasedChunkCallback<TState>(int index, ReadOnlySpan<T> value, TState state);

    /// <summary>
    /// Perform an action for each value, represented as a simple buffer.
    /// </summary>
    public int ForEach(LeasedChunkCallback<object> action) => ForEach(action, null!);

    /// <summary>
    /// Perform an action for each value, represented as a simple buffer.
    /// </summary>
    public int ForEach<TState>(LeasedChunkCallback<TState> action, TState state)
    {
        ThrowIfDisposed();
        int tally = 0;
        if (_length > 0)
        {
            ReadOnlySequence<T> _remaining = _buffer;
            T[] leased = []; // for linearizing values that span segments

            ReadOnlySpan<int> ranged = new(_lengthsPooled, 0, _length); // to elide bounds check
            for (int i = 0; i < ranged.Length; i++)
            {
                var chunkLength = ranged[i];
                if (chunkLength == 0)
                {
                    if (action(i, default, state)) tally++;
                }
                else
                {
#if NET6_0_OR_GREATER
                    var span = _remaining.FirstSpan;
#else
                    var span = _remaining.First.Span;
#endif
                    if (span.Length >= chunkLength)
                    {
                        // get from current, simple
                        if (action(i, span.Slice(0, chunkLength), state)) tally++;
                        _remaining = _remaining.Slice(chunkLength);
                    }
                    else
                    {
                        // multi-segment; rent etc
                        var split = _remaining.GetPosition(chunkLength);
                        var current = _remaining.Slice(0, split);
                        if (leased.Length < chunkLength)
                        {
                            // need a bigger buffer
                            ArrayPool<T>.Shared.Return(leased);
                            leased = ArrayPool<T>.Shared.Rent(chunkLength);
                        }
                        var sized = new Span<T>(leased, 0, chunkLength);
                        current.CopyTo(sized);
                        if (action(i, sized, state)) tally++;
                        _remaining = _remaining.Slice(split);
                    }
                }
            }
            ArrayPool<T>.Shared.Return(leased);
        }
        return tally;
    }

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public struct Enumerator : IEnumerator<ReadOnlySequence<T>>, IEnumerator
    {
        private readonly int[] _lengthsPooled;
        private readonly int _length;
        private int _index;
        private ReadOnlySequence<T> _remaining;

        internal Enumerator(in RefCountedBuffers<T> parent)
        {
            _length = parent.Length; // includes disposal/null handling
            _lengthsPooled = parent._lengthsPooled;
            _index = 0;
            _remaining = parent._buffer;
        }

        /// <summary>
        /// Move to the next value, if possible.
        /// </summary>
        public bool MoveNext()
        {
            if (_index < _length)
            {
                var split = _remaining.GetPosition(_lengthsPooled[_index++]);
                Current = _remaining.Slice(0, split);
                _remaining = _remaining.Slice(split);

                return true;
            }

            Current = default;
            return false;
        }

        /// <summary>
        /// Gets the current iteration value.
        /// </summary>
        public ReadOnlySequence<T> Current
        {
            readonly get;
            private set;
        }

        readonly object IEnumerator.Current => Current;

        readonly void IEnumerator.Reset() => throw new NotSupportedException();

        readonly void IDisposable.Dispose()
        {
        }
    }
}
