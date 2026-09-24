using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis
{
    /// <summary>The old <c>object</c>-argument <c>Execute</c>, rendered through the context surface.</summary>
    /// <remarks>
    /// <para>
    /// <b>Written out here rather than added to the context</b>, because every awkward thing about it
    /// is the old signature's: <see cref="ICollection{T}"/> of <see cref="object"/>, a type test per
    /// argument, and a <see cref="RedisResult"/> tree at the end. <c>ExecuteResp</c> is what the same
    /// command looks like when the caller can say what its arguments <i>are</i> - which is why
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
    /// <remarks>
    /// <para>
    /// <b>Shared between the database and the server surfaces</b>, which is why it is here rather than on
    /// either: <c>IDatabase.Execute</c> and <c>IServer.Execute</c> differ only in which context they send
    /// through and which database, if any, that context carries. The three-way argument branch below is
    /// the part worth having one copy of.
    /// </para>
    /// </remarks>
    internal static class RespAdHoc
    {
        /// <summary>Render and send an ad-hoc command whose arguments arrived boxed.</summary>
        /// <param name="context">The context to send through; carries the database, if the command needs one.</param>
        /// <param name="command">The command name.</param>
        /// <param name="args">Its arguments, or null.</param>
        /// <param name="flags">Command flags.</param>
        internal static ValueTask<RedisResult> ExecuteAsync(
            RespContext context,
            string command,
            ICollection<object>? args,
            CommandFlags flags)
        {
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
