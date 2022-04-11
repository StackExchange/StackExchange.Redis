using System;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace StackExchange.Redis.Transports
{
    internal static class Utilities
    {
        public static bool IsAlive<T>(this Memory<T> value)
        {
            return MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager)
                ? manager.IsAlive : true; // if it isn't ref-counted, it is always "alive"
        }
        public static bool IsAlive<T>(this ReadOnlyMemory<T> value)
        {
            return MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager)
                ? manager.IsAlive : true; // if it isn't ref-counted, it is always "alive"
        }
        /// <summary>
        /// If using a ref-counted memory manager: signal that this memory is no longer required.
        /// </summary>
        public static void Release<T>(this ReadOnlyMemory<T> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager))
                manager.Dispose();
        }

        /// <summary>
        /// If using a ref-counted memory manager: signal that this memory is no longer required.
        /// </summary>
        public static void Release<T>(this Memory<T> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager))
                manager.Dispose();
        }

        /// <summary>
        /// If using a ref-counted memory manager: signal that this memory required for an extended duration (must be paired with an additional call to <see cref="Release{T}(ReadOnlyMemory{T})"/>).
        /// </summary>
        public static void Preserve<T>(this ReadOnlyMemory<T> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager))
                manager.Preserve();
        }
        /// <summary>
        /// If using a ref-counted memory manager: signal that this memory required for an extended duration (must be paired with an additional call to <see cref="Release{T}(Memory{T})"/>).
        /// </summary>
        public static void Preserve<T>(this Memory<T> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<T, RefCountedMemoryManager<T>>(value, out var manager))
                manager.Preserve();
        }

        public static void Release<T>(this in ReadOnlySequence<T> value)
        {
            if (value.IsSingleSegment)
            {
                value.First.Release();
            }
            else
            {
                foreach (var segment in value)
                {
                    segment.Release();
                }
            }
        }
        public static void Preserve<T>(this in ReadOnlySequence<T> value)
        {
            if (value.IsSingleSegment)
            {
                value.First.Preserve();
            }
            else
            {
                foreach (var segment in value)
                {
                    segment.Preserve();
                }
            }
        }

#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER
        // note: here we use the sofware fallback implementation from the BCL
        // source: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs
        // "The .NET Foundation licenses this file to you under the MIT license." (so: we're fine for licensing)
        // With full credit to the donet runtime

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LeadingZeroCount(uint value)
            => 31 ^ Log2SoftwareFallback(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LeadingZeroCount(int value)
            => 31 ^ Log2SoftwareFallback(unchecked((uint)value));

        private static int Log2SoftwareFallback(uint value)
        {
            // No AggressiveInlining due to large method size
            // Has conventional contract 0->0 (Log(0) is undefined)

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
            return Unsafe.AddByteOffset(
                // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
                ref MemoryMarshal.GetReference(Log2DeBruijn),
                // uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
                (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
        }
        private static ReadOnlySpan<byte> Log2DeBruijn => new byte[32]
        {
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31
        };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySequence<T> AsReadOnlySequence<T>(this Memory<T> memory)
            => AsReadOnlySequence((ReadOnlyMemory<T>)memory);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySequence<T> AsReadOnlySequence<T>(this ReadOnlyMemory<T> memory)
        {
            // netfx has a nasty bug if you use `new ROS(memory)` with a custom manager with non-zero start; the bug
            // doesn't apply to sequence segments, though - so if we have a custom manager that doesn't support arrays: use that
            if (memory.IsEmpty) return default;

            if (MemoryMarshal.TryGetMemoryManager<T, MemoryManager<T>>(memory, out var manager, out var start, out var length)
                    && start != 0)
            {
                var seqSegment = manager is RefCountedMemoryManager<T> basic ? basic.SharedSegment : new IsolatedSequenceSegment<T>(manager.Memory);
                return new ReadOnlySequence<T>(seqSegment, start, seqSegment, start + length);
            }

            // push array optimizations below memory manager, because we usually want to retain the original manager (for release)
            if (MemoryMarshal.TryGetArray(memory, out var segment))
                return new ReadOnlySequence<T>(segment.Array, segment.Offset, segment.Count);

            // (shrug)
            return new ReadOnlySequence<T>(memory);
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LeadingZeroCount(uint value)
            => BitOperations.LeadingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LeadingZeroCount(int value)
            => BitOperations.LeadingZeroCount(unchecked((uint)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySequence<T> AsReadOnlySequence<T>(this ReadOnlyMemory<T> memory) => new ReadOnlySequence<T>(memory);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySequence<T> AsReadOnlySequence<T>(this Memory<T> memory) => new ReadOnlySequence<T>(memory);
#endif

#if !(NETCOREAPP3_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER)
        public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> value, CancellationToken cancellationToken)
        {
            return MemoryMarshal.TryGetArray<byte>(value, out var segment)
                ? new ValueTask<int>(stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken))
                : SlowReadAsync(stream, value, cancellationToken);

            static ValueTask<int> SlowReadAsync(Stream stream, Memory<byte> value, CancellationToken cancellationToken)
            {
                var oversized = ArrayPool<byte>.Shared.Rent(value.Length);
                var pending = stream.ReadAsync(oversized, 0, value.Length, cancellationToken);
                if (pending.Status == TaskStatus.RanToCompletion)
                {
                    var result = pending.Result;
                    new ReadOnlySpan<byte>(oversized, 0, result).CopyTo(value.Span);
                    ArrayPool<byte>.Shared.Return(oversized);
                    return new ValueTask<int>(result);
                }
                return Awaited(value, pending, oversized);
            }

            static async ValueTask<int> Awaited(Memory<byte> value, Task<int> pending, byte[] oversized)
            {
                try
                {
                    var result = await pending;
                    new ReadOnlySpan<byte>(oversized, 0, result).CopyTo(value.Span);
                    return result;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(oversized);
                }
            }
        }
        public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
        {
            return MemoryMarshal.TryGetArray<byte>(value, out var segment)
                ? new ValueTask(stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken))
                : SlowWriteAsync(stream, value, cancellationToken);

            static ValueTask SlowWriteAsync(Stream stream, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
            {
                var oversized = ArrayPool<byte>.Shared.Rent(value.Length);
                value.CopyTo(oversized);
                var pending = stream.WriteAsync(oversized, 0, value.Length, cancellationToken);
                if (pending.IsCompleted)
                {
                    ArrayPool<byte>.Shared.Return(oversized);
                    return new ValueTask(pending);
                }
                return Awaited(pending, oversized);
            }

            static async ValueTask Awaited(Task pending, byte[] oversized)
            {
                try
                {
                    await pending;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(oversized);
                }
            }
        }
#endif
    }
#if NETSTANDARD2_0_OR_GREATER || NET461_OR_GREATER
    internal sealed class IsolatedSequenceSegment<T> : ReadOnlySequenceSegment<T>
    {
        public IsolatedSequenceSegment(ReadOnlyMemory<T> memory)
        {
            Next = null;
            Memory = memory;
            RunningIndex = 0;
        }
    }
    partial class RefCountedMemoryManager<T>
    {
        private ReadOnlySequenceSegment<T>? _singleSegment;
        internal ReadOnlySequenceSegment<T> SharedSegment => _singleSegment ??= new IsolatedSequenceSegment<T>(Memory);
    }
#endif
}
