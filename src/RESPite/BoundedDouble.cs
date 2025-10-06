namespace RESPite;

public readonly struct BoundedDouble(double value, bool exclusive = false) : IEquatable<BoundedDouble>
{
     public double Value { get; } = value;
     public bool Inclusive { get; } = !exclusive;
     public override string ToString() => Inclusive ? $"{Value}" : $"({Value}";
     public override int GetHashCode() => unchecked((Value.GetHashCode() * 397) ^ Inclusive.GetHashCode());

     public override bool Equals(object? obj) => obj is BoundedDouble other && Equals(other);
     bool IEquatable<BoundedDouble>.Equals(BoundedDouble other) => Equals(other);
     public bool Equals(in BoundedDouble other) => Value.Equals(other.Value) && Inclusive == other.Inclusive;
     public static bool operator ==(BoundedDouble left, BoundedDouble right) => left.Equals(right);
     public static bool operator !=(BoundedDouble left, BoundedDouble right) => !left.Equals(right);
     public static implicit operator BoundedDouble(double value) => new(value);

     public static BoundedDouble MinValue => new(double.MinValue);
     public static BoundedDouble MaxValue => new(double.MaxValue);
}
