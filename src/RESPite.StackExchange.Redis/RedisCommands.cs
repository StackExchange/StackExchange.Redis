using System.Buffers;
using System.Runtime.CompilerServices;

namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    public static ref readonly RespContext Self(this in RespContext context)
        => ref context; // this just proves that the above are well-defined in terms of escape analysis
}

public readonly struct ScanResult<T>
{
    private const int MSB = 1 << 31;
    private readonly int _countAndIsPooled; // and use MSB for "ispooled"
    private readonly T[] values;

    public ScanResult(long cursor, T[] values)
    {
        Cursor = cursor;
        this.values = values;
        _countAndIsPooled = values.Length;
    }
    internal ScanResult(long cursor, T[] values, int count)
    {
        this.Cursor = cursor;
        this.values = values;
        _countAndIsPooled = count | MSB;
    }

    public long Cursor { get; }
    public ReadOnlySpan<T> Values => new(values, 0, _countAndIsPooled & ~MSB);

    internal void UnsafeRecycle()
    {
        var arr = values;
        bool recycle = (_countAndIsPooled & MSB) != 0;
        Unsafe.AsRef(in this) = default; // best effort at salting the earth
        if (recycle && arr is not null) ArrayPool<T>.Shared.Return(arr);
    }
}
