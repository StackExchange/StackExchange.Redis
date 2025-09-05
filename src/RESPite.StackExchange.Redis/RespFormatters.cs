using System.Buffers;
using RESPite.Messages;
using StackExchange.Redis;
using StorageType = StackExchange.Redis.RedisValue.StorageType;

namespace RESPite.StackExchange.Redis;

public static class RespFormatters
{
    public static IRespFormatter<RedisValue> RedisValue => DefaultFormatter.Instance;
    public static IRespFormatter<RedisKey> RedisKey => DefaultFormatter.Instance;

    private sealed class DefaultFormatter : IRespFormatter<RedisValue>, IRespFormatter<RedisKey>
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
