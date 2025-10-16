using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RESPite.Connections.Internal;

[Flags]
internal enum ShardFlags
{
    None = 0,
    Replica = 1,
}

internal readonly struct Shard(
    int from,
    int to,
    int port,
    ShardFlags flags,
    string primary,
    string secondary,
    IRespContextSource? source) : IEquatable<Shard>, IComparable<Shard>, IComparable
{
    public readonly int From = from;
    public readonly int To = to;
    public readonly int Port = port;
    public readonly ShardFlags Flags = flags;
    public readonly string Primary = primary;
    public readonly string Secondary = secondary;
    public bool Repliace => (Flags & ShardFlags.Replica) != 0;

    private readonly IRespContextSource? source = source;

    public override string ToString() => $"[{From}-{To}] {source}";
    public int CompareTo(object? obj) => obj is Shard shard ? CompareTo(in shard) : -1;

    public override int GetHashCode() => From ^ To ^ Port ^ (int)Flags ^ Primary.GetHashCode();

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is Shard other && Equals(other);

    bool IEquatable<Shard>.Equals(Shard other) => Equals(in other);

    public bool Equals(in Shard other) =>
        (From == other.From
         & To == other.To
         & Port == other.Port
         & Flags == other.Flags
         & Primary == other.Primary
         & Secondary == other.Secondary)
        && ReferenceEquals(source, other.source);

    int IComparable<Shard>.CompareTo(Shard other) => CompareTo(in other);

    public int CompareTo(in Shard other)
    {
        int delta = From - other.From;
        if (delta == 0)
        {
            delta = To - other.To;
            if (delta == 0)
            {
                delta = (int)Flags - (int)other.Flags;
                if (delta == 0)
                {
                    delta = string.CompareOrdinal(Primary, other.Primary);
                }
            }
        }

        return delta;
    }

    public RespConnection? GetConnection()
    {
        if (source is not null)
        {
            // in this *very specific* case: watch out for null by-refs; we don't
            // do this exhaustively!
            ref readonly RespContext ctx = ref source.Context;
            if (!Unsafe.IsNullRef(ref Unsafe.AsRef(in ctx))) return ctx.Connection;
        }

        return null;
    }
}
