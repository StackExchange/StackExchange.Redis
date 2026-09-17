using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The root a caller holds for a reply whose contents are <b>windows over the reply
/// buffer</b> rather than materialised objects: one disposable object at the top, uncounted struct views
/// inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What derived types add is meaning, not mechanism.</b> This holds the buffer, guards it, and hands
/// it back exactly once; a concrete reply adds the typed windows that say what the bytes are. That split
/// is why this is a base class and not an interface - the mechanism is real code, shared.
/// </para>
/// <para>
/// <b>The lifetime rule, stated once for the whole family:</b> every window reachable from a reply is
/// valid for exactly as long as the reply is. A <see cref="RespValue"/> or
/// <see cref="RespAggregate{T}"/> taken out of one and used after <see cref="Dispose"/> is reading a
/// buffer that is back in the pool - so <c>using</c> the reply, and not letting its windows escape that
/// scope, is the whole of the contract. Anything that must outlive the reply is materialised first, with
/// a <c>To*()</c> that says so in its name.
/// </para>
/// <para>
/// <b>Derivation is supported</b>, including from outside this library: a client building on this one -
/// adding a module's commands, say - should be able to define its own reply shapes rather than being
/// limited to the ones shipped here. That is the whole point of the exercise, so the constructor is
/// <see langword="protected"/> rather than <c>private protected</c>.
/// </para>
/// <para>
/// <b>What that costs, and the rule that pays for it.</b> External derivation freezes this type's shape:
/// an <c>abstract</c> member added later would break every derived type. So none will be added. If this
/// ever needs to grow a member that not every reply can answer, it grows a <c>virtual</c> that throws
/// alongside a capability twin, or a <c>Try*</c> - the arrangement <see cref="System.IO.Stream"/> has
/// used for a quarter of a century. That trades compile-time totality for the ability to evolve at all,
/// which is the right way round for a base type other people derive from.
/// </para>
/// </remarks>
public abstract class RespReply : IDisposable
{
    private RespPayload? _payload;

    /// <summary>Create a reply over a payload.</summary>
    /// <param name="payload">
    /// The reply's bytes, with <b>one reference already taken on this reply's behalf</b>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The reference is handed over, not acquired here.</b> Taking one can <i>fail</i> - a cached
    /// payload may be evicted between the lookup and the read, which is a miss rather than an error - and
    /// a constructor is the wrong place to discover that. So the race is resolved before any reply object
    /// exists, and by the time a derived constructor runs the bytes are guaranteed to be there.
    /// </para>
    /// <para>
    /// The consequence for a derived type: it never calls <c>TryRetain</c> or <c>Release</c>. It takes
    /// the payload, reads what it needs, and lets <see cref="Dispose"/> give the one reference back.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">If <paramref name="payload"/> is <see langword="null"/>.</exception>
    protected RespReply(RespPayload payload)
        => _payload = payload ?? throw new ArgumentNullException(nameof(payload));

    /// <summary>Whether this reply has given its buffer back.</summary>
    public bool IsDisposed => Volatile.Read(ref _payload) is null;

    /// <summary>The reply's bytes, for as long as this reply holds them.</summary>
    /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
    protected RespPayload Payload => _payload ?? ThrowDisposed();

    /// <summary>
    /// A reader over the whole reply - the escape hatch for a caller who wants to parse it themselves.
    /// </summary>
    /// <remarks>
    /// Public, where <see cref="Payload"/> is not, because a <c>ref struct</c> reader cannot escape the
    /// scope it is used in - so this lends the bytes without handing out the buffer's identity. Sharing
    /// that identity is how zero-copy is possible at all, and it stays a privilege of code that can be
    /// held to the no-writes rule.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">If this reply has already been disposed.</exception>
    public RespReader GetReader() => Payload.GetReader();

    /// <summary>Give the reply's buffer back.</summary>
    /// <remarks>
    /// <b>Not <c>virtual</c>, and idempotent.</b> The exchange is what makes disposing twice safe rather
    /// than double-releasing a pooled buffer - the failure that is invisible from outside until somebody
    /// else's data appears in your reply. A derived type with something of its own to release overrides
    /// <see cref="OnDisposed"/>; it cannot take the release itself away.
    /// </remarks>
    public void Dispose()
    {
        var payload = Interlocked.Exchange(ref _payload, null);
        if (payload is null) return; // already disposed; say nothing, do nothing

        try
        {
            OnDisposed();
        }
        finally
        {
            payload.Release();
        }
    }

    /// <summary>
    /// Called once, when this reply is disposed and before its buffer goes back, for a derived type that
    /// holds something of its own.
    /// </summary>
    /// <remarks>
    /// Defined now rather than added when something needs it: this type is derived from outside the
    /// library, so the set of virtuals is easier to get right up front than to extend politely later.
    /// </remarks>
    protected virtual void OnDisposed()
    {
    }

    [DoesNotReturn]
    private RespPayload ThrowDisposed() => throw new ObjectDisposedException(GetType().Name);
}
