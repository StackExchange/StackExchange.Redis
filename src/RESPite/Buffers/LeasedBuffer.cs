using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite.Internal;

namespace RESPite.Buffers;

/// <summary>
/// Allows construction of contiguous memory in a pooled buffer.
/// </summary>
public static class LeasedBuffer
{
    /// <summary>
    /// Create a new leased buffer from existing content.
    /// </summary>
    public static LeasedBuffer<T> Create<T>(ReadOnlySpan<T> value) => value.IsEmpty ? default : new(value);

    /// <summary>
    /// Create a new leased buffer from existing content.
    /// </summary>
    public static LeasedBuffer<T> Create<T>(ReadOnlyMemory<T> value) => value.IsEmpty ? default : new(value.Span);

    /// <summary>
    /// Create a new leased buffer by encoding text content.
    /// </summary>
    public static LeasedBuffer<byte> Utf8(string value) => Utf8(value.AsSpan());

    /// <summary>
    /// Create a new leased buffer by encoding text content.
    /// </summary>
    public static LeasedBuffer<byte> Utf8(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return default;

        int upperBound;
        if (value.Length <= 32)
        {
            // short enough; don't measure, there won't be much in it
            upperBound = Constants.UTF8.GetMaxByteCount(value.Length);
        }
        else
        {
            upperBound = Constants.UTF8.GetByteCount(value);
        }
        byte[] buffer = ArrayPool<byte>.Shared.Rent(upperBound);
        var count = Constants.UTF8.GetBytes(value, buffer);
        return new(buffer, count);
    }
}

/// <summary>
/// Represents contiguous memory in a pooled buffer.
/// </summary>
public readonly struct LeasedBuffer<T> : IDisposable
{
    /// <inheritdoc cref="Memory"/>
    public static implicit operator ReadOnlyMemory<T>(in LeasedBuffer<T> value) => value.Memory;

    /// <inheritdoc cref="Span"/>
    public static implicit operator ReadOnlySpan<T>(in LeasedBuffer<T> value) => value.Span;

    private readonly T[]? _array;
    private readonly int _length;

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override string ToString() => $"Leased {typeof(T).Name} buffer: {_length}";

    /// <summary>
    /// Gets the content associated with this buffer.
    /// </summary>
    public ReadOnlySpan<T> Span
    {
        get
        {
            if (_array is null)
            {
                ThrowIfDisposed();
                return default;
            }
            return new(_array, 0, _length);
        }
    }

    /// <summary>
    /// Gets the content associated with this buffer.
    /// </summary>
    public ReadOnlyMemory<T> Memory
    {
        get
        {
            if (_array is null)
            {
                ThrowIfDisposed();
                return default;
            }
            return new(_array, 0, _length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_array is null && _length != 0) Throw();

        [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw() => throw new ObjectDisposedException(nameof(LeasedBuffer<byte>));
    }

    internal LeasedBuffer(ReadOnlySpan<T> value)
    {
        if (value.IsEmpty)
        {
            Debug.Fail("should have checked for empty before getting here");
            this = default;
        }
        else
        {
            _array = ArrayPool<T>.Shared.Rent(value.Length);
            _length = value.Length;
            value.CopyTo(_array);
        }
    }

    internal LeasedBuffer(T[] array, int length)
    {
        _array = array;
        _length = length;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var arr = _array;
        Unsafe.AsRef(in _array) = default!;
        if (arr is not null)
        {
            ArrayPool<T>.Shared.Return(arr);
        }
    }
}
