using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite.Buffers.Internal;

namespace RESPite.Buffers;

/// <summary>
/// An arbitrary payload that uses ref-counting for retention; incorrect usage
/// may cause significant problems.
/// </summary>
/// <remarks>
/// The underlying buffer is not required to support ref-counting - GC rules will work otherwise.
/// </remarks>
public readonly struct RefCountedBuffer<T>(in ReadOnlySequence<T> content) : IDisposable
{
    /// <inheritdoc cref="ReadOnlySequence{T}.Length"/>
    public long Length => _content.Length;

    /// <inheritdoc cref="ReadOnlySequence{T}.IsEmpty"/>
    public bool IsEmpty => _content.IsEmpty;

    /// <summary>
    /// Convert the specified <see cref="RefCountedBuffer{T}"/> to a <see cref="ReadOnlySequence{T}"/>.
    /// </summary>
    /// <param name="value">The buffer to convert.</param>
    public static implicit operator ReadOnlySequence<T>(in RefCountedBuffer<T> value) => value._content;

    /// <inheritdoc/>
    public override string ToString() => _content.ToString();

    [SuppressMessage("Style", "IDE0032:Use auto property", Justification = "Clarity")]

    private readonly ReadOnlySequence<T> _content = content;

    /// <inheritdoc/>
    public override int GetHashCode() => _content.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj switch
    {
        RefCountedBuffer<T> refCounted => refCounted._content.Equals(_content),
        ReadOnlySequence<T> sequence => sequence.Equals(_content),
        _ => false,
    };

    /// <summary>
    /// Gets the data associated with this buffer.
    /// </summary>
    public ReadOnlySequence<T> Content => _content;

    /// <summary>
    /// Increments the ref-counter for this data, indicating that lifetime should
    /// be extended until <see cref="Release"/> has reduced the counter to zero.
    /// </summary>
    public void Retain()
    {
        if (_content.Start.GetObject() is RefCountedSequenceSegment<T> counted)
        {
            var end = _content.End.GetObject();
            do
            {
                counted.AddRef();
            }
            while (!ReferenceEquals(counted, end) && (counted = counted!.Next!) is not null);
        }
    }

    /// <summary>
    /// Decrements the ref-counter for this data; if the counter for any segment
    /// is reduced to zero, the data is released.
    /// </summary>
    public void Release()
    {
        if (_content.Start.GetObject() is RefCountedSequenceSegment<T> counted)
        {
            var end = _content.End.GetObject();
            do
            {
                counted.Release();
            }
            while (!ReferenceEquals(counted, end) && (counted = counted!.Next!) is not null);
        }
    }

    void IDisposable.Dispose() => Release();

    internal static RefCountedBuffer<T> CreateValidated(in ReadOnlySequence<T> content)
        => new(in content);

    /// <summary>
    /// Enumerate the values inside the buffer.
    /// </summary>
    public ReadOnlySequence<T>.Enumerator GetEnumerator() => _content.GetEnumerator();
}
