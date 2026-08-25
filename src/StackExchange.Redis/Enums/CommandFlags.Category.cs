namespace StackExchange.Redis;

internal static class CommandFlagsExtensions
{
    public static CommandFlags WithCategory(this CommandFlags flags, CommandFlags category)
    {
        // CommandServerSpecific is an orthogonal flag rather than part of the severity ladder, so it
        // is always additive - the caller choosing a retry category doesn't make a cursor-bearing
        // command any less node-affine.
        flags |= category & Message.CommandServerSpecific;

        // ...but for the ladder itself: if the user has already specified a category, that wins.
        return ((flags & Message.MaskRetryCategory) is 0) ? flags | (category & Message.MaskRetryCategory) : flags;
    }

    /// <summary>
    /// The retry category implied by an existence condition applied to an otherwise unconditional write;
    /// <see cref="CommandFlags.None"/> means "no opinion", leaving the per-command default in place.
    /// </summary>
    public static CommandFlags AsRetryCategory(this When when) => when switch
    {
        // NX/XX make the write conditional: a replay either no-ops or fails, and either way the
        // end-state matches the first attempt.
        When.Exists or When.NotExists => CommandFlags.CommandRetryWriteChecked,
        _ => CommandFlags.None,
    };

    /// <summary>
    /// The category for one page of a SCAN-family iteration. These are reads, but a *resumed* cursor only means
    /// something on the node that issued it (and, for the per-key variants, against that node's encoding of the
    /// object), so it is node-affine; a fresh iteration from the origin cursor can start anywhere.
    /// </summary>
    public static CommandFlags WithScanCursorCategory(this CommandFlags flags, in RedisValue cursor)
        => flags.WithCategory(cursor == RedisBase.CursorUtils.Origin
            ? CommandFlags.CommandRetryReadOnly
            : CommandFlags.CommandRetryReadOnly | Message.CommandServerSpecific);

    /// <inheritdoc cref="AsRetryCategory(When)"/>
    public static CommandFlags AsRetryCategory(this ExpireWhen when) => when switch
    {
        // NX/XX/GT/LT; GT/LT are monotone, so re-applying converges on the same deadline
        ExpireWhen.Always => CommandFlags.None,
        _ => CommandFlags.CommandRetryWriteChecked,
    };

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

                // CLIENT etc often use server-specific IDs. This is the *safest* subcommand's category;
                // RedisServer raises CLIENT KILL to server-admin, since it can't be inferred from the name.
                case RedisCommand.CLIENT:
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
                case RedisCommand.MEMORY: // note MEMORY PURGE is raised to server-admin in RedisServer
                case RedisCommand.SORT_RO:
                case RedisCommand.SORT: // the STORE variant is raised to a write where we can see the args
                case RedisCommand.LCS:
                case RedisCommand.GETEX: // the TTL-mutating variants are raised to a write where we can see the args
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
                case RedisCommand.HGETEX: // as GETEX
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
                case RedisCommand.BITFIELD_RO:
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
                case RedisCommand.COPY: // default behavior fails if dest exists; REPLACE is raised where we can see the args
                case RedisCommand.MOVE:
                case RedisCommand.RESTORE: // the typed API never emits REPLACE
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
                case RedisCommand.ZADD: // NX/XX/GT/LT and INCR are all handled in SortedSetAddMessage
                case RedisCommand.ZRANGESTORE:
                case RedisCommand.ZUNIONSTORE:
                case RedisCommand.ZINTERSTORE:
                case RedisCommand.ZDIFFSTORE:
                case RedisCommand.SDIFFSTORE:
                case RedisCommand.SINTERSTORE:
                case RedisCommand.SUNIONSTORE:
                case RedisCommand.GEOADD:
                case RedisCommand.GEOSEARCHSTORE:
                case RedisCommand.GEORADIUS: // because of store scenarios; the typed API demotes to read-only
                case RedisCommand.GEORADIUSBYMEMBER:
                case RedisCommand.XCLAIM: // JUSTID is demoted where we can see the args
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
                case RedisCommand.XADD: // explicit ids and IDMP/IDMPAUTO are demoted where we can see the args
                case RedisCommand.PFMERGE: // destination accumulates union each call
                case RedisCommand.BITFIELD: // INCRBY sub-ops compound; demoted to last-wins when the payload has none
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
                case RedisCommand.CONFIG: // note CONFIG GET is demoted to connection-level in RedisServer
                case RedisCommand.SLOWLOG: // note SLOWLOG GET is demoted to read-only in RedisServer
                case RedisCommand.LATENCY: // note LATENCY DOCTOR/HISTORY/LATEST likewise
                case RedisCommand.SCRIPT: // note SCRIPT EXISTS/LOAD are demoted in RedisServer/RedisDatabase
                case RedisCommand.CLUSTER: // note CLUSTER NODES is demoted to read-only where we know the subcommand
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
                case RedisCommand.XREADGROUP: // only for ">"; an explicit id re-reads the PEL and is demoted to read-only
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
                case RedisCommand.SENTINEL: // only because of FAILOVER; the introspection verbs are demoted in RedisServer
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
