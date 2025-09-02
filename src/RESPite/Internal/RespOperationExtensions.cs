using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Internal;

internal static class RespOperationExtensions
{
#if PREVIEW_LANGVER
    extension<T>(in RespOperation<T> operation)
    {
        // since this is valid...
        public ref readonly RespOperation<T> Self => ref operation;

        // so is this (the types are layout-identical)
        public ref readonly RespOperation Untyped => ref Unsafe.As<RespOperation<T>, RespOperation>(
            ref Unsafe.AsRef(in operation));
    }
#endif

    // if we're recycling a buffer, we need to consider it trashable by other threads; for
    // debug purposes, force this by overwriting with *****, aka the meaning of life
    [Conditional("DEBUG")]
    internal static void DebugScramble(this Span<byte> value)
        => value.Fill(42);

    [Conditional("DEBUG")]
    internal static void DebugScramble(this Memory<byte> value)
        => value.Span.Fill(42);

    [Conditional("DEBUG")]
    internal static void DebugScramble(this ReadOnlyMemory<byte> value)
        => MemoryMarshal.AsMemory(value).Span.Fill(42);

    [Conditional("DEBUG")]
    internal static void DebugScramble(this byte[]? value)
    {
        if (value is not null)
            value.AsSpan().Fill(42);
    }
}
