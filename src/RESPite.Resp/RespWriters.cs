using System;
using System.Buffers;
using RESPite.Messages;

namespace RESPite.Resp;

/// <summary>
/// Provides common RESP writer implementations.
/// </summary>
public static class RespWriters
{
    private static readonly Impl common = new();

    /// <summary>
    /// Writes ad-hoc payloads where the command and all arguments are <see cref="string"/>s.
    /// </summary>
    public static IWriter<ReadOnlyMemory<string>> Strings => common;

    private sealed class Impl : IWriter<ReadOnlyMemory<string>>
    {
        void IWriter<ReadOnlyMemory<string>>.Write(in ReadOnlyMemory<string> request, IBufferWriter<byte> target)
        {
            var writer = new RespWriter(target);
            writer.WriteArray(request.Length);
            foreach (var value in request.Span)
            {
                writer.WriteBulkString(value);
            }
            writer.Flush();
        }
    }
}
