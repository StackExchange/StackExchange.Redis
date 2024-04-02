using RESPite.Buffers.Internal;
using System;
using System.Buffers;

namespace RESPite.Buffers;

/// <summary>
/// Utility methods for working with buffers
/// </summary>
public static class BufferExtensions
{
    /// <summary>
    /// Retains the specified data; if it is already a <see cref="RefCountedBuffer{T}"/>,
    /// it is retained (<seealso cref="RefCountedBuffer{T}.Retain"/>); otherwise, the
    /// data is copied to a leased buffer.
    /// </summary>
    public static RefCountedBuffer<T> Retain<T>(in this ReadOnlySequence<T> sequence)
    {
        if (sequence.IsEmpty) return default; // nothing to retain
        if (sequence.Start.GetObject() is RefCountedSequenceSegment<T> segment)
        {
            var existing = RefCountedBuffer<T>.CreateValidated(in sequence);
            existing.Retain();
            return existing;
        }
        throw new NotImplementedException("TODO: copy to leased buffer");
    }
}
