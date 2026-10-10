using System;
using RESPite;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>Which commands need <c>AllowAdmin</c>, sub-command aware; moved out of <c>Message</c> unchanged.</summary>
internal static partial class AdminCommands
{
    [AsciiHash(nameof(SubCommandMetadata))]
    internal enum SubCommand
    {
        [AsciiHash("")]
        Unknown = 0,
        [AsciiHash("GETNAME")]
        GetName,
        [AsciiHash("ID")]
        Id,
        [AsciiHash("INFO")]
        Info,
        [AsciiHash("SETINFO")]
        SetInfo,
        [AsciiHash("SETNAME")]
        SetName,
    }

    internal static partial class SubCommandMetadata
    {
        [AsciiHash(CaseSensitive = false)]
        internal static partial bool TryParse(ReadOnlySpan<byte> value, out SubCommand subCommand);

        [AsciiHash(CaseSensitive = false)]
        internal static partial bool TryParse(ReadOnlySpan<char> value, out SubCommand subCommand);

        internal static bool TryGetSubCommand(in RedisValue value, out SubCommand subCommand)
        {
            switch (value.Type)
            {
                case RedisValue.StorageType.ByteArray:
                case RedisValue.StorageType.MemoryManager:
                case RedisValue.StorageType.ShortBlob:
                    // all three contiguous byte-blob kinds expose their bytes directly
                    // (the discard here *must* be stack-local; that's the "Unsafe" in this API)
                    return TryParse(value.UnsafeRawSpan(out _), out subCommand);
                case RedisValue.StorageType.String:
                    // char-backed: parse the chars directly, no UTF8 round-trip
                    return TryParse(value.RawString().AsSpan(), out subCommand);
                case RedisValue.StorageType.Sequence when value.GetByteCount() <= BufferBytes:
                    // non-contiguous: normalize into a small stack buffer
                    // (sub-commands are short, so anything longer cannot match)
                    Span<byte> tmp = stackalloc byte[BufferBytes];
                    var len = value.CopyTo(tmp);
                    return TryParse(tmp.Slice(0, len), out subCommand);
                // numeric / null / unknown are never a sub-command (e.g. it is never `123`);
                // if that ever changes, revisit
            }
            subCommand = SubCommand.Unknown;
            return false;
        }
    }

    /// <summary>
    /// Whether a command - given its sub-command, where one was recognised - needs <c>AllowAdmin</c>.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="subCommand">Its sub-command, where one was recognised.</param>
    /// <remarks>
    /// <b>Static so that a core with no <c>Message</c> can ask the same question.</b> The answer
    /// is a property of the command and its sub-command and nothing else; it was only ever an instance
    /// member because that is where the sub-command happened to be reachable. A second copy of this list
    /// would be a second place for <c>AllowAdmin</c> to disagree with itself.
    /// </remarks>
    internal static bool IsAdminCommand(RedisCommand command, SubCommand? subCommand)
    {
        {
            switch (command)
            {
                case RedisCommand.CLIENT when subCommand is { } sub:
                    switch (sub)
                    {
                        case SubCommand.GetName:
                        case SubCommand.SetName:
                        case SubCommand.Id:
                        case SubCommand.Info:
                        case SubCommand.SetInfo:
                            return false;
                    }
                    return true;
                /* possible? reasonable?
                case RedisCommand.CONFIG when TryGetSubCommand(out var subCommand):
                    // allow .Get?
                */
                case RedisCommand.BGREWRITEAOF:
                case RedisCommand.BGSAVE:
                case RedisCommand.CLIENT:
                case RedisCommand.CLUSTER:
                case RedisCommand.CONFIG:
                case RedisCommand.DEBUG:
                case RedisCommand.FLUSHALL:
                case RedisCommand.FLUSHDB:
                case RedisCommand.HOTKEYS:
                case RedisCommand.INFO:
                case RedisCommand.KEYS:
                case RedisCommand.MONITOR:
                case RedisCommand.REPLICAOF:
                case RedisCommand.SAVE:
                case RedisCommand.SHUTDOWN:
                case RedisCommand.SLAVEOF:
                case RedisCommand.SLOWLOG:
                case RedisCommand.SWAPDB:
                case RedisCommand.SYNC:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>The sub-command a rendered frame carries, if it is one we recognise.</summary>
    /// <param name="request">The rendered frame.</param>
    /// <param name="subCommand">The sub-command.</param>
    /// <remarks>
    /// <b>Recovered from the frame, because the admin gate is sub-command aware.</b> <c>CLIENT</c> as a
    /// whole is admin, but <c>CLIENT ID</c>, <c>GETNAME</c>, <c>SETNAME</c>, <c>INFO</c> and
    /// <c>SETINFO</c> are not - so a message reporting only its command has every one of those refused
    /// when <c>AllowAdmin</c> is off. The shipped ad-hoc message exposes its first argument for exactly
    /// this reason; once the command is rendered, the frame is the only copy of it left.
    /// </remarks>
    internal static bool TryGetSubCommand(in RespRequest request, out SubCommand subCommand)
    {
        // the command token is not an argument here, so index 0 IS the sub-command; and the span must
        // be big enough for every argument, because resolving refuses a short one rather than filling
        // what it can - so it is sized from the whole frame, which is an upper bound
        var wanted = request.ArgCount;
        Span<KeyRange> ranges = wanted <= 16 ? stackalloc KeyRange[16] : new KeyRange[wanted];
        if (request.TryGetAllArguments(ranges) > 0
            && SubCommandMetadata.TryParse(request.GetKey(ranges[0]), out subCommand))
        {
            return true;
        }

        subCommand = SubCommand.Unknown;
        return false;
    }
}
