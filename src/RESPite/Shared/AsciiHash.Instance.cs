using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RESPite;

public readonly partial struct AsciiHash : IEquatable<AsciiHash>
{
    // ReSharper disable InconsistentNaming
    private readonly long _hashCS, _hashUC;
    // ReSharper restore InconsistentNaming
    private readonly int _index, _length;
    private readonly byte[] _arr;

    public int Length => _length;

    /// <summary>
    /// The optimal buffer length (with padding) to use for this value.
    /// </summary>
    public int BufferLength => (Length + 1 + 7) & ~7; // an extra byte, then round up to word-size

    public ReadOnlySpan<byte> Span => new(_arr ?? [], _index, _length);
    public bool IsEmpty => Length != 0;

    public AsciiHash(ReadOnlySpan<byte> value) : this(value.ToArray(), 0, value.Length) { }
    public AsciiHash(string? value) : this(value is null ? [] : Encoding.ASCII.GetBytes(value)) { }

    /// <inheritdoc/>
    public override int GetHashCode() => _hashCS.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => _length == 0 ? "" : Encoding.ASCII.GetString(_arr, _index, _length);

    /// <inheritdoc/>
    public override bool Equals(object? other) => other is AsciiHash hash && Equals(hash);

    /// <inheritdoc cref="Equals(object)" />
    public bool Equals(in AsciiHash other)
    {
        return (_length == other.Length & _hashCS == other._hashCS)
               && (_length <= MaxBytesHashed || Span.SequenceEqual(other.Span));
    }

    bool IEquatable<AsciiHash>.Equals(AsciiHash other) => Equals(other);

    public AsciiHash(byte[] arr) : this(arr, 0, -1) { }

    public AsciiHash(byte[] arr, int index, int length)
    {
        _arr = arr ?? [];
        _index = index;
        _length = length < 0 ? (_arr.Length - index) : length;

        var span = new ReadOnlySpan<byte>(_arr, _index, _length);
        Hash(span, out _hashCS, out _hashUC);
    }

    public bool IsCS(ReadOnlySpan<byte> value)
    {
        var cs = HashCS(value);
        var len = _length;
        if (cs != _hashCS | value.Length != len) return false;
        return len <= MaxBytesHashed || Span.SequenceEqual(value);
    }

    public bool IsCI(ReadOnlySpan<byte> value)
    {
        var uc = HashUC(value);
        var len = _length;
        if (uc != _hashUC | value.Length != len) return false;
        return len <= MaxBytesHashed || SequenceEqualsCI(Span, value);
    }
}
