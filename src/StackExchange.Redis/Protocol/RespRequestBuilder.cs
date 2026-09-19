using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Protocol
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
    public ref struct RespRequestBuilder
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
        private RedisCommand _command; // the command's IDENTITY, for routing and diagnostics; the
                                       // bytes are already written, so this is never used to render

        /// <summary>Compose against a database context.</summary>
        /// <param name="literalLength">Total length of the literal segments; compiler-supplied.</param>
        /// <param name="formattedCount">Number of holes; compiler-supplied.</param>
        /// <param name="context">The receiver of the call; its <c>Raw</c> supplies the map and prefixes.</param>
        /// <remarks>
        /// The interpolated-string handler is constructed from the RECEIVER of the call, so a send written
        /// against a typed context - which is every send now that nothing extends a naked one - needs a
        /// constructor that accepts one. Two one-line overloads here are what let the send path stay a
        /// single implementation over <see cref="RespContext"/> instead of being written out per context.
        /// </remarks>
        public RespRequestBuilder(int literalLength, int formattedCount, RespDatabaseContext context)
            : this(literalLength, formattedCount, context.Raw)
        {
        }

        /// <summary>Compose against a server context.</summary>
        /// <param name="literalLength">The literal segments' total length; compiler-supplied.</param>
        /// <param name="formattedCount">How many holes there are; compiler-supplied.</param>
        /// <param name="context">The call's receiver; its <c>Raw</c> supplies the map and prefixes.</param>
        public RespRequestBuilder(int literalLength, int formattedCount, RespServerContext context)
            : this(literalLength, formattedCount, context.Raw)
        {
        }

        /// <summary>Initialize with the command supplied as the first hole.</summary>
        /// <param name="literalLength">Total length of the literal segments; compiler-supplied.</param>
        /// <param name="formattedCount">Number of holes; compiler-supplied.</param>
        /// <param name="context">The receiver of the call, supplying the command map and prefixes.</param>
        public RespRequestBuilder(int literalLength, int formattedCount, RespContext context)
        {
            _context = context;
            _buffer = ArrayPool<byte>.Shared.Rent(HeaderMax + 64 + literalLength + (formattedCount * 24));
            _offset = HeaderMax;
            _args = 0;
            _argIndex = 0;
            _slot = ServerSelectionStrategy.NoSlot;
            _hasCommand = false;
            _command = RedisCommand.UNKNOWN; // set by the first AppendFormatted, which is the command hole
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
        internal RespRequestBuilder(int literalLength, int formattedCount, RespContext context, RedisCommand command)
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
            _command = command;
            _args = 1;
            _argIndex = 1;
        }

        /// <summary>
        /// Initialize from a command <b>name</b>, for callers without access to the internal
        /// <c>RedisCommand</c> enum. The name is speculatively parsed to a known command so command-map
        /// aliasing and disabling still apply; anything unrecognised is framed verbatim, matching how
        /// <c>IDatabase.Execute(string, ...)</c> already behaves.
        /// </summary>
        public RespRequestBuilder(int literalLength, int formattedCount, RespContext context, string command)
        {
            if (command is null) Throw();

            // a RESP command token never contains a space, so "ACL SETUSER x" is always a caller mistake:
            // it frames as ONE unknown token and the server answers an opaque error. The shipped
            // ExecuteMessage has refused it for exactly that reason; this is the same guard on the route
            // that replaces it, so ExecuteResp stops being the one ad-hoc path that lets it through.
            if (command.IndexOf(' ') >= 0) ThrowWhitespace();

            var known = context.TryResolveCommand(command.AsSpan(), out var resp, out var parsed);

            var nameBytes = known ? 0 : Encoding.UTF8.GetByteCount(command);
            _context = context;

            // the command's identity, not just its bytes. This used to be left at the field default -
            // RedisCommand.NONE, which is 0 - so an ad-hoc command was neither a known command nor honestly
            // UNKNOWN. That matters beyond tidiness: the pipeline reads Command to decide IsPrimaryOnly, so
            // an ad-hoc write could be routed to a replica, and NONE is not on RequiresDatabase's allow-list,
            // so the same frame is rejected outright by a context with no database (a server's).
            _command = parsed;
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

            // no @this: this one fires before anything is rented, so there is nothing to hand back.
            // DoesNotReturn is load-bearing rather than decorative - `command` is dereferenced two lines
            // below the guard, and without it the nullable analysis reports CS8602 there
            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static void Throw() => throw new ArgumentNullException(nameof(command));

            // as Throw: before anything is rented, so nothing to hand back
            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            void ThrowWhitespace() => throw ExceptionFactory.CommandHasWhitespace(command);
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
                    CountArguments();
                    return;
                }

                _hasCommand = true; // unknown command name, framed below like any other token
            }

            WriteUtf8Bulk(value, start, length);
            CountArguments();
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
            if (_hasCommand) Throw(ref this);

            _command = value; // identity, for routing and diagnostics; see the field
            var resp = _context.ResolveCommand(value);

            Ensure(resp.Length);
            resp.CopyTo(_buffer.AsSpan(_offset));
            _offset += resp.Length;
            _hasCommand = true;
            CountArguments();

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static void Throw(scoped ref RespRequestBuilder @this)
            {
                @this.Dispose();
                throw new InvalidOperationException("The command must be the first argument, and may only be given once.");
            }
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
            if (value.IsEmpty) Throw(ref this);

            // resolution happens HERE, not at construction: a known command still has to go through this
            // context's map, which may rename or disable it
            var resp = value.GetResp(_context);
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

            if (!_hasCommand) _command = value.Command; // only the FIRST one is the command
            _hasCommand = true; // whether it was the command or merely the first thing written
            CountArguments();

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static void Throw(scoped ref RespRequestBuilder @this)
            {
                @this.Dispose();
                throw new ArgumentException("No command was supplied.", nameof(value));
            }
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
        /// It <b>moves</b> rather than shares, and the move is completed at both ends: the source is reset
        /// to <c>default</c> here, and the handler is reset by <c>Append</c> once it has been assigned back.
        /// So exactly one copy owns the pooled array at any instant, and the copy left behind cannot be
        /// used to reach an array that a growth inside the window has already returned to the pool.
        /// </para>
        /// <para>
        /// <c>default</c> rather than merely clearing the buffer, because every path off a moved-from
        /// handler is then a clean throw rather than a <see cref="NullReferenceException"/>: <c>_hasCommand</c>
        /// is false, so <see cref="Complete"/> and every <c>AppendFormatted</c> say what went wrong, and
        /// <see cref="Dispose"/> is a no-op instead of a double return to the pool. This is the same
        /// ownership-transfer idiom <see cref="Complete"/> uses.
        /// </para>
        /// <para>
        /// The parameter is <c>ref</c> and not <c>in</c> for exactly that reset; reading alone would be
        /// satisfied by <c>in</c>, and the compiler accepts either.
        /// </para>
        /// </remarks>
        public RespRequestBuilder(int literalLength, int formattedCount, scoped ref RespRequestBuilder command)
        {
            _ = literalLength;
            _ = formattedCount;
            this = command;
            command = default; // the move is only a move if the source stops owning the buffer
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
            CountArguments();
        }

        /// <summary>
        /// Append a <b>borrowed</b> key: prefixed, marked and slot-folded exactly as an owned one.
        /// </summary>
        /// <param name="value">The key to append; its bytes are copied before this returns.</param>
        /// <remarks>
        /// <para>
        /// The same four things <see cref="AppendFormatted(RedisKey)"/> does - context prefix, invalidation
        /// mark, cluster slot, argument count - because a key is a key regardless of where its bytes came
        /// from. Only the source differs, so only the length and copy differ.
        /// </para>
        /// <para>
        /// This is what lets a caller who already holds the bytes avoid materialising a
        /// <see cref="RedisKey"/> for them. It is safe here and nowhere else in the library: the copy
        /// happens before this method returns, so the borrowed span never has to outlive the call.
        /// </para>
        /// </remarks>
        public void AppendFormatted(RespKey value)
        {
            DemandCommand();

            var prefix = _context.KeyPrefixSpan;
            MarkKey(_offset);
            var keyLength = value.GetByteCount();
            var length = prefix.Length + keyLength;
            var payload = WriteBulk(length, out var payloadOffset);
            prefix.CopyTo(payload);
            var written = value.CopyTo(payload.Slice(prefix.Length));
            Debug.Assert(written == keyLength, "key length disagreed with itself");
            CommitBulk(payloadOffset, length);
            FoldSlot(payload);
            CountArguments();
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
            CountArguments();
        }

        /// <summary>
        /// Append any type that knows how to write itself, so the set of things that can appear in a hole
        /// is open to other assemblies rather than closed to this one.
        /// </summary>
        /// <typeparam name="T">The argument type; inferred from the hole.</typeparam>
        /// <param name="value">The argument to append.</param>
        /// <remarks>
        /// <para>
        /// The design notes (section 2.2) say <b>do not define <c>AppendFormatted&lt;T&gt;</c></b>, and that
        /// still holds for an <i>unconstrained</i> one: it is an exact match by inference, so it would beat
        /// every overload needing a conversion and quietly swallow anything undeclared into a
        /// <c>ToString()</c> path. The constraint is what makes this the exception rather than a reversal -
        /// a type that does not implement <see cref="IRespArgument"/> is not applicable at all, so it still
        /// fails to compile, and with a <i>better</i> diagnostic than before (CS0315 names the interface,
        /// where the closed overload set produced a CS1503 naming an arbitrary member).
        /// </para>
        /// <para>
        /// Measured, not assumed, on the three cases that decide whether this is safe: a dedicated
        /// non-generic overload still wins when both apply; an implicit conversion does <b>not</b> win
        /// (opting in beats an incidental conversion, which is the wanted answer); and a <c>struct</c>
        /// implementer is a constrained call, so nothing boxes.
        /// </para>
        /// <para>
        /// This is the funnel <see cref="Expiration"/> and <see cref="ValueCondition"/> arrive through:
        /// they had dedicated overloads first, and giving them up is the point - if the mechanism is good
        /// enough for other libraries' types, it should be good enough for ours.
        /// </para>
        /// </remarks>
        public void AppendFormatted<T>(T value) where T : IRespArgument
        {
            DemandCommand();
            if (value is null) ThrowValueNull();
            value.WriteTo(ref this);
        }

        /// <summary>
        /// Append a type that knows how to write itself in a requested format: <c>$"{radius:km}"</c>.
        /// </summary>
        /// <typeparam name="T">The argument type; inferred from the hole.</typeparam>
        /// <param name="value">The argument to append.</param>
        /// <param name="format">The text after the <c>:</c> in the hole.</param>
        /// <remarks>
        /// Constrained to <see cref="IRespFormattableArgument"/> and <b>not</b> to
        /// <see cref="IRespArgument"/>, which is what lets a type accept a format without accepting a bare
        /// hole, and vice versa; see the remarks on that interface. The two overloads differ in arity, so
        /// a type implementing both is unambiguous.
        /// <para>
        /// There is no <c>int alignment</c> counterpart, and there should never be: padding a
        /// length-prefixed binary payload changes what is sent.
        /// </para>
        /// </remarks>
        public void AppendFormatted<T>(T value, string? format) where T : IRespFormattableArgument
        {
            DemandCommand();
            if (value is null) ThrowValueNull();
            value.WriteTo(ref this, format);
        }

        /// <summary>Frame raw bytes as one bulk argument.</summary>
        /// <param name="payload">The payload; framed here, so it must NOT already carry a <c>$len</c> prefix.</param>
        /// <remarks>
        /// The primitive an <see cref="IRespArgument"/> implementation needs when its payload is bytes it
        /// computed rather than a <see cref="RedisValue"/> it was handed - writing those through a
        /// <c>RedisValue</c> would mean allocating a <c>byte[]</c> to carry them.
        /// <para>
        /// Deliberately a named method and <b>not</b> an <c>AppendFormatted</c> overload: a bare span in a
        /// hole is ambiguous between "I already framed this" and "you frame this", which is precisely the
        /// distinction <see cref="RespFragment"/> and <see cref="RedisValue"/> exist to keep apart (see
        /// design notes 2.2). Keeping it off the hole vocabulary means the question never arises.
        /// </para>
        /// </remarks>
        public void AppendBulk(scoped ReadOnlySpan<byte> payload)
        {
            DemandCommand();

            var target = WriteBulk(payload.Length, out var payloadOffset);
            payload.CopyTo(target);
            CommitBulk(payloadOffset, payload.Length);
            CountArguments();
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
            CountArguments(value.ArgCount);
        }

        /// <summary>
        /// Append a run of keys, each one prefixed, marked and folded into the slot exactly as a single
        /// key is: <c>$"{RedisCommand.MGET}{keys}"</c>.
        /// </summary>
        /// <param name="value">The keys to append; an empty run appends nothing.</param>
        /// <remarks>
        /// <para>
        /// A variadic command is the one shape the single-expression form could not otherwise reach: the
        /// argument count is a run-time quantity, so the alternative is <c>Compose</c> plus a loop plus a
        /// <c>try</c>/<c>finally</c> to hand the rented buffer back if an interpolation throws. This makes
        /// <c>MGET</c>, <c>DEL</c> and <c>BITOP</c> read like every other command.
        /// </para>
        /// <para>
        /// A span rather than an array, so a caller with a slice, a <c>stackalloc</c>, or an array it does
        /// not want copied pays nothing; an array converts implicitly, so <c>$"{keys}"</c> compiles either
        /// way. <c>scoped</c> for the usual reason: nothing here retains it.
        /// </para>
        /// </remarks>
        public void AppendFormatted(scoped ReadOnlySpan<RedisKey> value)
        {
            foreach (ref readonly var key in value)
            {
                AppendFormatted(key);
            }
        }

        /// <summary>
        /// Append a run of key/value pairs, in the order <c>MSET</c> wants them:
        /// <c>$"{RedisCommand.MSET}{values}"</c>.
        /// </summary>
        /// <param name="value">The pairs to append; an empty run appends nothing.</param>
        /// <remarks>
        /// Each pair contributes TWO arguments, and the key half goes through the key path - prefix, mark,
        /// slot - while the value half does not. That asymmetry is the entire reason this is a hole rather
        /// than something the caller loops over as values: writing a key as a value would lose the prefix,
        /// the invalidation mark and the cross-slot check, all silently.
        /// </remarks>
        public void AppendFormatted(scoped ReadOnlySpan<KeyValuePair<RedisKey, RedisValue>> value)
        {
            foreach (ref readonly var pair in value)
            {
                AppendFormatted(pair.Key);
                AppendFormatted(pair.Value);
            }
        }

        /// <summary>
        /// Append a run of values; none of them keys, and none marked as such:
        /// <c>$"{RedisCommand.HMGET}{key}{fields}"</c>.
        /// </summary>
        /// <param name="value">The values to append; an empty run appends nothing.</param>
        /// <remarks>
        /// The twin of the key overload, and deliberately a separate one rather than something a caller
        /// picks: the difference between a run of keys and a run of values is the prefix, the invalidation
        /// mark and the cross-slot check, and none of those can be inferred from the element type at the
        /// call site - which is exactly why the two spellings are distinct here.
        /// </remarks>
        public void AppendFormatted(scoped ReadOnlySpan<RedisValue> value)
        {
            foreach (ref readonly var item in value)
            {
                AppendFormatted(item);
            }
        }

        /// <summary>
        /// Append a run of anything that knows how to write itself:
        /// <c>$"{RedisCommand.HSETEX}{key}{entries}"</c>.
        /// </summary>
        /// <typeparam name="T">The element type; inferred from the hole.</typeparam>
        /// <param name="value">The elements to append; an empty run appends nothing.</param>
        /// <remarks>
        /// <para>
        /// The open one, and the reason the vocabulary does not have to grow a span overload per data
        /// type. A <see cref="HashEntry"/> writes two arguments, a stream entry will write more, and
        /// another library's type writes whatever it likes - all through the same hole, because the
        /// element decides rather than the handler.
        /// </para>
        /// <para>
        /// A constrained call on a value type, so a struct element does not box - the same measurement
        /// that made the single-value <c>AppendFormatted&lt;T&gt;</c> acceptable applies here per element,
        /// where it matters more.
        /// </para>
        /// </remarks>
        public void AppendFormatted<T>(scoped ReadOnlySpan<T> value) where T : IRespArgument
        {
            DemandCommand();
            foreach (ref readonly var item in value)
            {
                // the only guard here that sits inside a loop
                if (item is null) ThrowValueNull();
                item.WriteTo(ref this);
            }
        }

        /// <summary>Append raw bytes as one value: <c>$"{command}{key}{payload}"</c>.</summary>
        /// <param name="value">The payload, framed here - so it must NOT already carry a <c>$len</c> prefix.</param>
        /// <remarks>
        /// <b>One argument, not a run.</b> The other span overloads write an argument per element;
        /// <see cref="byte"/> is the case where the span <i>is</i> the argument, which is what commands
        /// taking an opaque blob want - <c>RESTORE</c>'s dump payload, a serialised value. Without it the
        /// caller has to reach for <see cref="AppendBulk"/> by hand and lose the interpolated spelling, or
        /// copy the bytes into a <see cref="RedisValue"/> for no reason.
        /// </remarks>
        public void AppendFormatted(scoped ReadOnlySpan<byte> value)
        {
            DemandCommand();
            AppendBulk(value);
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
            CountArguments();
        }

        /// <summary>Append a string as a value; <b>not a key</b>, and not marked as one.</summary>
        /// <param name="value">The value to append; <see langword="null"/> writes an empty argument.</param>
        /// <remarks>
        /// <para>
        /// <b>This overload exists to break a tie, and the tie is the interesting part.</b> A bare string
        /// hole - <c>$"{cmd}{key}{s}"</c> - converts implicitly to <see cref="RedisKey"/>,
        /// <see cref="RedisValue"/> <i>and</i> <see cref="RedisChannel"/>, none better than the others, so
        /// it did not compile at all: CS0121 naming two of the three, which tells a caller nothing about
        /// why. A <see cref="string"/> parameter is an exact match, and a standard conversion beats every
        /// user-defined one, so this wins outright - no
        /// <see cref="OverloadResolutionPriorityAttribute"/> needed.
        /// </para>
        /// <para>
        /// <b>Value, not key, is a deliberate choice and a silent one</b>, so it is worth being explicit:
        /// a string written into a hole is sent as a plain argument, which means no key prefix, no slot for
        /// routing, and nothing for the client-side cache to invalidate on. That is right because
        /// <see cref="RedisKey"/> is a distinct type and code that has a key has a <see cref="RedisKey"/> -
        /// the strings left over are arguments. If a key of yours is living in a <see cref="string"/>,
        /// cast it: <c>$"{cmd}{(RedisKey)s}"</c>. This is the same rule as
        /// <see cref="RedisKeyOrValue"/> on the ad-hoc path, arrived at from the other direction.
        /// </para>
        /// </remarks>
        public void AppendFormatted(string? value) => AppendFormatted(value.AsRedisValue());

        /// <summary>
        /// Append an optional number: the value when it has one, and <b>nothing at all</b> when it does
        /// not - no argument, and nothing added to the argument count.
        /// </summary>
        /// <param name="value">The value, or <see langword="null"/> to write nothing.</param>
        /// <remarks>
        /// <para>
        /// <b>Deliberately not the same as a null <see cref="RedisValue"/></b>, which writes an
        /// <i>empty</i> argument. The difference is that <c>RedisValue.Null</c> is a value - the protocol's
        /// nil - whereas a <c>null</c> of a value type is the absence of one, and the absence of an
        /// argument is written by not writing it.
        /// </para>
        /// <para>
        /// This is the other half of <c>RespFragment.When</c>: the token disappears when the value does,
        /// so <c>$"{cmd}{key}{RespLiterals.Count.When(count)}{count}"</c> writes both arguments or neither.
        /// </para>
        /// <para>
        /// <see cref="long"/> rather than one overload per width, because the lifted implicit conversion
        /// carries <c>int?</c> here - and beats the user-defined conversion to <see cref="RedisValue"/>,
        /// so a nullable hole binds to this without the caller asking.
        /// </para>
        /// </remarks>
        public void AppendFormatted(long? value)
        {
            if (value is long actual) AppendFormatted((RedisValue)actual);
        }

        /// <summary>Append a duration in the unit the command asks for: <c>$"{idle:ms}"</c>.</summary>
        /// <param name="value">The duration.</param>
        /// <param name="format">The unit: <c>ms</c> for milliseconds, <c>s</c> for seconds.</param>
        /// <remarks>
        /// <para>
        /// <b>There is deliberately no unit-less overload.</b> <see cref="TimeSpan"/> does not implement
        /// <see cref="IRespArgument"/> and cannot - it is a BCL type - so <c>$"{idle}"</c> does not
        /// compile, and a duration can only be written by saying which unit the server wants. Redis uses
        /// both, sometimes on the same command, and a wrong unit is a silent factor of a thousand.
        /// </para>
        /// <para>
        /// <b>Truncates</b>, matching every hand-rolled <c>(long)x.TotalMilliseconds</c> this replaces.
        /// That matters more than it looks: the shipped <c>XREADGROUP</c> writers passed
        /// <c>TotalMilliseconds</c> - a <see cref="double"/> - straight through, so
        /// <c>TimeSpan.FromMilliseconds(1500.5)</c> put <c>CLAIM 1500.5</c> on the wire and the server
        /// answered "value is not an integer". Going through a unit-bearing hole is what stops that
        /// happening again somewhere else.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="format"/> is not a known unit.</exception>
        public void AppendFormatted(TimeSpan value, string? format)
            => AppendFormatted((RedisValue)ToUnits(value, format));

        /// <summary>
        /// Append an optional duration: the value when it has one, and <b>nothing at all</b> when it does
        /// not.
        /// </summary>
        /// <param name="value">The duration, or <see langword="null"/> to write nothing.</param>
        /// <param name="format">The unit: <c>ms</c> for milliseconds, <c>s</c> for seconds.</param>
        /// <remarks>
        /// The <see cref="TimeSpan"/> counterpart of <see cref="AppendFormatted(long?)"/>, and pairs with
        /// <c>RespFragment.When</c> the same way: <c>$"{RespLiterals.Claim.When(idle)}{idle:ms}"</c>
        /// writes both arguments or neither.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="format"/> is not a known unit.</exception>
        public void AppendFormatted(TimeSpan? value, string? format)
        {
            if (value is TimeSpan actual) AppendFormatted(actual, format);
        }

        /// <summary>Convert a duration to whole units of <paramref name="format"/>.</summary>
        /// <remarks>
        /// An unknown unit throws rather than guessing. A duration written in the wrong unit is still a
        /// valid command, so the server cannot catch it and neither can a test that only checks the shape
        /// - which makes this one of the few places where being loud at the call site is the whole value.
        /// </remarks>
        private static long ToUnits(TimeSpan value, string? format) => format switch
        {
            "ms" => (long)value.TotalMilliseconds,
            "s" => (long)value.TotalSeconds,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Unknown duration unit; use 'ms' for milliseconds or 's' for seconds."),
        };

        /// <inheritdoc cref="AppendFormatted(long?)"/>
        public void AppendFormatted(double? value)
        {
            if (value is double actual) AppendFormatted((RedisValue)actual);
        }

        /// <summary>
        /// Back-fill the <c>*N</c> header into the reserved prologue, right-aligned, and take ownership of
        /// the buffer away from the handler.
        /// </summary>
        public RespRequestFrame Complete()
        {
            if (!_hasCommand) Throw(ref this);

            // the writer's own limit, applied in the writer's terms: it counts arguments WITHOUT the
            // command and adds one for the header, whereas _args already includes it. Checked here rather
            // than only at dispatch because this is where the count is final and where the '*N' is about to
            // be written - a header past the limit is a frame that is invalid by construction, and a frame
            // may never be dispatched at all (a cache lookup key, an ad-hoc composition).
            // the same four lines ThrowTooManyArguments already had, so it is the same call: this used to
            // be a second copy, and a second copy of a throw is a second thing to keep in step
            if (_args - 1 >= MessageWriter.REDIS_MAX_ARGS) ThrowTooManyArguments();

            Span<byte> header = stackalloc byte[HeaderMax];
            header[0] = (byte)'*';
            var headerLength = MessageWriter.WriteRaw(header, _args, offset: 1);
            var start = HeaderMax - headerLength;
            header.Slice(0, headerLength).CopyTo(_buffer.AsSpan(start));

            var frame = new RespRequestFrame(_buffer, start, _offset - start, _args, _slot, PackKeyMarks(), _command);
            _buffer = null!; // ownership transferred to the frame
            return frame;

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static void Throw(scoped ref RespRequestBuilder @this)
            {
                @this.Dispose();
                throw new InvalidOperationException("No command was written.");
            }
        }

        /// <summary>Whether the rented buffer has been handed back; for tests that assert no leak.</summary>
        /// <remarks>
        /// A leak from <see cref="ArrayPool{T}"/> is invisible from outside - an empty bucket simply
        /// allocates - so "rent a lot and see if it breaks" proves nothing. This is the observable.
        /// </remarks>
        internal readonly bool BufferReturned => _buffer is null;

        /// <summary>Return the buffer to the pool, if it has not already been handed to a frame.</summary>
        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = null!;
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>Refuse to write an argument before the command.</summary>
        /// <remarks>
        /// <b>Every <c>AppendFormatted</c> starts here</b>, so the guard has to be free when it passes. The
        /// test is one bool; the throw is a string literal, a <c>newobj</c> and a <c>throw</c>, which is the
        /// bulk of the IL and all of it cold. Out of line in a local function, what inlines into ten call
        /// sites is the branch alone. Same reasoning as <see cref="ThrowTooManyArguments"/>, which had it
        /// first.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DemandCommand()
        {
            if (!_hasCommand) Throw(ref this);

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static void Throw(scoped ref RespRequestBuilder @this)
            {
                @this.Dispose();
                throw new InvalidOperationException("The first argument must be a RedisCommand.");
            }
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
            if (!_context.NeedsSlots) return; // late-bound, and speculative while unknown: see RespTopology
            _slot = ServerSelectionStrategy.CombineSlot(_slot, ServerSelectionStrategy.GetClusterSlot(payload));
        }

        /// <summary>
        /// Record that arguments have been written, and refuse to go past the protocol's limit.
        /// </summary>
        /// <param name="count">How many arguments were written; more than one for a multi-token fragment.</param>
        /// <remarks>
        /// <para>
        /// <b>The single place the counters move.</b> They were bumped in pairs at nine sites, which is two
        /// invariants maintained by hand: that the count and the index stay in step - the index drives key
        /// marking, so a drift there mismarks keys - and that neither runs past the limit. Both are now
        /// structural, and a tenth site cannot forget either.
        /// </para>
        /// <para>
        /// Checking here rather than only at <see cref="Complete"/> costs a compare against a value already
        /// in a register, on a path that is about to write a bulk string - free in practice - and it fails
        /// <i>at the offending argument</i> instead of after writing megabytes that were never going to be
        /// sent.
        /// </para>
        /// <para>
        /// Counted the writer's way: <c>_args</c> includes the command, and the limit is on the arguments
        /// without it, so the frame becomes illegal one past the limit rather than at it.
        /// </para>
        /// </remarks>
        private void CountArguments(int count = 1)
        {
            _args += count;
            _argIndex += count;
            if (_args > MessageWriter.REDIS_MAX_ARGS) ThrowTooManyArguments();
        }

        // EVERY throw out of this type hands the buffer back first. Nothing else will: the handler lives
        // in the CALLER's frame and no `finally` is generated around an interpolated string, so a throw
        // from mid-append is the one path where the rented array is otherwise simply dropped. This is not
        // theoretical - the (literalLength, formattedCount, context) constructor rents and THEN sets
        // _hasCommand = false, so $"{key}{value}" with no command reaches DemandCommand's guard with a
        // live array every single time it fires.
        //
        // The rest are the house `static void Throw()` local function, which reaches Dispose through an
        // explicit `scoped ref RespRequestBuilder @this` - the same `ref this` the IRespArgument calls
        // already pass - and gets `nameof` on the enclosing method's parameters into the bargain. These
        // two are methods only because they have more than one caller each, which is also why their
        // ArgumentNullException names "value" as a literal: all three of ThrowValueNull's callers spell
        // the parameter that way, and passing nameof(value) in would leave an ldstr at each call site,
        // making the extraction exactly IL-neutral (measured).
        [MethodImpl(MethodImplOptions.NoInlining)]
        [DoesNotReturn]
        private void ThrowValueNull()
        {
            Dispose();
            throw new ArgumentNullException("value");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [DoesNotReturn]
        private void ThrowTooManyArguments()
        {
            var command = _command;
            var count = _args - 1;
            Dispose();
            throw ExceptionFactory.TooManyArgs(command.ToString(), count);
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
        /// <c>RespRequestFrame.HasNoKeys</c>. That is only sound because a key can never START at offset 0: the
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

            if (_argIndex <= RespRequestFrame.MaxBitmapArg) _keyBitmap |= 1UL << _argIndex;
            else _keyBitmap |= RespRequestFrame.TruncatedFlag; // no bit for it; say so rather than report a subset
        }

        /// <summary>Pack the key marks into the frame's single 64-bit field.</summary>
        /// <remarks>
        /// Two keys or fewer keep the byte offsets, which resolve with no scan and do not care how far out
        /// the arguments were. Beyond that the bitmap is the only form that fits, and resolving it costs a
        /// walk - see <see cref="RespRequestFrame.TryGetKeys"/>.
        /// </remarks>
        private readonly ulong PackKeyMarks()
        {
            if (_keyCount == 0) return 0;
            if (_keyCount <= 2)
            {
                return ((ulong)_keyOffsetA & RespRequestFrame.SlotMask)
                     | (((ulong)_keyOffsetB & RespRequestFrame.SlotMask) << RespRequestFrame.SlotBits);
            }

            return RespRequestFrame.OverflowFlag | _keyBitmap;
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

        /// <summary>Make room for <paramref name="extra"/> more bytes, growing the buffer if needed.</summary>
        /// <remarks>
        /// <b>Every write starts here, and almost none of them grow.</b> The common case is one subtract,
        /// one compare and a return; the growth is a rent, a copy and a return to the pool, which is the
        /// bulk of the IL and is cold. Out of line it stays, so what inlines into each writer is the
        /// check. Same reasoning as <see cref="DemandCommand"/>, and the same <c>scoped ref</c> trick to
        /// reach the instance from a <c>static</c> local.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Ensure(int extra)
        {
            if (_buffer.Length - _offset < extra) Grow(ref this, extra);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void Grow(scoped ref RespRequestBuilder @this, int extra)
            {
                var bigger = ArrayPool<byte>.Shared.Rent(Math.Max(@this._buffer.Length * 2, @this._offset + extra));
                Buffer.BlockCopy(@this._buffer, 0, bigger, 0, @this._offset);
                ArrayPool<byte>.Shared.Return(@this._buffer);
                @this._buffer = bigger;
            }
        }
    }
}
