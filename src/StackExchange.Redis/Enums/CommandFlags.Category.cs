namespace StackExchange.Redis;

internal static class CommandFlagsExtensions
{
    public static CommandFlags WithCategory(this CommandFlags flags, CommandFlags category)
        // if the user hasn't already specified a category: use the category supplied
        => ((flags & Message.MaskRetryCategory) is 0) ? flags | (category & Message.MaskRetryCategory) : flags;

    public static CommandFlags WithDefaultCategory(this CommandFlags flags, RedisCommand command)
    {
        if ((flags & Message.MaskRetryCategory) is 0)
        {
            // Get the suggested flags; note that the user might have included CommandServerSpecific,
            // but we *also* suggest that below - we'll live with it, additively.
            // Note also that some commands may have *conditionally* included their category based on
            // rules specific to the parameters, for example SCAN 0 is not server specific,
            // but SCAN 12341234 *is*.
            flags |= DefaultCategory(command);
        }

        return flags;

        static CommandFlags DefaultCategory(RedisCommand command)
        {
            // This is *not* using switch expressions very deliberately, because there are a *lot* of
            // options in each; let's keep things vertical rather than horizontal.
            switch (command)
            {
                // ==========================================================================
                // CONNECTION / SESSION — node-agnostic, no keyspace side effects, safe to
                // replay on a fresh connection.
                // ==========================================================================
                case RedisCommand.PING:
                case RedisCommand.ECHO:
                case RedisCommand.AUTH:
                case RedisCommand.HELLO:
                case RedisCommand.SELECT:
                case RedisCommand.QUIT:
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.PUNSUBSCRIBE:
                case RedisCommand.SSUBSCRIBE:
                case RedisCommand.SUNSUBSCRIBE:
                case RedisCommand.INFO:
                case RedisCommand.TIME:
                case RedisCommand.DBSIZE:
                case RedisCommand.LASTSAVE:
                case RedisCommand.COMMAND:
                    return CommandFlags.CommandRetryConnection;

                // CLIENT etc often use server-specific IDs
                case RedisCommand.CLIENT: // note some can be considered admin, overridden locally
                    return CommandFlags.CommandRetryConnection | Message.CommandServerSpecific;

                // ==========================================================================
                // READ-ONLY — no mutation, always safe to retry.
                // ==========================================================================
                case RedisCommand.GET:
                case RedisCommand.MGET:
                case RedisCommand.STRLEN:
                case RedisCommand.GETRANGE:
                case RedisCommand.EXISTS:
                case RedisCommand.TYPE:
                case RedisCommand.TTL:
                case RedisCommand.PTTL:
                case RedisCommand.EXPIRETIME:
                case RedisCommand.PEXPIRETIME:
                case RedisCommand.KEYS:
                case RedisCommand.RANDOMKEY:
                case RedisCommand.DUMP:
                case RedisCommand.TOUCH: // technically bumps LRU/LFU state, but that's not a "real" side effect worth blocking retries over
                case RedisCommand.OBJECT:
                case RedisCommand.MEMORY:
                case RedisCommand.SORT_RO:
                case RedisCommand.SORT: // ignoring the STORE variant
                case RedisCommand.LCS:
                case RedisCommand.GETEX: // ignoring the TTL-mutating option variants
                case RedisCommand.HGET:
                case RedisCommand.HMGET:
                case RedisCommand.HGETALL:
                case RedisCommand.HKEYS:
                case RedisCommand.HVALS:
                case RedisCommand.HLEN:
                case RedisCommand.HEXISTS:
                case RedisCommand.HSTRLEN:
                case RedisCommand.HRANDFIELD:
                case RedisCommand.HPTTL:
                case RedisCommand.HEXPIRETIME:
                case RedisCommand.HPEXPIRETIME:
                case RedisCommand.HGETEX: // ignoring TTL-mutating option variants
                case RedisCommand.LLEN:
                case RedisCommand.LRANGE:
                case RedisCommand.LINDEX:
                case RedisCommand.LPOS:
                case RedisCommand.SMEMBERS:
                case RedisCommand.SCARD:
                case RedisCommand.SISMEMBER:
                case RedisCommand.SMISMEMBER:
                case RedisCommand.SRANDMEMBER:
                case RedisCommand.SDIFF:
                case RedisCommand.SINTER:
                case RedisCommand.SINTERCARD:
                case RedisCommand.SUNION:
                case RedisCommand.ZCARD:
                case RedisCommand.ZSCORE:
                case RedisCommand.ZMSCORE:
                case RedisCommand.ZRANK:
                case RedisCommand.ZREVRANK:
                case RedisCommand.ZCOUNT:
                case RedisCommand.ZLEXCOUNT:
                case RedisCommand.ZRANGE:
                case RedisCommand.ZREVRANGE:
                case RedisCommand.ZRANGEBYSCORE:
                case RedisCommand.ZREVRANGEBYSCORE:
                case RedisCommand.ZRANGEBYLEX:
                case RedisCommand.ZREVRANGEBYLEX:
                case RedisCommand.ZRANDMEMBER:
                case RedisCommand.ZDIFF:
                case RedisCommand.ZINTER:
                case RedisCommand.ZUNION:
                case RedisCommand.ZINTERCARD:
                case RedisCommand.BITCOUNT:
                case RedisCommand.BITPOS:
                case RedisCommand.GETBIT:
                case RedisCommand.PFCOUNT:
                case RedisCommand.GEOPOS:
                case RedisCommand.GEODIST:
                case RedisCommand.GEOHASH:
                case RedisCommand.GEOSEARCH:
                case RedisCommand.XLEN:
                case RedisCommand.XRANGE:
                case RedisCommand.XREVRANGE:
                case RedisCommand.XREAD: // group-less read only; XREADGROUP is handled separately below
                case RedisCommand.XPENDING:
                case RedisCommand.XINFO:
                case RedisCommand.EVAL_RO:
                case RedisCommand.EVALSHA_RO:
                    return CommandFlags.CommandRetryReadOnly;

                // PUBSUB/SCAN/etc are *basically* read-only, but make limited sense between nodes
                case RedisCommand.PUBSUB:
                case RedisCommand.SCAN:
                case RedisCommand.ZSCAN:
                case RedisCommand.SSCAN:
                case RedisCommand.HSCAN:
                    return CommandFlags.CommandRetryReadOnly | Message.CommandServerSpecific;

                // ==========================================================================
                // WRITE - CHECKED — inherently conditional/idempotent; a retry either
                // no-ops or fails in a way that leaves the end-state identical.
                // ==========================================================================
                case RedisCommand.SETNX:
                case RedisCommand.MSETNX:
                case RedisCommand.HSETNX:
                case RedisCommand.DEL:
                case RedisCommand.UNLINK:
                case RedisCommand.PERSIST:
                case RedisCommand.RENAMENX:
                case RedisCommand.COPY: // ignoring REPLACE — default behavior fails if dest exists
                case RedisCommand.MOVE:
                case RedisCommand.RESTORE: // ignoring REPLACE
                case RedisCommand.GETDEL:
                case RedisCommand.HGETDEL:
                case RedisCommand.HPERSIST:
                case RedisCommand.LTRIM:
                case RedisCommand.SADD:
                case RedisCommand.SREM:
                case RedisCommand.SMOVE:
                case RedisCommand.ZREM:
                case RedisCommand.ZREMRANGEBYRANK:
                case RedisCommand.ZREMRANGEBYSCORE:
                case RedisCommand.ZREMRANGEBYLEX:
                case RedisCommand.HDEL:
                case RedisCommand.PFADD:
                case RedisCommand.XDEL:
                case RedisCommand.XTRIM:
                case RedisCommand.XACK:
                case RedisCommand.XGROUP:
                case RedisCommand.XNACK:
                    return CommandFlags.CommandRetryWriteChecked;

                // ==========================================================================
                // WRITE - LAST WINS — unconditional overwrite of a specific value/state;
                // repeating with the same args always converges to the same final value.
                // ==========================================================================
                case RedisCommand.SET:
                case RedisCommand.GETSET:
                case RedisCommand.MSET:
                case RedisCommand.SETEX:
                case RedisCommand.PSETEX:
                case RedisCommand.SETRANGE:
                case RedisCommand.SETBIT:
                case RedisCommand.BITOP:
                case RedisCommand.RENAME:
                case RedisCommand.EXPIRE:
                case RedisCommand.PEXPIRE:
                case RedisCommand.EXPIREAT:
                case RedisCommand.PEXPIREAT:
                case RedisCommand.HSET:
                case RedisCommand.HMSET:
                case RedisCommand.HEXPIRE:
                case RedisCommand.HPEXPIRE:
                case RedisCommand.HEXPIREAT:
                case RedisCommand.HPEXPIREAT:
                case RedisCommand.LSET:
                case RedisCommand.ZADD: // ignoring INCR option
                case RedisCommand.ZRANGESTORE:
                case RedisCommand.ZUNIONSTORE:
                case RedisCommand.ZINTERSTORE:
                case RedisCommand.ZDIFFSTORE:
                case RedisCommand.SDIFFSTORE:
                case RedisCommand.SINTERSTORE:
                case RedisCommand.SUNIONSTORE:
                case RedisCommand.GEOADD:
                case RedisCommand.GEOSEARCHSTORE:
                case RedisCommand.GEORADIUS: // because of store scenarios
                case RedisCommand.GEORADIUSBYMEMBER:
                case RedisCommand.XCLAIM:
                case RedisCommand.XAUTOCLAIM:
                    return CommandFlags.CommandRetryWriteLastWins;

                // ==========================================================================
                // WRITE - ACCUMULATING — effect compounds with every additional call.
                // ==========================================================================
                case RedisCommand.INCR:
                case RedisCommand.DECR:
                case RedisCommand.INCRBY:
                case RedisCommand.DECRBY:
                case RedisCommand.INCRBYFLOAT:
                case RedisCommand.APPEND:
                case RedisCommand.HINCRBY:
                case RedisCommand.HINCRBYFLOAT:
                case RedisCommand.ZINCRBY:
                case RedisCommand.LPUSH:
                case RedisCommand.RPUSH:
                case RedisCommand.LPUSHX:
                case RedisCommand.RPUSHX:
                case RedisCommand.LINSERT:
                case RedisCommand.LREM:
                case RedisCommand.LPOP:
                case RedisCommand.RPOP:
                case RedisCommand.RPOPLPUSH:
                case RedisCommand.LMOVE:
                case RedisCommand.LMPOP:
                case RedisCommand.SPOP:
                case RedisCommand.ZPOPMIN:
                case RedisCommand.ZPOPMAX:
                case RedisCommand.ZMPOP:
                case RedisCommand.XADD:
                case RedisCommand.PFMERGE: // destination accumulates union each call
                    return CommandFlags.CommandRetryWriteAccumulating;

                // ==========================================================================
                // SERVER ADMIN / NODE-SPECIFIC
                // ==========================================================================
                case RedisCommand.REPLICAOF:
                case RedisCommand.SLAVEOF:
                case RedisCommand.BGSAVE:
                case RedisCommand.BGREWRITEAOF:
                case RedisCommand.SAVE:
                case RedisCommand.SHUTDOWN:
                case RedisCommand.FLUSHALL:
                case RedisCommand.FLUSHDB:
                case RedisCommand.SWAPDB:
                case RedisCommand.MIGRATE:
                case RedisCommand.DEBUG:
                case RedisCommand.MONITOR:
                case RedisCommand.CONFIG: // note CONFIG GET con be considered more safe
                case RedisCommand.SLOWLOG:
                case RedisCommand.LATENCY:
                case RedisCommand.SCRIPT:
                case RedisCommand.CLUSTER: // note: some like MYID can be considered more safe
                    return CommandFlags.CommandRetryServerAdmin | Message.CommandServerSpecific;

                // ==========================================================================
                // NEVER — transactions, arbitrary scripts, and blocking/destructive or
                // fire-and-forget commands where a lost ack makes blind retry dangerous.
                // ==========================================================================
                case RedisCommand.MULTI:
                case RedisCommand.EXEC:
                case RedisCommand.DISCARD:
                case RedisCommand.WATCH:
                case RedisCommand.UNWATCH:
                case RedisCommand.PUBLISH:
                case RedisCommand.SPUBLISH:
                case RedisCommand.XREADGROUP:
                case RedisCommand.BLPOP:
                case RedisCommand.BRPOP:
                case RedisCommand.BRPOPLPUSH:
                    return CommandFlags.CommandRetryNever;

                // ==========================================================================
                // scripts / modules / functions; we're going to assume nothing too weird,
                // at worst similar to INCR; but it is *hoped* that callers will supply hints.
                // ==========================================================================
                case RedisCommand.EVAL:
                case RedisCommand.EVALSHA:
                    return CommandFlags.CommandRetryWriteAccumulating;

                // ---- CONNECTION / SESSION ----
                case RedisCommand.ASKING: // cluster ASK redirection flag - per-connection state
                case RedisCommand.READONLY: // cluster client read-routing flag - per-connection state
                case RedisCommand.READWRITE: // cluster client read-routing flag - per-connection state
                case RedisCommand.ROLE: // replication role report, like INFO - no keyspace effect
                    return CommandFlags.CommandRetryConnection;

                // ---- READ-ONLY ----
                case RedisCommand.ARCOUNT:
                case RedisCommand.ARINFO: // structure metadata, like INFO
                case RedisCommand.ARGET:
                case RedisCommand.ARGETRANGE:
                case RedisCommand.ARGREP:
                case RedisCommand.ARLASTITEMS:
                case RedisCommand.ARLEN:
                case RedisCommand.ARMGET:
                case RedisCommand.ARSCAN:
                case RedisCommand.AROP:
                case RedisCommand.DIGEST: // computes a hash of existing data, doesn't mutate it
                case RedisCommand.VCARD:
                case RedisCommand.VDIM:
                case RedisCommand.VEMB:
                case RedisCommand.VGETATTR:
                case RedisCommand.VINFO:
                case RedisCommand.VISMEMBER:
                case RedisCommand.VLINKS:
                case RedisCommand.VRANDMEMBER:
                case RedisCommand.VRANGE:
                case RedisCommand.VSIM:
                    return CommandFlags.CommandRetryReadOnly;

                // ---- WRITE - CHECKED ----
                case RedisCommand.DELEX: // conditional/expiry-aware delete - converges same as DEL
                case RedisCommand.VADD: // idempotent add-member, like SADD
                case RedisCommand.VREM: // idempotent remove-member, like SREM/ZREM
                case RedisCommand.XACKDEL: // ack+delete, converges like XACK/XDEL combined
                case RedisCommand.XDELEX:
                    return CommandFlags.CommandRetryWriteChecked;

                // ---- WRITE - LAST WINS ----
                case RedisCommand.ARDEL:
                case RedisCommand.ARDELRANGE:
                case RedisCommand.ARMSET:
                case RedisCommand.ARSEEK: // repositions a cursor to an explicit point - overwrite semantics
                case RedisCommand.ARSET:
                case RedisCommand.HSETEX: // HSET + expiry, unconditional overwrite
                case RedisCommand.MSETEX:
                case RedisCommand.VSETATTR: // unconditional attribute overwrite
                case RedisCommand.XCFGSET:
                    return CommandFlags.CommandRetryWriteLastWins;

                // ---- WRITE - ACCUMULATING ----
                case RedisCommand.ARRING: // presumed create/configure-ring, unconditional define
                case RedisCommand.ARINSERT: // ring-buffer insert, compounds like a push
                case RedisCommand.ARNEXT: // advances a cursor/position with each call
                case RedisCommand.INCREX: // counter semantics + expiry, still accumulating
                    return CommandFlags.CommandRetryWriteAccumulating;

                // ---- SERVER ADMIN / NODE-SPECIFIC ----
                case RedisCommand.HOTKEYS: // diagnostic/introspection, node-local
                case RedisCommand.SENTINEL:
                case RedisCommand.SYNC: // replication stream handshake
                    return CommandFlags.CommandRetryServerAdmin | Message.CommandServerSpecific;

                // if we don't recognize it: default to the most pessimistic
                case RedisCommand.NONE:
                case RedisCommand.UNKNOWN:
                default:
                    return CommandFlags.CommandRetryNever;
            }
        }
    }
}
