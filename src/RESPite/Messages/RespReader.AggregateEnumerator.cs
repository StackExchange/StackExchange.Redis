using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Messages;

public ref partial struct RespReader
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
#if DEBUG
#if NET6_0 || NET8_0
        [Experimental("SERDBG")]
#else
        [Experimental("SERDBG", Message = $"Prefer {nameof(Value)}")]
#endif
#endif
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
            bool result = MoveNextRaw();
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
            bool result = MoveNextRaw(respAttributeReader, ref attributes);
            if (result)
            {
                Value.MoveNext(prefix);
            }
            return result;
        }

        /// <summary>
        /// Move to the next child and leave the reader *ahead of* the first element,
        /// allowing us to read attribute data.
        /// </summary>
        /// <remarks>If you are not consuming attribute data, <see cref="MoveNext()"/> is preferred.</remarks>
        public bool MoveNextRaw()
        {
            object? attributes = null;
            return MoveNextCore(null, ref attributes);
        }

        /// <summary>
        /// Move to the next child and move into the first element (skipping attributes etc), leaving it ready to consume.
        /// </summary>
        public bool MoveNext()
        {
            object? attributes = null;
            if (MoveNextCore(null, ref attributes))
            {
                Value.MoveNext();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Move to the next child (capturing attribute data) and leave the reader *ahead of* the first element,
        /// allowing us to also read attribute data of the child.
        /// </summary>
        /// <typeparam name="T">The type of attribute data represented by this reader.</typeparam>
        /// <remarks>If you are not consuming attribute data, <see cref="MoveNext()"/> is preferred.</remarks>
        public bool MoveNextRaw<T>(RespAttributeReader<T> respAttributeReader, ref T attributes)
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
            while (MoveNextRaw()) { }
            reader = _reader;
        }

        /// <summary>
        /// Moves to the next element, and moves into that element (skipping attributes etc), leaving it ready to consume.
        /// </summary>
        public void DemandNext()
        {
            if (!MoveNext()) ThrowEof();
        }

        public T ReadOne<T>(Projection<T> projection)
        {
            DemandNext();
            return projection(ref Value);
        }

        public void FillAll<TResult>(scoped Span<TResult> target, Projection<TResult> projection)
        {
            FillAll(target, projection, static (in projection, ref reader) => projection(ref reader));
        }

        public void FillAll<TState, TResult>(scoped Span<TResult> target, in TState state, Projection<TState, TResult> projection)
#if NET9_0_OR_GREATER
            where TState : allows ref struct
#endif
        {
            for (int i = 0; i < target.Length; i++)
            {
                DemandNext();
                target[i] = projection(in state, ref Value);
            }
        }

        public void FillAll<TFirst, TSecond, TResult>(
            scoped Span<TResult> target,
            Projection<TFirst> first,
            Projection<TSecond> second,
            Func<TFirst, TSecond, TResult> combine)
        {
            for (int i = 0; i < target.Length; i++)
            {
                DemandNext();

                var x = first(ref Value);

                DemandNext();

                var y = second(ref Value);
                target[i] = combine(x, y);
            }
        }

        public void FillAll<TState, TFirst, TSecond, TResult>(
            scoped Span<TResult> target,
            in TState state,
            Projection<TState, TFirst> first,
            Projection<TState, TSecond> second,
            Func<TState, TFirst, TSecond, TResult> combine)
#if NET9_0_OR_GREATER
            where TState : allows ref struct
#endif
        {
            for (int i = 0; i < target.Length; i++)
            {
                DemandNext();

                var x = first(state, ref Value);

                DemandNext();

                var y = second(state, ref Value);
                target[i] = combine(state, x, y);
            }
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
