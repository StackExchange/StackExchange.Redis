namespace RESPite;

public readonly partial struct AsciiHash
{
    public static IEqualityComparer<AsciiHash> CaseSensitiveEqualityComparer => CaseSensitiveComparer.Instance;
    public static IEqualityComparer<AsciiHash> CaseInsensitiveEqualityComparer => CaseInsensitiveComparer.Instance;

    private sealed class CaseSensitiveComparer : IEqualityComparer<AsciiHash>
    {
        private CaseSensitiveComparer() { }
        public static readonly CaseSensitiveComparer Instance = new();

        public bool Equals(AsciiHash x, AsciiHash y)
        {
            var len = x.Length;
            return (len == y.Length & x._hashCS == y._hashCS)
                   && (len <= MaxBytesHashIsEqualityCS || x.Span.SequenceEqual(y.Span));
        }

        public int GetHashCode(AsciiHash obj) => obj._hashCS.GetHashCode();
    }

    private sealed class CaseInsensitiveComparer : IEqualityComparer<AsciiHash>
    {
        private CaseInsensitiveComparer() { }
        public static readonly CaseInsensitiveComparer Instance = new();

        public bool Equals(AsciiHash x, AsciiHash y)
        {
            var len = x.Length;
            return (len == y.Length & x._hashLC == y._hashLC)
                   && (len <= MaxBytesHashIsEqualityCS || SequenceEqualsCI(x.Span, y.Span));
        }

        public int GetHashCode(AsciiHash obj) => obj._hashLC.GetHashCode();
    }
}
