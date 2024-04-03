using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

    //public static int Receive(this Socket socket, Memory<byte> buffer, SocketFlags flags)
    //    => socket.Receive(buffer.Span, flags);
    //public static int Send(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags flags)
    //    => socket.Send(buffer.Span, flags);

#else

    //public static async ValueTask<int> ReceiveAsync(this Socket socket, Memory<byte> buffer, SocketFlags flags, CancellationToken cancellationToken = default)
    //{
    //    var arr = LeaseForReceive(buffer, out var lease);
    //    int count = await socket.ReceiveAsync(arr.Array, arr.Offset, arr.Count, flags, cancellationToken);
    //    new Span<byte>(arr.Array, arr.Offset, count).CopyTo(buffer.Span);
    //    Return(lease);
    //    return count;
    //}
    //public static ValueTask<int> SendAsync(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags flags, CancellationToken cancellationToken = default)
    //{
    //    return default;
    //}
    //public static int Receive(this Socket socket, Memory<byte> buffer, SocketFlags flags)
    //{
    //    var arr = LeaseForReceive(buffer, out var lease);
    //    int count = socket.Receive(arr.Array, arr.Offset, arr.Count, flags);
    //    new Span<byte>(arr.Array, arr.Offset, count).CopyTo(buffer.Span);
    //    Return(lease);
    //    return count;
    //}
    //public static int Send(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags flags)
    //{
    //    if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
    //        return socket.Send(segment.Array, segment.Offset, segment.Count, flags);
    //    return default;
    //}
    //private static void Return(byte[]? lease)
    //{
    //    if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
    //}
    //private static ArraySegment<byte> LeaseForReceive(ReadOnlyMemory<byte> buffer, out byte[]? lease)
    //{
    //    if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
    //    {
    //        lease = null;
    //        return segment;
    //    }
    //    lease = ArrayPool<byte>.Shared.Rent(buffer.Length);
    //    return new(lease, 0, buffer.Length);
    //}
    //private static ArraySegment<byte> LeaseForSend(Memory<byte> buffer, out byte[]? lease)
    //{
    //    if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
    //    {
    //        lease = null;
    //        return segment;
    //    }
    //    lease = ArrayPool<byte>.Shared.Rent(buffer.Length);
    //    buffer.CopyTo(lease);
    //    return new(lease, 0, buffer.Length);
    //}


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
    public static unsafe int GetByteCount(this UTF8Encoding encoding, ReadOnlySpan<char> source)
    {
        if (source.IsEmpty) return 0;
        fixed (char* cPtr = source)
        {
            return encoding.GetByteCount(cPtr, source.Length);
        }
    }
    public static unsafe void Convert(this Encoder encoder, ReadOnlySpan<char> source, ReadOnlySpan<byte> target, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
    {
        fixed (char* cPtr = source)
        fixed (byte* bPtr = target)
        {
            encoder.Convert(cPtr, source.Length, bPtr, target.Length, flush, out charsUsed, out bytesUsed, out completed);
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
