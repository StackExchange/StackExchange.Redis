using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Messages;

namespace RESPite.Resp.Commands;

internal sealed unsafe class UnsafeRawCommand<T> : IWriter<T>
{
    private readonly byte* ptr;
    private readonly int length;

    /// <summary>
    /// Creates a raw command using an externally pinned literal value, for example a u8 string literal.
    /// </summary>
    public UnsafeRawCommand(ReadOnlySpan<byte> value)
    {
        ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(value));
        length = value.Length;
    }

    /// <summary>
    /// Gets the value represented by this command.
    /// </summary>
    public ReadOnlySpan<byte> Value => new(ptr, length);

    void IWriter<T>.Write(in T request, IBufferWriter<byte> target) => target.Write(Value);
}
