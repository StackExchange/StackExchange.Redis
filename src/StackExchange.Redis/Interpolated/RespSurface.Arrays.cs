using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The array group: <c>target.Arrays.GetAsync(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The family that needed no shape decision: <see cref="RedisArrayIndex"/> wraps a <c>ulong</c>,
    /// <see cref="RedisArrayEntry"/> and <see cref="RedisArrayRange"/> are pairs of those, and
    /// <see cref="ArrayInfo"/> is seven of them - nothing here is a composite holding an array, so the
    /// whole group follows the rule directly: spans in, leases out.
    /// </para>
    /// <para>
    /// The three element types render <i>themselves</i> (<see cref="IRespArgument"/>), which is what lets a
    /// <c>ReadOnlySpan&lt;RedisArrayIndex&gt;</c> or a span of ranges go straight into a hole. No overload
    /// per element type on the handler, and no boxing: a constrained call on a struct.
    /// </para>
    /// <para>
    /// <c>ARGREP</c> is absent. <see cref="ArrayGrepRequest"/> is a mutable builder whose predicates render
    /// themselves through the <i>old</i> <c>MessageWriter</c>, so moving it is a design decision about that
    /// type rather than a transcription of a command; see the queue.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespArrays
    {
        private readonly RespContext _context;

        /// <summary>Group the array commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespArrays(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespKeyspaceTarget target)
        {
            /// <summary>The array commands.</summary>
            public RespArrays Arrays => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The array commands.</summary>
            public RespArrays Arrays => new(context);
        }

        /// <summary>ARSET; whether the slot was empty before.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="index">The slot to write.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> SetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<bool>(
                $"{RedisCommand.ARSET}{key}{index}{value}", flags);

        /// <summary>ARSET of consecutive slots from <paramref name="index"/>; how many were written.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="index">The first slot to write.</param>
        /// <param name="values">The values to store.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> SetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => values.IsEmpty
                ? default
                : arrays.Context.SendAsync<long>(
                    $"{RedisCommand.ARSET}{key}{index}{values}", flags);

        /// <summary>ARSET of scattered slots; how many were written.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="entries">The index/value pairs to store.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> SetAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayEntry> entries, CommandFlags flags = CommandFlags.None)
            => entries.IsEmpty
                ? default
                : arrays.Context.SendAsync<long>(
                    $"{RedisCommand.ARSET}{key}{entries}", flags);

        /// <summary>ARGET; the value at one slot.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="index">The slot to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> GetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisValue>(
                $"{RedisCommand.ARGET}{key}{index}", flags);

        /// <summary>ARMGET; the values at scattered slots.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="indices">The slots to read.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisValue>> GetAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None)
            => indices.IsEmpty
                ? new(ReadOnlyLease<RedisValue>.Empty)
                : arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                    $"{RedisCommand.ARMGET}{key}{indices}", flags);

        /// <summary>ARGETRANGE; the values across a contiguous span of slots.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="start">The first slot.</param>
        /// <param name="end">The last slot.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisValue>> GetRangeAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                $"{RedisCommand.ARGETRANGE}{key}{start}{end}", flags);

        /// <summary>ARLEN; the highest index in use, plus one.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> LengthAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARLEN}{key}", flags);

        /// <summary>ARCOUNT; how many slots hold a value.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> CountAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARCOUNT}{key}", flags);

        /// <summary>ARDEL; whether the slot held a value.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="index">The slot to clear.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> DeleteAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<bool>(
                $"{RedisCommand.ARDEL}{key}{index}", flags);

        /// <summary>ARDEL of scattered slots; how many held a value.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="indices">The slots to clear.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<long> DeleteAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None)
            => indices.IsEmpty
                ? default
                : arrays.Context.SendAsync<long>(
                    $"{RedisCommand.ARDEL}{key}{indices}", flags);

        /// <summary>ARDELRANGE; how many slots were cleared.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="start">The first slot.</param>
        /// <param name="end">The last slot.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> DeleteRangeAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARDELRANGE}{key}{start}{end}", flags);

        /// <summary>ARDELRANGE across several ranges; how many slots were cleared.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="ranges">The ranges to clear.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> DeleteRangeAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayRange> ranges, CommandFlags flags = CommandFlags.None)
            => ranges.IsEmpty
                ? default
                : arrays.Context.SendAsync<RedisArrayIndex>(
                    $"{RedisCommand.ARDELRANGE}{key}{ranges}", flags);

        /// <summary>ARSCAN; the occupied slots in a range, as index/value pairs.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="start">The first slot.</param>
        /// <param name="end">The last slot.</param>
        /// <param name="limit">The most entries to return; zero for no limit.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisArrayEntry>> ScanAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int? limit = null, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<ReadOnlyLease<RedisArrayEntry>>(
                $"{RedisCommand.ARSCAN}{key}{start}{end}{RespLiterals.Limit.When(limit)}{limit}",
                flags);

        /// <summary>AROP; an aggregate over a range of slots.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="start">The first slot.</param>
        /// <param name="end">The last slot.</param>
        /// <param name="operation">The aggregate to compute.</param>
        /// <param name="operand">The value to match; only for <see cref="ArrayOperation.Match"/>.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisValue> OperationAsync(
            this in RespArrays arrays,
            RedisKey key,
            RedisArrayIndex start,
            RedisArrayIndex end,
            ArrayOperation operation,
            RedisValue operand = default,
            CommandFlags flags = CommandFlags.None)
        {
            DemandOperandMatchesOperation(operation, operand);
            return arrays.Context.SendAsync<RedisValue>(
                $"{RedisCommand.AROP}{key}{start}{end}{OperationToken(operation)}{new OptionalValue(operand)}",
                flags);
        }

        /// <summary>ARRING; append to a ring buffer, evicting from the front past <paramref name="maxLength"/>.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="maxLength">The length to hold.</param>
        /// <param name="value">The value to append.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> RingAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARRING}{key}{maxLength}{value}", flags);

        /// <inheritdoc cref="RingAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisValue, CommandFlags)"/>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="maxLength">The length to hold.</param>
        /// <param name="values">The values to append.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> RingAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex maxLength, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => values.IsEmpty
                ? default
                : arrays.Context.SendAsync<RedisArrayIndex>(
                    $"{RedisCommand.ARRING}{key}{maxLength}{values}", flags);

        /// <summary>ARNEXT; the next free slot, or <c>null</c> if the array is full.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex?> NextAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex?>(
                $"{RedisCommand.ARNEXT}{key}", flags);

        /// <summary>ARINSERT; store at the next free slot and report which.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> InsertAsync(this in RespArrays arrays, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARINSERT}{key}{value}", flags);

        /// <inheritdoc cref="InsertAsync(in RespArrays, RedisKey, RedisValue, CommandFlags)"/>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="values">The values to store.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<RedisArrayIndex> InsertAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => values.IsEmpty
                ? default
                : arrays.Context.SendAsync<RedisArrayIndex>(
                    $"{RedisCommand.ARINSERT}{key}{values}", flags);

        /// <summary>ARSEEK; move the insertion cursor.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="index">The slot to seek to.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<bool> SeekAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<bool>(
                $"{RedisCommand.ARSEEK}{key}{index}", flags);

        /// <summary>ARLASTITEMS; the most recently occupied slots' values.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="count">How many to return.</param>
        /// <param name="reverse">Whether to return them newest-first.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ReadOnlyLease<RedisValue>> LastItemsAsync(this in RespArrays arrays, RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                $"{RedisCommand.ARLASTITEMS}{key}{count}{RespLiterals.Rev.When(reverse)}",
                flags);

        /// <summary>ARINFO; the array's shape.</summary>
        /// <param name="arrays">The array command group.</param>
        /// <param name="key">The array.</param>
        /// <param name="full">Whether to ask for the fuller report.</param>
        /// <param name="flags">Command flags.</param>
        public static ValueTask<ArrayInfo> InfoAsync(this in RespArrays arrays, RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<ArrayInfo>(
                $"{RedisCommand.ARINFO}{key}{RespLiterals.Full.When(full)}",
                flags);

        /// <summary>A value that is written only when it is there.</summary>
        /// <remarks>
        /// An operand rather than a conditional hole, because a <see cref="RedisValue"/> hole is not a way
        /// to write nothing: <c>AppendFormatted(RedisValue)</c> always writes an argument and counts it, so
        /// a null one sends an <i>empty</i> argument. That is the trap this shape exists to avoid - the
        /// frame stays well-formed, so the mistake shows up as the server misreading the command rather
        /// than as anything failing here. The <c>TOKEN n</c> form of the same idea is
        /// <c>RespLiterals.X.When(value)</c> followed by the value itself; this one carries no token.
        /// </remarks>
        private readonly struct OptionalValue(RedisValue value) : IRespArgument
        {
            public void WriteTo(scoped ref RespCommandHandler handler)
            {
                // absent: writes no arguments, and so contributes nothing to the frame's argument count
                if (value.IsNull) return;
                handler.AppendFormatted(value);
            }
        }

        /// <inheritdoc cref="GetAsync(in RespArrays, RedisKey, ReadOnlySpan{RedisArrayIndex}, CommandFlags)"/>
        /// <remarks><inheritdoc cref="ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags)" path="/remarks"/></remarks>
        internal static ValueTask<RedisValue[]> GetArray(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None)
            => indices.IsEmpty
                ? new(Array.Empty<RedisValue>())
                : arrays.Context.SendAsync<RedisValue[]>(
                    $"{RedisCommand.ARMGET}{key}{indices}", flags);

        /// <inheritdoc cref="GetRangeAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, CommandFlags)"/>
        /// <remarks><inheritdoc cref="ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags)" path="/remarks"/></remarks>
        internal static ValueTask<RedisValue[]> GetRangeArray(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.ARGETRANGE}{key}{start}{end}", flags);

        /// <inheritdoc cref="ScanAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags)"/>
        /// <remarks>
        /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> promises an array and is not going anywhere,
        /// so this is how that signature is served from the new core. Internal because the array is the
        /// <i>old</i> spelling: new code reaches for the lease, and nothing outside this assembly should be
        /// able to choose otherwise.
        /// </remarks>
        internal static ValueTask<RedisArrayEntry[]> ScanArray(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int? limit = null, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisArrayEntry[]>(
                $"{RedisCommand.ARSCAN}{key}{start}{end}{RespLiterals.Limit.When(limit)}{limit}",
                flags);

        /// <inheritdoc cref="LastItemsAsync(in RespArrays, RedisKey, int, bool, CommandFlags)"/>
        /// <remarks><inheritdoc cref="ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags)" path="/remarks"/></remarks>
        internal static ValueTask<RedisValue[]> LastItemsArray(this in RespArrays arrays, RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None)
            => arrays.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.ARLASTITEMS}{key}{count}{RespLiterals.Rev.When(reverse)}",
                flags);

        /// <summary>The token for an <c>AROP</c> aggregate.</summary>
        private static RespFragment OperationToken(ArrayOperation operation) => operation switch
        {
            ArrayOperation.Sum => RespLiterals.Sum,
            ArrayOperation.Min => RespLiterals.Min,
            ArrayOperation.Max => RespLiterals.Max,
            ArrayOperation.And => RespLiterals.And,
            ArrayOperation.Or => RespLiterals.Or,
            ArrayOperation.Xor => RespLiterals.Xor,
            ArrayOperation.Match => RespLiterals.Match,
            ArrayOperation.Used => RespLiterals.Used,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        /// <summary>
        /// The operand belongs to <see cref="ArrayOperation.Match"/> and to nothing else - checked here
        /// rather than left to the server, because "an operand the command ignores" is the sort of mistake
        /// that looks like it worked.
        /// </summary>
        private static void DemandOperandMatchesOperation(ArrayOperation operation, RedisValue operand)
        {
            var hasOperand = !operand.IsNull;
            if (operation == ArrayOperation.Match)
            {
                if (!hasOperand) throw new ArgumentException("The Match operation requires a non-null operand.", nameof(operand));
            }
            else if (hasOperand)
            {
                throw new ArgumentException("An operand is only supported for the Match operation.", nameof(operand));
            }
        }
    }
}
