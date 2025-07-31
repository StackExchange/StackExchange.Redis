using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace StackExchange.Redis.Resp;

internal ref partial struct RespReader
{
    /// <summary>
     /// Reads the sub-elements associated with an aggregate value.
     /// </summary>
    public readonly AggregateEnumerator AggregateChildren() => new(in this);

    /// <summary>
    /// Reads the sub-elements associated with an aggregate value.
    /// </summary>
    public ref struct AggregateEnumerator
    {
        // Note that _reader is the overall reader that can see outside this aggregate, as opposed
        // to Current which is the sub-tree of the current element *only*
        private RespReader _reader;
        private int _remaining;

        /// <summary>
        /// Create a new enumerator for the specified <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The reader containing the data for this operation.</param>
        public AggregateEnumerator(scoped in RespReader reader)
        {
            reader.DemandAggregate();
            _remaining = reader.IsStreaming ? -1 : reader._length;
            _reader = reader;
            Value = default;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator()"/>
        public readonly AggregateEnumerator GetEnumerator() => this;

        /// <inheritdoc cref="IEnumerator{T}.Current"/>
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RespReader Current => Value;

        /// <summary>
        /// Gets the current element associated with this reader.
        /// </summary>
        public RespReader Value; // intentionally a field, because of ref-semantics

        /// <summary>
        /// Move to the next child if possible, and move the child element into the next node.
        /// </summary>
        public bool MoveNext(RespPrefix prefix)
        {
            bool result = MoveNext();
            if (result)
            {
                Value.MoveNext(prefix);
            }
            return result;
        }

        /// <summary>
        /// Move to the next child if possible, and move the child element into the next node.
        /// </summary>
        /// <typeparam name="T">The type of data represented by this reader.</typeparam>
        public bool MoveNext<T>(RespPrefix prefix, RespAttributeReader<T> respAttributeReader, ref T attributes)
        {
            bool result = MoveNext(respAttributeReader, ref attributes);
            if (result)
            {
                Value.MoveNext(prefix);
            }
            return result;
        }

        /// <inheritdoc cref="IEnumerator.MoveNext()"/>>
        public bool MoveNext()
            => MoveNextCore(null, ref Unsafe.NullRef<object>());

        /// <inheritdoc cref="IEnumerator.MoveNext()"/>>
        /// <typeparam name="T">The type of data represented by this reader.</typeparam>
        public bool MoveNext<T>(RespAttributeReader<T> respAttributeReader, ref T attributes)
            => MoveNextCore<T>(respAttributeReader, ref attributes);

        /// <inheritdoc cref="IEnumerator.MoveNext()"/>>
        private bool MoveNextCore<T>(RespAttributeReader<T>? attributeReader, ref T attributes)
        {
            if (_remaining == 0)
            {
                Value = default;
                return false;
            }

            // in order to provide access to attributes etc, we want Current to be positioned
            // *before* the next element; for that, we'll take a snapshot before we read
            _reader.MovePastCurrent();
            var snapshot = _reader.Clone();

            if (attributeReader is null)
            {
                _reader.MoveNext();
            }
            else
            {
                _reader.MoveNext(attributeReader, ref attributes);
            }
            if (_remaining > 0)
            {
                // non-streaming, decrement
                _remaining--;
            }
            else if (_reader.Prefix == RespPrefix.StreamTerminator)
            {
                // end of streaming aggregate
                _remaining = 0;
                Value = default;
                return false;
            }

            // move past that sub-tree and trim the "snapshot" state, giving
            // us a scoped reader that is *just* that sub-tree
            _reader.SkipChildren();
            snapshot.TrimToTotal(_reader.BytesConsumed);

            Value = snapshot;
            return true;
        }

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

    internal void TrimToTotal(long length) => TrimToRemaining(length - BytesConsumed);

    internal void TrimToRemaining(long bytes)
    {
        if (_prefix != RespPrefix.None || bytes < 0) Throw();

        var current = CurrentAvailable;
        if (bytes <= current)
        {
            UnsafeTrimCurrentBy(current - (int)bytes);
            _remainingTailLength = 0;
            return;
        }

        bytes -= current;
        if (bytes <= _remainingTailLength)
        {
            _remainingTailLength = bytes;
            return;
        }

        Throw();
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(bytes));
    }
}
