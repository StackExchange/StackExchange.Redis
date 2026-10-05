using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>
    /// The ad-hoc escape hatch, where it has moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own file rather than a command group, because it is not one: this is the route for commands
    /// nobody modelled, which is how another library's surface reaches a server through this one.
    /// </para>
    /// <para>
    /// <c>ExecuteResp</c> is a straight pass-through - the interface signature and the context method agree
    /// exactly, down to <see cref="RedisKeyOrValue"/> for the arguments, which is the part that matters:
    /// it keeps key-ness, so an ad-hoc command routes, invalidates and caches like a modelled one.
    /// </para>
    /// </remarks>
    internal partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RespResult ExecuteResp(string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
            => Wait(SyncCall.Begin(), _inner.Raw.ExecuteAsync(command, args, flags));

        /// <inheritdoc/>
        public Task<RespResult> ExecuteRespAsync(string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
            => _inner.Raw.ExecuteAsync(command, args, flags).AsTask(AsyncState, flags);

        /// <inheritdoc/>
        public RedisResult Execute(string command, params object[] args)
            => Execute(command, args, CommandFlags.None);

        /// <inheritdoc/>
        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => Wait(ExecuteCore(command, args, flags));

        /// <inheritdoc/>
        public Task<RedisResult> ExecuteAsync(string command, params object[] args)
            => ExecuteAsync(command, args, CommandFlags.None);

        /// <inheritdoc/>
        public Task<RedisResult> ExecuteAsync(string command, ICollection<object>? args, CommandFlags flags = CommandFlags.None)
            => ExecuteCore(command, args, flags).AsTask(AsyncState, flags);

        /// <inheritdoc cref="RespAdHoc.ExecuteAsync"/>
        private ValueTask<RedisResult> ExecuteCore(string command, ICollection<object>? args, CommandFlags flags)
            => RespAdHoc.ExecuteAsync(_inner.Raw, command, args, flags);
    }
}
