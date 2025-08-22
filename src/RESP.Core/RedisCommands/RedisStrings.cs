#pragma warning disable SA1403 // multiple namespaces for demo only

namespace Resp.RedisCommands.Strings
{
    public static partial class StringExtensions
    {
        [RespCommand("get")]
        public static partial string? StringGet(this in RespContext context, string key);
    }
}

namespace Resp.RedisCommands
{
    public readonly partial struct RedisDatabase(in RespContext context)
    {
        private readonly RespContext _context = context;

        [RespCommand]
        public partial void FlushDb();
    }

    public static partial class RespConnectionExtensions
    {
        public static RedisStrings Strings(this in RespContext context) => new(context);
        public static RedisStrings Strings(this IRespConnection connection) => new RespContext(connection).Strings();
    }


    public readonly partial struct RedisStrings(in RespContext context)
    {
        private readonly RespContext _context = context;

        [RespCommand]
        public partial string? Get(string key);

        [RespCommand]
        public partial void Set(string key, string value);

        [RespCommand]
        public partial void Set(string key, int value);

        [RespCommand]
        public partial int Incr(string key);

        [RespCommand]
        public partial int IncrBy(string key, int value);

        [RespCommand]
        public partial int Decr(string key);

        [RespCommand]
        public partial int DecrBy(string key, int value);
    }
}
