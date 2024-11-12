using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Messages;

namespace RESPite.Resp.Writers;

/// <summary>
/// Utility writers for simple commands, using command fragments from externally
/// pinned data, such as <c>"EXAMPLE"u8</c> literals.
/// </summary>
public static class PinnedPrefixWriter
{
    /// <summary>
    /// Writes a command that takes no parameters.
    /// </summary>
    public static IRespWriter<Empty> None(ReadOnlySpan<byte> command)
        => new PrefixWriterNone(command);

    /// <summary>
    /// Writes a command that takes one parameter.
    /// </summary>
    public static IRespWriter<ReadOnlyMemory<byte>> Memory(ReadOnlySpan<byte> prefix)
        => new PrefixWriterMemory(prefix);

    private unsafe sealed class PrefixWriterNone : IWriter<Empty>, IRespWriter<Empty>
    {
        private readonly byte* ptr;
        private readonly int length;

        public PrefixWriterNone(ReadOnlySpan<byte> command)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(command));
            length = command.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        public void Write(in Empty request, IBufferWriter<byte> target) => target.Write(Span);
        void IRespWriter<Empty>.Write(in Empty request, ref RespWriter writer) => writer.WriteRaw(Span);
    }

    private unsafe sealed class PrefixWriterMemory : IWriter<ReadOnlyMemory<byte>>, IRespWriter<ReadOnlyMemory<byte>>
    {
        private readonly byte* ptr;
        private readonly int length;

        public PrefixWriterMemory(ReadOnlySpan<byte> prefix)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(prefix));
            length = prefix.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        void IRespWriter<ReadOnlyMemory<byte>>.Write(in ReadOnlyMemory<byte> request, ref RespWriter writer)
        {
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Span);
        }

        public void Write(in ReadOnlyMemory<byte> request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Span);
            writer.Flush();
        }
    }
}
