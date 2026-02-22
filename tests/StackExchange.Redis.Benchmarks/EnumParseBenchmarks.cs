using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using RESPite;

namespace StackExchange.Redis.Benchmarks;

[ShortRunJob, MemoryDiagnoser, GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
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

    private byte[] _bytes = [];
    private string _value = "";

    [ParamsSource(nameof(Values))]
    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            _bytes = Encoding.UTF8.GetBytes(value);
            _value = value;
        }
    }

    [BenchmarkCategory("Case sensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Baseline = true)]
    public RedisCommand EnumParse_CS()
    {
        var value = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            Enum.TryParse(value, false, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case insensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke,  Baseline = true)]
    public RedisCommand EnumParse_CI()
    {
        var value = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            Enum.TryParse(value, true, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case sensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand AsciiHash_CS()
    {
        ReadOnlySpan<char> value = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParse_CS(value, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case insensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand AsciiHash_CI()
    {
        ReadOnlySpan<char> value = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParse_CI(value, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case sensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand Bytes_CS()
    {
        ReadOnlySpan<byte> value = _bytes;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParse_CS(value, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case insensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand Bytes_CI()
    {
        ReadOnlySpan<byte> value = _bytes;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParse_CI(value, out r);
        }

        return r;
    }

    [BenchmarkCategory("Case sensitive")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public RedisCommand Switch_CS()
    {
        var value = Value;
        RedisCommand r = default;
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            TryParseSwitch(value, out r);
        }

        return r;
    }

    private static bool TryParseSwitch(string s, out RedisCommand r)
    {
        r = s switch
        {
            "NONE" => RedisCommand.NONE,
            "APPEND" => RedisCommand.APPEND,
            "ASKING" => RedisCommand.ASKING,
            "AUTH" => RedisCommand.AUTH,
            "BGREWRITEAOF" => RedisCommand.BGREWRITEAOF,
            "BGSAVE" => RedisCommand.BGSAVE,
            "BITCOUNT" => RedisCommand.BITCOUNT,
            "BITOP" => RedisCommand.BITOP,
            "BITPOS" => RedisCommand.BITPOS,
            "BLPOP" => RedisCommand.BLPOP,
            "BRPOP" => RedisCommand.BRPOP,
            "BRPOPLPUSH" => RedisCommand.BRPOPLPUSH,
            "CLIENT" => RedisCommand.CLIENT,
            "CLUSTER" => RedisCommand.CLUSTER,
            "CONFIG" => RedisCommand.CONFIG,
            "COPY" => RedisCommand.COPY,
            "COMMAND" => RedisCommand.COMMAND,
            "DBSIZE" => RedisCommand.DBSIZE,
            "DEBUG" => RedisCommand.DEBUG,
            "DECR" => RedisCommand.DECR,
            "DECRBY" => RedisCommand.DECRBY,
            "DEL" => RedisCommand.DEL,
            "DELEX" => RedisCommand.DELEX,
            "DIGEST" => RedisCommand.DIGEST,
            "DISCARD" => RedisCommand.DISCARD,
            "DUMP" => RedisCommand.DUMP,
            "ECHO" => RedisCommand.ECHO,
            "EVAL" => RedisCommand.EVAL,
            "EVALSHA" => RedisCommand.EVALSHA,
            "EVAL_RO" => RedisCommand.EVAL_RO,
            "EVALSHA_RO" => RedisCommand.EVALSHA_RO,
            "EXEC" => RedisCommand.EXEC,
            "EXISTS" => RedisCommand.EXISTS,
            "EXPIRE" => RedisCommand.EXPIRE,
            "EXPIREAT" => RedisCommand.EXPIREAT,
            "EXPIRETIME" => RedisCommand.EXPIRETIME,
            "FLUSHALL" => RedisCommand.FLUSHALL,
            "FLUSHDB" => RedisCommand.FLUSHDB,
            "GEOADD" => RedisCommand.GEOADD,
            "GEODIST" => RedisCommand.GEODIST,
            "GEOHASH" => RedisCommand.GEOHASH,
            "GEOPOS" => RedisCommand.GEOPOS,
            "GEORADIUS" => RedisCommand.GEORADIUS,
            "GEORADIUSBYMEMBER" => RedisCommand.GEORADIUSBYMEMBER,
            "GEOSEARCH" => RedisCommand.GEOSEARCH,
            "GEOSEARCHSTORE" => RedisCommand.GEOSEARCHSTORE,
            "GET" => RedisCommand.GET,
            "GETBIT" => RedisCommand.GETBIT,
            "GETDEL" => RedisCommand.GETDEL,
            "GETEX" => RedisCommand.GETEX,
            "GETRANGE" => RedisCommand.GETRANGE,
            "GETSET" => RedisCommand.GETSET,
            "HDEL" => RedisCommand.HDEL,
            "HELLO" => RedisCommand.HELLO,
            "HEXISTS" => RedisCommand.HEXISTS,
            "HEXPIRE" => RedisCommand.HEXPIRE,
            "HEXPIREAT" => RedisCommand.HEXPIREAT,
            "HEXPIRETIME" => RedisCommand.HEXPIRETIME,
            "HGET" => RedisCommand.HGET,
            "HGETEX" => RedisCommand.HGETEX,
            "HGETDEL" => RedisCommand.HGETDEL,
            "HGETALL" => RedisCommand.HGETALL,
            "HINCRBY" => RedisCommand.HINCRBY,
            "HINCRBYFLOAT" => RedisCommand.HINCRBYFLOAT,
            "HKEYS" => RedisCommand.HKEYS,
            "HLEN" => RedisCommand.HLEN,
            "HMGET" => RedisCommand.HMGET,
            "HMSET" => RedisCommand.HMSET,
            "HOTKEYS" => RedisCommand.HOTKEYS,
            "HPERSIST" => RedisCommand.HPERSIST,
            "HPEXPIRE" => RedisCommand.HPEXPIRE,
            "HPEXPIREAT" => RedisCommand.HPEXPIREAT,
            "HPEXPIRETIME" => RedisCommand.HPEXPIRETIME,
            "HPTTL" => RedisCommand.HPTTL,
            "HRANDFIELD" => RedisCommand.HRANDFIELD,
            "HSCAN" => RedisCommand.HSCAN,
            "HSET" => RedisCommand.HSET,
            "HSETEX" => RedisCommand.HSETEX,
            "HSETNX" => RedisCommand.HSETNX,
            "HSTRLEN" => RedisCommand.HSTRLEN,
            "HVALS" => RedisCommand.HVALS,
            "INCR" => RedisCommand.INCR,
            "INCRBY" => RedisCommand.INCRBY,
            "INCRBYFLOAT" => RedisCommand.INCRBYFLOAT,
            "INFO" => RedisCommand.INFO,
            "KEYS" => RedisCommand.KEYS,
            "LASTSAVE" => RedisCommand.LASTSAVE,
            "LATENCY" => RedisCommand.LATENCY,
            "LCS" => RedisCommand.LCS,
            "LINDEX" => RedisCommand.LINDEX,
            "LINSERT" => RedisCommand.LINSERT,
            "LLEN" => RedisCommand.LLEN,
            "LMOVE" => RedisCommand.LMOVE,
            "LMPOP" => RedisCommand.LMPOP,
            "LPOP" => RedisCommand.LPOP,
            "LPOS" => RedisCommand.LPOS,
            "LPUSH" => RedisCommand.LPUSH,
            "LPUSHX" => RedisCommand.LPUSHX,
            "LRANGE" => RedisCommand.LRANGE,
            "LREM" => RedisCommand.LREM,
            "LSET" => RedisCommand.LSET,
            "LTRIM" => RedisCommand.LTRIM,
            "MEMORY" => RedisCommand.MEMORY,
            "MGET" => RedisCommand.MGET,
            "MIGRATE" => RedisCommand.MIGRATE,
            "MONITOR" => RedisCommand.MONITOR,
            "MOVE" => RedisCommand.MOVE,
            "MSET" => RedisCommand.MSET,
            "MSETEX" => RedisCommand.MSETEX,
            "MSETNX" => RedisCommand.MSETNX,
            "MULTI" => RedisCommand.MULTI,
            "OBJECT" => RedisCommand.OBJECT,
            "PERSIST" => RedisCommand.PERSIST,
            "PEXPIRE" => RedisCommand.PEXPIRE,
            "PEXPIREAT" => RedisCommand.PEXPIREAT,
            "PEXPIRETIME" => RedisCommand.PEXPIRETIME,
            "PFADD" => RedisCommand.PFADD,
            "PFCOUNT" => RedisCommand.PFCOUNT,
            "PFMERGE" => RedisCommand.PFMERGE,
            "PING" => RedisCommand.PING,
            "PSETEX" => RedisCommand.PSETEX,
            "PSUBSCRIBE" => RedisCommand.PSUBSCRIBE,
            "PTTL" => RedisCommand.PTTL,
            "PUBLISH" => RedisCommand.PUBLISH,
            "PUBSUB" => RedisCommand.PUBSUB,
            "PUNSUBSCRIBE" => RedisCommand.PUNSUBSCRIBE,
            "QUIT" => RedisCommand.QUIT,
            "RANDOMKEY" => RedisCommand.RANDOMKEY,
            "READONLY" => RedisCommand.READONLY,
            "READWRITE" => RedisCommand.READWRITE,
            "RENAME" => RedisCommand.RENAME,
            "RENAMENX" => RedisCommand.RENAMENX,
            "REPLICAOF" => RedisCommand.REPLICAOF,
            "RESTORE" => RedisCommand.RESTORE,
            "ROLE" => RedisCommand.ROLE,
            "RPOP" => RedisCommand.RPOP,
            "RPOPLPUSH" => RedisCommand.RPOPLPUSH,
            "RPUSH" => RedisCommand.RPUSH,
            "RPUSHX" => RedisCommand.RPUSHX,
            "SADD" => RedisCommand.SADD,
            "SAVE" => RedisCommand.SAVE,
            "SCAN" => RedisCommand.SCAN,
            "SCARD" => RedisCommand.SCARD,
            "SCRIPT" => RedisCommand.SCRIPT,
            "SDIFF" => RedisCommand.SDIFF,
            "SDIFFSTORE" => RedisCommand.SDIFFSTORE,
            "SELECT" => RedisCommand.SELECT,
            "SENTINEL" => RedisCommand.SENTINEL,
            "SET" => RedisCommand.SET,
            "SETBIT" => RedisCommand.SETBIT,
            "SETEX" => RedisCommand.SETEX,
            "SETNX" => RedisCommand.SETNX,
            "SETRANGE" => RedisCommand.SETRANGE,
            "SHUTDOWN" => RedisCommand.SHUTDOWN,
            "SINTER" => RedisCommand.SINTER,
            "SINTERCARD" => RedisCommand.SINTERCARD,
            "SINTERSTORE" => RedisCommand.SINTERSTORE,
            "SISMEMBER" => RedisCommand.SISMEMBER,
            "SLAVEOF" => RedisCommand.SLAVEOF,
            "SLOWLOG" => RedisCommand.SLOWLOG,
            "SMEMBERS" => RedisCommand.SMEMBERS,
            "SMISMEMBER" => RedisCommand.SMISMEMBER,
            "SMOVE" => RedisCommand.SMOVE,
            "SORT" => RedisCommand.SORT,
            "SORT_RO" => RedisCommand.SORT_RO,
            "SPOP" => RedisCommand.SPOP,
            "SPUBLISH" => RedisCommand.SPUBLISH,
            "SRANDMEMBER" => RedisCommand.SRANDMEMBER,
            "SREM" => RedisCommand.SREM,
            "STRLEN" => RedisCommand.STRLEN,
            "SUBSCRIBE" => RedisCommand.SUBSCRIBE,
            "SUNION" => RedisCommand.SUNION,
            "SUNIONSTORE" => RedisCommand.SUNIONSTORE,
            "SSCAN" => RedisCommand.SSCAN,
            "SSUBSCRIBE" => RedisCommand.SSUBSCRIBE,
            "SUNSUBSCRIBE" => RedisCommand.SUNSUBSCRIBE,
            "SWAPDB" => RedisCommand.SWAPDB,
            "SYNC" => RedisCommand.SYNC,
            "TIME" => RedisCommand.TIME,
            "TOUCH" => RedisCommand.TOUCH,
            "TTL" => RedisCommand.TTL,
            "TYPE" => RedisCommand.TYPE,
            "UNLINK" => RedisCommand.UNLINK,
            "UNSUBSCRIBE" => RedisCommand.UNSUBSCRIBE,
            "UNWATCH" => RedisCommand.UNWATCH,
            "VADD" => RedisCommand.VADD,
            "VCARD" => RedisCommand.VCARD,
            "VDIM" => RedisCommand.VDIM,
            "VEMB" => RedisCommand.VEMB,
            "VGETATTR" => RedisCommand.VGETATTR,
            "VINFO" => RedisCommand.VINFO,
            "VISMEMBER" => RedisCommand.VISMEMBER,
            "VLINKS" => RedisCommand.VLINKS,
            "VRANDMEMBER" => RedisCommand.VRANDMEMBER,
            "VREM" => RedisCommand.VREM,
            "VSETATTR" => RedisCommand.VSETATTR,
            "VSIM" => RedisCommand.VSIM,
            "WATCH" => RedisCommand.WATCH,
            "XACK" => RedisCommand.XACK,
            "XACKDEL" => RedisCommand.XACKDEL,
            "XADD" => RedisCommand.XADD,
            "XAUTOCLAIM" => RedisCommand.XAUTOCLAIM,
            "XCLAIM" => RedisCommand.XCLAIM,
            "XCFGSET" => RedisCommand.XCFGSET,
            "XDEL" => RedisCommand.XDEL,
            "XDELEX" => RedisCommand.XDELEX,
            "XGROUP" => RedisCommand.XGROUP,
            "XINFO" => RedisCommand.XINFO,
            "XLEN" => RedisCommand.XLEN,
            "XPENDING" => RedisCommand.XPENDING,
            "XRANGE" => RedisCommand.XRANGE,
            "XREAD" => RedisCommand.XREAD,
            "XREADGROUP" => RedisCommand.XREADGROUP,
            "XREVRANGE" => RedisCommand.XREVRANGE,
            "XTRIM" => RedisCommand.XTRIM,
            "ZADD" => RedisCommand.ZADD,
            "ZCARD" => RedisCommand.ZCARD,
            "ZCOUNT" => RedisCommand.ZCOUNT,
            "ZDIFF" => RedisCommand.ZDIFF,
            "ZDIFFSTORE" => RedisCommand.ZDIFFSTORE,
            "ZINCRBY" => RedisCommand.ZINCRBY,
            "ZINTER" => RedisCommand.ZINTER,
            "ZINTERCARD" => RedisCommand.ZINTERCARD,
            "ZINTERSTORE" => RedisCommand.ZINTERSTORE,
            "ZLEXCOUNT" => RedisCommand.ZLEXCOUNT,
            "ZMPOP" => RedisCommand.ZMPOP,
            "ZMSCORE" => RedisCommand.ZMSCORE,
            "ZPOPMAX" => RedisCommand.ZPOPMAX,
            "ZPOPMIN" => RedisCommand.ZPOPMIN,
            "ZRANDMEMBER" => RedisCommand.ZRANDMEMBER,
            "ZRANGE" => RedisCommand.ZRANGE,
            "ZRANGEBYLEX" => RedisCommand.ZRANGEBYLEX,
            "ZRANGEBYSCORE" => RedisCommand.ZRANGEBYSCORE,
            "ZRANGESTORE" => RedisCommand.ZRANGESTORE,
            "ZRANK" => RedisCommand.ZRANK,
            "ZREM" => RedisCommand.ZREM,
            "ZREMRANGEBYLEX" => RedisCommand.ZREMRANGEBYLEX,
            "ZREMRANGEBYRANK" => RedisCommand.ZREMRANGEBYRANK,
            "ZREMRANGEBYSCORE" => RedisCommand.ZREMRANGEBYSCORE,
            "ZREVRANGE" => RedisCommand.ZREVRANGE,
            "ZREVRANGEBYLEX" => RedisCommand.ZREVRANGEBYLEX,
            "ZREVRANGEBYSCORE" => RedisCommand.ZREVRANGEBYSCORE,
            "ZREVRANK" => RedisCommand.ZREVRANK,
            "ZSCAN" => RedisCommand.ZSCAN,
            "ZSCORE" => RedisCommand.ZSCORE,
            "ZUNION" => RedisCommand.ZUNION,
            "ZUNIONSTORE" => RedisCommand.ZUNIONSTORE,
            "UNKNOWN" => RedisCommand.UNKNOWN,
            _ => (RedisCommand)(-1),
        };
        return r != (RedisCommand)(-1);
    }

    [AsciiHash]
    internal static partial bool TryParse_CS(ReadOnlySpan<char> value, out RedisCommand command);

    [AsciiHash]
    internal static partial bool TryParse_CS(ReadOnlySpan<byte> value, out RedisCommand command);

    [AsciiHash(CaseSensitive = false)]
    internal static partial bool TryParse_CI(ReadOnlySpan<char> value, out RedisCommand command);

    [AsciiHash(CaseSensitive = false)]
    internal static partial bool TryParse_CI(ReadOnlySpan<byte> value, out RedisCommand command);

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
