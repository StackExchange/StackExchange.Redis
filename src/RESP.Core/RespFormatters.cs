using System;

namespace Resp;

public static class RespFormatters
{
    public static IRespFormatter<Void> Void => InbuiltFormatters.Default;
    public static IRespFormatter<string> String => InbuiltFormatters.Default;
    internal sealed class InbuiltFormatters : IRespFormatter<Void>, IRespFormatter<string>
    {
        private InbuiltFormatters() { }
        public static readonly InbuiltFormatters Default = new();
        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in Void value)
        {
            writer.WriteCommand(command, 0);
        }
        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in string value)
        {
            writer.WriteCommand(command, 1);
            writer.WriteBulkString(value);
        }
    }
}
