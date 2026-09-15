using RESPite.Messages;

namespace StackExchange.Redis;

public readonly partial struct VectorSetLink
{
    /// <summary>
    /// Read a neighbour and its distance: two successive scalars, member then score.
    /// </summary>
    /// <param name="reader">The reader, positioned on the member.</param>
    /// <remarks>
    /// Shared by both readers - the <c>ResultProcessor</c> path and the interpolated surface's handler -
    /// because the interleaving is the shape: <c>VLINKS ... WITHSCORES</c> sends a flat run of pairs, not
    /// an array of two-element arrays, and getting that wrong reads a member as a score.
    /// </remarks>
    internal static VectorSetLink Read(ref RespReader reader)
    {
        var member = reader.ReadRedisValue();
        return reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var score)
            ? new VectorSetLink(member, score)
            : default;
    }
}
