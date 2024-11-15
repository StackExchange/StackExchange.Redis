using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace RESPite.Resp;

/// <summary>
/// Represents a fixed-length sequence of <see cref="SimpleString"/> values.
/// </summary>
public readonly struct SimpleStrings : IEnumerable<SimpleString>, IEnumerable
{
    /// <inheritdoc />
    public override string ToString() => IsNull ? "(null)" : $"{Count} strings";

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => throw new NotSupportedException();

    private readonly SimpleString _buffer;

    /// <summary>
    /// Gets the number of elements in this sequence.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets whether this is a null sequence.
    /// </summary>
    public bool IsNull => _buffer.IsNull;

    /// <summary>
    /// An empty <see cref="SimpleString"/>.
    /// </summary>
    public static ref readonly SimpleStrings Empty => ref _empty;

    private static readonly SimpleStrings _empty = new(true);

    private SimpleStrings(bool dummy) // dummy constructor only used to create _empty
    {
        _buffer = SimpleString.Empty;
        Count = 0;
    }

    /// <summary>
    /// Create a new <see cref="SimpleStrings"/> value by partioning <paramref name="count"/> values over the provided <see cref="SimpleString"/> from
    /// <paramref name="value"/>; each value is interpreted as length-prefixed using a little-endian <see cref="int"/> value. The
    /// <paramref name="value"/> must be byte-based for non-empty sequences.
    /// </summary>
    public SimpleStrings(in SimpleString value, int count)
    {
        if (count < 0) ThrowCount();
        if (count == 0)
        {
            this = value.IsNull ? default : _empty;
            return;
        }

        if (value.IsEmpty) ThrowCount();
        if (!value.IsBytes) ThrowNotBytes();

        // we need a 4 byte length-prefix per item; if we don't have *that*, we're definitely wrong
        if (count > (value.GetByteCount() / sizeof(int))) ThrowCount();

        _buffer = value;
        Count = count;

        static void ThrowCount() => throw new ArgumentOutOfRangeException(nameof(count));
        static void ThrowNotBytes() => throw new ArgumentException("The provided value must be byte-based data", nameof(value));
    }

    /// <summary>
    /// Allows efficient iteration of the strings that make up this content.
    /// </summary>
    public Enumerator GetEnumerator() => new(_buffer, Count);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<SimpleString> IEnumerable<SimpleString>.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Allows efficient iteration of the strings that make up this content.
    /// </summary>
    public struct Enumerator : IEnumerator, IEnumerator<SimpleString>
    {
        internal Enumerator(in SimpleString buffer, int count)
        {
            _count = count;
            _buffer = buffer;
        }

        private readonly int _count;
        private readonly SimpleString _buffer;
        private SimpleString _current;
        private int _elementIndex;
        private int _byteIndex;

        /// <inheritdoc cref="IEnumerator{SimpleString}.Current"/>
        public readonly SimpleString Current => _current;

        readonly object IEnumerator.Current => _current;

        /// <inheritdoc cref="IEnumerator.MoveNext()" />
        public bool MoveNext()
        {
            if (_elementIndex >= _count)
            {
                _current = default;
                return false;
            }
            var nextLen = _buffer.ReadLitteEndianInt64(_byteIndex);
            if (nextLen > 0)
            {
                _current = _buffer.SliceBytes(_byteIndex + sizeof(int), nextLen);
                _byteIndex += nextLen + sizeof(int);
            }
            else
            {
                _current = nextLen < 0 ? default : SimpleString.Empty;
                _byteIndex += sizeof(int);
            }

            _elementIndex++;
            return true;
        }

        /// <inheritdoc cref="IEnumerator.Reset" />
        public void Reset() => _elementIndex = 0;

        readonly void IDisposable.Dispose() { }
    }
}
