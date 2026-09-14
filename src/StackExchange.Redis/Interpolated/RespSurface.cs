using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The root of the context-based surface: one member, from which everything else
    /// hangs as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of having exactly one member is that it is the <b>last</b> addition to an interface. Once a
    /// context is reachable, new commands - ours and other libraries' - are extension members over it, and
    /// break nobody. See design notes section 9.4.
    /// </para>
    /// <para>
    /// <see cref="Context"/> returns <b>by value</b>. A <c>ref readonly</c> would save a copy of roughly
    /// four registers, and cost the ability to use the result in an <c>async</c> method - which is the only
    /// kind of method this surface has.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public interface IRespTarget
    {
        /// <summary>The context commands are composed and sent through.</summary>
        RespContext Context { get; }
    }

    /// <summary>EXPERIMENTAL SPIKE. Reply handlers for the prototype command surface.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespHandlers
    {
        /// <summary>Reads a bulk string reply as a <see cref="RedisValue"/>; null stays null.</summary>
        public static IRespHandler<RedisValue> Value { get; } = new ValueHandler();

        /// <summary>Reads a simple-string reply as success.</summary>
        public static IRespHandler<bool> Ok { get; } = new OkHandler();

        /// <summary>Checks the reply for a server error, and reads nothing else.</summary>
        /// <remarks>
        /// What a command with no result still has to do. Without it a failed command would complete
        /// quietly, because there would be no value whose absence gave the game away - the error is the
        /// <i>only</i> thing such a call can report.
        /// </remarks>
        public static IRespHandler<bool> Success { get; } = new SuccessHandler();

        /// <summary>The handler used when a call does not name one; resolved by result type.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <remarks>
        /// This is what lets a command surface be one expression: most commands want the obvious handler
        /// for their result type, and naming it every time is noise. A type with no registered handler
        /// throws where the call is written, saying which type and what to do - not at the point the reply
        /// arrives.
        /// </remarks>
        internal static class Inbuilt<T>
        {
            internal static readonly IRespHandler<T>? Handler = Resolve();

            internal static IRespHandler<T> Require()
                => Handler ?? throw new InvalidOperationException(
                    $"No built-in RESP handler for '{typeof(T).Name}'; pass one explicitly.");

            private static IRespHandler<T>? Resolve()
            {
                object? handler = null;
                if (typeof(T) == typeof(RedisValue)) handler = Value;
                else if (typeof(T) == typeof(bool)) handler = Ok;
                return (IRespHandler<T>?)handler;
            }
        }

        private sealed class ValueHandler : IRespHandler<RedisValue>
        {
            public RedisValue Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.IsNull ? RedisValue.Null : reader.ReadRedisValue();
            }
        }

        private sealed class SuccessHandler : IRespHandler<bool>
        {
            public bool Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext(); // skips attributes, and throws RespException on an error element
                return true;
            }
        }

        private sealed class OkHandler : IRespHandler<bool>
        {
            public bool Parse(ReadOnlySpan<byte> response)
            {
                var reader = new RespReader(response);
                reader.MoveNext();
                return reader.Is("OK"u8);
            }
        }
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. The command surface, as extension members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape the whole design exists to enable: <c>ctx.Strings.Set(key, value)</c> reads like a
    /// built-in method, groups the surface the way Redis documents itself, and is reachable by any library -
    /// including one that is not this one - without a wrapper interface or a forked surface.
    /// </para>
    /// <para>
    /// One partial file per command group - <c>RespSurface.Strings.cs</c>, and so on - matching how Redis
    /// documents itself, and how <c>RedisDatabase</c>'s ~6k lines would have liked to be split.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static partial class RespSurface
    {
    }
}
