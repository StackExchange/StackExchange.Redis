using RESPite.Messages;

namespace RESPite;

public static class RespFormatters
{
    public static IRespFormatter<string> String(bool isKey) => isKey ? Key.String : Value.String;
    public static IRespFormatter<ReadOnlyMemory<char>> Chars(bool isKey) => isKey ? Key.Chars : Value.Chars;
    public static IRespFormatter<byte[]> ByteArray(bool isKey) => isKey ? Key.ByteArray : Value.ByteArray;
    public static IRespFormatter<ReadOnlyMemory<byte>> Bytes(bool isKey) => isKey ? Key.Bytes : Value.Bytes;
    public static IRespFormatter<bool> Empty => EmptyFormatter.Instance;
    public static IRespFormatter<int> Int32 => Value.Formatter.Default;
    public static IRespFormatter<long> Int64 => Value.Formatter.Default;
    public static IRespFormatter<float> Single => Value.Formatter.Default;
    public static IRespFormatter<int> Double => Value.Formatter.Default;
    internal static IRespFormatter<byte[]> Raw => RawFormatter.Instance;

    public static class Key
    {
        // ReSharper disable MemberHidesStaticFromOuterClass
        public static IRespFormatter<string> String => Formatter.Default;
        public static IRespFormatter<ReadOnlyMemory<char>> Chars => Formatter.Default;
        public static IRespFormatter<byte[]> ByteArray => Formatter.Default;

        public static IRespFormatter<ReadOnlyMemory<byte>> Bytes => Formatter.Default;
        // ReSharper restore MemberHidesStaticFromOuterClass

        // (just to fix an auto-format glitch)
        internal sealed class Formatter : IRespFormatter<string>, IRespFormatter<byte[]>,
            IRespFormatter<ReadOnlyMemory<char>>, IRespFormatter<ReadOnlyMemory<byte>>
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

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in ReadOnlyMemory<char> value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteKey(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in ReadOnlyMemory<byte> value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteKey(value);
            }
        }
    }

    public static class Value
    {
        // ReSharper disable MemberHidesStaticFromOuterClass
        public static IRespFormatter<string> String => Formatter.Default;
        public static IRespFormatter<ReadOnlyMemory<char>> Chars => Formatter.Default;
        public static IRespFormatter<byte[]> ByteArray => Formatter.Default;

        public static IRespFormatter<ReadOnlyMemory<byte>> Bytes => Formatter.Default;
        // ReSharper restore MemberHidesStaticFromOuterClass

        // (just to fix an auto-format glitch)
        internal sealed class Formatter : IRespFormatter<string>, IRespFormatter<byte[]>,
            IRespFormatter<ReadOnlyMemory<char>>, IRespFormatter<ReadOnlyMemory<byte>>,
            IRespFormatter<int>, IRespFormatter<long>,
            IRespFormatter<float>, IRespFormatter<double>
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

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in ReadOnlyMemory<char> value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in ReadOnlyMemory<byte> value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in int value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in long value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in float value)
            {
                writer.WriteCommand(command, 1);
                writer.WriteBulkString(value);
            }

            public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in double value)
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

    private sealed class RawFormatter : IRespFormatter<byte[]>
    {
        private RawFormatter() { }
        public static readonly RawFormatter Instance = new();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in byte[] value)
        {
            if (writer.CommandMap is null)
            {
                writer.WriteRaw(value);
            }
            else
            {
                writer.WriteCommand(command, 0);
            }
        }
    }
}
