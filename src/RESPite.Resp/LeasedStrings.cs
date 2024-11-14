using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using RESPite.Internal;

namespace RESPite.Resp;

/// <summary>
/// Represents multiple <see cref="LeasedString"/> payloads sharing an underlying buffer.
/// </summary>
public readonly struct LeasedStrings : IDisposable, IEnumerable<SimpleString>, IEnumerable
{
    private readonly Lease<byte> _data;
    private readonly Lease<int> _lengths;

    /// <summary>
    /// An empty instance.
    /// </summary>
    public static LeasedStrings Empty { get; } = new(Lease<int>.Empty, Lease<byte>.Empty);

    /// <summary>
    /// Indicates whether this is a null value.
    /// </summary>
    public bool IsNull => _lengths.IsNull;

    /// <summary>
    /// Gets the number of values in this instance.
    /// </summary>
    public int Length => _lengths.Length;

    /// <summary>
    /// Indicates whether this is an empty instance.
    /// </summary>
    public bool IsEmpty => _lengths.IsEmpty;

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        _data.Dispose();
        _lengths.Dispose();
    }

    private LeasedStrings(Lease<int> lengths, Lease<byte> data)
    {
        _lengths = lengths;
        _data = data;
    }

    /// <summary>
    /// Allows construction of a <see cref="LeasedStrings"/> value.
    /// </summary>
    public struct Builder(int itemHint, int byteHint = 0)
    {
        private int _count, _dataOffset;
        private int[]? _lengths = ArrayPool<int>.Shared.Rent(itemHint);
        private byte[]? _data = ArrayPool<byte>.Shared.Rent(byteHint);

        /// <summary>
        /// Add an additional value.
        /// </summary>
        public void AddNull() => Add(-1);

        /// <summary>
        /// Add an additional value.
        /// </summary>
        public void Add(ReadOnlySpan<byte> data)
        {
            var target = Add(data.Length);
            Debug.Assert(data.Length == target.Length);
            data.CopyTo(target);
        }

        /// <summary>
        /// Add an additional value.
        /// </summary>
        public void Add(byte[]? data)
        {
            if (data is null)
            {
                Add(-1);
            }
            else
            {
                var target = Add(data.Length);
                Debug.Assert(data.Length == target.Length);
                data.CopyTo(target);
            }
        }

        /// <summary>
        /// Add an additional value.
        /// </summary>
        public void Add(string? data)
        {
            if (data is null)
            {
                Add(-1);
            }
            else
            {
                var len = Constants.UTF8.GetByteCount(data);
                var target = Add(data.Length);

                var actual = Constants.UTF8.GetBytes(data.AsSpan(), target);
                Debug.Assert(len == actual);
            }
        }

        /// <summary>
        /// Add an additional value.
        /// </summary>
        public void Add(ReadOnlySpan<char> data)
        {
            var len = Constants.UTF8.GetByteCount(data);
            var target = Add(data.Length);
            var actual = Constants.UTF8.GetBytes(data, target);
            Debug.Assert(len == actual);
        }

        /// <summary>
        /// Add an additional value. Negative numbers are interpreted as <c>null</c> values.
        /// </summary>
        /// <returns>The payload buffer to write to.</returns>
        public Span<byte> Add(int length)
        {
            const int MAX_LEN = 2_146_435_071;

            if (_lengths is null || _lengths.Length == 0)
            {
                _lengths = ArrayPool<int>.Shared.Rent(8);
            }
            else if (_count == _lengths.Length)
            {
                var delta = Math.Min(_lengths.Length, 16384);
                Grow(ref _lengths, delta, MAX_LEN);
            }

            _lengths[_count++] = length;

            if (length <= 0)
            {
                return default;
            }
            else
            {
                if (_data is null)
                {
                    long estimate = ((long)length) * 4; // anticipate more
                    _data = ArrayPool<byte>.Shared.Rent((int)Math.Min(estimate, MAX_LEN));
                }
                else if (checked(_dataOffset + length) > _data.Length)
                {
                    var delta = Math.Max(length - _dataOffset, _data.Length);
                    Grow(ref _data, delta, MAX_LEN);
                }
                Span<byte> result = new(_data, _dataOffset, length);
                _dataOffset += length;
                return result;
            }
        }

        private static void Grow<T>(ref T[] arr, int delta, int cap)
        {
            Debug.Assert(delta > 0);
            var newSize = (int)Math.Min(arr.Length + (long)delta, cap);
            var bigger = ArrayPool<T>.Shared.Rent(newSize);
            arr.CopyTo(bigger, 0);
            ArrayPool<T>.Shared.Return(arr);
            arr = bigger;
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose()
        {
            var lengths = _lengths;
            var data = _data;
            this = default;
            if (lengths is not null) ArrayPool<int>.Shared.Return(lengths);
            if (data is not null) ArrayPool<byte>.Shared.Return(data);
        }

        /// <summary>
        /// Create the <see cref="LeasedStrings"/> value.
        /// </summary>
        public LeasedStrings Create()
        {
            LeasedStrings value = new(new(_lengths!, _count), new(_data!, _dataOffset));
            this = default;
            return value;
        }
    }

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(in this);

    IEnumerator<SimpleString> IEnumerable<SimpleString>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public struct Enumerator : IEnumerator<SimpleString>, IEnumerator
    {
        private readonly ReadOnlyMemory<int> _lengths;
        private readonly ReadOnlyMemory<byte> _data;
        private readonly int _length;
        private int _index, _offset = 0;

        internal Enumerator(in LeasedStrings parent)
        {
            _lengths = parent._lengths.Memory;
            _data = parent._data.Memory;
            _length = _lengths.Length;

            _index = _offset = 0;
        }

        /// <summary>
        /// Move to the next value, if possible.
        /// </summary>
        public bool MoveNext()
        {
            if (_index < _length)
            {
                var len = _lengths.Span[_index++];
                if (len < 0)
                {
                    Current = default;
                }
                else if (len == 0)
                {
                    Current = SimpleString.Empty;
                }
                else
                {
                    Current = _data.Slice(_offset, len);
                    _offset += len;
                }
                return true;
            }

            Current = default;
            return false;
        }

        /// <summary>
        /// Gets the current iteration value.
        /// </summary>
        public SimpleString Current
        {
            readonly get;
            private set;
        }

        readonly object IEnumerator.Current => Current;

        void IEnumerator.Reset() => _index = _offset = 0;

        readonly void IDisposable.Dispose() { }
    }
}
