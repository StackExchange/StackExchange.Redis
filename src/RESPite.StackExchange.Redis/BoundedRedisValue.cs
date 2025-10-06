using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

public readonly struct BoundedRedisValue : IEquatable<BoundedRedisValue>
{
    internal readonly RedisValue ValueRaw;
    public RedisValue Value => ValueRaw;
    private readonly BoundType _type;
    internal BoundType Type => _type;

    public BoundedRedisValue(RedisValue value, bool exclusive = false)
    {
        ValueRaw = value;
        _type = exclusive ? BoundType.Exclusive : BoundType.Inclusive;
    }

    private BoundedRedisValue(BoundType type)
    {
        _type = type;
        ValueRaw = RedisValue.Null;
    }

    internal enum BoundType : byte
    {
        Inclusive,
        Exclusive,
        MinValue,
        MaxValue,
    }
    public bool Inclusive => _type == BoundType.Inclusive;

    public override string ToString() => _type switch
    {
        BoundType.Inclusive => $"[{ValueRaw}",
        BoundType.Exclusive => $"({ValueRaw}",
        BoundType.MinValue => "-",
        BoundType.MaxValue => "+",
        _ => _type.ToString(),
    };

    public override int GetHashCode() => unchecked((Value.GetHashCode() * 397) ^ _type.GetHashCode());

    public override bool Equals(object? obj) => obj is BoundedRedisValue other && Equals(other);
    bool IEquatable<BoundedRedisValue>.Equals(BoundedRedisValue other) => Equals(other);
    public bool Equals(in BoundedRedisValue other) => Value.Equals(other.Value) && Inclusive == other.Inclusive;
    public static bool operator ==(BoundedRedisValue left, BoundedRedisValue right) => left.Equals(right);
    public static bool operator !=(BoundedRedisValue left, BoundedRedisValue right) => !left.Equals(right);
    public static implicit operator BoundedRedisValue(RedisValue value) => new(value);

    public static BoundedRedisValue MinValue => new(BoundType.MinValue);
    public static BoundedRedisValue MaxValue => new(BoundType.MaxValue);
}
