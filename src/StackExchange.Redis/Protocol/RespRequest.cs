using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;

namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. A rendered RESP request, detached from its builder: the bytes to send, and - the
    /// same bytes - the cache key, without ever being copied into a <c>byte[]</c> or a <c>string</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bytes that were going to be sent anyway ARE the cache key, so a lookup costs a render and no
    /// allocation at all. Deliberately not a <c>ref struct</c>: it has to survive as a <c>TKey</c>, cross an
    /// <c>await</c>, and be parked in a backlog for a resend - none of which a <c>ref struct</c> or a raw
    /// span can do. That is why the bytes live in a pooled array behind a
    /// <see cref="RefCountedBuffer"/>, and why the executor can <see cref="TryRetain"/> to hold one
    /// past the call.
    /// </para>
    /// <para>
    /// <b>Lifetime.</b> Whoever retains, releases. <see cref="RespRequestFrame.Detach"/> hands back a key holding
    /// one reference; <see cref="TryRetain"/> takes another. Dispose each one exactly once. The rule for the
    /// dictionary is that the STORED key holds its own reference for as long as it is in the dictionary -
    /// see the remarks on <see cref="TryRetain"/> - which is what section 6.4 of the design doc means by "the
    /// cache entry pins the lease".
    /// </para>
    /// <para>
    /// <b>Why a reference count and not ownership transfer.</b> The design doc originally sketched a
    /// neuterable <c>Dispose</c> plus <c>TransferOwnership</c>. A count is less error-prone: with transfer,
    /// every holder has to know whether ownership moved, and the answer is only known after dispatch.
    /// </para>
    /// </remarks>
    public readonly struct RespRequest : IEquatable<RespRequest>, IDisposable
    {
        private readonly byte[]? _array;
        private readonly RefCountedBuffer? _lease;   // null => BORROWED: this key owns no reference
        private readonly int _offset;
        private readonly int _length;
        private readonly int _hash;

        // carried over from the frame, because an executor decorator sees only a request. Routing needs the
        // slot; a cache needs the key marks to register dependencies; retry and cacheability need the flags.
        // Without these a decorator can ask nothing about what it is sending.
        private readonly ulong _keyMarks;

        internal RespRequest(
            byte[] array,
            RefCountedBuffer? lease,
            int offset,
            int length,
            ulong keyMarks = 0,
            int slot = ServerSelectionStrategy.NoSlot,
            int argCount = 0,
            CommandFlags flags = CommandFlags.None,
            RedisCommand command = RedisCommand.UNKNOWN)
        {
            _array = array;
            _lease = lease;
            _offset = offset;
            _length = length;
            _hash = RedisValue.GetHashCode(array.AsSpan(offset, length));
            _keyMarks = keyMarks;
            Slot = slot;
            ArgCount = argCount;
            Flags = flags;
            Command = command;
        }

        /// <summary>The combined cluster slot; routing needs this and nothing else about the keys.</summary>
        public int Slot { get; }

        /// <summary>The number of RESP arguments, including the command itself.</summary>
        public int ArgCount { get; }

        /// <inheritdoc cref="RespRequestFrame.Command"/>
        internal RedisCommand Command { get; }

        /// <summary>
        /// The command's flags: the retry category a retrying executor needs, and the caching gates.
        /// </summary>
        public CommandFlags Flags { get; }

        /// <summary>
        /// How many arguments were keys, or <c>-1</c> when the request cannot report them.
        /// </summary>
        /// <inheritdoc cref="RespRequestFrame.KeyCount" path="/remarks"/>
        public int KeyCount => RespRequestFrame.KeyCountOf(_keyMarks);

        /// <summary>Recover the key payloads; see <see cref="RespRequestFrame.TryGetKeys"/>.</summary>
        /// <param name="target">Receives the ranges; size it from <see cref="KeyCount"/>.</param>
        public int TryGetKeys(scoped Span<KeyRange> target)
            => _array is null ? -1 : RespRequestFrame.ResolveKeys(_array, _offset, _length, _keyMarks, target);

        /// <summary>Every argument, whether or not marked as a key; see <see cref="RespRequestFrame.ResolveAllArguments"/>.</summary>
        internal int TryGetAllArguments(scoped Span<KeyRange> target)
            => _array is null ? -1 : RespRequestFrame.ResolveAllArguments(_array, _offset, _length, target);

        /// <summary>Resolve a <see cref="KeyRange"/> against the underlying buffer.</summary>
        /// <param name="range">The range to resolve.</param>
        public ReadOnlySpan<byte> GetKey(in KeyRange range)
            => _array is null ? default : new(_array, range.Offset, range.Length);

        /// <summary>Whether this key refers to anything; a <c>default</c> instance does not.</summary>
        public bool IsEmpty => _array is null;

        /// <summary>
        /// Whether this key owns a reference to its buffer, and so may be stored.
        /// </summary>
        /// <remarks>
        /// False for a key from <see cref="RespRequestFrame.AsLookupKey"/>, which borrows the frame's buffer and is
        /// valid only until the frame is disposed. Storing a borrowed key would put a pooled array into a
        /// cache and then hand it back to the pool - design doc section 6.4, whose failure mode is wrong data
        /// served from cache rather than a crash. <see cref="TryRetain"/> refuses, so the documented
        /// retain-then-store idiom cannot express the mistake.
        /// </remarks>
        public bool IsOwned => _lease is not null;

        /// <summary>
        /// The rendered frame. Throws once the last reference has gone, rather than quietly reading bytes
        /// that now belong to somebody else's rent.
        /// </summary>
        /// <remarks>
        /// The throw comes from <see cref="RefCountedBuffer"/>, which is a <see cref="MemoryManager{T}"/>
        /// precisely so that every span access routes through one check. It is a misuse detector, not a
        /// substitute for holding a reference - see <see cref="RespPayload.TryRetain"/>.
        /// </remarks>
        // owned: routed through the lease, so use-after-free throws; borrowed: straight at the frame's array
        public ReadOnlySpan<byte> Span => _lease is not null
            ? _lease.GetSpan().Slice(_offset, _length)
            : _array is null ? default : _array.AsSpan(_offset, _length);

        /// <summary>Read the frame back, for tests and diagnostics.</summary>
        public RespReader GetReader() => new(Span);

        /// <summary>
        /// Take another reference and return a key that owns it, for handing to a cache that will outlive
        /// the caller's own <c>using</c>.
        /// </summary>
        /// <remarks>
        /// Returns <c>false</c> if the buffer is already dead. Store the key this produces, not the one you
        /// called it on: they compare equal and address the same bytes, but they are separate references
        /// and each must be disposed once. The usual shape is retain, try to add, and dispose the retained
        /// copy if the add lost a race.
        /// </remarks>
        public bool TryRetain(out RespRequest retained)
        {
            if (_lease is not null && _lease.TryAddRef())
            {
                retained = this;
                return true;
            }

            retained = default;
            return false; // borrowed, or the buffer is already back in the pool
        }

        /// <summary>Release this key's reference; the buffer returns to the pool with the last one.</summary>
        /// <remarks>Release exactly one reference per retain. A <c>default</c> key holds none.</remarks>
        public void Dispose() => _lease?.Release();

        /// <summary>Compare by CONTENT, so a freshly rendered frame finds a cached one.</summary>
        /// <remarks>
        /// Content equality is the entire point: the lookup key and the stored key are different rentals of
        /// different arrays. Canonicality of the rendering is therefore a correctness property - see design
        /// doc section 6.
        /// </remarks>
        /// <remarks>
        /// Note what is NOT compared: slot, arg count, flags and key marks. Identity is the rendered bytes
        /// and only the rendered bytes, because that is what the cache is keyed on - two callers issuing
        /// the same command with different <see cref="CommandFlags"/> are asking the same question.
        /// </remarks>
        public bool Equals(RespRequest other)
            => _hash == other._hash && _length == other._length && Span.SequenceEqual(other.Span);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is RespRequest other && Equals(other);

        /// <inheritdoc/>
        /// <remarks>Computed once, when the key is detached, while the bytes are already in cache.</remarks>
        public override int GetHashCode() => _hash;

        /// <inheritdoc/>
        public override string ToString() =>
            _lease is null ? "(empty)" : System.Text.Encoding.UTF8.GetString(Span.ToArray()).Replace("\r\n", "|");
    }
}
