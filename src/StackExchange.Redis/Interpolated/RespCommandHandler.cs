using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        /// <summary>'*' plus an int32 text form plus CRLF; reserved at the front so the header can be
        /// back-filled right-aligned once the final argument count is known.</summary>
        /// <remarks>
        /// Sized from the TYPE, not from what callers plausibly pass: <c>_args</c> is an <c>int</c>, so the
        /// text form can be 10 digits, and the previous "up to nine digits" reserve of 12 was one short of
        /// the 13 that needs. Unreachable via an interpolated string - but see <see cref="MaxBulkPrefix"/>
        /// for the same assumption in a place that was very much reachable.
        /// </remarks>
        private const int HeaderMax = 3 + Format.MaxInt32TextLen;

        /// <summary>'$' plus an int32 text form plus CRLF: the prefix of one bulk string.</summary>
        /// <remarks>
        /// A DIFFERENT quantity from <see cref="HeaderMax"/>, which is why it is now a different constant.
        /// <see cref="WriteBulk"/> used to reserve <c>HeaderMax</c> for this, which happened to be the same
        /// number and was one byte short once the length reached 10 digits.
        /// </remarks>
        private const int MaxBulkPrefix = 3 + Format.MaxInt32TextLen;

        private readonly RespContext _context;
        private byte[] _buffer;
        private int _offset;        // absolute write cursor within _buffer
        private int _args;          // RESP argument count, including the command
        private int _argIndex;      // logical argument position, for the overflow bitmap
        private int _slot;
        private ulong _keyBitmap;   // argument indices that are keys; bit 0 => a key too far out to record
        private int _keyCount;
        private int _keyOffsetA;    // buffer-absolute offsets of the first two keys
        private int _keyOffsetB;
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
            var resp = context.ResolveCommand(command);

            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + resp.Length + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _slot = ServerSelectionStrategy.NoSlot;

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

            var known = context.TryResolveCommand(command.AsSpan(), out var resp);

            var nameBytes = known ? 0 : Encoding.UTF8.GetByteCount(command);
            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + resp.Length + nameBytes + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _slot = ServerSelectionStrategy.NoSlot;
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
        /// Literal text becomes arguments: whitespace-separated tokens, with the first one - if nothing has
        /// been written yet - taken as the command.
        /// </summary>
        /// <remarks>
        /// <para>
        /// So <c>$"SET {key} {value}"</c> and <c>$"{RedisCommand.SET}{key}{value}"</c> produce the same
        /// frame, and <c>$"CONFIG GET {name}"</c> produces three arguments. Splitting on whitespace is what
        /// makes container commands come out right for free: <c>CONFIG</c> is the command and goes through
        /// the command map, while <c>GET</c> is an ordinary argument and does not - which is exactly how
        /// <see cref="CommandMap"/> works, since it maps container verbs only.
        /// </para>
        /// <para>
        /// A literal that is only whitespace contributes nothing, so the spaces in
        /// <c>$"{cmd} {key} {value}"</c> are still just separators.
        /// </para>
        /// <para>
        /// <b>This is the slow way to say it</b>, and the analyzer says so - a warning, not an error,
        /// because the result is correct, merely suboptimal. Each token is parsed and encoded on every call,
        /// where a <see cref="RespCommand"/> or a <c>RespFragment</c> resolves once. The fixer promotes
        /// literals to those. Working-but-slower is the right default here: the alternative was rejecting
        /// code that does exactly what it looks like.
        /// </para>
        /// <para>
        /// Nothing here allocates: the split is span slicing over the literal, and the encode goes straight
        /// into the frame buffer.
        /// </para>
        /// </remarks>
        /// <param name="value">The literal text.</param>
        public void AppendLiteral(string value)
        {
            // The two cases on the recommended path, and by far the most common: nothing at all, and the
            // single space of $"{cmd} {key} {value}". Both contribute no arguments, so neither is worth
            // entering the tokenizer for - and keeping them here means the preferred spelling pays nothing
            // for the existence of the readable one.
            if (value is null || value.Length == 0) return;
            if (value.Length == 1 && value[0] == ' ') return;

            AppendLiteralSlow(value);
        }

        /// <summary>Tokenize literal text into a command and/or arguments.</summary>
        /// <remarks>
        /// Separate and not inlined: this is the uncommon path, and inlining a loop plus an encoder into
        /// <see cref="AppendLiteral"/> would change codegen for the fast one - the same split, for the same
        /// reason, as the fallbacks in <c>MessageWriter</c>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendLiteralSlow(string value)
        {
            var pos = 0;
            while (pos < value.Length)
            {
                while (pos < value.Length && IsSeparator(value[pos])) pos++;
                if (pos >= value.Length) return;

                var tokenStart = pos;
                while (pos < value.Length && !IsSeparator(value[pos])) pos++;
                AppendToken(value, tokenStart, pos - tokenStart);
            }

            static bool IsSeparator(char c) => c is ' ' or '\t' or '\r' or '\n';
        }

        /// <summary>One whitespace-separated run of literal text: the command if first, else an argument.</summary>
        private void AppendToken(string value, int start, int length)
        {
            if (!_hasCommand)
            {
                // first thing written: this is the command. A name we know goes through the map - which may
                // rename or disable it; anything else is framed verbatim, as Execute(string, ...) already does
                if (_context.TryResolveCommand(value.AsSpan(start, length), out var resp))
                {
                    Ensure(resp.Length);
                    resp.CopyTo(_buffer.AsSpan(_offset));
                    _offset += resp.Length;
                    _hasCommand = true;
                    _args++;
                    _argIndex++;
                    return;
                }

                _hasCommand = true; // unknown command name, framed below like any other token
            }

            WriteUtf8Bulk(value, start, length);
            _args++;
            _argIndex++;
        }

        /// <summary>Write part of a string as a bulk string, encoding straight into the frame buffer.</summary>
        /// <remarks>
        /// Pointer-based because <c>Encoding.GetByteCount(ReadOnlySpan&lt;char&gt;)</c> does not exist on
        /// netstandard2.0 or net461; the <c>char*</c> overloads do, and this way there is no intermediate
        /// array on any target.
        /// </remarks>
        private unsafe void WriteUtf8Bulk(string value, int start, int length)
        {
            int byteCount;
            fixed (char* chars = value)
            {
                byteCount = Encoding.UTF8.GetByteCount(chars + start, length);
                var payload = WriteBulk(byteCount, out var payloadOffset);
                fixed (byte* bytes = &MemoryMarshal.GetReference(payload))
                {
                    Encoding.UTF8.GetBytes(chars + start, length, bytes, byteCount);
                }

                CommitBulk(payloadOffset, byteCount);
            }
        }

        internal void AppendFormatted(RedisCommand value)
        {
            if (_hasCommand) throw new InvalidOperationException("The command must be the first argument, and may only be given once.");

            var resp = _context.ResolveCommand(value);

            Ensure(resp.Length);
            resp.CopyTo(_buffer.AsSpan(_offset));
            _offset += resp.Length;
            _hasCommand = true;
            _args++;
            _argIndex++;
        }

        /// <summary>Append a resolved command name.</summary>
        /// <param name="value">The command, from <see cref="RespCommands"/>.</param>
        /// <remarks>
        /// <para>
        /// <b>Position decides the meaning, and the bytes are the same either way.</b> First, it is the
        /// command; later, it is an argument that happens to name a command - which is what
        /// <c>COMMAND INFO &lt;name&gt;</c>, <c>COMMAND DOCS</c> and <c>ACL</c> rules need.
        /// </para>
        /// <para>
        /// Either way it resolves through this context's <see cref="CommandMap"/>, and that is not merely
        /// tidy: the server knows a renamed command <i>only</i> by its new name. <c>COMMAND INFO HGET</c>
        /// returns nothing on a server where <c>HGET</c> was renamed - you have to ask for the mapped name,
        /// and the reply then reports the canonical one. Passing the mapped name is therefore the only
        /// thing that works, and taking it from the map is the only way to get it.
        /// </para>
        /// </remarks>
        public void AppendFormatted(RespCommand value)
        {
            if (value.IsEmpty) throw new ArgumentException("No command was supplied.", nameof(value));

            // resolution happens HERE, not at construction: a known command still has to go through this
            // context's map, which may rename or disable it
            var resp = value.GetResp(in _context);
            if (resp.IsEmpty)
            {
                // an unknown command kept as a name: encode straight into the frame, no intermediate array
                var name = value.Name!;
                var nameBytes = Encoding.UTF8.GetByteCount(name);
                WriteBulk(nameBytes, out var payloadOffset);
                Encoding.UTF8.GetBytes(name, 0, name.Length, _buffer, _offset + payloadOffset);
                CommitBulk(payloadOffset, nameBytes);
            }
            else
            {
                Ensure(resp.Length);
                resp.CopyTo(_buffer.AsSpan(_offset));
                _offset += resp.Length;
            }

            _hasCommand = true; // whether it was the command or merely the first thing written
            _args++;
            _argIndex++;
        }

        /// <summary>
        /// Continue an existing command, for <c>cmd.Append($"...")</c>.
        /// </summary>
        /// <param name="literalLength">Total length of the literal segments; compiler-supplied.</param>
        /// <param name="formattedCount">Number of holes; compiler-supplied.</param>
        /// <param name="command">The command being built; moved in, and moved back out by <c>Append</c>.</param>
        /// <remarks>
        /// <para>
        /// The handler for an append <b>is</b> the command handler, so there is exactly one set of
        /// <c>AppendFormatted</c> overloads and no way for a proxy to fall out of step with it. A separate
        /// proxy type was the obvious design and it could not even hold a reference to its target:
        /// <c>CS9050, a ref field cannot refer to a ref struct</c>, on every target.
        /// </para>
        /// <para>
        /// It <b>moves</b> rather than shares. The command is copied in here, appended to, and assigned
        /// back by <c>Append</c>. Both copies reference the same pooled array in between, but only this one
        /// is touched, and the original is overwritten as the window closes - including when a growth
        /// inside the window swapped the array, which is what separates a move from a share.
        /// </para>
        /// </remarks>
        public RespCommandHandler(int literalLength, int formattedCount, scoped in RespCommandHandler command)
        {
            _ = literalLength;
            _ = formattedCount;
            this = command;
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

            var frame = new RespFrame(_buffer, start, _offset - start, _args, _slot, PackKeyMarks());
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
        /// <remarks>
        /// The reservation is <see cref="BulkReservation"/>, sized for the widest int32 text form rather
        /// than for the lengths callers are expected to use. See that method for why.
        /// </remarks>
        private Span<byte> WriteBulk(int payloadLength, out int payloadOffset)
        {
            Ensure(BulkReservation(payloadLength));
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
        /// <remarks>
        /// <para>
        /// BOTH representations are maintained as we write, and <see cref="Complete"/> publishes whichever
        /// fits. That costs a few bytes in this handler - a <c>ref struct</c> on the stack, where there is no
        /// size pressure - and it is what removes the old promotion problem: the previous code overwrote the
        /// two stored offsets with a bare overflow flag on the third key, so the first three keys were
        /// recorded in neither form and the frame could report nothing at all.
        /// </para>
        /// <para>
        /// Zero is the "no key here" sentinel for the offset form, in both slots and in
        /// <c>RespFrame.HasNoKeys</c>. That is only sound because a key can never START at offset 0: the
        /// first <see cref="HeaderMax"/> bytes are the reserved prologue, and <see cref="DemandCommand"/>
        /// puts the command ahead of any key. Both halves are load-bearing - do not let
        /// <see cref="HeaderMax"/> become 0, and do not allow a key before the command, without giving the
        /// marks a real "unset" representation.
        /// </para>
        /// </remarks>
        private void MarkKey(int offset)
        {
            _keyCount++;
            if (_keyCount == 1) _keyOffsetA = offset;
            else if (_keyCount == 2) _keyOffsetB = offset;

            if (_argIndex <= RespFrame.MaxBitmapArg) _keyBitmap |= 1UL << _argIndex;
            else _keyBitmap |= RespFrame.TruncatedFlag; // no bit for it; say so rather than report a subset
        }

        /// <summary>Pack the key marks into the frame's single 64-bit field.</summary>
        /// <remarks>
        /// Two keys or fewer keep the byte offsets, which resolve with no scan and do not care how far out
        /// the arguments were. Beyond that the bitmap is the only form that fits, and resolving it costs a
        /// walk - see <see cref="RespFrame.TryGetKeys"/>.
        /// </remarks>
        private readonly ulong PackKeyMarks()
        {
            if (_keyCount == 0) return 0;
            if (_keyCount <= 2)
            {
                return ((ulong)_keyOffsetA & RespFrame.SlotMask)
                     | (((ulong)_keyOffsetB & RespFrame.SlotMask) << RespFrame.SlotBits);
            }

            return RespFrame.OverflowFlag | _keyBitmap;
        }

        /// <summary>
        /// Bytes that must be free for a complete <c>$len\r\n{payload}\r\n</c> bulk string.
        /// </summary>
        /// <remarks>
        /// Sized from <see cref="Format.MaxInt32TextLen"/>, not from the digit count of any particular
        /// length. The earlier reservation assumed at most nine digits, so from 1,000,000,000 bytes it was
        /// one byte short. That is invisible most of the time - <c>ArrayPool&lt;byte&gt;.Shared</c> rounds up
        /// to a power of two, so the extra byte lands in slack - but above 2^30 the pool hands back an array
        /// of EXACTLY the requested length, and the write goes out of bounds. Verified both ways: masked at
        /// 1,000,000,000 and an <see cref="IndexOutOfRangeException"/> at 1,100,000,000.
        /// </remarks>
        internal static int BulkReservation(int payloadLength) => payloadLength + MaxBulkPrefix + 2;

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
