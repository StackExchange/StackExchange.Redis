using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The publish/subscribe commands.
/// </summary>
/// <remarks>
/// <inheritdoc cref="HyperLogLog" path="/remarks/para[1]"/>
/// </remarks>
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see HyperLogLog")]
public static partial class PubSub
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The pub/sub group: <c>target.PubSub.PublishAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only <c>PUBLISH</c> so far, and it is on the database surface rather than the subscriber one</b>
/// because that is where <see cref="IDatabaseAsync.PublishAsync"/> has always lived. Subscribing is a
/// different shape - it registers a handler and outlives the call - so it is not simply the next method
/// in this file; it arrives when the subscriber surface moves.
/// </para>
/// <para>
/// <b>Publishing is a command, not a side channel.</b> It has to go through the context like anything
/// else, because <c>PUBLISH</c> inside a <c>MULTI</c> is legal and <see cref="IBatch"/>/
/// <see cref="ITransaction"/> both expose it: a shortcut that routed straight to a subscriber would run
/// it outside the transaction that queued it, which is a silent semantic change rather than a
/// missing feature.
/// </para>
/// </remarks>
public readonly struct RespPubSub
{
    /// <summary>Group the pub/sub commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespPubSub(RespContext context) => Context = context;

    /// <summary>The context these commands are sent through.</summary>
    /// <remarks><inheritdoc cref="RespHyperLogLog.Context" path="/remarks"/></remarks>
    internal readonly RespContext Context;
}

public static partial class RespDatabaseExtensions
{
    extension(in RespDatabaseContext context)
    {
        /// <summary>The pub/sub commands.</summary>
        public RespPubSub PubSub => new(context.Raw);
    }
}
