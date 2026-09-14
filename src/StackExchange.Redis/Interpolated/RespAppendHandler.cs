using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Appends to a command already being built, so a conditional fragment is written
    /// the same way as the command itself.
    /// </summary>
    /// <remarks>
    /// <code>
    /// var cmd = ctx.Compose($"{RedisCommand.SET}{key}{value}");
    /// if (withTtl) cmd.Append($"{RespLiterals.EX}{ttl}");
    /// using var frame = ctx.Execute(ref cmd);
    /// </code>
    /// <para>
    /// <b>It moves rather than proxies.</b> The obvious design - hold <c>ref RespCommandHandler</c> and
    /// forward each call - does not compile on <i>any</i> target: <c>CS9050, a ref field cannot refer to a
    /// ref struct</c>. That is a language rule, not a down-level runtime gap, so targeting only modern
    /// frameworks would not have helped.
    /// </para>
    /// <para>
    /// So the command is copied in, appended to, and assigned back. Both structs reference the same pooled
    /// array during that window, but only the copy is ever touched, and the original is overwritten by
    /// <c>Append</c> the moment the window closes - including when a growth
    /// inside the window swapped the array. Move semantics, not sharing; nothing is copied but the struct
    /// itself, and no second buffer is rented.
    /// </para>
    /// </remarks>
    /// <summary>EXPERIMENTAL SPIKE. Appending to a command already being built.</summary>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public static class RespAppend
    {
        /// <summary>
        /// Append more arguments, written the same way as the command itself.
        /// </summary>
        /// <param name="command">The command being built.</param>
        /// <param name="handler">The fragment; supplied by the compiler from an interpolated string.</param>
        /// <remarks>
        /// An extension rather than an instance method on <see cref="RespCommandHandler"/>: as an instance
        /// method the compiler must pass <c>ref this</c> into the handler's constructor, and then refuses
        /// the call outright (CS8350/CS8352), because it cannot see that the reference does not escape. As
        /// an explicit <c>scoped ref</c> parameter it can. The call site is identical either way.
        /// </remarks>
        public static void Append(
            this ref RespCommandHandler command,
            [InterpolatedStringHandlerArgument(nameof(command))] ref RespAppendHandler handler)
            => command = handler.Take();
    }

    /// <summary>
    /// EXPERIMENTAL SPIKE. Collects the arguments of an <c>Append</c> onto the command being built.
    /// </summary>
    /// <remarks>See <see cref="RespAppend"/> for the intended usage; this type is the compiler's.</remarks>
    [InterpolatedStringHandler]
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public ref struct RespAppendHandler
    {
        private RespCommandHandler _inner;

        /// <summary>Begin appending to a command.</summary>
        /// <param name="literalLength">Total length of the literal segments; compiler-supplied.</param>
        /// <param name="formattedCount">Number of holes; compiler-supplied.</param>
        /// <param name="target">The command being built; moved in, and moved back out by <c>Append</c>.</param>
        /// <remarks>
        /// <c>scoped</c> is what makes this legal: without it the compiler must assume the constructor
        /// might store the reference, and refuses the call (CS8350/CS8352). It cannot - the command is
        /// copied by value - and <c>scoped</c> is how that is said.
        /// </remarks>
        public RespAppendHandler(int literalLength, int formattedCount, scoped ref RespCommandHandler target)
        {
            _ = literalLength;
            _ = formattedCount;
            _inner = target;
        }

        /// <inheritdoc cref="RespCommandHandler.AppendLiteral"/>
        /// <param name="value">The literal text.</param>
        public void AppendLiteral(string value) => _inner.AppendLiteral(value);

        /// <summary>Append a key: prefixed, marked for invalidation, and folded into the cluster slot.</summary>
        /// <param name="value">The key.</param>
        public void AppendFormatted(RedisKey value) => _inner.AppendFormatted(value);

        /// <summary>Append a value.</summary>
        /// <param name="value">The value.</param>
        public void AppendFormatted(RedisValue value) => _inner.AppendFormatted(value);

        /// <summary>Append a channel.</summary>
        /// <param name="value">The channel.</param>
        public void AppendFormatted(RedisChannel value) => _inner.AppendFormatted(value);

        /// <summary>Append a pre-framed fragment.</summary>
        /// <param name="value">The fragment.</param>
        public void AppendFormatted(RespFragment value) => _inner.AppendFormatted(value);

        /// <summary>Append a resolved command name.</summary>
        /// <param name="value">The command.</param>
        public void AppendFormatted(RespCommand value) => _inner.AppendFormatted(value);

        /// <summary>Hand the command back, with everything appended.</summary>
        internal RespCommandHandler Take() => _inner;
    }
}
