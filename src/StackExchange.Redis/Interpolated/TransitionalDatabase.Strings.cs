using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The string commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One file per command group, named to match the group it adapts - <c>RespSurface.Strings.cs</c>
    /// declares <c>ctx.Strings.Get</c>, and this file is the <see cref="IDatabase"/> spelling of it. The
    /// pairing is the point: the two halves of a command move together, and a group is either done or
    /// visibly not.
    /// </para>
    /// <para>
    /// Implementing a member here removes it from the generated set automatically, because
    /// <c>[AutoDatabase]</c> skips whatever the class declares itself - so nothing has to be deleted from
    /// a list of stubs, and SER352's count falls by itself.
    /// </para>
    /// <para>
    /// Note how little each one is. The body is the whole implementation - no message type, no result
    /// processor, no overload ladder - because the command already exists on the context surface and this
    /// is purely the adapter from the old interface to it.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.Get(key, flags));

        /// <inheritdoc/>
        public Task<RedisValue> StringGetAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
            => Context.Strings.Get(key, flags).AsTask();

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Strings.Set(key, value, expiry, when, flags));

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, Expiration expiry = default, ValueCondition when = default, CommandFlags flags = CommandFlags.None)
            => Context.Strings.Set(key, value, expiry, when, flags).AsTask();

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => StringSet(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None)
            => StringSetAsync(key, value, Expiration.CreateOrKeepTtl(expiry, keepTtl), when, flags);

        // the older When/TimeSpan? shapes, which is what most existing callers actually bind to; `When`
        // converts implicitly to ValueCondition, and CreateOrKeepTtl is the same helper RedisDatabase
        // uses, so these are pure adapters with no second opinion about semantics

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
            => StringSet(key, value, expiry, keepTtl: false, when);

        /// <inheritdoc/>
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSet(key, value, expiry, keepTtl: false, when, flags);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when)
            => StringSetAsync(key, value, expiry, keepTtl: false, when);

        /// <inheritdoc/>
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags)
            => StringSetAsync(key, value, expiry, keepTtl: false, when, flags);
    }
}
