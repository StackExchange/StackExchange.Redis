using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Protocol
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Turns a reply into a <see cref="RespReply"/> that <b>holds the reply's
    /// buffer</b>, rather than copying anything out of it.
    /// </summary>
    /// <typeparam name="TReply">The reply shape to construct.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>This is the one public door onto buffer retention</b>, and it is narrow on purpose. Retaining a
    /// reply's buffer is safe only for a result that cannot write through it, which is why
    /// <c>IRespPayloadHandler</c> is internal - judging that is a privilege the library keeps. The
    /// constraint here <i>is</i> that judgement, expressed in the type system: a
    /// <see cref="RespReply"/> exposes no way to write through the buffer, and gives it back exactly once.
    /// So anything that satisfies the constraint is already safe, and a library building on this one can
    /// define its own reply shapes without the privilege being handed out generally.
    /// </para>
    /// <para>
    /// <b>A delegate rather than a <c>new()</c> constraint.</b> <c>new T()</c> under a constraint does not
    /// compile to a <c>newobj</c> - it goes through <c>Activator.CreateInstance&lt;T&gt;</c>, which is no
    /// faster than invoking a cached delegate - and it would force two-phase construction: an object that
    /// exists before it has a buffer, so every accessor would need an initialised-guard for a state the
    /// type otherwise never has. One static readonly instance of this per reply shape costs nothing per
    /// call.
    /// </para>
    /// </remarks>
    public sealed class RespReplyHandler<TReply> : IRespHandler<TReply>, IRespPayloadHandler<TReply>
        where TReply : RespReply
    {
        private readonly Func<RespPayload, TReply> _factory;

        /// <summary>Create a handler that builds replies with <paramref name="factory"/>.</summary>
        /// <param name="factory">
        /// Builds the reply over a payload that already carries <b>one reference taken on its behalf</b>;
        /// normally just <c>static payload =&gt; new MyReply(payload)</c>.
        /// </param>
        public RespReplyHandler(Func<RespPayload, TReply> factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        /// <summary>Build the reply, handing it a reference to the payload.</summary>
        /// <param name="payload">The reply's bytes.</param>
        /// <remarks>
        /// The reference is taken <b>here</b>, so that losing the race is resolved before any reply object
        /// exists - see <see cref="RespReply(RespPayload)"/>. Losing it means the buffer is already on its
        /// way back to the pool, so this copies rather than resurrecting a count from zero, which is the
        /// one thing that must not happen.
        /// </remarks>
        TReply IRespPayloadHandler<TReply>.Parse(RespPayload payload)
        {
            if (!payload.TryRetain())
            {
                var copy = RespPayload.Create(payload.Span);
                try
                {
                    return _factory(copy);
                }
                catch
                {
                    copy.Dispose();
                    throw;
                }
            }

            try
            {
                return _factory(payload);
            }
            catch
            {
                payload.Release();
                throw;
            }
        }

        /// <summary>Never reached: a reply that holds the buffer needs the buffer, not a view of it.</summary>
        /// <remarks>
        /// The same shape as the <see cref="RespValue"/> and <c>RespResult</c> handlers, and unreachable
        /// for the same reason: a reader carries a position, not the identity of what it is reading, and
        /// these replies are defined by holding that identity. Callers route through
        /// <see cref="IRespPayloadHandler{TResult}"/> first.
        /// </remarks>
        TReply IRespHandler<TReply>.Parse(ref RespReader reader)
            => throw new NotSupportedException(
                $"{typeof(TReply).Name} holds the reply buffer, so it is parsed from the payload rather than a positioned reader.");
    }
}
