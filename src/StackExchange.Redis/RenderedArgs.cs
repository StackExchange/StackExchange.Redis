using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Implemented by a message that holds a <see cref="RenderedArgs"/> for the lifetime of its request.
    /// </summary>
    /// <remarks>
    /// Deliberately an interface rather than a virtual on <c>Message</c>: only the handful of messages that
    /// render their arguments up front care, and the one processor that calls this is used by nothing else,
    /// so the type test always hits. No reason to put a slot on the base type of every command in the
    /// library for it.
    /// </remarks>
    internal interface IRenderedArgsOwner
    {
        /// <summary>
        /// Called once a reply has arrived that is known to be the final one for this message, so the
        /// request buffer will not be needed again.
        /// </summary>
        /// <remarks>
        /// The arrival of a reply is what makes this safe: it proves the write completed, which completion
        /// alone does not - a message becomes visible to the async-timeout heartbeat when it is queued,
        /// before WriteImpl has run. Must be idempotent; a redirect or a NOSCRIPT retry re-issues the same
        /// instance, so those replies deliberately do not call it.
        /// </remarks>
        void ReleaseRenderedArgs();
    }

    /// <summary>
    /// The keys and values of a request, rendered once into a single pooled buffer at the point of the
    /// call, so that the request no longer refers to any memory the caller owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callers can pass keys and values backed by their own arrays - and are encouraged to, for the sake
    /// of allocation - but the request is not necessarily written before the call returns: it may be
    /// queued in the backlog, or in a batch, and fire-and-forget callers get no completion signal at all.
    /// Rendering here means the caller's buffers are theirs again the moment the call returns, and it also
    /// moves the formatting off the writer thread, where it would otherwise be done while holding the
    /// single-writer lock.
    /// </para>
    /// <para>
    /// Entries are laid out back to back as a 4-byte little-endian length followed by that many payload
    /// bytes. A negative length marks a key, with <c>~length</c> giving the real size; the complement
    /// rather than negation so that a zero-length key is still distinguishable from a zero-length value.
    /// </para>
    /// <para>
    /// <b>This type owns a pooled buffer, so it must live in exactly one place and must never be copied.</b>
    /// Copying it duplicates the ownership and gets the buffer returned to the pool twice, which is a
    /// silent corruption rather than a loud failure. Note also that the field holding it must not be
    /// <c>readonly</c> - see <see cref="Recycle"/>.
    /// </para>
    /// </remarks>
    internal struct RenderedArgs
    {
        // deliberately not readonly: Recycle swaps this out atomically. Either a byte[] from
        // ArrayPool<byte>.Shared or an IMemoryOwner<byte> from the configured RequestBufferPool - the
        // same shape RespResult and Lease<T> use, so that a caller who supplies a pool gets it honoured
        // on the request side too.
        private object _buffer;
        private int _count, _length;

        /// <summary>The number of entries rendered.</summary>
        public readonly int Count => _count;

        private const int PrefixLength = sizeof(int);

        /// <summary>
        /// Render the arguments of an ad-hoc command, which may be a mix of keys and values.
        /// </summary>
        public static RenderedArgs Create(ReadOnlySpan<RedisKeyOrValue> args, MemoryPool<byte>? pool)
        {
            int total = 0;
            foreach (ref readonly var arg in args)
            {
                // rejected here rather than at write time: the caller finds out on the call that made
                // the mistake, instead of from a background writer some time later
                if (arg.IsNull) throw new InvalidOperationException("A null is not valid in this context");
                total += PrefixLength + (arg.IsKey ? arg.Key.TotalLength() : arg.Value.GetByteCount());
            }

            var result = Rent(args.Length, total, pool);
            var target = result.Buffer;
            foreach (ref readonly var arg in args)
            {
                target = arg.IsKey ? WriteKey(target, arg.Key) : WriteValue(target, arg.Value);
            }

            Debug.Assert(target.IsEmpty, "should have filled the buffer exactly");
            return result;
        }

        /// <summary>
        /// Render the keys and values of a script invocation; keys are emitted first, as EVAL expects.
        /// </summary>
        public static RenderedArgs Create(ReadOnlySpan<RedisKey> keys, ReadOnlySpan<RedisValue> values, MemoryPool<byte>? pool)
        {
            int total = 0;
            foreach (ref readonly var key in keys)
            {
                key.AssertNotNull();
                total += PrefixLength + key.TotalLength();
            }
            foreach (ref readonly var value in values)
            {
                value.AssertNotNull();
                total += PrefixLength + value.GetByteCount();
            }

            var result = Rent(keys.Length + values.Length, total, pool);
            var target = result.Buffer;
            foreach (ref readonly var key in keys) target = WriteKey(target, key);
            foreach (ref readonly var value in values) target = WriteValue(target, value);

            Debug.Assert(target.IsEmpty, "should have filled the buffer exactly");
            return result;
        }

        private static RenderedArgs Rent(int count, int length, MemoryPool<byte>? pool) => new RenderedArgs
        {
            _buffer = length == 0 ? Array.Empty<byte>()
                : pool is null ? ArrayPool<byte>.Shared.Rent(length) : pool.Rent(length),
            _count = count,
            _length = length,
        };

        // the rented buffer is usually larger than we asked for; only the used span is ours
        private readonly Span<byte> Buffer => _buffer switch
        {
            byte[] arr => new Span<byte>(arr, 0, _length),
            IMemoryOwner<byte> owner => owner.Memory.Span.Slice(0, _length),
            _ => default,
        };

        private static Span<byte> WriteKey(Span<byte> target, scoped in RedisKey key)
        {
            var length = key.TotalLength();
            Unsafe.WriteUnaligned(ref target[0], ~length);
            var written = key.CopyTo(target.Slice(PrefixLength));
            Debug.Assert(written == length, "key length disagreed with itself");
            return target.Slice(PrefixLength + length);
        }

        private static Span<byte> WriteValue(Span<byte> target, scoped in RedisValue value)
        {
            var length = value.GetByteCount();
            Unsafe.WriteUnaligned(ref target[0], length);
            var written = value.CopyTo(target.Slice(PrefixLength));
            Debug.Assert(written == length, "value length disagreed with itself");
            return target.Slice(PrefixLength + length);
        }

        /// <summary>
        /// Write every entry as a RESP bulk string, in the order they were rendered.
        /// </summary>
        /// <remarks>
        /// Keys and values are indistinguishable on the wire - the flag exists for slot routing, not for
        /// framing - so this writes them identically.
        /// </remarks>
        public readonly void WriteTo(in MessageWriter writer)
        {
            var written = 0;
            var iter = GetEnumerator();
            while (iter.MoveNext())
            {
                writer.WriteBulkString(iter.Current);
                written++;
            }

            // deliberately not a Debug.Assert: the header has already declared Count arguments, so writing
            // a different number puts a malformed frame on the wire, and the server's complaint arrives
            // later and somewhere else. If the buffer went away underneath us - recycled early, or torn by
            // a race - fail here, loudly, attached to the request that caused it.
            if (written != _count) ThrowArgCountMismatch(written, _count);
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowArgCountMismatch(int written, int expected) =>
            throw new InvalidOperationException(
                $"Rendered arguments were expected to write {expected} value(s), but wrote {written}; the request buffer is no longer valid.");

        /// <summary>
        /// The combined cluster slot of the keys, or <see cref="ServerSelectionStrategy.NoSlot"/> when
        /// there are none, or <see cref="ServerSelectionStrategy.MultipleSlots"/> when they disagree.
        /// </summary>
        /// <remarks>
        /// Values are skipped: only arguments the caller declared as keys take part in routing. Note this
        /// hashes the rendered bytes directly, where <see cref="ServerSelectionStrategy.GetHashSlot"/> has
        /// to copy the key out first - we already have exactly the bytes it would have produced.
        /// </remarks>
        public readonly int GetHashSlot(ServerSelectionStrategy strategy)
        {
            if (strategy.ServerType is ServerType.Standalone) return ServerSelectionStrategy.NoSlot;

            var slot = ServerSelectionStrategy.NoSlot;
            var iter = GetEnumerator();
            while (iter.MoveNext())
            {
                if (!iter.IsKey) continue;
                slot = ServerSelectionStrategy.CombineSlot(slot, ServerSelectionStrategy.GetClusterSlot(iter.Current));
            }
            return slot;
        }

        /// <summary>
        /// Walks the rendered entries in order.
        /// </summary>
        public readonly Enumerator GetEnumerator() => new Enumerator(Buffer);

        /// <summary>
        /// Return the buffer to the pool; safe to call repeatedly, and from multiple threads - only the
        /// first caller sees the buffer.
        /// </summary>
        /// <remarks>
        /// Taken by <c>ref</c> on purpose. As an instance method this would compile perfectly happily
        /// against a <c>readonly</c> field and silently operate on a defensive copy, leaving the real
        /// buffer stranded; as a <c>ref</c> parameter, that same mistake is CS0192 at build time.
        /// </remarks>
        public static void Recycle(ref RenderedArgs args)
        {
            // length first, and before the exchange: a reader derives its span from _buffer and then
            // _length, so anything that observes the swapped-out buffer is guaranteed to see a length of
            // zero, and anything still holding the old buffer may see it too. Losing this race then writes
            // nothing - which WriteTo turns into a loud count mismatch - instead of writing whatever the
            // next renter has since put in that array. The exchange below is the barrier that orders it.
            args._length = 0;

            var buffer = Interlocked.Exchange(ref args._buffer, Array.Empty<byte>());

            // _count is deliberately left alone: it is what WriteTo checks itself against, and zeroing it
            // would make a write after recycling look perfectly consistent while emitting nothing.

            // null when never rendered, empty when already recycled or nothing to render
            if (buffer is byte[] { Length: > 0 } arr) ArrayPool<byte>.Shared.Return(arr);
            else if (buffer is IMemoryOwner<byte> owner) owner.Dispose();
        }

        /// <summary>Walks rendered entries.</summary>
        internal ref struct Enumerator(ReadOnlySpan<byte> remaining)
        {
            // read-only: walking never writes to the rendered buffer, it only slices through it
            private ReadOnlySpan<byte> _remaining = remaining;

            /// <summary>The payload of the current entry.</summary>
            public ReadOnlySpan<byte> Current { get; private set; }

            /// <summary>Whether the current entry was supplied as a key rather than a value.</summary>
            public bool IsKey { get; private set; }

            /// <summary>Move to the next entry.</summary>
            public bool MoveNext()
            {
                if (_remaining.IsEmpty)
                {
                    Current = default;
                    IsKey = false;
                    return false;
                }

                var prefix = Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(_remaining));
                IsKey = prefix < 0;
                var length = IsKey ? ~prefix : prefix;
                Current = _remaining.Slice(PrefixLength, length);
                _remaining = _remaining.Slice(PrefixLength + length);
                return true;
            }
        }
    }
}
