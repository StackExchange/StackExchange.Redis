using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The bitmap-command group: <c>target.Bitmaps.CountAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Its own group, not a corner of <see cref="RespStrings"/>.</b> A bitmap is physically a string on
    /// the server, and the old surface says so by name - <c>StringBitCount</c>, <c>StringSetBit</c>. But
    /// the whole argument for grouping (design notes 9.4, reason 1) is that the shape of the API should
    /// teach the API, and Redis documents bitmaps as a type of their own with its own page. A caller
    /// reaching for <c>BITPOS</c> is thinking about bitmaps, not about the fact that the bytes live in a
    /// string key; <c>ctx.Bitmaps.</c> is the list they wanted, and <c>ctx.Strings.</c> stays the list
    /// someone storing a value wanted.
    /// </para>
    /// <para>
    /// The adapters in <c>TransitionalDatabase.Bitmaps.cs</c> keep the old <c>StringBitXxx</c> names
    /// working, so the regrouping costs existing callers nothing.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespBitmaps
    {
        private readonly RespContext _context;

        /// <summary>Group the bitmap commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespBitmaps(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The bitmap commands.</summary>
            public RespBitmaps Bitmaps => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The bitmap commands.</summary>
            public RespBitmaps Bitmaps => new(context);
        }

        /// <summary>GETBIT.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to read.</param>
        /// <param name="offset">The bit offset.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> GetAsync(this in RespBitmaps bitmaps, RedisKey key, long offset, CommandFlags flags = CommandFlags.None)
            => bitmaps.Context.SendAsync<bool>(
                $"{RedisCommand.GETBIT}{key}{offset}", flags.WithDefaultCategory(RedisCommand.GETBIT));

        /// <summary>SETBIT; the reply is the bit that was there before.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to write.</param>
        /// <param name="offset">The bit offset; the value is zero-extended up to it.</param>
        /// <param name="bit">The bit to set.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> SetAsync(this in RespBitmaps bitmaps, RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None)
            => bitmaps.Context.SendAsync<bool>(
                $"{RedisCommand.SETBIT}{key}{offset}{bit}", flags.WithDefaultCategory(RedisCommand.SETBIT));

        /// <summary>BITCOUNT: how many bits are set, over a range.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to count.</param>
        /// <param name="start">The inclusive start of the range; negative counts back from the end.</param>
        /// <param name="end">The inclusive end of the range; negative counts back from the end.</param>
        /// <param name="indexType">Whether the range is in bytes or in bits.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The index type is a token on the wire and nothing at all when it is the default, which is
        /// exactly what a zero-argument <see cref="RespFragment"/> spells - so the whole command is one
        /// expression with no branch at the call site. <c>BYTE</c> is not omitted to save bytes but
        /// because it is the server's own default, and older servers do not accept the token at all.
        /// </remarks>
        public static ValueTask<long> CountAsync(
            this in RespBitmaps bitmaps,
            RedisKey key,
            long start = 0,
            long end = -1,
            StringIndexType indexType = StringIndexType.Byte,
            CommandFlags flags = CommandFlags.None)
            => bitmaps.Context.SendAsync<long>(
                $"{RedisCommand.BITCOUNT}{key}{start}{end}{AsFragment(indexType)}",
                flags.WithDefaultCategory(RedisCommand.BITCOUNT));

        /// <summary>BITPOS: the offset of the first bit with the given value.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to search.</param>
        /// <param name="bit">The bit value to look for.</param>
        /// <param name="start">The inclusive start of the range; negative counts back from the end.</param>
        /// <param name="end">
        /// The inclusive end of the range; negative counts back from the end, and
        /// <see cref="StringIndex.Unbounded"/> leaves the range open, which is not the same thing when
        /// <paramref name="bit"/> is <see langword="false"/>.
        /// </param>
        /// <param name="indexType">Whether the range is in bytes or in bits.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// An open-ended range and a bit index cannot be combined: the server takes the BYTE/BIT token only
        /// <i>after</i> an explicit end, so there is nowhere to put it. Dropping it silently would
        /// reinterpret <paramref name="start"/> as a byte offset, which is why this says so instead.
        /// </remarks>
        public static ValueTask<long> PositionAsync(
            this in RespBitmaps bitmaps,
            RedisKey key,
            bool bit,
            long start = 0,
            long end = -1,
            StringIndexType indexType = StringIndexType.Byte,
            CommandFlags flags = CommandFlags.None)
        {
            if (end == StringIndex.Unbounded)
            {
                if (indexType != StringIndexType.Byte)
                {
                    throw new ArgumentException(
                        $"{nameof(StringIndex)}.{nameof(StringIndex.Unbounded)} requires {nameof(StringIndexType)}.{nameof(StringIndexType.Byte)};"
                        + " the server accepts a bit/byte index type only after an explicit end.",
                        nameof(indexType));
                }

                return bitmaps.Context.SendAsync<long>(
                    $"{RedisCommand.BITPOS}{key}{bit}{start}", flags.WithDefaultCategory(RedisCommand.BITPOS));
            }

            return bitmaps.Context.SendAsync<long>(
                $"{RedisCommand.BITPOS}{key}{bit}{start}{end}{AsFragment(indexType)}",
                flags.WithDefaultCategory(RedisCommand.BITPOS));
        }

        /// <summary>BITOP: combine bitmaps into a destination key; the reply is the destination's length.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="operation">The operation to apply.</param>
        /// <param name="destination">The key to write the result to.</param>
        /// <param name="keys">The source keys.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// <b>One method where the old surface has two.</b> The <c>(first, second)</c> overload exists only
        /// because building a variadic message used to be work; with <c>{keys}</c> as a hole it is the same
        /// expression either way, so the fixed-arity spelling has nothing left to offer. Note that the old
        /// one also has a trap this does not - a default <c>second</c> silently means "unary".
        /// </para>
        /// <para>
        /// <see cref="Bitwise.Not"/> takes exactly one source key, and every other operation takes at least
        /// one; both are checked here rather than left to the server, because the failure is a round trip
        /// away from the mistake.
        /// </para>
        /// </remarks>
        public static ValueTask<long> OperationAsync(
            this in RespBitmaps bitmaps,
            Bitwise operation,
            RedisKey destination,
            ReadOnlySpan<RedisKey> keys,
            CommandFlags flags = CommandFlags.None)
        {
            if (keys.IsEmpty) throw new ArgumentException("At least one source key is required.", nameof(keys));
            if (operation == Bitwise.Not && keys.Length != 1)
            {
                throw new ArgumentException("BITOP NOT takes exactly one source key.", nameof(keys));
            }

            return bitmaps.Context.SendAsync<long>(
                $"{RedisCommand.BITOP}{AsFragment(operation)}{destination}{keys}",
                flags.WithDefaultCategory(RedisCommand.BITOP));
        }

        /// <summary>BITFIELD: several sub-operations against one key, in one command.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to operate on.</param>
        /// <param name="operations">The sub-operations, in order; the reply has one element per operation.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// The one command in this group that cannot be a single interpolated expression, and not because
        /// of its arity: <c>OVERFLOW</c> is <b>sticky</b>, so whether an operation writes it depends on
        /// what the previous one left in force. That is state a hole cannot carry, so this composes and
        /// loops - which is exactly what <c>Compose</c> exists for.
        /// </para>
        /// <para>
        /// <b>An all-GET payload goes out as <c>BITFIELD_RO</c></b>, which is what makes a replica willing
        /// to serve it - <c>BITFIELD</c> is a write command to the server however read-only its
        /// sub-operations are, and a replica rejects it outright. The old surface decides this by asking
        /// the chosen endpoint's version; a context knows its command map but not the endpoint, so the
        /// test here is the map alone. Disabling <c>BITFIELD_RO</c> in the map is therefore the way to
        /// support a server older than 6.2, and is the same escape hatch that covers every other command
        /// a given server does not have.
        /// </para>
        /// <para>
        /// The lease must be disposed. A nil element means that operation was skipped by
        /// <c>OVERFLOW FAIL</c>, which is why the element type is nullable.
        /// </para>
        /// </remarks>
        public static ValueTask<ReadOnlyLease<long?>> FieldAsync(
            this in RespBitmaps bitmaps,
            RedisKey key,
            ReadOnlySpan<BitFieldOperation> operations,
            CommandFlags flags = CommandFlags.None)
            => FieldCore<ReadOnlyLease<long?>>(in bitmaps, key, operations, ReadOnlyLease<long?>.Empty, flags);

        /// <inheritdoc cref="FieldAsync(in RespBitmaps, RedisKey, ReadOnlySpan{BitFieldOperation}, CommandFlags)"/>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to operate on.</param>
        /// <param name="operations">The sub-operations, in order.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The writable-lease sibling, for <c>IDatabase.StringBitField</c>; see
        /// <see cref="GetWritableLease(in RespStrings, RedisKey, CommandFlags)"/>.
        /// </remarks>
        internal static ValueTask<Lease<long?>> FieldWritableLease(
            this in RespBitmaps bitmaps,
            RedisKey key,
            ReadOnlySpan<BitFieldOperation> operations,
            CommandFlags flags = CommandFlags.None)
            => FieldCore<Lease<long?>>(in bitmaps, key, operations, Lease<long?>.Empty, flags);

        /// <summary>
        /// The command both BITFIELD shapes send; they differ only in which lease the reply becomes.
        /// </summary>
        private static ValueTask<TResult> FieldCore<TResult>(
            in RespBitmaps bitmaps,
            RedisKey key,
            ReadOnlySpan<BitFieldOperation> operations,
            TResult empty,
            CommandFlags flags)
        {
            if (operations.IsEmpty) return new ValueTask<TResult>(empty);

            // counted up front, so a default operation throws at the caller rather than mid-write
            var argCount = BitFieldOperation.CountArgs(operations, nameof(operations));
            var command = SelectCommand(bitmaps.Context, key, operations, ref flags);

            var cmd = bitmaps.Context.Compose(command, argCount);
            try
            {
                cmd.AppendFormatted(key);
                var overflow = BitFieldOverflow.Wrap;
                foreach (ref readonly var operation in operations)
                {
                    operation.WriteTo(ref cmd, ref overflow);
                }
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return bitmaps.Context.SendAsync(ref frame, flags, RespHandlers.Inbuilt<TResult>.Require());
        }

        /// <summary>BITFIELD with a single sub-operation, whose reply is one value rather than a run.</summary>
        /// <param name="bitmaps">The bitmap command group.</param>
        /// <param name="key">The key to operate on.</param>
        /// <param name="operation">The sub-operation.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// The same bytes as the span form with one element, unwrapped from the array the server always
        /// replies with - so the common case costs neither a lease nor a disposal. <see langword="null"/>
        /// means the operation was skipped by <c>OVERFLOW FAIL</c>.
        /// </remarks>
        public static ValueTask<long?> FieldAsync(this in RespBitmaps bitmaps, RedisKey key, BitFieldOperation operation, CommandFlags flags = CommandFlags.None)
        {
            // deliberately NOT a one-element span: BitFieldOperation holds a RedisValue, so it cannot be
            // stackalloc'd, and the span-from-a-single-value constructor does not exist on every target
            var argCount = BitFieldOperation.CountArgs(in operation, nameof(operation));
            var command = SelectCommand(
                bitmaps.Context,
                key,
                allGet: operation.Kind == BitFieldOperation.OperationKind.Get,
                anyIncrement: operation.Kind == BitFieldOperation.OperationKind.IncrementBy,
                ref flags);

            var cmd = bitmaps.Context.Compose(command, argCount);
            try
            {
                cmd.AppendFormatted(key);
                var overflow = BitFieldOverflow.Wrap;
                operation.WriteTo(ref cmd, ref overflow);
            }
            catch
            {
                cmd.Dispose();
                throw;
            }

            var frame = cmd.Complete();
            return bitmaps.Context.SendAsync<long?>(ref frame, flags, RespHandlers.NullableInt64);
        }

        /// <summary>
        /// Which <c>BITFIELD</c> to send, and what retry category it deserves - both of which depend on the
        /// payload rather than on the command's name.
        /// </summary>
        /// <remarks>
        /// The decision itself is <c>RedisDatabase.SelectBitFieldCommand</c>, shared rather than restated:
        /// all-GET is a pure read whatever command carries it, a payload of SETs replays to the same end
        /// state, and only INCRBY compounds. Only the "is the read-only command available?" input differs,
        /// because here it is answered by the command map instead of by the endpoint's version.
        /// </remarks>
        private static RedisCommand SelectCommand(in RespContext context, in RedisKey key, ReadOnlySpan<BitFieldOperation> operations, ref CommandFlags flags)
        {
            bool allGet = true, anyIncrement = false;
            foreach (ref readonly var operation in operations)
            {
                switch (operation.Kind)
                {
                    case BitFieldOperation.OperationKind.Get:
                        break;
                    case BitFieldOperation.OperationKind.IncrementBy:
                        anyIncrement = true;
                        allGet = false;
                        break;
                    default:
                        allGet = false;
                        break;
                }
            }

            return SelectCommand(in context, key, allGet, anyIncrement, ref flags);
        }

        /// <inheritdoc cref="SelectCommand(in RespContext, in RedisKey, ReadOnlySpan{BitFieldOperation}, ref CommandFlags)"/>
        private static RedisCommand SelectCommand(in RespContext context, in RedisKey key, bool allGet, bool anyIncrement, ref CommandFlags flags)
        {
            // BITFIELD_RO has to be BOTH mapped and actually present: the map is configuration, the
            // version is fact, and guessing wrong here costs an unknown-command error rather than a
            // slower path. Where nothing is known - a bare context, a cold multiplexer - this reports
            // false and the writable command goes out, which works everywhere.
            var readOnlyAvailable = allGet
                && context.CommandMap.IsAvailable(RedisCommand.BITFIELD_RO)
                && context.TryGetFeatures(RedisCommand.BITFIELD_RO, in key, flags, out var features)
                && features.BitFieldReadOnly;
            var command = RedisDatabase.SelectBitFieldCommand(allGet, anyIncrement, readOnlyAvailable, ref flags);
            flags = flags.WithDefaultCategory(command);
            return command;
        }

        /// <summary>The BYTE/BIT index token; <c>BYTE</c> is the server's default and renders nothing.</summary>
        private static RespFragment AsFragment(StringIndexType indexType) => indexType switch
        {
            StringIndexType.Byte => default, // a zero-argument fragment: written, contributes nothing
            StringIndexType.Bit => RespLiterals.Bit,
            _ => throw new ArgumentOutOfRangeException(nameof(indexType)),
        };

        /// <summary>The BITOP operation token.</summary>
        private static RespFragment AsFragment(Bitwise operation) => operation switch
        {
            Bitwise.And => RespLiterals.And,
            Bitwise.Or => RespLiterals.Or,
            Bitwise.Xor => RespLiterals.Xor,
            Bitwise.Not => RespLiterals.Not,
            Bitwise.Diff => RespLiterals.Diff,
            Bitwise.Diff1 => RespLiterals.Diff1,
            Bitwise.AndOr => RespLiterals.AndOr,
            Bitwise.One => RespLiterals.One,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
}
