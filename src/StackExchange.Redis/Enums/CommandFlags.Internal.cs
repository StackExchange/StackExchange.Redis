namespace StackExchange.Redis;

/// <summary>
/// The library's own <see cref="CommandFlags"/> bits, masks and routing rules - the ones that are not public members.
/// </summary>
/// <remarks>
/// Moved out of <c>Message</c>, unchanged, because both cores read them and <c>Message</c> is being deleted: the bit
/// values are a wire of their own between the surfaces and the executors, so there must stay exactly one copy.
/// </remarks>
internal static class CommandFlagsInternal
{
    internal const CommandFlags
        InternalCallFlag = (CommandFlags)128,
        NoFlushFlag = (CommandFlags)1024,
        // "server specific" (bit 18): tied to a specific endpoint, never retry elsewhere. Not (yet) a
        // public CommandFlags member - see the note on the hidden bit-18 value in CommandFlags.cs.
        CommandServerSpecific = (CommandFlags)(1 << 18),
        // "probe" (bit 19): health-check traffic. Deliberately *not* InternalCallFlag, which also decides
        // queuing - see Message.IsCallerFacing.
        ProbeFlag = (CommandFlags)(1 << 19);

    internal const CommandFlags MaskPrimaryServerPreference = CommandFlags.DemandMaster
                                                             | CommandFlags.DemandReplica
                                                             | CommandFlags.PreferMaster
                                                             | CommandFlags.PreferReplica;

    // the 5-bit retry-category severity region (bits 13-17); numerically equal to CommandRetryNever.
    // deliberately excludes CommandServerSpecific (bit 18), which is an orthogonal flag, not part of
    // the <=-comparable severity ladder.
    internal const CommandFlags MaskRetryCategory = CommandFlags.CommandRetryNever;

    internal const CommandFlags UserSelectableFlags = CommandFlags.None
                                                     | CommandFlags.DemandMaster
                                                     | CommandFlags.DemandReplica
                                                     | CommandFlags.PreferMaster
                                                     | CommandFlags.PreferReplica
                                                     | (CommandFlags)1 // CommandFlags.HighPriority; obsolete-as-error, but still tolerated from callers
                                                     | CommandFlags.FireAndForget
                                                     | CommandFlags.NoRedirect
                                                     | CommandFlags.NoScriptCache
                                                     | CommandFlags.NoClientCache
                                                     | MaskRetryCategory // caller may override the retry category...
                                                     | CommandServerSpecific // ...and the server-specific flag
                                                     | NoFlushFlag // we'll allow this one even though not advertised
                                                     // ...and the probe flag, which *has* to survive this
                                                     // whitelist: health-check probes reach the pipeline
                                                     // through the public API (HealthCheckContext.
                                                     // ProbeFlags), so it arrives as caller-supplied flags
                                                     // or not at all. A caller passing it deliberately only
                                                     // opts their own command out of endpoint-idleness
                                                     // accounting, which is harmless.
                                                     | ProbeFlag;

    /// <summary>
    /// Route a command to the primary, whatever the caller asked for - unless they demanded a replica,
    /// which is a request that cannot be honoured rather than a preference to override.
    /// </summary>
    /// <remarks>
    /// One copy of the rule, because two surfaces need it: the interpolated writer discovers the same
    /// thing about <c>PFCOUNT</c> and <c>SORT</c> that <c>Message.SetPrimaryOnly</c> discovers here,
    /// and the primary/replica bits are a 2-bit region rather than a flag, so "or in DemandMaster"
    /// silently turns <see cref="CommandFlags.PreferReplica"/> into
    /// <see cref="CommandFlags.DemandReplica"/>.
    /// </remarks>
    internal static CommandFlags DemandPrimary(CommandFlags flags, RedisCommand command)
    {
        switch (GetPrimaryReplicaFlags(flags))
        {
            case CommandFlags.DemandReplica:
                throw ExceptionFactory.PrimaryOnly(false, command, null, null);
            case CommandFlags.DemandMaster:
                // already fine as-is
                return flags;
            case CommandFlags.PreferMaster:
            case CommandFlags.PreferReplica:
            default: // we will run this on the primary, then
                return SetPrimaryReplicaFlags(flags, CommandFlags.DemandMaster);
        }
    }

    internal static CommandFlags GetPrimaryReplicaFlags(CommandFlags flags)
    {
        // for the purposes of the switch, we only care about two bits
        return flags & MaskPrimaryServerPreference;
    }

    internal static CommandFlags GetRetryCategory(CommandFlags flags)
    {
        // isolate the retry-category region; 0 here means "not specified" (resolved downstream)
        return flags & MaskRetryCategory;
    }

    internal static bool RequiresDatabase(RedisCommand command)
    {
        switch (command)
        {
            case RedisCommand.ASKING:
            case RedisCommand.AUTH:
            case RedisCommand.BGREWRITEAOF:
            case RedisCommand.BGSAVE:
            case RedisCommand.CLIENT:
            case RedisCommand.CLUSTER:
            case RedisCommand.COMMAND:
            case RedisCommand.CONFIG:
            case RedisCommand.DISCARD:
            case RedisCommand.ECHO:
            case RedisCommand.FLUSHALL:
            case RedisCommand.HELLO:
            case RedisCommand.HOTKEYS:
            case RedisCommand.INFO:
            case RedisCommand.LASTSAVE:
            case RedisCommand.LATENCY:
            case RedisCommand.MEMORY:
            case RedisCommand.MONITOR:
            case RedisCommand.MULTI:
            case RedisCommand.PING:
            case RedisCommand.PUBLISH:
            case RedisCommand.PUBSUB:
            case RedisCommand.PUNSUBSCRIBE:
            case RedisCommand.PSUBSCRIBE:
            case RedisCommand.QUIT:
            case RedisCommand.READONLY:
            case RedisCommand.READWRITE:
            case RedisCommand.REPLICAOF:
            case RedisCommand.ROLE:
            case RedisCommand.SAVE:
            case RedisCommand.SCRIPT:
            case RedisCommand.SHUTDOWN:
            case RedisCommand.SLAVEOF:
            case RedisCommand.SLOWLOG:
            case RedisCommand.SUBSCRIBE:
            case RedisCommand.SPUBLISH:
            case RedisCommand.SSUBSCRIBE:
            case RedisCommand.SUNSUBSCRIBE:
            case RedisCommand.SWAPDB:
            case RedisCommand.SYNC:
            case RedisCommand.TIME:
            case RedisCommand.UNSUBSCRIBE:
            case RedisCommand.SENTINEL:
                return false;
            default:
                return true;
        }
    }

    internal static CommandFlags SetPrimaryReplicaFlags(CommandFlags everything, CommandFlags primaryReplica)
    {
        // take away the two flags we don't want, and add back the ones we care about
        return (everything & ~(CommandFlags.DemandMaster | CommandFlags.DemandReplica | CommandFlags.PreferMaster |
                               CommandFlags.PreferReplica))
               | primaryReplica;
    }
}
