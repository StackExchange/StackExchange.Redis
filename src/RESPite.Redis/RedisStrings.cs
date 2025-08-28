using System;
using System.Threading.Tasks;
using Resp;

namespace RESPite.Redis;

// note that members may also be added as extensions if necessary
public partial struct RedisStrings(in RespContext context)
{
    private readonly RespContext _context = context;

    // re-expose del
    public void Del(string key) => _context.Keys.Del(key);
    public ValueTask DelAsync(string key) => _context.Keys.DelAsync(key);

    [RespCommand]
    public partial int Append(string key, string value);

    [RespCommand]
    public partial int Append(string key, ReadOnlyMemory<byte> value);

    [RespCommand]
    public partial int Decr(string key);

    [RespCommand]
    public partial int DecrBy(string key, int value);

    [RespCommand]
    public partial string Get(string key);

    [RespCommand("get")]
    public partial int GetInt32(string key);

    [RespCommand("get")]
    public partial double GetDouble(string key);

    [RespCommand]
    public partial string GetDel(string key);

    [RespCommand(Formatter = ExpiryTimeSpanFormatter.Formatter)]
    public partial string GetEx(string key, TimeSpan expiry);

    private sealed class ExpiryTimeSpanFormatter : IRespFormatter<(string Key, TimeSpan Expiry)>
    {
        public const string Formatter = $"{nameof(ExpiryTimeSpanFormatter)}.{nameof(Instance)}";

        public static readonly ExpiryTimeSpanFormatter Instance = new ExpiryTimeSpanFormatter();

        public void Format(
            scoped ReadOnlySpan<byte> command,
            ref RespWriter writer,
            in (string Key, TimeSpan Expiry) request)
        {
            writer.WriteCommand(command, 3);
            writer.WriteKey(request.Key);
            writer.WriteBulkString("PX"u8);
            writer.WriteBulkString((long)request.Expiry.TotalMilliseconds);
        }
    }

    [RespCommand]
    public partial string GetRange(string key, int start, int end);

    [RespCommand]
    public partial string GetSet(string key, string value);

    [RespCommand]
    public partial string GetSet(string key, ReadOnlyMemory<byte> value);

    [RespCommand]
    public partial int Incr(string key);

    [RespCommand]
    public partial int IncrBy(string key, int value);

    [RespCommand]
    public partial double IncrByFloat(string key, double value);
}
