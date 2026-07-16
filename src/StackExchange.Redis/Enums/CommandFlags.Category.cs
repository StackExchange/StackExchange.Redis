namespace StackExchange.Redis;

internal static class CommandFlagsExtensions
{
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
                case RedisCommand.INFO:
                    // etc
                    return CommandFlags.CommandRetryConnection;
                case RedisCommand.GET:
                    // etc
                    return CommandFlags.CommandRetryReadOnly;
                case RedisCommand.SETNX:
                    // etc
                    return CommandFlags.CommandRetryWriteChecked;
                case RedisCommand.SET:
                    // etc
                    return CommandFlags.CommandRetryWriteLastWins;
                case RedisCommand.INCR:
                    // etc
                    return CommandFlags.CommandRetryWriteAccumulating;
                case RedisCommand.REPLICAOF:
                    // etc
                    return CommandFlags.CommandRetryServerAdmin | CommandFlags.CommandServerSpecific;
                default:
                    // fail safe
                    return CommandFlags.CommandRetryNever;
            }
        }
    }
}
