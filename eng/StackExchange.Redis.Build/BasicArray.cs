using System.Collections;

namespace StackExchange.Redis.Build;

// like ImmutableArray<T>, but with decent equality semantics
public readonly struct BasicArray<T> : IEquatable<BasicArray<T>>, IReadOnlyList<T>
{
    private readonly T[] _elements;

    private BasicArray(T[] elements, int length)
    {
        _elements = elements;
        Length = length;
    }

    private static readonly EqualityComparer<T> _comparer = EqualityComparer<T>.Default;

    public int Length { get; }
    public bool IsEmpty => Length == 0;

    public ref readonly T this[int index]
    {
        get
        {
            if (index < 0 | index >= Length) Throw();
            return ref _elements[index];

            static void Throw() => throw new IndexOutOfRangeException();
        }
    }

    public ReadOnlySpan<T> Span => _elements.AsSpan(0, Length);

    public bool Equals(BasicArray<T> other)
    {
        if (Length != other.Length) return false;
        var y = other.Span;
        int i = 0;
        foreach (ref readonly T el in this.Span)
        {
            if (!_comparer.Equals(el, y[i++])) return false;
        }

        return true;
    }

    public ReadOnlySpan<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

    private IEnumerator<T> EnumeratorCore()
    {
        for (int i = 0; i < Length; i++) yield return this[i];
    }

    public override bool Equals(object? obj) => obj is BasicArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = Length;
        foreach (ref readonly T el in this.Span)
        {
            hash = (hash * -37) + _comparer.GetHashCode(el);
        }

        return hash;
    }
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => EnumeratorCore();
    IEnumerator IEnumerable.GetEnumerator() => EnumeratorCore();

    int IReadOnlyCollection<T>.Count => Length;
    T IReadOnlyList<T>.this[int index] => this[index];

    public struct Builder(int maxLength)
    {
        public int Count { get; private set; }
        private readonly T[] elements = maxLength == 0 ? [] : new T[maxLength];

        public void Add(in T value)
        {
            elements[Count] = value;
            Count++;
        }

        public BasicArray<T> Build() => new(elements, Count);
    }

    public static BasicArray<T> From(ICollection<T> collection)
    {
        if (collection.Count is 0) return default;
        var arr = new T[collection.Count];
        collection.CopyTo(arr, 0);
        return new(arr, arr.Length);
    }

    public static BasicArray<T> From<TSource>(ICollection<TSource> collection, Func<TSource, T> selector)
    {
        if (collection.Count is 0) return default;
        var arr = new T[collection.Count];
        int i = 0;
        foreach (var item in collection)
        {
            arr[i++] = selector(item);
        }

        return new(arr, i);
    }
}
