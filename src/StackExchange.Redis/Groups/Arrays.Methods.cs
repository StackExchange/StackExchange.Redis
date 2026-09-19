using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The arrays commands.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place. The extension methods
/// bind by namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </remarks>
public static partial class Arrays
{
    /// <summary>ARSET; whether the slot was empty before.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="index">The slot to write.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> SetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<bool>(
            $"{RedisCommand.ARSET}{key}{index}{value}", flags, cancellationToken: cancellationToken);

    /// <summary>ARSET of consecutive slots from <paramref name="index"/>; how many were written.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="index">The first slot to write.</param>
    /// <param name="values">The values to store.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> SetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => values.IsEmpty
            ? default
            : arrays.Context.SendAsync<long>(
                $"{RedisCommand.ARSET}{key}{index}{values}", flags, cancellationToken: cancellationToken);

    /// <summary>ARSET of scattered slots; how many were written.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="entries">The index/value pairs to store.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> SetAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayEntry> entries, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => entries.IsEmpty
            ? default
            : arrays.Context.SendAsync<long>(
                $"{RedisCommand.ARSET}{key}{entries}", flags, cancellationToken: cancellationToken);

    /// <summary>ARGET; the value at one slot.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="index">The slot to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> GetAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisValue>(
            $"{RedisCommand.ARGET}{key}{index}", flags, cancellationToken: cancellationToken);

    /// <summary>ARMGET; the values at scattered slots.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="indices">The slots to read.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RedisValue>> GetAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => indices.IsEmpty
            ? new(ReadOnlyLease<RedisValue>.Empty)
            : arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(
                $"{RedisCommand.ARMGET}{key}{indices}", flags, cancellationToken: cancellationToken);

    /// <summary>ARGETRANGE; the values across a contiguous span of slots.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="start">The first slot.</param>
    /// <param name="end">The last slot.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RedisValue>> GetRangeAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(
            $"{RedisCommand.ARGETRANGE}{key}{start}{end}", flags, cancellationToken: cancellationToken);

    /// <summary>ARLEN; the highest index in use, plus one.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> LengthAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex>(
            $"{RedisCommand.ARLEN}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>ARCOUNT; how many slots hold a value.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> CountAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex>(
            $"{RedisCommand.ARCOUNT}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>ARDEL; whether the slot held a value.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="index">The slot to clear.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> DeleteAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<bool>(
            $"{RedisCommand.ARDEL}{key}{index}", flags, cancellationToken: cancellationToken);

    /// <summary>ARDEL of scattered slots; how many held a value.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="indices">The slots to clear.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<long> DeleteAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => indices.IsEmpty
            ? default
            : arrays.Context.SendAsync<long>(
                $"{RedisCommand.ARDEL}{key}{indices}", flags, cancellationToken: cancellationToken);

    /// <summary>ARDELRANGE; how many slots were cleared.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="start">The first slot.</param>
    /// <param name="end">The last slot.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> DeleteRangeAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex>(
            $"{RedisCommand.ARDELRANGE}{key}{start}{end}", flags, cancellationToken: cancellationToken);

    /// <summary>ARDELRANGE across several ranges; how many slots were cleared.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="ranges">The ranges to clear.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> DeleteRangeAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayRange> ranges, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => ranges.IsEmpty
            ? default
            : arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARDELRANGE}{key}{ranges}", flags, cancellationToken: cancellationToken);

    /// <summary>ARSCAN; the occupied slots in a range, as index/value pairs.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="start">The first slot.</param>
    /// <param name="end">The last slot.</param>
    /// <param name="limit">The most entries to return; zero for no limit.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RedisArrayEntry>> ScanAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int? limit = null, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = ScanCommand(arrays.Context, key, start, end, limit);
        return arrays.Context.SendAsync<ReadOnlyLease<RedisArrayEntry>>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Render <c>ARSCAN</c> - the one place the command is composed.
    /// </summary>
    /// <remarks>
    /// <c>LIMIT</c> is optional and its token is written only when the value is, which is a decision -
    /// and a decision copied per overload is a decision that drifts. The lease form and the array form
    /// differ only in what parses the reply. The returned frame owns a pooled buffer and must be sent.
    /// </remarks>
    private static RespRequestFrame ScanCommand(RespContext context, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int? limit)
        => context.Render($"{RedisCommand.ARSCAN}{key}{start}{end}{RespLiterals.Limit.When(limit)}{limit}");

    /// <summary>AROP; an aggregate over a range of slots.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="start">The first slot.</param>
    /// <param name="end">The last slot.</param>
    /// <param name="operation">The aggregate to compute.</param>
    /// <param name="operand">The value to match; only for <see cref="ArrayOperation.Match"/>.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisValue> OperationAsync(
        this in RespArrays arrays,
        RedisKey key,
        RedisArrayIndex start,
        RedisArrayIndex end,
        ArrayOperation operation,
        RedisValue operand = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
    {
        DemandOperandMatchesOperation(operation, operand);
        return arrays.Context.SendAsync<RedisValue>(
            $"{RedisCommand.AROP}{key}{start}{end}{OperationToken(operation)}{new OptionalValue(operand)}",
            flags,
            cancellationToken: cancellationToken);
    }

    /// <summary>ARRING; append to a ring buffer, evicting from the front past <paramref name="maxLength"/>.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="maxLength">The length to hold.</param>
    /// <param name="value">The value to append.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> RingAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex maxLength, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex>(
            $"{RedisCommand.ARRING}{key}{maxLength}{value}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Arrays.RingAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="maxLength">The length to hold.</param>
    /// <param name="values">The values to append.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> RingAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex maxLength, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => values.IsEmpty
            ? default
            : arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARRING}{key}{maxLength}{values}", flags, cancellationToken: cancellationToken);

    /// <summary>ARNEXT; the next free slot, or <c>null</c> if the array is full.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex?> NextAsync(this in RespArrays arrays, RedisKey key, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex?>(
            $"{RedisCommand.ARNEXT}{key}", flags, cancellationToken: cancellationToken);

    /// <summary>ARINSERT; store at the next free slot and report which.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> InsertAsync(this in RespArrays arrays, RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisArrayIndex>(
            $"{RedisCommand.ARINSERT}{key}{value}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Arrays.InsertAsync(in RespArrays, RedisKey, RedisValue, CommandFlags, CancellationToken)"/>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="values">The values to store.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<RedisArrayIndex> InsertAsync(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisValue> values, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => values.IsEmpty
            ? default
            : arrays.Context.SendAsync<RedisArrayIndex>(
                $"{RedisCommand.ARINSERT}{key}{values}", flags, cancellationToken: cancellationToken);

    /// <summary>ARSEEK; move the insertion cursor.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="index">The slot to seek to.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<bool> SeekAsync(this in RespArrays arrays, RedisKey key, RedisArrayIndex index, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<bool>(
            $"{RedisCommand.ARSEEK}{key}{index}", flags, cancellationToken: cancellationToken);

    /// <summary>ARLASTITEMS; the most recently occupied slots' values.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="count">How many to return.</param>
    /// <param name="reverse">Whether to return them newest-first.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ReadOnlyLease<RedisValue>> LastItemsAsync(this in RespArrays arrays, RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = LastItemsCommand(arrays.Context, key, count, reverse);
        return arrays.Context.SendAsync<ReadOnlyLease<RedisValue>>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Render <c>ARLASTITEMS</c> - the one place the command is composed.
    /// </summary>
    /// <remarks><inheritdoc cref="ScanCommand" path="/remarks"/></remarks>
    private static RespRequestFrame LastItemsCommand(RespContext context, RedisKey key, int count, bool reverse)
        => context.Render($"{RedisCommand.ARLASTITEMS}{key}{count}{RespLiterals.Rev.When(reverse)}");

    /// <summary>ARINFO; the array's shape.</summary>
    /// <param name="arrays">The array command group.</param>
    /// <param name="key">The array.</param>
    /// <param name="full">Whether to ask for the fuller report.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    public static ValueTask<ArrayInfo> InfoAsync(this in RespArrays arrays, RedisKey key, bool full = false, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<ArrayInfo>(
            $"{RedisCommand.ARINFO}{key}{RespLiterals.Full.When(full)}",
            flags,
            cancellationToken: cancellationToken);

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
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            // absent: writes no arguments, and so contributes nothing to the frame's argument count
            if (value.IsNull) return;
            handler.AppendFormatted(value);
        }
    }

    /// <inheritdoc cref="Arrays.GetAsync(in RespArrays, RedisKey, ReadOnlySpan{RedisArrayIndex}, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="Arrays.ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    internal static ValueTask<RedisValue[]> GetArray(this in RespArrays arrays, RedisKey key, ReadOnlySpan<RedisArrayIndex> indices, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => indices.IsEmpty
            ? new(Array.Empty<RedisValue>())
            : arrays.Context.SendAsync<RedisValue[]>(
                $"{RedisCommand.ARMGET}{key}{indices}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Arrays.GetRangeAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="Arrays.ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    internal static ValueTask<RedisValue[]> GetRangeArray(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
        => arrays.Context.SendAsync<RedisValue[]>(
            $"{RedisCommand.ARGETRANGE}{key}{start}{end}", flags, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Arrays.ScanAsync(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags, CancellationToken)"/>
    /// <remarks>
    /// <b>Permanent, not scaffolding.</b> <c>IDatabase</c> promises an array and is not going anywhere,
    /// so this is how that signature is served from the new core. Internal because the array is the
    /// <i>old</i> spelling: new code reaches for the lease, and nothing outside this assembly should be
    /// able to choose otherwise.
    /// </remarks>
    internal static ValueTask<RedisArrayEntry[]> ScanArray(this in RespArrays arrays, RedisKey key, RedisArrayIndex start, RedisArrayIndex end, int? limit = null, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = ScanCommand(arrays.Context, key, start, end, limit);
        return arrays.Context.SendAsync<RedisArrayEntry[]>(ref cmd, flags, cancellationToken: cancellationToken);
    }

    /// <inheritdoc cref="Arrays.LastItemsAsync(in RespArrays, RedisKey, int, bool, CommandFlags, CancellationToken)"/>
    /// <remarks><inheritdoc cref="Arrays.ScanArray(in RespArrays, RedisKey, RedisArrayIndex, RedisArrayIndex, int?, CommandFlags, CancellationToken)" path="/remarks"/></remarks>
    internal static ValueTask<RedisValue[]> LastItemsArray(this in RespArrays arrays, RedisKey key, int count, bool reverse = false, CommandFlags flags = CommandFlags.None, CancellationToken cancellationToken = default)
    {
        var cmd = LastItemsCommand(arrays.Context, key, count, reverse);
        return arrays.Context.SendAsync<RedisValue[]>(ref cmd, flags, cancellationToken: cancellationToken);
    }

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
