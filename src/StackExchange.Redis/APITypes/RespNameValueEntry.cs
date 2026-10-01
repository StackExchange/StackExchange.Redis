using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. One name/value pair, as a pair of windows over the reply that produced it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Top-level, and not nested in a command group</b>, because the shape is not any one group's: a
/// name/value run is what <c>HGETALL</c>, <c>CONFIG GET</c>, <c>XINFO</c>, a stream entry's fields and
/// several <c>CLIENT</c> replies all are. The nesting convention exists to keep <i>group-specific</i>
/// entities out of a shared namespace; something every group can use is not one of those. It also matches
/// where its materialised counterpart already lives - <see cref="NameValueEntry"/> is top-level too.
/// </para>
/// <para>
/// Uncounted, and freely copyable: whichever <see cref="RespReply"/> the pair came from holds the single
/// reference, and this is a view valid for exactly as long as that is.
/// </para>
/// </remarks>
public readonly struct RespNameValueEntry
{
    /// <summary>Captures both halves of a pair; handed readers positioned before each.</summary>
    internal static readonly RespReader.PairProjection<object?, RespNameValueEntry> Projection =
        static (ref object? owner, ref RespReader first, ref RespReader second) =>
        {
            RespValue.TryCaptureNext(owner, ref first, out var name);
            RespValue.TryCaptureNext(owner, ref second, out var value);
            return new RespNameValueEntry(name, value);
        };

    private readonly RespValue _name;
    private readonly RespValue _value;

    private RespNameValueEntry(RespValue name, RespValue value)
    {
        _name = name;
        _value = value;
    }

    /// <summary>The name.</summary>
    public RespValue Name => _name;

    /// <summary>The value.</summary>
    public RespValue Value => _value;

    /// <summary>Materialise this pair, so it outlives the reply it came from.</summary>
    /// <remarks>
    /// <b><c>To</c>, not <c>As</c></b>: both halves are copied out. A <c>ToHashEntry()</c> twin belongs
    /// here too when the hash commands move - the wire shape is the same pair, and only the materialised
    /// type differs.
    /// </remarks>
    public NameValueEntry ToNameValueEntry() => new(_name.AsRedisValue(), _value.AsRedisValue());

    /// <summary>Materialise a run of pairs.</summary>
    internal static NameValueEntry[] ToNameValueEntries(RespNameValueEntry[] pairs)
    {
        if (pairs.Length == 0) return [];

        var result = new NameValueEntry[pairs.Length];
        for (var i = 0; i < pairs.Length; i++)
        {
            result[i] = pairs[i].ToNameValueEntry();
        }
        return result;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{_name}: {_value}";
}
