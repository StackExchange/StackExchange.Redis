using System.Buffers;
using System.Runtime.InteropServices;

#if !(NETCOREAPP || NETSTANDARD2_1_OR_GREATER)
// ReSharper disable once CheckNamespace
namespace System.IO
{
    internal static class StreamExtensions
    {
        public static void Write(this Stream stream, ReadOnlyMemory<byte> value)
        {
            if (MemoryMarshal.TryGetArray(value, out var segment))
            {
                stream.Write(segment.Array!, segment.Offset, segment.Count);
            }
            else
            {
                var leased = ArrayPool<byte>.Shared.Rent(value.Length);
                value.CopyTo(leased);
                stream.Write(leased, 0, value.Length);
                ArrayPool<byte>.Shared.Return(leased); // on success only
            }
        }

        public static int Read(this Stream stream, Memory<byte> value)
        {
            if (MemoryMarshal.TryGetArray<byte>(value, out var segment))
            {
                return stream.Read(segment.Array!, segment.Offset, segment.Count);
            }
            else
            {
                var leased = ArrayPool<byte>.Shared.Rent(value.Length);
                int bytes = stream.Read(leased, 0,  value.Length);
                if (bytes > 0)
                {
                    leased.AsSpan(0, bytes).CopyTo(value.Span);
                }
                ArrayPool<byte>.Shared.Return(leased); // on success only
                return bytes;
            }
        }

        public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> value, CancellationToken cancellationToken)
        {
            if (MemoryMarshal.TryGetArray<byte>(value, out var segment))
            {
                return new(stream.ReadAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken));
            }
            else
            {
                var leased = ArrayPool<byte>.Shared.Rent(value.Length);
                var pending = stream.ReadAsync(leased, 0, value.Length, cancellationToken);
                if (!pending.IsCompleted)
                {
                    return Awaited(pending, value, leased);
                }

                var bytes = pending.GetAwaiter().GetResult();
                if (bytes > 0)
                {
                    leased.AsSpan(0, bytes).CopyTo(value.Span);
                }
                ArrayPool<byte>.Shared.Return(leased); // on success only
                return new(bytes);

                static async ValueTask<int> Awaited(Task<int> pending, Memory<byte> value, byte[] leased)
                {
                    var bytes = await pending.ConfigureAwait(false);
                    if (bytes > 0)
                    {
                        leased.AsSpan(0, bytes).CopyTo(value.Span);
                    }
                    ArrayPool<byte>.Shared.Return(leased); // on success only
                    return bytes;
                }
            }
        }

        public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
        {
            if (MemoryMarshal.TryGetArray(value, out var segment))
            {
                return new(stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken));
            }
            else
            {
                var leased = ArrayPool<byte>.Shared.Rent(value.Length);
                value.CopyTo(leased);
                var pending = stream.WriteAsync(leased, 0, value.Length, cancellationToken);
                if (!pending.IsCompleted)
                {
                    return Awaited(pending, leased);
                }
                pending.GetAwaiter().GetResult();
                ArrayPool<byte>.Shared.Return(leased); // on success only
                return default;
            }
            static async ValueTask Awaited(Task pending, byte[] leased)
            {
                await pending.ConfigureAwait(false);
                ArrayPool<byte>.Shared.Return(leased); // on success only
            }
        }
    }
}
#endif
