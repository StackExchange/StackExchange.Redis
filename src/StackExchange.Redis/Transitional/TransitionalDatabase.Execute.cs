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
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RespResult ExecuteResp(string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Raw.ExecuteAsync(command, args, flags));

        /// <inheritdoc/>
        public Task<RespResult> ExecuteRespAsync(string command, ReadOnlyMemory<RedisKeyOrValue> args, CommandFlags flags = CommandFlags.None)
            => _inner.Raw.ExecuteAsync(command, args, flags).AsTask();

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
            => ExecuteCore(command, args, flags).AsTask();

        /// <summary>The old <c>object</c>-argument <c>Execute</c>, rendered through the context surface.</summary>
        /// <remarks>
        /// <para>
        /// <b>Written out here rather than added to the context</b>, because every awkward thing about it
        /// is the old signature's: <see cref="ICollection{T}"/> of <see cref="object"/>, a type test per
        /// argument, and a <see cref="RedisResult"/> tree at the end. <c>ExecuteResp</c> above is what the
        /// same command looks like when the caller can say what its arguments <i>are</i> - which is why
        /// this one cannot take part in routing or caching as well as that one can, and why the new
        /// surface is not growing a version of it.
        /// </para>
        /// <para>
        /// The three-way branch is the shipped <c>ExecuteMessage.WriteImpl</c>, kept exactly: a
        /// <see cref="RedisKey"/> goes through the key path so it is prefixed and contributes a slot, a
        /// <see cref="RedisChannel"/> through the channel path so it is prefixed, and anything else is
        /// parsed as a value or refused. Losing that distinction is precisely what boxing into
        /// <see cref="object"/> costs.
        /// </para>
        /// <para>
        /// The guards the shipped message applied before rendering still apply, but from further in: the
        /// command map and a disabled command are settled by the builder's constructor, the argument
        /// ceiling by the builder as it writes, and whitespace in the command name by the same
        /// constructor - which is where that check moved to so that <c>ExecuteResp</c> gets it too.
        /// </para>
        /// </remarks>
        private ValueTask<RedisResult> ExecuteCore(string command, ICollection<object>? args, CommandFlags flags)
        {
            var context = _inner.Raw;
            var handler = new RespRequestBuilder(0, args?.Count ?? 0, context, command);
            try
            {
                if (args is not null)
                {
                    foreach (var arg in args)
                    {
                        if (arg is RedisKey key)
                        {
                            handler.AppendFormatted(key);
                        }
                        else if (arg is RedisChannel channel)
                        {
                            handler.AppendFormatted(channel);
                        }
                        else
                        {
                            // recognises well-known types
                            var value = RedisValue.TryParse(arg, out var valid);
                            if (!valid) throw new InvalidCastException($"Unable to parse value: '{arg}'");
                            handler.AppendFormatted(value);
                        }
                    }
                }
            }
            catch
            {
                handler.Dispose(); // Complete did not happen, so the buffer is still ours
                throw;
            }

            var frame = handler.Complete();
            return context.SendAsync(ref frame, flags, RedisResultHandler.Instance, default);
        }
    }
}
