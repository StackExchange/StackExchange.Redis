namespace StackExchange.Redis;

/// <summary>
/// The result of a GeoRadius command.
/// </summary>
public readonly struct GeoRadiusResult
{
    /// <summary>
    /// Indicate the member being represented.
    /// </summary>
    public override string ToString() => Member.ToString();

    /// <summary>
    /// The matched member.
    /// </summary>
    public RedisValue Member { get; }

    /// <summary>
    /// The distance of the matched member from the center of the geo radius command.
    /// </summary>
    public double? Distance { get; }

    /// <summary>
    /// The hash value of the matched member as an integer. (The key in the sorted set).
    /// </summary>
    /// <remarks>Note that this is not the same as the hash returned from GeoHash</remarks>
    public long? Hash { get; }

    /// <summary>
    /// The coordinates of the matched member.
    /// </summary>
    public GeoPosition? Position { get; }

    /// <summary>
    /// Returns a new GeoRadiusResult.
    /// </summary>
    /// <param name="member">The value from the result.</param>
    /// <param name="distance">The distance from the result.</param>
    /// <param name="hash">The hash of the result.</param>
    /// <param name="position">The GeoPosition of the result.</param>
    public GeoRadiusResult(in RedisValue member, double? distance, long? hash, GeoPosition? position)
    {
        Member = member;
        Distance = distance;
        Hash = hash;
        Position = position;
    }
}
