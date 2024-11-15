using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using RESPite.Internal;

namespace RESPite.Resp;

/// <summary>
/// Represents multiple <see cref="LeasedString"/> payloads sharing an underlying buffer.
/// </summary>
public readonly struct LeasedStrings : IDisposable, IEnumerable<SimpleString>, IEnumerable
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => throw new NotSupportedException();

    private readonly LeasedString _chunk;
    private readonly int _count;

    /// <summary>
    /// An empty instance.
    /// </summary>
    public static LeasedStrings Empty { get; } = new(LeasedString.Empty, 0);

    /// <inheritdoc cref="Value"/>
    public static implicit operator SimpleStrings(in LeasedStrings value) => value.Value;

    /// <summary>
    /// Gets the values associated with this lease.
    /// </summary>
    public SimpleStrings Value => new(_chunk.Value, _count);

    /// <summary>
    /// Indicates whether this is a null value.
    /// </summary>
    public bool IsNull => _chunk.IsNull;

    /// <summary>
    /// Gets the number of values in this instance.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Indicates whether this is an empty instance.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose() => _chunk.Dispose();

    private LeasedStrings(in LeasedString chunk, int count)
    {
        _chunk = chunk;
        _count = count;
    }

    private LeasedStrings(int elementCount, byte[] buffer, int byteCount)
    {
        _chunk = new(buffer, byteCount);
        _count = elementCount;
    }

    /// <summary>
    /// Create a new instance, copying data from an existing collection.
    /// </summary>
    public LeasedStrings(IReadOnlyCollection<SimpleString> values)
    {
        int count;
        long bytes = 0;
        if (values is null)
        {
            this = default;
            return;
        }
        if ((count = values.Count) == 0)
        {
            this = Empty;
            return;
        }

        if (values is List<SimpleString> list)
        {
            foreach (var value in list)
            {
                bytes += value.GetByteCount();
            }
            Builder builder = new(count, checked((int)bytes));
            try
            {
                foreach (var value in list)
                {
                    builder.Add(value);
                }
                this = builder.Create();
            }
            catch
            {
                builder.Dispose();
                throw;
            }
        }
        else if (values is SimpleString[] arr)
        {
            foreach (var value in arr)
            {
                bytes += value.GetByteCount();
            }
            Builder builder = new(count, checked((int)bytes));
            try
            {
                foreach (var value in arr)
                {
                    builder.Add(value);
                }
                this = builder.Create();
            }
            catch
            {
                builder.Dispose();
                throw;
            }
        }
        else
        {
            foreach (var value in values)
            {
                bytes += value.GetByteCount();
            }
            Builder builder = new(count, checked((int)bytes));
            try
            {
                foreach (var value in values)
                {
                    builder.Add(value);
                }
                this = builder.Create();
            }
            catch
            {
                builder.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Allows construction of a <see cref="LeasedStrings"/> value.
    /// </summary>
    public struct Builder : IDisposable
    {
        private readonly int _maxCount;
        private byte[]? _buffer;
        private int _elementIndex;
        private int _byteOffset;

        /// <summary>
        /// The number of values added to this instance.
        /// </summary>
        public int Count => _elementIndex;

        /// <summary>
        /// The total number of bytes, excluding header tokens, written to this instance.
        /// </summary>
        public int PayloadBytes => _byteOffset - (_elementIndex * sizeof(int));

        /// <summary>
        /// The total number of bytes, including header tokens, written to this instance.
        /// </summary>
        public int TotalBytes => _byteOffset;

        /// <summary>
        /// Allows construction of a <see cref="LeasedStrings"/> value.
        /// </summary>
        /// <param name="maxCount">The maximum number of elements that can be added (it is acceptable to add less), including empty/null values.</param>
        /// <param name="minBytes">Total payload bytes required for all non empty/null values.</param>
        /// <remarks>Over-estimating may lead to inefficient buffer usage. Underestimating will lead to exceptions when adding data.</remarks>
        public Builder(int maxCount, int minBytes)
        {
            if (maxCount < 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (minBytes < 0) throw new ArgumentOutOfRangeException(nameof(minBytes));
            if (maxCount == 0) minBytes = 0;

            // rent a buffer, including space for N * int length markers
            _buffer = ArrayPool<byte>.Shared.Rent(checked(minBytes + (maxCount * sizeof(int))));
            _maxCount = maxCount;
            _byteOffset = _elementIndex = 0;
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = null;
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>
        /// Add a null value.
        /// </summary>
        public void AddNull() => AddCore(-1);

        /// <summary>
        /// Add an empty value.
        /// </summary>
        public void AddEmpty() => AddCore(0);

        /// <summary>
        /// Reserve a new value of length <paramref name="length"/>.
        /// </summary>
        public Span<byte> Add(int length, bool clear = true)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            var span = AddCore(length);
            if (clear) span.Clear();
            return span;
        }

        /// <summary>
        /// Add an existing value.
        /// </summary>
        public void Add(in SimpleString value)
        {
            var len = value.GetByteCount();
            value.CopyTo(Add(len, clear: false));
        }

        private Span<byte> AddCore(int length)
        {
            CheckAlive();
            if (_maxCount == 0) throw new InvalidOperationException("The builder is empty and does not allow items to be added");
            if (_elementIndex >= _maxCount) throw new InvalidOperationException("All items already written");

            var spaceNeeded = length <= 0 ? sizeof(int) : checked(sizeof(int) + length);
            if (_byteOffset + spaceNeeded > _buffer.Length) throw new InvalidOperationException("Buffer capacity exceeded; was enough reserved initially?");

            BinaryPrimitives.WriteInt32LittleEndian(new(_buffer, _byteOffset, sizeof(int)), length);

            var result = length <= 0 ? default : new Span<byte>(_buffer, _byteOffset + sizeof(int), length);
            _elementIndex++;
            _byteOffset += spaceNeeded;
            return result;
        }

        /// <summary>
        /// Create a <see cref="LeasedStrings"/> for the data added.
        /// </summary>
        public LeasedStrings Create()
        {
            CheckAlive();

            var result = new LeasedStrings(_elementIndex, _buffer, _byteOffset);
            _buffer = null;
            return result;
        }

        [MemberNotNull(nameof(_buffer))]
        private void CheckAlive()
        {
            if (_buffer is null) throw new ObjectDisposedException(nameof(Builder));
        }
    }

    /// <summary>
    /// Enumerates all elements contained by this instance.
    /// </summary>
    public SimpleStrings.Enumerator GetEnumerator() => Value.GetEnumerator();

    IEnumerator<SimpleString> IEnumerable<SimpleString>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
