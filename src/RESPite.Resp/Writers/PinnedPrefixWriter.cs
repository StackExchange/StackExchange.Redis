using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Internal;
using RESPite.Messages;
using RESPite.Resp.Readers;

namespace RESPite.Resp.Writers;

/// <summary>
/// Utility writers for simple commands, using command fragments from externally
/// pinned data, such as <c>"EXAMPLE"u8</c> literals.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1414:Tuple types in signatures should have element names", Justification = "In this case, the meaning is context-dependent")]
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
    public static IRespWriter<SimpleString> SimpleString(ReadOnlySpan<byte> prefix)
        => new PrefixWriterSimpleString(prefix);

    internal static IRespWriter<(SimpleString, int, ReadOnlySequence<byte>)> SimpleStringInt32Sequence(ReadOnlySpan<byte> prefix)
        => new PrefixWriterSimpleStringInt32Sequence(prefix);

    internal static IRespWriter<(SimpleString, int, int)> SimpleStringInt32Int32(ReadOnlySpan<byte> prefix)
    => new PrefixWriterSimpleStringInt32Int32(prefix);

    private static string GetCommand(ReadOnlySpan<byte> span)
    {
        try
        {
            RespReader reader = new(span);
            if (reader.TryReadNext() && reader.Prefix == RespPrefix.Array
                && reader.TryReadNext() && reader.Prefix == RespPrefix.BulkString)
            {
                return reader.ReadString() ?? "";
            }
        }
        catch { }
        return Constants.UTF8.GetString(span);
    }

    private unsafe sealed class PrefixWriterNone : IWriter<Empty>, IRespWriter<Empty>
    {
        private readonly byte* ptr;
        private readonly int length;

        public override string ToString() => GetCommand(Span);

        public PrefixWriterNone(ReadOnlySpan<byte> command)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(command));
            length = command.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        public void Write(in Empty request, IBufferWriter<byte> target) => target.Write(Span);
        void IRespWriter<Empty>.Write(in Empty request, ref RespWriter writer) => writer.WriteRaw(Span);
    }

    private unsafe sealed class PrefixWriterSimpleString : IWriter<SimpleString>, IRespWriter<SimpleString>
    {
        private readonly byte* ptr;
        private readonly int length;

        public override string ToString() => GetCommand(Span);

        public PrefixWriterSimpleString(ReadOnlySpan<byte> prefix)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(prefix));
            length = prefix.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        public void Write(in SimpleString request, ref RespWriter writer)
        {
            writer.WriteRaw(Span);
            writer.WriteBulkString(in request);
        }

        public void Write(in SimpleString request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw(Span);
            writer.WriteBulkString(in request);
            writer.Flush();
        }
    }

    private unsafe sealed class PrefixWriterSimpleStringInt32Sequence :
        IWriter<(SimpleString, int, ReadOnlySequence<byte>)>,
        IRespWriter<(SimpleString, int, ReadOnlySequence<byte>)>
    {
        private readonly byte* ptr;
        private readonly int length;

        public override string ToString() => GetCommand(Span);

        public PrefixWriterSimpleStringInt32Sequence(ReadOnlySpan<byte> prefix)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(prefix));
            length = prefix.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        public void Write(in (SimpleString, int, ReadOnlySequence<byte>) request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
            writer.Flush();
        }
        public void Write(in (SimpleString, int, ReadOnlySequence<byte>) request, ref RespWriter writer)
        {
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
        }
    }

    private unsafe sealed class PrefixWriterSimpleStringInt32Int32 :
    IWriter<(SimpleString, int, int)>,
    IRespWriter<(SimpleString, int, int)>
    {
        private readonly byte* ptr;
        private readonly int length;

        public override string ToString() => GetCommand(Span);

        public PrefixWriterSimpleStringInt32Int32(ReadOnlySpan<byte> prefix)
        {
            ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(prefix));
            length = prefix.Length;
        }

        private ReadOnlySpan<byte> Span => new(ptr, length);

        public void Write(in (SimpleString, int, int) request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
            writer.Flush();
        }
        public void Write(in (SimpleString, int, int) request, ref RespWriter writer)
        {
            writer.WriteRaw(Span);
            writer.WriteBulkString(request.Item1);
            writer.WriteBulkString(request.Item2);
            writer.WriteBulkString(request.Item3);
        }
    }
}
