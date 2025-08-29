using System;
using Resp;

namespace RESPite.Redis;

internal sealed class KeyStringArrayFormatter : IRespFormatter<ReadOnlyMemory<string>>
{
    public static readonly KeyStringArrayFormatter Instance = new();

    public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in ReadOnlyMemory<string> keys)
    {
        writer.WriteCommand(command, keys.Length);
        foreach (var key in keys.Span)
        {
            writer.WriteKey(key);
        }
    }
}
