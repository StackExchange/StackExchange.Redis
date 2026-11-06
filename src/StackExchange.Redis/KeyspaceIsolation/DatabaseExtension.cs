using System;
using StackExchange.Redis.Interfaces;

namespace StackExchange.Redis.KeyspaceIsolation
{
    /// <summary>
    ///     Provides the <see cref="WithKeyPrefix"/> extension method to <see cref="IDatabase"/>.
    /// </summary>
    public static class DatabaseExtensions
    {
        /// <summary>
        ///     Creates a new <see cref="IDatabase"/> instance that provides an isolated key space
        ///     of the specified underlying database instance.
        /// </summary>
        /// <param name="database">
        ///     The underlying database instance that the returned instance shall use.
        /// </param>
        /// <param name="keyPrefix">
        ///     The prefix that defines a key space isolation for the returned database instance.
        /// </param>
        /// <returns>
        ///     A new <see cref="IDatabase"/> instance that invokes the specified underlying
        ///     <paramref name="database"/> but prepends the specified <paramref name="keyPrefix"/>
        ///     to all key parameters and thus forms a logical key space isolation.
        /// </returns>
        /// <remarks>
        /// <para>
        ///     The following methods are not supported in a key space isolated database and
        ///     will throw an <see cref="NotSupportedException"/> when invoked:
        /// </para>
        /// <list type="bullet">
        ///     <item><see cref="IDatabaseAsync.KeyRandomAsync(CommandFlags)"/></item>
        ///     <item><see cref="IDatabase.KeyRandom(CommandFlags)"/></item>
        /// </list>
        /// <para>
        ///     Please notice that keys passed to a script are prefixed (as normal) but care must
        ///     be taken when a script returns the name of a key as that will (currently) not be
        ///     "unprefixed".
        /// </para>
        /// </remarks>
        public static IDatabase WithKeyPrefix(this IDatabase database, RedisKey keyPrefix)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (keyPrefix.IsNull)
            {
                throw new ArgumentNullException(nameof(keyPrefix));
            }

            if (keyPrefix.IsEmpty)
            {
                return database; // fine - you can keep using the original, then
            }

            // The prefix is CONTEXT state, applied to every RedisKey at render time - so the result is a
            // plain database over a prefixed context, not a wrapper. Nesting composes the same way the
            // old decorators did: the prefixes concatenate, outermost call innermost on the wire.
            //
            // A foreign IDatabase has no context to re-point, and - unlike WithRetry, where "no retry" is
            // merely the behaviour the caller already had - quietly not prefixing would hand every command
            // to the WRONG KEYSPACE; that must throw rather than degrade.
            if (database.Context is not { Raw: { Executor: not null } raw })
            {
                throw new NotSupportedException(
                    $"WithKeyPrefix requires a database from this library; {database.GetType().Name} exposes no context to apply the prefix to.");
            }

            // a batch or transaction cannot be re-contexted into a plain database without silently losing
            // what it is; take the prefix BEFORE creating it (the batch inherits the prefixed context)
            database.RejectFlags(DatabaseFeatureFlags.Batch | DatabaseFeatureFlags.Transaction);

            // the channel prefix too: the old decorator prefixed the channel on Publish, and a tenant
            // boundary that covered keys but not the messages about them would be a silent hole
            var context = new RespDatabaseContext(raw
                .AppendKeyPrefix(keyPrefix)
                .AppendChannelPrefix(new RedisChannel((byte[]?)keyPrefix, RedisChannel.PatternMode.Literal)));
            return new RedisDatabase(context, database.Multiplexer, database.GetAsyncState());
        }
    }
}
