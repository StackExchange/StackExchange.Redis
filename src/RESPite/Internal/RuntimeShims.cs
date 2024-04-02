using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Internal;

internal static class RuntimeShims
{
#if NETCOREAPP3_1_OR_GREATER
    public static void Write(this Stream stream, ReadOnlyMemory<byte> buffer)
        => stream.Write(buffer.Span);
    public static int Read(this Stream stream, Memory<byte> buffer)
        => stream.Read(buffer.Span);
#else
    public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            stream.Write([], 0, 0);
        }
        else
        {
            var arr = ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.CopyTo(arr);
            stream.Write(arr, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(arr);
        }
    }
    public static void Write(this Stream stream, ReadOnlyMemory<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            // zero-length write can represent a flush; need to preserve it
            stream.Write([], 0, 0);
        }
        else if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            stream.Write(segment.Array, segment.Offset, segment.Count);
        }
        else
        {
            var oversized = ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.CopyTo(oversized);
            stream.Write(oversized, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(oversized);
        }
    }

    public static int Read(this Stream stream, Memory<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            // zero-length read can represent a "wait"; need to preserve it
            return stream.Read([], 0, 0);
        }
        else if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            return stream.Read(segment.Array, segment.Offset, segment.Count);
        }
        else
        {
            var oversized = ArrayPool<byte>.Shared.Rent(buffer.Length);
            int count = stream.Read(oversized, 0, buffer.Length);
            if (count > 0)
            {
                new ReadOnlySpan<byte>(oversized, 0, count).CopyTo(buffer.Span);
            }
            ArrayPool<byte>.Shared.Return(oversized);
            return count;
        }
    }
    public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        if (buffer.IsEmpty)
        {
            // zero-length read can represent a "wait"; need to preserve it
            return new(stream.ReadAsync([], 0, 0, token));
        }
        else if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            return new(stream.ReadAsync(segment.Array, segment.Offset, segment.Count, token));
        }
        else
        {
            var oversized = ArrayPool<byte>.Shared.Rent(buffer.Length);
            var pending = stream.ReadAsync(oversized, 0, buffer.Length, token);
            if (pending.Status != TaskStatus.RanToCompletion) return Awaited(pending, buffer, oversized);
            var count = pending.GetAwaiter().GetResult();
            if (count > 0)
            {
                new ReadOnlySpan<byte>(oversized, 0, count).CopyTo(buffer.Span);
            }
            ArrayPool<byte>.Shared.Return(oversized);
            return new(count);
        }

        static async ValueTask<int> Awaited(Task<int> pending, Memory<byte> buffer, byte[] oversized)
        {
            var count = await pending.ConfigureAwait(false);
            if (count > 0)
            {
                new ReadOnlySpan<byte>(oversized, 0, count).CopyTo(buffer.Span);
            }
            ArrayPool<byte>.Shared.Return(oversized);
            return count;
        }
    }

    public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken token)
    {
        if (buffer.IsEmpty)
        {
            // zero-length write can represent a flush; need to preserve it
            return new(stream.WriteAsync([], 0, 0, token));
        }
        else if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
        {
            return new(stream.WriteAsync(segment.Array, segment.Offset, segment.Count, token));
        }
        else
        {
            var oversized = ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.CopyTo(oversized);
            stream.Write(oversized, 0, buffer.Length);
            var pending = stream.WriteAsync(oversized, 0, buffer.Length, token);
            if (pending.Status != TaskStatus.RanToCompletion) return Awaited(pending, oversized);
            pending.GetAwaiter().GetResult(); // just to observe exception (we don't expect one)
            ArrayPool<byte>.Shared.Return(oversized);
            return default;
        }

        static async ValueTask Awaited(Task pending, byte[] oversized)
        {
            await pending.ConfigureAwait(false);
            ArrayPool<byte>.Shared.Return(oversized);
        }
    }

    public static unsafe string GetString(this UTF8Encoding encoding, ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return "";
        fixed (byte* ptr = buffer)
        {
            return encoding.GetString(ptr, buffer.Length);
        }
    }
    public static unsafe int GetBytes(this UTF8Encoding encoding, ReadOnlySpan<char> source, Span<byte> target)
    {
        if (source.IsEmpty) return 0;
        fixed (char* cPtr = source)
        fixed (byte* bPtr = target)
        {
            return encoding.GetBytes(cPtr, source.Length, bPtr, target.Length);
        }
    }
#endif
#if !NET6_0_OR_GREATER
    public static string GetString(this UTF8Encoding encoding, in ReadOnlySequence<byte> buffer)
    {
        if (buffer.IsSingleSegment) return buffer.IsEmpty ? "" : encoding.GetString(buffer.First.Span);

        var len = checked((int)buffer.Length);
        var arr = ArrayPool<byte>.Shared.Rent(len);
        buffer.CopyTo(arr);
        var s = encoding.GetString(arr, 0, len);
        ArrayPool<byte>.Shared.Return(arr);
        return s;
    }
#endif
}
