using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite;

namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Appending to a command already being built, so a conditional fragment is
    /// written the same way as the command itself.
    /// </summary>
    /// <remarks>
    /// <code>
    /// var cmd = ctx.Compose($"{RedisCommand.SET}{key}{value}");
    /// if (withTtl) cmd.Append($"{RespLiterals.EX}{ttl}");
    /// using var frame = ctx.Render(ref cmd);
    /// </code>
    /// </remarks>
    public static class RespAppend
    {
        /// <summary>Append more arguments, written the same way as the command itself.</summary>
        /// <param name="command">The command being built.</param>
        /// <param name="handler">The fragment; supplied by the compiler from an interpolated string.</param>
        /// <remarks>
        /// <para>
        /// The handler here is a <see cref="RespRequestBuilder"/> - the same type, moved in and moved back
        /// out - so an append accepts exactly what the command does, by construction rather than by
        /// keeping two lists aligned.
        /// </para>
        /// <para>
        /// The handler is reset to <c>default</c> once its value has been taken, closing the other half of
        /// the move begun by the constructor. The compiler's temporary is dead at this point either way, so
        /// this buys nothing on the happy path - it is here so that any future caller who names the handler
        /// itself finds an empty one rather than a second owner of a live pooled array.
        /// </para>
        /// <para>
        /// An extension with an explicit <c>ref</c> parameter, not an instance method: as an instance
        /// method the compiler must pass <c>ref this</c> into the handler's constructor and then refuses
        /// the call (CS8350/CS8352), because it cannot see that the reference does not escape. The
        /// <c>scoped</c> on that constructor is how it is told. The call site is identical either way.
        /// </para>
        /// </remarks>
        public static void Append(
            this ref RespRequestBuilder command,
            [InterpolatedStringHandlerArgument(nameof(command))] ref RespRequestBuilder handler)
        {
            command = handler;
            handler = default;
        }
    }
}
