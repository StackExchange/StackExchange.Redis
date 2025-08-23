using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;
#if DEBUG
public partial class DebugCounters
#else
internal partial class DebugCounters
#endif
{
    private static int _tallyRead, _tallyAsyncRead, _tallyGrow, _tallyShuffleCount;
    private static long _tallyShuffleBytes, _tallyReadBytes;

    [Conditional("DEBUG")]
    internal static void OnRead(int bytes)
    {
        Interlocked.Increment(ref _tallyRead);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
    }

    [Conditional("DEBUG")]
    internal static void OnAsyncRead(int bytes)
    {
        Interlocked.Increment(ref _tallyAsyncRead);
        if (bytes > 0) Interlocked.Add(ref _tallyReadBytes, bytes);
    }

    [Conditional("DEBUG")]
    internal static void OnGrow() => Interlocked.Increment(ref _tallyGrow);

    [Conditional("DEBUG")]
    internal static void OnShuffle(int bytes)
    {
        Interlocked.Increment(ref _tallyShuffleCount);
        if (bytes > 0) Interlocked.Add(ref _tallyShuffleBytes, bytes);
    }

    public static DebugCounters Flush() => new();
    private DebugCounters() { }

    public int Read { get; } = Interlocked.Exchange(ref _tallyRead, 0);
    public int AsyncRead { get; } = Interlocked.Exchange(ref _tallyAsyncRead, 0);
    public long ReadBytes { get; } = Interlocked.Exchange(ref _tallyReadBytes, 0);
    public int Grow { get; } = Interlocked.Exchange(ref _tallyGrow, 0);
    public int ShuffleCount { get; } = Interlocked.Exchange(ref _tallyShuffleCount, 0);
    public long ShuffleBytes { get; } = Interlocked.Exchange(ref _tallyShuffleBytes, 0);
}
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
        DebugCounters.OnGrow();
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
        Debug.Assert(bytes <= Available, $"Insufficient bytes in OnRead; got {bytes}, Available={Available}");
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
    public void Consume(int count)
    {
        if (count == 0) return;
        if (count < 0 || count > _count) Throw(count);

        // flush down any remaining unconsumed data
        var remaining = _count - count;
        if (remaining != 0)
        {
            DebugCounters.OnShuffle(remaining);
            new ReadOnlySpan<byte>(_buffer, count, remaining).CopyTo(_buffer);
        }
        _count = remaining;
        static void Throw(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Attempted to consume negative amount of data.");
            else throw new ArgumentOutOfRangeException(nameof(count), "Attempted to consume more data than is available.");
        }
    }
}
