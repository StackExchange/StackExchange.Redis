using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Messages;

public ref partial struct RespReader
{
    /// <summary>
    /// Gets the chunks associated with a scalar value.
    /// </summary>
    public readonly ScalarEnumerator ScalarChunks() => new(in this);

    /// <summary>
    /// Allows enumeration of chunks in a scalar value; this includes simple values
    /// that span multiple <see cref="ReadOnlySequence{T}"/> segments, and streaming
    /// scalar RESP values.
    /// </summary>
    public ref struct ScalarEnumerator
    {
        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
        public readonly ScalarEnumerator GetEnumerator() => this;

        private RespReader _reader;

        private ReadOnlySpan<byte> _current;
        private ReadOnlySequenceSegment<byte>? _tail;
        private int _offset, _remaining;

        /// <summary>
        /// Create a new enumerator for the specified <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader containing the data for this operation.</param>
        public ScalarEnumerator(scoped in RespReader reader)
        {
            reader.DemandScalar();
            _reader = reader;
            InitSegment();
        }

        private void InitSegment()
        {
            _current = _reader.CurrentSpan();
            _tail = _reader._tail;
            _offset = CurrentLength = 0;
            _remaining = _reader._length;
            if (_reader.TotalAvailable < _remaining) ThrowEOF();
        }

        /// <inheritdoc cref="IEnumerator.MoveNext"/>
        public bool MoveNext()
        {
            while (true) // for each streaming element
            {
                _offset += CurrentLength;
                while (_remaining > 0) // for each span in the current element
                {
                    // look in the active span
                    var take = Math.Min(_remaining, _current.Length - _offset);
                    if (take > 0) // more in the current chunk
                    {
                        _remaining -= take;
                        CurrentLength = take;
                        return true;
                    }

                    // otherwise, we expect more tail data
                    if (_tail is null) ThrowEOF();

                    _current = _tail.Memory.Span;
                    _offset = 0;
                    _tail = _tail.Next;
                }

                if (!_reader.MoveNextStreamingScalar()) break;
                InitSegment();
            }

            CurrentLength = 0;
            return false;
        }

        /// <inheritdoc cref="IEnumerator{T}.Current"/>
        public readonly ReadOnlySpan<byte> Current => _current.Slice(_offset, CurrentLength);

        /// <summary>
        /// Gets the <see cref="ReadOnlySpan{T}.Length"/> or <see cref="Current"/>.
        /// </summary>
        public int CurrentLength { readonly get; private set; }

        /// <summary>
        /// Move to the end of this aggregate and export the state of the <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader positioned at the end of the data; this is commonly
        /// used to update a tree reader, to get to the next data after the aggregate.</param>
        public void MovePast(out RespReader reader)
        {
            while (MoveNext()) { }
            reader = _reader;
        }
    }
}
