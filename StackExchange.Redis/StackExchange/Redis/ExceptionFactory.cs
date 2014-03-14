using System;

namespace StackExchange.Redis
{
    internal static class ExceptionFactory
    {
        internal static Exception AdminModeNotEnabled(RedisCommand command)
        {
            return new RedisCommandException("This operation is not available unless admin mode is enabled: " + command.ToString());
        }
        internal static Exception NoConnectionAvailable(RedisCommand command)
        {
            return new RedisConnectionException(ConnectionFailureType.UnableToResolvePhysicalConnection, "No connection is available to service this operation: " + command.ToString());
        }
        internal static Exception CommandDisabled(RedisCommand command)
        {
            return new RedisCommandException("This operation has been disabled in the command-map and cannot be used: " + command.ToString());
        }
        internal static Exception MultiSlot()
        {
            return new RedisCommandException("Multi-key operations must involve a single slot; keys can use 'hash tags' to help this, i.e. '{/users/12345}/account' and '{/users/12345}/contacts' will always be in the same slot");
        }

        internal static Exception DatabaseOutfRange(int targetDatabase)
        {
            return new RedisCommandException("The database does not exist on the server: " + targetDatabase);
        }

        internal static Exception DatabaseRequired(RedisCommand command)
        {
            return new RedisCommandException("A target database is required for " + command.ToString());
        }

        internal static Exception DatabaseNotRequired(RedisCommand command)
        {
            return new RedisCommandException("A target database is not required for " + command.ToString());
        }

        internal static Exception MasterOnly(RedisCommand command)
        {
            return new RedisCommandException(command + " cannot be issued to a slave");
        }
    }
}
