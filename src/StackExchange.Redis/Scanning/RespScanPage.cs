using System;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. One page of a cursor scan: the items, and where to resume.
/// </summary>
/// <typeparam name="T">The element type of the scan.</typeparam>
/// <remarks>
/// <para>
/// <b>This is the raw half of the scan API, and it is the half that is honest about the protocol.</b>
/// <c>SCAN</c> and its relatives are a cursor loop, not a sequence: the server decides how much to return,
/// a page may be empty without the scan being over, and only a cursor of zero means finished. A caller who
/// wants that control - checkpointing across restarts, bounding work per tick - can have it here, and the
/// <c>IAsyncEnumerable</c> form is built on top rather than beside.
/// </para>
/// <para>
/// <b>Dispose it.</b> The items are a pooled <see cref="ReadOnlyLease{T}"/>; holding them past disposal is
/// the same mistake as holding a <see cref="RespReply"/>'s windows past its.
/// </para>
/// </remarks>
public readonly struct RespScanPage<T> : IDisposable
{
    internal RespScanPage(long cursor, ReadOnlyLease<T> items)
    {
        Cursor = cursor;
        Items = items;
    }

    /// <summary>The cursor to resume from; <b>zero means the scan is complete</b>.</summary>
    public long Cursor { get; }

    /// <summary>This page's items, which may be empty even when the scan is not over.</summary>
    public ReadOnlyLease<T> Items { get; }

    /// <summary>Whether the scan has finished, i.e. <see cref="Cursor"/> is zero.</summary>
    /// <remarks>
    /// <b>Not "the page was empty".</b> A scan can return an empty page and a non-zero cursor as often as
    /// the server likes - a bucket that happened to hold nothing matching - and stopping on the first
    /// empty page is the classic way to miss most of a keyspace.
    /// </remarks>
    public bool IsComplete => Cursor == 0;

    /// <summary>How many items this page carries.</summary>
    public int Count => Items.Length;

    /// <summary>Return the page's storage to the pool.</summary>
    public void Dispose() => Items.Dispose();

    /// <inheritdoc/>
    public override string ToString() => $"{Count} item(s), cursor {Cursor}";
}

/// <summary>Reads a <c>[cursor, [items]]</c> scan reply into a <see cref="RespScanPage{T}"/>.</summary>
/// <typeparam name="T">The element type of the scan.</typeparam>
/// <remarks>
/// One handler parameterised by how to read an element, rather than one per command: every scan reply has
/// this shape and they differ only in the element, which is exactly what <c>ReadScalarLease</c> already
/// takes. The pair-shaped scans (<c>HSCAN</c> with values, <c>ZSCAN</c>) read their element from two
/// consecutive children, which the projection handles.
/// </remarks>
internal sealed class RespScanPageHandler<T>(RespReader.Projection<T> projection) : IRespHandler<RespScanPage<T>>
{
    public RespScanPage<T> Parse(ref RespReader reader)
    {
        var cursor = ReadCursor(ref reader, out var items);
        return new RespScanPage<T>(cursor, RespHandlers.ReadScalarLease(ref items, projection));
    }

    /// <summary>Read the cursor and hand back a reader positioned on the item array.</summary>
    /// <remarks>Shared with the pair handler, so the two cannot disagree about the envelope.</remarks>
    internal static long ReadCursor(ref RespReader reader, out RespReader items)
    {
        if (!reader.IsAggregate || !reader.AggregateLengthIs(2))
        {
            throw new InvalidOperationException("Expected a two-element scan reply of [cursor, items].");
        }

        var iter = reader.AggregateChildren();
        iter.DemandNext();
        if (!iter.Value.TryReadInt64(out var cursor))
        {
            throw new InvalidOperationException("Expected an integer cursor in a scan reply.");
        }

        iter.DemandNext();
        items = iter.Value;
        return cursor;
    }
}

/// <summary>
/// Reads a <c>[cursor, [items]]</c> scan reply whose items are <b>interleaved pairs</b>.
/// </summary>
/// <typeparam name="T">The element type of the scan.</typeparam>
/// <remarks>
/// The twin of <see cref="RespScanPageHandler{T}"/> for <c>HSCAN</c> and <c>ZSCAN</c>, whose item arrays
/// are <c>field, value, field, value, ...</c> rather than a run of scalars - so each element consumes two
/// children and a per-element projection cannot express it. The shape comes from the same
/// <c>ValuePairInterleavedProcessorBase</c> the classic path uses, which is also what handles RESP3
/// turning some of these replies jagged.
/// </remarks>
internal sealed class RespScanPagePairHandler<T>(ResultProcessor.ValuePairInterleavedProcessorBase<T> shape) : IRespHandler<RespScanPage<T>>
{
    public RespScanPage<T> Parse(ref RespReader reader)
    {
        var cursor = RespScanPageHandler<T>.ReadCursor(ref reader, out var items);
        return new RespScanPage<T>(cursor, RespHandlers.ReadPairLease(ref items, shape));
    }
}

/// <summary>Renders a cursor scan; shared by every group that has one.</summary>
/// <remarks>
/// <c>MATCH</c> is omitted for a nil-or-<c>*</c> pattern and <c>COUNT</c> when the caller did not ask,
/// matching the shipped writer: both are hints, and sending the default explicitly is a wire cost for
/// nothing. <c>NOVALUES</c> goes last, as <c>HSCAN</c> requires.
/// </remarks>
internal static class RespScan
{
    internal static RespRequestFrame Command(
        RespContext context,
        RedisCommand command,
        RedisKey key,
        long cursor,
        RedisValue pattern,
        int? pageSize,
        bool noValues = false)
    {
        if (pageSize is <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var match = RedisBase.CursorUtils.IsNil(pattern) ? RedisValue.Null : pattern;
        return context.Render(
            $"{command}{key}{cursor}{RespLiterals.Match.When(match.HasValue)}{new OptionalValue(match)}{RespLiterals.Count.When(pageSize)}{pageSize}{RespLiterals.NoValues.When(noValues)}");
    }

    /// <summary>The default <c>COUNT</c> the server applies, used as the enumerable's page size.</summary>
    internal static int DefaultPageSize => RedisBase.CursorUtils.DefaultRedisPageSize;
}
