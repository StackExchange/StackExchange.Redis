using System;
using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// The idempotent id for a stream entry, ensuring at-most-once production. Each producer should have a unique
/// <see cref="ProducerId"/> that is stable and consistent between runs. When adding stream entries, the
/// caller can specify an <see cref="IdempotentId"/> that is unique and repeatable for a given data item, or omit it
/// and let the server generate it from the content of the data item. In either event: duplicates are rejected.
/// </summary>
[Experimental(Experiments.Server_8_6, UrlFormat = Experiments.UrlFormat)]
public readonly struct StreamIdempotentId
{
    // note: if exposing wider, maybe expose as a by-ref property rather than a readonly field
    internal static readonly StreamIdempotentId Empty = default;

    /// <summary>
    /// Create a new <see cref="StreamIdempotentId"/> with the given producer id.
    /// </summary>
    public StreamIdempotentId(RedisValue producerId)
    {
        if (producerId.IsNull) throw new ArgumentNullException(nameof(producerId));
        ProducerId = producerId;
        IdempotentId = RedisValue.Null;
    }

    /// <summary>
    /// The idempotent id for a stream entry, ensuring at-most-once production.
    /// </summary>
    public StreamIdempotentId(RedisValue producerId, RedisValue idempotentId)
    {
        if (!producerId.HasValue) throw new ArgumentNullException(nameof(producerId));
        ProducerId = producerId;
        IdempotentId = idempotentId; // can be explicit null, fine
    }

    /// <summary>
    /// The producer of the idempotent id; this is fixed for a given data generator.
    /// </summary>
    public RedisValue ProducerId { get; }

    /// <summary>
    /// The optional idempotent id; this should be unique for a given data item. If omitted / null,
    /// the server will generate the idempotent id from the content of the data item.
    /// </summary>
    public RedisValue IdempotentId { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IdempotentId.HasValue) return $"IDMP {ProducerId} {IdempotentId}";
        if (ProducerId.HasValue) return $"IDMPAUTO {ProducerId}";
        return "";
    }

    internal int ArgCount => IdempotentId.HasValue ? 3 : ProducerId.HasValue ? 2 : 0;

    internal void WriteTo(RedisValue[] args, ref int index)
    {
        if (IdempotentId.HasValue)
        {
            args[index++] = RedisLiterals.IDMP;
            args[index++] = ProducerId;
            args[index++] = IdempotentId;
        }
        else if (ProducerId.HasValue)
        {
            args[index++] = RedisLiterals.IDMPAUTO;
            args[index++] = ProducerId;
        }
    }

    /// <inheritdoc/>
    public override int GetHashCode() => ProducerId.GetHashCode() ^ IdempotentId.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is StreamIdempotentId other
        && ProducerId == other.ProducerId
        && IdempotentId == other.IdempotentId;
}
