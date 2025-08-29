using RESPite.Messages;

namespace RESPite;

public static class RespContextExtensions
{
    public static RespOperationBuilder<T> Command<T>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        T value,
        IRespFormatter<T> formatter)
        => new(in context, command, value, formatter);

    /*
    public static RespOperationBuilder<T> Command<T>(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        T value)
        => new(in context, command, value, RespFormatters.Get<T>());
*/

    public static RespOperationBuilder<bool> Command(this in RespContext context, ReadOnlySpan<byte> command)
        => new(in context, command, false, RespFormatters.Empty);

    /*
    public static RespOperationBuilder<string> Command(this in RespContext context, ReadOnlySpan<byte> command,
        string value, bool isKey)
        => new(in context, command, value, RespFormatters.String(isKey));

    public static RespOperationBuilder<byte[]> Command(
        this in RespContext context,
        ReadOnlySpan<byte> command,
        byte[] value,
        bool isKey)
        => new(in context, command, value, RespFormatters.ByteArray(isKey));
        */
}
