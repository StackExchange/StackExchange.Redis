using System;
using BenchmarkDotNet.Attributes;
using RESPite;

namespace StackExchange.Redis.Benchmarks;

[ShortRunJob, MemoryDiagnoser]
public partial class EnumParseBenchmarks
{
    private const int OperationsPerInvoke = 1000;

    public string[] Values() =>
    [
        nameof(RedisCommand.GET),
        nameof(RedisCommand.EXPIREAT),
        nameof(RedisCommand.ZREMRANGEBYSCORE),
        "~~~~",
        "get",
        "expireat",
        "zremrangebyscore"
    ];
    [ParamsSource(nameof(Values))]
    public string Value { get; set; } = "";

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand EnumParse_CS()
    {
        var s = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            Enum.TryParse(s, out r);
        }
        return r;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand FastHash_CS()
    {
        var s = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParse_CS(s, out r);
        }
        return r;
    }

    [FastHash]
    internal static partial bool TryParse_CS(string s, out RedisCommand r);

    public enum RedisCommand
    {
        NONE, // must be first for "zero reasons"

        APPEND,
        ASKING,
        AUTH,

        BGREWRITEAOF,
        BGSAVE,
        BITCOUNT,
        BITOP,
        BITPOS,
        BLPOP,
        BRPOP,
        BRPOPLPUSH,

        CLIENT,
        CLUSTER,
        CONFIG,
        COPY,
        COMMAND,

        DBSIZE,
        DEBUG,
        DECR,
        DECRBY,
        DEL,
        DELEX,
        DIGEST,
        DISCARD,
        DUMP,

        ECHO,
        EVAL,
        EVALSHA,
        EVAL_RO,
        EVALSHA_RO,
        EXEC,
        EXISTS,
        EXPIRE,
        EXPIREAT,
        EXPIRETIME,

        FLUSHALL,
        FLUSHDB,

        GEOADD,
        GEODIST,
        GEOHASH,
        GEOPOS,
        GEORADIUS,
        GEORADIUSBYMEMBER,
        GEOSEARCH,
        GEOSEARCHSTORE,

        GET,
        GETBIT,
        GETDEL,
        GETEX,
        GETRANGE,
        GETSET,

        HDEL,
        HELLO,
        HEXISTS,
        HEXPIRE,
        HEXPIREAT,
        HEXPIRETIME,
        HGET,
        HGETEX,
        HGETDEL,
        HGETALL,
        HINCRBY,
        HINCRBYFLOAT,
        HKEYS,
        HLEN,
        HMGET,
        HMSET,
        HOTKEYS,
        HPERSIST,
        HPEXPIRE,
        HPEXPIREAT,
        HPEXPIRETIME,
        HPTTL,
        HRANDFIELD,
        HSCAN,
        HSET,
        HSETEX,
        HSETNX,
        HSTRLEN,
        HVALS,

        INCR,
        INCRBY,
        INCRBYFLOAT,
        INFO,

        KEYS,

        LASTSAVE,
        LATENCY,
        LCS,
        LINDEX,
        LINSERT,
        LLEN,
        LMOVE,
        LMPOP,
        LPOP,
        LPOS,
        LPUSH,
        LPUSHX,
        LRANGE,
        LREM,
        LSET,
        LTRIM,

        MEMORY,
        MGET,
        MIGRATE,
        MONITOR,
        MOVE,
        MSET,
        MSETEX,
        MSETNX,
        MULTI,

        OBJECT,

        PERSIST,
        PEXPIRE,
        PEXPIREAT,
        PEXPIRETIME,
        PFADD,
        PFCOUNT,
        PFMERGE,
        PING,
        PSETEX,
        PSUBSCRIBE,
        PTTL,
        PUBLISH,
        PUBSUB,
        PUNSUBSCRIBE,

        QUIT,

        RANDOMKEY,
        READONLY,
        READWRITE,
        RENAME,
        RENAMENX,
        REPLICAOF,
        RESTORE,
        ROLE,
        RPOP,
        RPOPLPUSH,
        RPUSH,
        RPUSHX,

        SADD,
        SAVE,
        SCAN,
        SCARD,
        SCRIPT,
        SDIFF,
        SDIFFSTORE,
        SELECT,
        SENTINEL,
        SET,
        SETBIT,
        SETEX,
        SETNX,
        SETRANGE,
        SHUTDOWN,
        SINTER,
        SINTERCARD,
        SINTERSTORE,
        SISMEMBER,
        SLAVEOF,
        SLOWLOG,
        SMEMBERS,
        SMISMEMBER,
        SMOVE,
        SORT,
        SORT_RO,
        SPOP,
        SPUBLISH,
        SRANDMEMBER,
        SREM,
        STRLEN,
        SUBSCRIBE,
        SUNION,
        SUNIONSTORE,
        SSCAN,
        SSUBSCRIBE,
        SUNSUBSCRIBE,
        SWAPDB,
        SYNC,

        TIME,
        TOUCH,
        TTL,
        TYPE,

        UNLINK,
        UNSUBSCRIBE,
        UNWATCH,

        VADD,
        VCARD,
        VDIM,
        VEMB,
        VGETATTR,
        VINFO,
        VISMEMBER,
        VLINKS,
        VRANDMEMBER,
        VREM,
        VSETATTR,
        VSIM,

        WATCH,

        XACK,
        XACKDEL,
        XADD,
        XAUTOCLAIM,
        XCLAIM,
        XCFGSET,
        XDEL,
        XDELEX,
        XGROUP,
        XINFO,
        XLEN,
        XPENDING,
        XRANGE,
        XREAD,
        XREADGROUP,
        XREVRANGE,
        XTRIM,

        ZADD,
        ZCARD,
        ZCOUNT,
        ZDIFF,
        ZDIFFSTORE,
        ZINCRBY,
        ZINTER,
        ZINTERCARD,
        ZINTERSTORE,
        ZLEXCOUNT,
        ZMPOP,
        ZMSCORE,
        ZPOPMAX,
        ZPOPMIN,
        ZRANDMEMBER,
        ZRANGE,
        ZRANGEBYLEX,
        ZRANGEBYSCORE,
        ZRANGESTORE,
        ZRANK,
        ZREM,
        ZREMRANGEBYLEX,
        ZREMRANGEBYRANK,
        ZREMRANGEBYSCORE,
        ZREVRANGE,
        ZREVRANGEBYLEX,
        ZREVRANGEBYSCORE,
        ZREVRANK,
        ZSCAN,
        ZSCORE,
        ZUNION,
        ZUNIONSTORE,

        UNKNOWN,
    }
}
