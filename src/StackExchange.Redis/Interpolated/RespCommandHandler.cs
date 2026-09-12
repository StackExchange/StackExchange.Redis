using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Renders a RESP command from an interpolated string, folding the cluster slot and
    /// marking which arguments were keys as it writes.
    /// </summary>
    /// <remarks>
    /// Every part must be a hole - <c>$"{RedisCommand.SET}{key}{value}"</c> - because that makes the
    /// compiler-supplied <c>formattedCount</c> the argument count. See <c>design/interpolated-resp-writer.md</c>.
    /// </remarks>
    [InterpolatedStringHandler]
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public ref struct RespCommandHandler
    {
        /// <summary>'*' plus up to nine digits plus CRLF; reserved at the front so the header can be
        /// back-filled right-aligned once the final argument count is known.</summary>
        private const int HeaderMax = 12;

        private readonly RespContext _context;
        private byte[] _buffer;
        private int _offset;        // absolute write cursor within _buffer
        private int _args;          // RESP argument count, including the command
        private int _argIndex;      // logical argument position, for the overflow bitmap
        private int _slot;
        private ulong _keyMarks;
        private bool _hasCommand;

        /// <summary>Initialize with the command supplied as the first hole.</summary>
        /// <param name="literalLength">Total length of the literal segments; compiler-supplied.</param>
        /// <param name="formattedCount">Number of holes; compiler-supplied.</param>
        /// <param name="context">The receiver of the call, supplying the command map and prefixes.</param>
        public RespCommandHandler(int literalLength, int formattedCount, RespContext context)
        {
            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _args = 0;
            _argIndex = 0;
            _slot = ServerSelectionStrategy.NoSlot;
            _keyMarks = 0;
            _hasCommand = false;
        }

        /// <summary>
        /// Initialize with the command supplied as a real argument rather than as the first hole, so that
        /// the interpolation carries only the arguments.
        /// </summary>
        /// <remarks>
        /// Preferred over the command-as-a-hole form: the command map is consulted <b>before</b> the buffer
        /// is rented, so a disabled command - the most likely throw in this window, and the most likely to
        /// repeat, being configuration-driven - drops nothing on the floor. See
        /// <c>design/interpolated-resp-writer.md</c> section 6.5.
        /// </remarks>
        internal RespCommandHandler(int literalLength, int formattedCount, RespContext context, RedisCommand command)
        {
            // resolve FIRST: this throws before anything is rented
            var resp = context.CommandMap.GetResp(command);
            if (resp.IsEmpty) throw ExceptionFactory.CommandDisabled(command);

            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + resp.Length + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _slot = ServerSelectionStrategy.NoSlot;
            _keyMarks = 0;

            resp.CopyTo(_buffer.AsSpan(_offset));
            _offset += resp.Length;
            _hasCommand = true;
            _args = 1;
            _argIndex = 1;
        }

        /// <summary>
        /// Initialize from a command <b>name</b>, for callers without access to the internal
        /// <c>RedisCommand</c> enum. The name is speculatively parsed to a known command so command-map
        /// aliasing and disabling still apply; anything unrecognised is framed verbatim, matching how
        /// <c>IDatabase.Execute(string, ...)</c> already behaves.
        /// </summary>
        public RespCommandHandler(int literalLength, int formattedCount, RespContext context, string command)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));

            ReadOnlySpan<byte> resp = default;
            var known = RedisCommandMetadata.TryParseCI(command.AsSpan(), out var parsed) && parsed != RedisCommand.UNKNOWN;
            if (known)
            {
                resp = context.CommandMap.GetResp(parsed);
                if (resp.IsEmpty) throw ExceptionFactory.CommandDisabled(parsed);
            }

            var nameBytes = known ? 0 : Encoding.UTF8.GetByteCount(command);
            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + resp.Length + nameBytes + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _slot = ServerSelectionStrategy.NoSlot;
            _keyMarks = 0;
            _hasCommand = true;
            _args = 1;
            _argIndex = 1;

            if (known)
            {
                resp.CopyTo(_buffer.AsSpan(_offset));
                _offset += resp.Length;
            }
            else
            {
                var payloadOffset = 0;
                WriteBulk(nameBytes, out payloadOffset);
                Encoding.UTF8.GetBytes(command, 0, command.Length, _buffer, _offset + payloadOffset);
                CommitBulk(payloadOffset, nameBytes);
            }
        }

        /// <summary>
        /// Literal text is rejected, with one exception: a single space, which is discarded. That keeps
        /// <c>$"{RedisCommand.SET} {key} {value}"</c> readable - it mirrors how the command is written
        /// everywhere else - without the space becoming an argument.
        /// </summary>
        /// <remarks>
        /// Rejecting literals is what makes the compiler-supplied <c>formattedCount</c> the argument count,
        /// so the <c>*N</c> header can be a compile-time constant. A discarded space does not affect that:
        /// spaces are literal segments, not holes. See <c>design/interpolated-resp-writer.md</c> section 2.1.
        /// <para>
        /// This is a runtime check; the analyzer is expected to catch it at build time, which it must, since
        /// two spaces look exactly like one.
        /// </para>
        /// </remarks>
        public void AppendLiteral(string value)
        {
            if (value is not " ") ThrowNotSeparator(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSeparator(string value) => throw new ArgumentException(
            $"Only a single space may separate arguments; every other part must be a hole. Saw \"{value}\". "
            + "Write $\"{RedisCommand.SET} {key} {value}\", not $\"SET {key} {value}\".",
            nameof(value));

        internal void AppendFormatted(RedisCommand value)
        {
            if (_hasCommand) throw new InvalidOperationException("The command must be the first argument, and may only be given once.");

            var resp = _context.CommandMap.GetResp(value);
            if (resp.IsEmpty) throw ExceptionFactory.CommandDisabled(value);

            Ensure(resp.Length);
            resp.CopyTo(_buffer.AsSpan(_offset));
            _offset += resp.Length;
            _hasCommand = true;
            _args++;
            _argIndex++;
        }

        /// <summary>Append a key: prefixed, marked for invalidation, and folded into the cluster slot.</summary>
        /// <param name="value">The key to append.</param>
        public void AppendFormatted(RedisKey value)
        {
            DemandCommand();

            // Both prefix mechanisms have to coexist: the context's prefix, and any prefix the key already
            // carries from a KeyPrefixed* decorator. RedisKey.WithPrefix would ALLOCATE to combine them (see
            // its "two prefixes; darn" branch) because it has to hand back a RedisKey - but we only need the
            // combined BYTES, and we are writing bytes anyway. TotalLength()/CopyTo() already account for the
            // key's own prefix, so writing the context prefix ahead of it composes both for free.
            var prefix = _context.KeyPrefixSpan;
            MarkKey(_offset);
            var keyLength = value.TotalLength();
            var length = prefix.Length + keyLength;
            var payload = WriteBulk(length, out var payloadOffset);
            prefix.CopyTo(payload);
            var written = value.CopyTo(payload.Slice(prefix.Length));
            Debug.Assert(written == keyLength, "key length disagreed with itself");
            CommitBulk(payloadOffset, length);
            FoldSlot(payload);
            _args++;
            _argIndex++;
        }

        /// <summary>Append a channel, applying the channel prefix unless the channel opts out.</summary>
        /// <param name="value">The channel to append.</param>
        public void AppendFormatted(RedisChannel value)
        {
            DemandCommand();

            // unlike keys, the channel prefix is writer state and is conditional on the channel itself -
            // keyspace notification channels are server-generated names and opt out
            ReadOnlySpan<byte> prefix = value.IgnoreChannelPrefix ? default : (byte[]?)_context.ChannelPrefix;
            ReadOnlySpan<byte> body = (byte[]?)value;

            var length = prefix.Length + body.Length;
            var payload = WriteBulk(length, out var payloadOffset);
            prefix.CopyTo(payload);
            body.CopyTo(payload.Slice(prefix.Length));
            CommitBulk(payloadOffset, length);
            FoldSlot(payload);
            _args++;
            _argIndex++;
        }

        /// <summary>
        /// Write an already-framed fragment verbatim. Note the argument counters advance by
        /// <see cref="RespFragment.ArgCount"/>, not by one, so a multi-token fragment does not shift the
        /// key-mark bit positions of everything after it.
        /// </summary>
        public void AppendFormatted(RespFragment value)
        {
            DemandCommand();

            var bytes = value.Bytes;
            Ensure(bytes.Length);
            bytes.CopyTo(_buffer.AsSpan(_offset));
            _offset += bytes.Length;
            _args += value.ArgCount;
            _argIndex += value.ArgCount;
        }

        /// <summary>Append a value; not a key, and not marked as one.</summary>
        /// <param name="value">The value to append.</param>
        public void AppendFormatted(RedisValue value)
        {
            DemandCommand();

            var length = value.GetByteCount();
            var payload = WriteBulk(length, out var payloadOffset);
            var written = value.CopyTo(payload);
            Debug.Assert(written == length, "value length disagreed with itself");
            CommitBulk(payloadOffset, length);
            _args++;
            _argIndex++;
        }

        /// <summary>
        /// Back-fill the <c>*N</c> header into the reserved prologue, right-aligned, and take ownership of
        /// the buffer away from the handler.
        /// </summary>
        public RespFrame Complete()
        {
            if (!_hasCommand) throw new InvalidOperationException("No command was written.");

            Span<byte> header = stackalloc byte[HeaderMax];
            header[0] = (byte)'*';
            var headerLength = MessageWriter.WriteRaw(header, _args, offset: 1);
            var start = HeaderMax - headerLength;
            header.Slice(0, headerLength).CopyTo(_buffer.AsSpan(start));

            var frame = new RespFrame(_buffer, start, _offset - start, _args, _slot, _keyMarks);
            _buffer = null!; // ownership transferred to the frame
            return frame;
        }

        /// <summary>Return the buffer to the pool, if it has not already been handed to a frame.</summary>
        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = null!;
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
        }

        private void DemandCommand()
        {
            if (!_hasCommand) throw new InvalidOperationException("The first argument must be a RedisCommand.");
        }

        /// <summary>Write '$len\r\n' and return the span the payload should be written into.</summary>
        private Span<byte> WriteBulk(int payloadLength, out int payloadOffset)
        {
            Ensure(payloadLength + HeaderMax + 2);
            var span = _buffer.AsSpan(_offset);
            span[0] = (byte)'$';
            payloadOffset = MessageWriter.WriteRaw(span, payloadLength, offset: 1);
            return span.Slice(payloadOffset, payloadLength);
        }

        /// <summary>Terminate the fragment begun by <see cref="WriteBulk"/> and advance the cursor.</summary>
        private void CommitBulk(int payloadOffset, int payloadLength)
        {
            MessageWriter.WriteCrlf(_buffer.AsSpan(_offset), payloadOffset + payloadLength);
            _offset += payloadOffset + payloadLength + 2;
        }

        /// <summary>
        /// Routing needs only O(1) state, and the bytes have just been written - so fold over those rather
        /// than re-materialising the key, which is what <see cref="ServerSelectionStrategy.GetHashSlot"/> has
        /// to do.
        /// </summary>
        private void FoldSlot(scoped ReadOnlySpan<byte> payload)
        {
            if (_context.ServerType != ServerType.Cluster) return;
            _slot = ServerSelectionStrategy.CombineSlot(_slot, ServerSelectionStrategy.GetClusterSlot(payload));
        }

        /// <summary>
        /// Record that a key starts at this BUFFER-ABSOLUTE offset. Absolute matters: <see cref="Complete"/>
        /// right-aligns the header, so the FRAME start moves with the digit count of the argument count.
        /// </summary>
        private void MarkKey(int offset)
        {
            if ((_keyMarks & RespFrame.OverflowFlag) != 0)
            {
                if (_argIndex < 63) _keyMarks |= 1UL << _argIndex;
                return;
            }

            if ((_keyMarks & RespFrame.SlotMask) == 0)
            {
                _keyMarks |= (ulong)offset & RespFrame.SlotMask;
            }
            else if (((_keyMarks >> RespFrame.SlotBits) & RespFrame.SlotMask) == 0)
            {
                _keyMarks |= ((ulong)offset & RespFrame.SlotMask) << RespFrame.SlotBits;
            }
            else
            {
                // a third key: the inline offsets cannot express it, so fall back to a walk
                _keyMarks = RespFrame.OverflowFlag;
            }
        }

        private void Ensure(int extra)
        {
            if (_buffer.Length - _offset >= extra) return;
            var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _offset + extra));
            Buffer.BlockCopy(_buffer, 0, bigger, 0, _offset);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }
    }
}
