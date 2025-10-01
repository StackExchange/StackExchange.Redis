using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal static partial class RedisCommands
{
    // this is just a "type pun" - it should be an invisible/magic pointer cast to the JIT
    public static ref readonly KeyCommands Keys(this in RespContext context)
        => ref Unsafe.As<RespContext, KeyCommands>(ref Unsafe.AsRef(in context));
}

public readonly struct KeyCommands(in RespContext context)
{
    public readonly RespContext Context = context; // important: this is the only field
}

internal static partial class KeyCommandsExtensions
{
    [RespCommand]
    public static partial RespOperation<bool> Del(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> Del(this in KeyCommands context, [RespKey] RedisKey[] keys);

    [RespCommand]
    public static partial RespOperation<byte[]?> Dump(this in KeyCommands context, RedisKey key);

    [RespCommand("object")]
    public static partial RespOperation<string?> ObjectEncoding(this in KeyCommands context, [RespPrefix("ENCODING")] RedisKey key);

    [RespCommand("object", Parser = "RespParsers.TimeSpanFromSeconds")]
    public static partial RespOperation<TimeSpan?> ObjectIdleTime(this in KeyCommands context, [RespPrefix("IDLETIME")] RedisKey key);

    [RespCommand("object")]
    public static partial RespOperation<long?> ObjectRefCount(this in KeyCommands context, [RespPrefix("REFCOUNT")] RedisKey key);

    [RespCommand("object")]
    public static partial RespOperation<long?> ObjectFreq(this in KeyCommands context, [RespPrefix("FREQ")] RedisKey key);

    [RespCommand(Parser = "RespParsers.TimeSpanFromSeconds")]
    public static partial RespOperation<TimeSpan?> Ttl(this in KeyCommands context, RedisKey key);

    [RespCommand(Parser = "RespParsers.TimeSpanFromMilliseconds")]
    public static partial RespOperation<TimeSpan?> Pttl(this in KeyCommands context, RedisKey key);

    [RespCommand(Parser = "RespParsers.DateTimeFromSeconds")]
    public static partial RespOperation<DateTime?> ExpireTime(this in KeyCommands context, RedisKey key);

    [RespCommand(Parser = "RespParsers.DateTimeFromMilliseconds")]
    public static partial RespOperation<DateTime?> PExpireTime(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Exists(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Move(this in KeyCommands context, RedisKey key, int db);

    [RespCommand]
    public static partial RespOperation<long> Exists(this in KeyCommands context, [RespKey] RedisKey[] keys);

    public static RespOperation<bool> Expire(this in KeyCommands context, RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always)
    {
        if (expiry is null || expiry == TimeSpan.MaxValue)
        {
            if (when != ExpireWhen.Always) Throw(when);
            return Persist(context, key);
            static void Throw(ExpireWhen when) => throw new ArgumentException($"PERSIST cannot be used with {when}.");
        }
        var millis = (long)expiry.GetValueOrDefault().TotalMilliseconds;
        if (millis % 1000 == 0) // use seconds
        {
            return Expire(context, key, millis / 1000, when);
        }
        return PExpire(context, key, millis, when);
    }

    public static RespOperation<bool> ExpireAt(this in KeyCommands context, RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always)
    {
        if (expiry is null || expiry == DateTime.MaxValue)
        {
            if (when != ExpireWhen.Always) Throw(when);
            return Persist(context, key);
            static void Throw(ExpireWhen when) => throw new ArgumentException($"PERSIST cannot be used with {when}.");
        }
        var millis = RedisDatabase.GetUnixTimeMilliseconds(expiry.GetValueOrDefault());
        if (millis % 1000 == 0) // use seconds
        {
            return ExpireAt(context, key, millis / 1000, when);
        }
        return PExpireAt(context, key, millis, when);
    }

    [RespCommand]
    public static partial RespOperation<bool> Persist(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<bool> Touch(this in KeyCommands context, RedisKey key);

    [RespCommand]
    public static partial RespOperation<long> Touch(this in KeyCommands context, [RespKey] RedisKey[] keys);

    [RespCommand(Parser = "RedisTypeParser.Instance")]
    public static partial RespOperation<RedisType> Type(this in KeyCommands context, RedisKey key);

    private sealed class RedisTypeParser : IRespParser<RedisType>
    {
        public static readonly RedisTypeParser Instance = new();
        private RedisTypeParser() { }

        public RedisType Parse(ref RespReader reader)
        {
            if (reader.IsNull) return RedisType.None;
            if (reader.Is("zset"u8)) return RedisType.SortedSet;
            return reader.ReadEnum(RedisType.Unknown);
        }
    }

    [RespCommand]
    public static partial RespOperation<bool> Rename(this in KeyCommands context, RedisKey key, RedisKey newKey);

    [RespCommand(Formatter = "RestoreFormatter.Instance")]
    public static partial RespOperation Restore(this in KeyCommands context, RedisKey key, TimeSpan? ttl, byte[] serializedValue);

    private sealed class RestoreFormatter : IRespFormatter<(RedisKey Key, TimeSpan? Ttl, byte[] SerializedValue)>
    {
        public static readonly RestoreFormatter Instance = new();
        private RestoreFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, TimeSpan? Ttl, byte[] SerializedValue) request)
        {
            writer.WriteCommand(command, 3);
            writer.Write(request.Key);
            if (request.Ttl.HasValue)
            {
                writer.WriteBulkString((long)request.Ttl.Value.TotalMilliseconds);
            }
            else
            {
                writer.WriteRaw("$1\r\n0\r\n"u8);
            }
            writer.WriteBulkString(request.SerializedValue);
        }
    }

    [RespCommand]
    public static partial RespOperation<RedisKey> RandomKey(this in KeyCommands context);

    [RespCommand(Formatter = "ExpireFormatter.Instance")]
    public static partial RespOperation<bool> Expire(this in KeyCommands context, RedisKey key, long seconds, ExpireWhen when = ExpireWhen.Always);

    [RespCommand(Formatter = "ExpireFormatter.Instance")]
    public static partial RespOperation<bool> PExpire(this in KeyCommands context, RedisKey key, long milliseconds, ExpireWhen when = ExpireWhen.Always);

    [RespCommand(Formatter = "ExpireFormatter.Instance")]
    public static partial RespOperation<bool> ExpireAt(this in KeyCommands context, RedisKey key, long seconds, ExpireWhen when = ExpireWhen.Always);

    [RespCommand(Formatter = "ExpireFormatter.Instance")]
    public static partial RespOperation<bool> PExpireAt(this in KeyCommands context, RedisKey key, long milliseconds, ExpireWhen when = ExpireWhen.Always);

    private sealed class ExpireFormatter : IRespFormatter<(RedisKey Key, long Value, ExpireWhen When)>
    {
        public static readonly ExpireFormatter Instance = new();
        private ExpireFormatter() { }

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (RedisKey Key, long Value, ExpireWhen When) request)
        {
            writer.WriteCommand(command, request.When == ExpireWhen.Always ? 2 : 3);
            writer.Write(request.Key);
            writer.Write(request.Value);
            switch (request.When)
            {
                case ExpireWhen.Always:
                    break;
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
                    static void Throw() => throw new ArgumentOutOfRangeException(nameof(request.When));
                    break;
            }
        }
    }
}
