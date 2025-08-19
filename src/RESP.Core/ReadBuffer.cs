using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

internal struct ReadBuffer
{
    private byte[]? _buffer;
    private int _count;

    private int Available => _buffer is null ? 0 : (_buffer.Length - _count);

    public void Release()
    {
        var buffer = _buffer;
        _buffer = null;
        _count = 0;
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private const int MIN_READ = 64;
    private static void ThrowEOF() => throw new EndOfStreamException();

    private void Grow()
    {
        var oldLength = _buffer?.Length ?? 0;
        var newLength = Math.Max(_count + MIN_READ, checked((oldLength * 3) / 2));
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newLength);
        var oldBuffer = _buffer;
        if (_count != 0)
        {
            new ReadOnlySpan<byte>(oldBuffer, 0, _count).CopyTo(newBuffer);
        }
        _buffer = newBuffer;
        if (oldBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(oldBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool OnRead(int bytes)
    {
        if (bytes <= 0) return false;
        Debug.Assert(bytes < Available);
        _count += bytes;
        return true;
    }

    public ArraySegment<byte> GetWriteBuffer(int sizeHint = MIN_READ)
    {
        if (Available < sizeHint) Grow();
        return new(_buffer!, _count, Available);
    }

    /// <summary>
    /// Gets the available data, optionally skipping data that has already been parsed but not consumed.
    /// </summary>
    public ReadOnlySpan<byte> GetSpan(int skip = 0)
        => new(_buffer, skip, _count - skip);

    /// <summary>
    /// Gets the available data, optionally skipping data that has already been parsed but not consumed.
    /// </summary>
    public ReadOnlySequence<byte> GetSequence(int skip = 0)
        => new(_buffer!, skip, _count - skip);

    /// <summary>
    /// Copy out the requested portion, resetting the buffer (but retaining unconsumed data).
    /// </summary>
    public void Consume(Span<byte> target)
    {
        if (target.Length > _count) Throw();
        new ReadOnlySpan<byte>(_buffer, 0, target.Length).CopyTo(target);
        _count -= target.Length;

        // flush down any remaining unconsumed data
        var remaining = _count - target.Length;
        if (remaining != 0)
        {
            new ReadOnlySpan<byte>(_buffer, target.Length, remaining).CopyTo(_buffer);
        }

        static void Throw() => throw new InvalidOperationException("Attempted to consume more data than is available.");
    }
}
