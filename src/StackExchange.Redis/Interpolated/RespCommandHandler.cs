using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    internal ref struct RespCommandHandler
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
        public RespCommandHandler(int literalLength, int formattedCount, RespContext context, RedisCommand command)
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

        [Obsolete("Every part must be a hole, so that the argument count is known at compile time; write $\"{RedisCommand.SET}{key}{value}\", not $\"SET{key}{value}\".", error: true)]
        public void AppendLiteral(string value) => throw new NotSupportedException();

        public void AppendFormatted(RedisCommand value)
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
