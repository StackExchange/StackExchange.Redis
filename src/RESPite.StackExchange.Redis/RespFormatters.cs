using System.Buffers;
using RESPite.Messages;
using StackExchange.Redis;
using StorageType = StackExchange.Redis.RedisValue.StorageType;

namespace RESPite.StackExchange.Redis;

public static class RespFormatters
{
    public static IRespFormatter<RedisValue> RedisValue => DefaultFormatter.Instance;
    public static IRespFormatter<RedisKey> RedisKey => DefaultFormatter.Instance;
    public static IRespFormatter<RedisKey[]> RedisKeyArray => DefaultFormatter.Instance;

    private sealed class DefaultFormatter : IRespFormatter<RedisValue>, IRespFormatter<RedisKey>, IRespFormatter<RedisKey[]>
    {
        public static readonly DefaultFormatter Instance = new();
        private DefaultFormatter() { }

        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in RedisValue request)
        {
            writer.WriteCommand(command, 1);
            writer.Write(request);
        }

        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in RedisKey request)
        {
            writer.WriteCommand(command, 1);
            writer.Write(request);
        }

        public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in RedisKey[] request)
        {
            writer.WriteCommand(command, 1 + request.Length);
            foreach (var key in request)
            {
                writer.Write(key);
            }
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static void Write(this ref RespWriter writer, in RedisKey key)
    {
        if (key.TryGetSimpleBuffer(out var arr))
        {
            key.AssertNotNull();
            writer.WriteKey(arr);
        }
        else
        {
            var len = key.TotalLength();
            byte[]? lease = null;
            var span = len <= 128 ? stackalloc byte[128] : (lease = ArrayPool<byte>.Shared.Rent(len));
            var written = key.CopyTo(span);
            writer.WriteKey(span.Slice(0, written));
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }
    }

    internal static void WriteBulkString(this ref RespWriter writer, HashCommandsExtensions.HGetExMode when)
    {
        switch (when)
        {
            case HashCommandsExtensions.HGetExMode.EX:
                writer.WriteRaw("$2\r\nEX\r\n"u8);
                break;
            case HashCommandsExtensions.HGetExMode.PX:
                writer.WriteRaw("$2\r\nPX\r\n"u8);
                break;
            case HashCommandsExtensions.HGetExMode.EXAT:
                writer.WriteRaw("$4\r\nEXAT\r\n"u8);
                break;
            case HashCommandsExtensions.HGetExMode.PXAT:
                writer.WriteRaw("$4\r\nPXAT\r\n"u8);
                break;
            case HashCommandsExtensions.HGetExMode.PERSIST:
                writer.WriteRaw("$7\r\nPERSIST\r\n"u8);
                break;
            default:
                Throw();
                static void Throw() => throw new ArgumentOutOfRangeException(nameof(when));
                break;
        }
    }

    internal static void WriteBulkString(this ref RespWriter writer, ExpireWhen when)
    {
        switch (when)
        {
            case ExpireWhen.HasExpiry:
                writer.WriteRaw("$2\r\nXX\r\n"u8);
                break;
            case ExpireWhen.HasNoExpiry:
                writer.WriteRaw("$2\r\nNX\r\n"u8);
                break;
            case ExpireWhen.GreaterThanCurrentExpiry:
                writer.WriteRaw("$2\r\nGT\r\n"u8);
                break;
            case ExpireWhen.LessThanCurrentExpiry:
                writer.WriteRaw("$2\r\nLT\r\n"u8);
                break;
            default:
                Throw();
                static void Throw() => throw new ArgumentOutOfRangeException(nameof(when));
                break;
        }
    }

    internal static void WriteBulkString(this ref RespWriter writer, ListSide side)
    {
        switch (side)
        {
            case ListSide.Left:
                writer.WriteRaw("$4\r\nLEFT\r\n"u8);
                break;
            case ListSide.Right:
                writer.WriteRaw("$5\r\nRIGHT\r\n"u8);
                break;
            default:
                Throw();
                static void Throw() => throw new ArgumentOutOfRangeException(nameof(side));
                break;
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static void Write(this ref RespWriter writer, in RedisValue value)
    {
        switch (value.Type)
        {
            case StorageType.Double:
                writer.WriteBulkString(value.OverlappedValueDouble);
                break;
            case StorageType.Int64:
                writer.WriteBulkString(value.OverlappedValueInt64);
                break;
            case StorageType.UInt64:
                writer.WriteBulkString(value.OverlappedValueUInt64);
                break;
            case StorageType.String:
                writer.WriteBulkString((string)value.DirectObject!);
                break;
            case StorageType.Raw:
                writer.WriteBulkString((ReadOnlyMemory<byte>)value);
                break;
            case StorageType.Null:
                value.AssertNotNull();
                break;
            default:
                Throw(value.Type);
                break;
        }
        static void Throw(StorageType type)
            => throw new InvalidOperationException($"Unexpected {type} value.");
    }
}
