using System.Collections;

namespace StackExchange.Redis.Build;

/// <summary>
/// Think <c>ImmutableArray{T}</c>, but with structural equality.
/// </summary>
/// <typeparam name="T">The data being wrapped.</typeparam>
internal readonly struct EasyArray<T>(T[]? array) : IEquatable<EasyArray<T>>, IEnumerable<T>
{
    public static readonly EasyArray<T> Empty = new([]);
    private readonly T[]? _array = array ?? [];
    public int Length => _array?.Length ?? 0;
    public ref readonly T this[int index] => ref _array![index];
    public ReadOnlySpan<T> Span => _array.AsSpan();
    public bool IsEmpty => Length == 0;

    public static bool operator ==(EasyArray<T> x, EasyArray<T> y)
        => x.Equals(y);

    public static bool operator !=(EasyArray<T> x, EasyArray<T> y)
        => x.Equals(y);

    public bool Equals(EasyArray<T> other)
    {
        T[]? tArr = this._array, oArr = other._array;
        if (tArr is null) return oArr is null || oArr.Length == 0;
        if (oArr is null) return tArr.Length == 0;

        if (tArr.Length != oArr.Length) return false;
        for (int i = 0; i < tArr.Length; i++)
        {
            if (ReferenceEquals(tArr[i], oArr[i]))
                return false;
        }
        return true;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? [])).GetEnumerator();

    public override bool Equals(object? obj)
        => obj is EasyArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var arr = _array;
        if (arr is null) return 0;
        // use length and first item for a quick hash
        return arr.Length == 0
            ? 0
            : arr.Length ^ EqualityComparer<T>.Default.GetHashCode(arr[0]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
