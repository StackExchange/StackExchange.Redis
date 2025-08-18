using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resp.RedisCommands;

public readonly struct RedisString(IRespConnection connection, string key)
{
    public string? Get() => connection.Send("get"u8, key, StringFormatter.Instance, StringParser.Instance);
    public ValueTask<string?> GetAsync(CancellationToken cancellationToken = default) => connection.SendAsync("get"u8, key, StringFormatter.Instance, StringParser.Instance, cancellationToken);
}

internal sealed class StringFormatter : IRespFormatter<string>
{
    public static readonly StringFormatter Instance = new();
    public int Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in string value)
    {
        writer.WriteCommand(command, 1);
        writer.WriteBulkString(value);
        return 1;
    }
}

internal sealed class StringParser : IRespParser<string?>
{
    public static readonly StringParser Instance = new();
    public string? Parse(ref RespReader reader) => reader.ReadString();
}
