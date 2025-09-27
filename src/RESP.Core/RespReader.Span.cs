#define USE_UNSAFE_SPAN

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace Resp;

/*
 How we actually implement the underlying buffer depends on the capabilities of the runtime.
 */

#if NET7_0_OR_GREATER && USE_UNSAFE_SPAN

public ref partial struct RespReader
{
    // intent: avoid lots of slicing by dealing with everything manually, and accepting the "don't get it wrong" rule
    private ref byte _bufferRoot;
    private int _bufferLength;

    private partial void UnsafeTrimCurrentBy(int count)
    {
        Debug.Assert(count >= 0 && count <= _bufferLength, "Unsafe trim length");
        _bufferLength -= count;
    }

    private readonly partial ref byte UnsafeCurrent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.Add(ref _bufferRoot, _bufferIndex);
    }

    private readonly partial int CurrentLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bufferLength;
    }

    private readonly partial ReadOnlySpan<byte> CurrentSpan() => MemoryMarshal.CreateReadOnlySpan(
        ref UnsafeCurrent, CurrentAvailable);

    private readonly partial ReadOnlySpan<byte> UnsafePastPrefix() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex + 1),
        _bufferLength - (_bufferIndex + 1));

    private partial void SetCurrent(ReadOnlySpan<byte> value)
    {
        _bufferRoot = ref MemoryMarshal.GetReference(value);
        _bufferLength = value.Length;
    }
}
#else
public ref partial struct RespReader // much more conservative - uses slices etc
{
    private ReadOnlySpan<byte> _buffer;

    private partial void UnsafeTrimCurrentBy(int count)
    {
        _buffer = _buffer.Slice(0, _buffer.Length - count);
    }

    private readonly partial ref byte UnsafeCurrent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.AsRef(in _buffer[_bufferIndex]); // hack around CS8333
    }

    private readonly partial int CurrentLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;
    }

    private readonly partial ReadOnlySpan<byte> UnsafePastPrefix() => _buffer.Slice(_bufferIndex + 1);

    private readonly partial ReadOnlySpan<byte> CurrentSpan() => _buffer.Slice(_bufferIndex);

    private partial void SetCurrent(ReadOnlySpan<byte> value) => _buffer = value;
}
#endif
