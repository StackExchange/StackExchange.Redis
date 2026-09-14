using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
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

    /// <summary>
    /// EXPERIMENTAL SPIKE. The string-command group: <c>target.Strings.Set(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain wrapper over one <see cref="RespContext"/> field - deliberately NOT a reinterpret-cast of a
    /// layout-compatible struct. Holding exactly one field of that type makes the layout identical <i>by
    /// construction</i>, so the wrapper IS the pun, enforced by the compiler and with no <c>Unsafe</c>. A
    /// by-value pun would copy the same bytes anyway; only a <c>ref</c> pun avoids the copy, and that
    /// requires a stable address, which drags <c>ref readonly</c> and its lifetime rules into every caller
    /// to save a few register moves ahead of a network round trip.
    /// </para>
    /// <para>
    /// Not a <c>ref struct</c>, for the same reason <see cref="RespContext"/> is not: these have to survive
    /// an <c>await</c>.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespStrings
    {
        private readonly RespContext _context;

        /// <summary>Group the string commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespStrings(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    /// <summary>EXPERIMENTAL SPIKE. Reply handlers for the prototype command surface.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespHandlers
    {
        /// <summary>Reads a bulk string reply as a <see cref="RedisValue"/>; null stays null.</summary>
        public static IRespHandler<RedisValue> Value { get; } = new ValueHandler();

        /// <summary>Reads a simple-string reply as success.</summary>
        public static IRespHandler<bool> Ok { get; } = new OkHandler();

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
    /// This is the shape the whole design exists to enable: <c>ctx.Strings.Set(key, value)</c> reads like a
    /// built-in method, groups the surface the way Redis documents itself, and is reachable by any library -
    /// including one that is not this one - without a wrapper interface or a forked surface.
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespSurface
    {
        extension(IRespTarget target)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The string commands.</summary>
            public RespStrings Strings => new(context);
        }

        extension(RespStrings strings)
        {
            /// <summary>GET.</summary>
            /// <param name="key">The key to read.</param>
            /// <param name="flags">Command flags.</param>
            public ValueTask<RedisValue> Get(RedisKey key, CommandFlags flags = CommandFlags.None)
                => strings.Context.SendAsync<RedisValue>(
                    $"{RedisCommand.GET}{key}", flags.WithRetryCategory(CommandFlags.CommandRetryReadOnly));

            /// <summary>SET, in full: expiration and value condition included.</summary>
            /// <param name="key">The key to write.</param>
            /// <param name="value">The value to write.</param>
            /// <param name="expiry">When the key should expire; default for no expiration.</param>
            /// <param name="when">The condition the write is subject to; default to write unconditionally.</param>
            /// <param name="flags">Command flags.</param>
            /// <remarks>
            /// <para>
            /// Deliberately the <b>most complicated</b> command in the spike, because it is the one that
            /// tests the design rather than demonstrating it: between them <paramref name="expiry"/> and
            /// <paramref name="when"/> render anywhere from zero to five extra arguments, so the command's
            /// arity is not known until run time.
            /// </para>
            /// <para>
            /// It is still one straight line of writing. The legacy builder
            /// (<c>RedisDatabase.GetStringSetMessage</c>) is a ~17-branch decision tree, and most of those
            /// branches are not about Redis at all - they pick between fixed-arity <c>Message.Create</c>
            /// overloads, one branch per token count, which is a cost the interpolated form simply does not
            /// have.
            /// </para>
            /// <para>
            /// <b>It emits the canonical <c>SET</c> and nothing else</b>, where the legacy builder also
            /// reaches for <c>SETNX</c>, <c>SETEX</c> and <c>PSETEX</c>. <c>SETEX</c>/<c>PSETEX</c> are pure
            /// arity relics - identical semantics and reply to <c>SET ... EX n</c>. <c>SETNX</c> is
            /// <b>not</b>: it replies <c>:1</c>/<c>:0</c> where <c>SET ... NX</c> replies <c>+OK</c>/nil, so
            /// collapsing it here is a real divergence from the old surface, taken deliberately -
            /// <c>SET ... NX</c> has been available since 2.6.12 and one reply shape beats two.
            /// </para>
            /// <para>
            /// <b>Condition before expiration</b>, which is the documented grammar:
            /// <c>SET key value [NX|XX|IFEQ cmp] [GET] [EX s|PX ms|EXAT|PXAT|KEEPTTL]</c>. Redis itself
            /// parses the tail as an order-insensitive loop - which is how the legacy builder gets away with
            /// emitting <c>EX n XX</c> - but other RESP servers need not be as forgiving, and matching the
            /// documentation costs nothing.
            /// </para>
            /// <para>
            /// The retry category comes from the condition: a conditional write is checked, an
            /// unconditional one is last-wins. <see cref="ValueCondition.RetryCategory"/> returns
            /// <see cref="CommandFlags.None"/> for "no opinion", and <c>WithRetryCategory</c> is first-wins,
            /// so a caller who names a category still keeps it.
            /// </para>
            /// </remarks>
            public ValueTask<bool> Set(
                RedisKey key,
                RedisValue value,
                Expiration expiry = default,
                ValueCondition when = default,
                CommandFlags flags = CommandFlags.None)
            {
                var context = strings.Context;
                flags = flags.WithRetryCategory(when.RetryCategory)
                             .WithRetryCategory(CommandFlags.CommandRetryWriteLastWins);

                var command = context.Compose($"{RedisCommand.SET}{key}{value}{when}{expiry}");
                var frame = command.Complete();
                return context.SendAsync(ref frame, flags, RespHandlers.Ok);
            }
        }
    }
}
