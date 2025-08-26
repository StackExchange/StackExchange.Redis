#pragma warning disable SA1403 // single namespace

#if NET5_0_OR_GREATER
// context: https://github.com/StackExchange/StackExchange.Redis/issues/2619
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Runtime.CompilerServices.IsExternalInit))]
#else
// To support { get; init; } properties
using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
#endif

#if !(NETCOREAPP || NETSTANDARD2_1_OR_GREATER)
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
namespace System.Text
{
    internal static unsafe class EncodingExtensions
    {
        public static int GetBytes(this Encoding encoding, ReadOnlySpan<char> source, Span<byte> destination)
        {
            fixed (byte* bPtr = destination)
            {
                fixed (char* cPtr = source)
                {
                    return encoding.GetBytes(cPtr, source.Length, bPtr, destination.Length);
                }
            }
        }
        public static string GetString(this Encoding encoding, ReadOnlySpan<byte> source)
        {
            fixed (byte* bPtr = source)
            {
                return encoding.GetString(bPtr, source.Length);
            }
        }
        public static int GetChars(this Encoding encoding, ReadOnlySpan<byte> source, Span<char> destination)
        {
            fixed (byte* bPtr = source)
            {
                fixed (char* cPtr = destination)
                {
                    return encoding.GetChars(bPtr, source.Length, cPtr, destination.Length);
                }
            }
        }

        public static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> source)
        {
            fixed (char* cPtr = source)
            {
                return encoding.GetByteCount(cPtr, source.Length);
            }
        }

        public static void Convert(this Encoder encoder, ReadOnlySpan<char> source, Span<byte> destination, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            fixed (char* cPtr = source)
            {
                fixed (byte* bPtr = destination)
                {
                    encoder.Convert(cPtr, source.Length, bPtr, destination.Length, flush, out charsUsed, out bytesUsed, out completed);
                }
            }
        }
    }
}
#endif


#pragma warning restore SA1403
