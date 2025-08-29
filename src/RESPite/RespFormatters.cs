using RESPite.Messages;

namespace RESPite;

public static class RespFormatters
{
    public static IRespFormatter<string> String(bool isKey) => isKey ? Key.String : Value.String;
    public static IRespFormatter<byte[]> ByteArray(bool isKey) => isKey ? Key.ByteArray : Value.ByteArray;
    public static IRespFormatter<bool> Empty => EmptyFormatter.Instance;

    public static class Key
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static IRespFormatter<string> String => Formatter.Default;
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static IRespFormatter<byte[]> ByteArray => Formatter.Default;

        internal sealed class Formatter : IRespFormatter<string>, IRespFormatter<byte[]>
        {
            private Formatter() { }
            public static readonly Formatter Default = new();

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in string value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteKey(value);
            }
            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in byte[] value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteKey(value);
            }
        }
    }

    public static class Value
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static IRespFormatter<string> String => Formatter.Default;
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static IRespFormatter<byte[]> ByteArray => Formatter.Default;

        internal sealed class Formatter : IRespFormatter<string>, IRespFormatter<byte[]>
        {
            private Formatter() { }
            public static readonly Formatter Default = new();

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in string value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }
            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in byte[] value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }
        }
    }

    private sealed class EmptyFormatter : IRespFormatter<bool>
    {
        private EmptyFormatter() { }
        public static readonly EmptyFormatter Instance = new();
        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in bool value)
        {
            writer.WriteCommand(command, 0);
        }
    }
}
