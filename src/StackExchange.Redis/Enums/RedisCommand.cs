using System;

namespace StackExchange.Redis;

internal enum RedisCommand
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
    DISCARD,
    DUMP,

    ECHO,
    EVAL,
    EVALSHA,
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
    HEXISTS,
    HGET,
    HGETALL,
    HINCRBY,
    HINCRBYFLOAT,
    HKEYS,
    HLEN,
    HMGET,
    HMSET,
    HRANDFIELD,
    HSCAN,
    HSET,
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
    SRANDMEMBER,
    SREM,
    STRLEN,
    SUBSCRIBE,
    SUNION,
    SUNIONSTORE,
    SSCAN,
    SWAPDB,
    SYNC,

    TIME,
    TOUCH,
    TTL,
    TYPE,

    UNLINK,
    UNSUBSCRIBE,
    UNWATCH,

    WATCH,

    XACK,
    XADD,
    XAUTOCLAIM,
    XCLAIM,
    XDEL,
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

internal static class RedisCommandExtensions
{
    /// <summary>
    /// Gets whether a given command can be issued only to a primary, or if any server is eligible.
    /// </summary>
    /// <param name="command">The <see cref="RedisCommand"/> to check.</param>
    /// <returns><see langword="true"/> if the command is primary-only, <see langword="false"/> otherwise.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "No, it'd be ridiculous.")]
    internal static bool IsPrimaryOnly(this RedisCommand command)
    {
        switch (command)
        {
            // Commands that can only be issued to a primary (writable) server
            // If a command *may* be writable (e.g. an EVAL script), it should *not* be primary-only
            //   because that'd block a legitimate use case of a read-only script on replica servers,
            //   for example spreading load via a .DemandReplica flag in the caller.
            // Basically: would it fail on a read-only replica in 100% of cases? Then it goes in the list.
            case RedisCommand.APPEND:
            case RedisCommand.BITOP:
            case RedisCommand.BLPOP:
            case RedisCommand.BRPOP:
            case RedisCommand.BRPOPLPUSH:
            case RedisCommand.COPY:
            case RedisCommand.DECR:
            case RedisCommand.DECRBY:
            case RedisCommand.DEL:
            case RedisCommand.EXPIRE:
            case RedisCommand.EXPIREAT:
            case RedisCommand.EXPIRETIME:
            case RedisCommand.FLUSHALL:
            case RedisCommand.FLUSHDB:
            case RedisCommand.GEOADD:
            case RedisCommand.GEOSEARCHSTORE:
            case RedisCommand.GETDEL:
            case RedisCommand.GETEX:
            case RedisCommand.GETSET:
            case RedisCommand.HDEL:
            case RedisCommand.HINCRBY:
            case RedisCommand.HINCRBYFLOAT:
            case RedisCommand.HMSET:
            case RedisCommand.HSET:
            case RedisCommand.HSETNX:
            case RedisCommand.INCR:
            case RedisCommand.INCRBY:
            case RedisCommand.INCRBYFLOAT:
            case RedisCommand.LINSERT:
            case RedisCommand.LMOVE:
            case RedisCommand.LMPOP:
            case RedisCommand.LPOP:
            case RedisCommand.LPUSH:
            case RedisCommand.LPUSHX:
            case RedisCommand.LREM:
            case RedisCommand.LSET:
            case RedisCommand.LTRIM:
            case RedisCommand.MIGRATE:
            case RedisCommand.MOVE:
            case RedisCommand.MSET:
            case RedisCommand.MSETNX:
            case RedisCommand.PERSIST:
            case RedisCommand.PEXPIRE:
            case RedisCommand.PEXPIREAT:
            case RedisCommand.PEXPIRETIME:
            case RedisCommand.PFADD:
            case RedisCommand.PFMERGE:
            case RedisCommand.PSETEX:
            case RedisCommand.RENAME:
            case RedisCommand.RENAMENX:
            case RedisCommand.RESTORE:
            case RedisCommand.RPOP:
            case RedisCommand.RPOPLPUSH:
            case RedisCommand.RPUSH:
            case RedisCommand.RPUSHX:
            case RedisCommand.SADD:
            case RedisCommand.SDIFFSTORE:
            case RedisCommand.SET:
            case RedisCommand.SETBIT:
            case RedisCommand.SETEX:
            case RedisCommand.SETNX:
            case RedisCommand.SETRANGE:
            case RedisCommand.SINTERSTORE:
            case RedisCommand.SMOVE:
            case RedisCommand.SORT:
            case RedisCommand.SPOP:
            case RedisCommand.SREM:
            case RedisCommand.SUNIONSTORE:
            case RedisCommand.SWAPDB:
            case RedisCommand.TOUCH:
            case RedisCommand.UNLINK:
            case RedisCommand.XACK:
            case RedisCommand.XADD:
            case RedisCommand.XAUTOCLAIM:
            case RedisCommand.XCLAIM:
            case RedisCommand.XDEL:
            case RedisCommand.XGROUP:
            case RedisCommand.XREADGROUP:
            case RedisCommand.XTRIM:
            case RedisCommand.ZADD:
            case RedisCommand.ZDIFFSTORE:
            case RedisCommand.ZINTERSTORE:
            case RedisCommand.ZINCRBY:
            case RedisCommand.ZMPOP:
            case RedisCommand.ZPOPMAX:
            case RedisCommand.ZPOPMIN:
            case RedisCommand.ZRANGESTORE:
            case RedisCommand.ZREM:
            case RedisCommand.ZREMRANGEBYLEX:
            case RedisCommand.ZREMRANGEBYRANK:
            case RedisCommand.ZREMRANGEBYSCORE:
            case RedisCommand.ZUNIONSTORE:
                return true;
            // Commands that can be issued anywhere
            case RedisCommand.NONE:
            case RedisCommand.ASKING:
            case RedisCommand.AUTH:
            case RedisCommand.BGREWRITEAOF:
            case RedisCommand.BGSAVE:
            case RedisCommand.BITCOUNT:
            case RedisCommand.BITPOS:
            case RedisCommand.CLIENT:
            case RedisCommand.CLUSTER:
            case RedisCommand.COMMAND:
            case RedisCommand.CONFIG:
            case RedisCommand.DBSIZE:
            case RedisCommand.DEBUG:
            case RedisCommand.DISCARD:
            case RedisCommand.DUMP:
            case RedisCommand.ECHO:
            case RedisCommand.EVAL:
            case RedisCommand.EVALSHA:
            case RedisCommand.EXEC:
            case RedisCommand.EXISTS:
            case RedisCommand.GEODIST:
            case RedisCommand.GEOHASH:
            case RedisCommand.GEOPOS:
            case RedisCommand.GEORADIUS:
            case RedisCommand.GEORADIUSBYMEMBER:
            case RedisCommand.GEOSEARCH:
            case RedisCommand.GET:
            case RedisCommand.GETBIT:
            case RedisCommand.GETRANGE:
            case RedisCommand.HEXISTS:
            case RedisCommand.HGET:
            case RedisCommand.HGETALL:
            case RedisCommand.HKEYS:
            case RedisCommand.HLEN:
            case RedisCommand.HMGET:
            case RedisCommand.HRANDFIELD:
            case RedisCommand.HSCAN:
            case RedisCommand.HSTRLEN:
            case RedisCommand.HVALS:
            case RedisCommand.INFO:
            case RedisCommand.KEYS:
            case RedisCommand.LASTSAVE:
            case RedisCommand.LATENCY:
            case RedisCommand.LCS:
            case RedisCommand.LINDEX:
            case RedisCommand.LLEN:
            case RedisCommand.LPOS:
            case RedisCommand.LRANGE:
            case RedisCommand.MEMORY:
            case RedisCommand.MGET:
            case RedisCommand.MONITOR:
            case RedisCommand.MULTI:
            case RedisCommand.OBJECT:
            case RedisCommand.PFCOUNT:
            case RedisCommand.PING:
            case RedisCommand.PSUBSCRIBE:
            case RedisCommand.PTTL:
            case RedisCommand.PUBLISH:
            case RedisCommand.PUBSUB:
            case RedisCommand.PUNSUBSCRIBE:
            case RedisCommand.QUIT:
            case RedisCommand.RANDOMKEY:
            case RedisCommand.READONLY:
            case RedisCommand.READWRITE:
            case RedisCommand.REPLICAOF:
            case RedisCommand.ROLE:
            case RedisCommand.SAVE:
            case RedisCommand.SCAN:
            case RedisCommand.SCARD:
            case RedisCommand.SCRIPT:
            case RedisCommand.SDIFF:
            case RedisCommand.SELECT:
            case RedisCommand.SENTINEL:
            case RedisCommand.SHUTDOWN:
            case RedisCommand.SINTER:
            case RedisCommand.SINTERCARD:
            case RedisCommand.SISMEMBER:
            case RedisCommand.SLAVEOF:
            case RedisCommand.SLOWLOG:
            case RedisCommand.SMEMBERS:
            case RedisCommand.SMISMEMBER:
            case RedisCommand.SORT_RO:
            case RedisCommand.SRANDMEMBER:
            case RedisCommand.STRLEN:
            case RedisCommand.SUBSCRIBE:
            case RedisCommand.SUNION:
            case RedisCommand.SSCAN:
            case RedisCommand.SYNC:
            case RedisCommand.TIME:
            case RedisCommand.TTL:
            case RedisCommand.TYPE:
            case RedisCommand.UNSUBSCRIBE:
            case RedisCommand.UNWATCH:
            case RedisCommand.WATCH:
            // Stream commands verified working on replicas
            case RedisCommand.XINFO:
            case RedisCommand.XLEN:
            case RedisCommand.XPENDING:
            case RedisCommand.XRANGE:
            case RedisCommand.XREAD:
            case RedisCommand.XREVRANGE:
            case RedisCommand.ZCARD:
            case RedisCommand.ZCOUNT:
            case RedisCommand.ZDIFF:
            case RedisCommand.ZINTER:
            case RedisCommand.ZINTERCARD:
            case RedisCommand.ZLEXCOUNT:
            case RedisCommand.ZMSCORE:
            case RedisCommand.ZRANDMEMBER:
            case RedisCommand.ZRANGE:
            case RedisCommand.ZRANGEBYLEX:
            case RedisCommand.ZRANGEBYSCORE:
            case RedisCommand.ZRANK:
            case RedisCommand.ZREVRANGE:
            case RedisCommand.ZREVRANGEBYLEX:
            case RedisCommand.ZREVRANGEBYSCORE:
            case RedisCommand.ZREVRANK:
            case RedisCommand.ZSCAN:
            case RedisCommand.ZSCORE:
            case RedisCommand.ZUNION:
            case RedisCommand.UNKNOWN:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), $"Every RedisCommand must be defined in Message.IsPrimaryOnly, unknown command '{command}' encountered.");
        }
    }
}
