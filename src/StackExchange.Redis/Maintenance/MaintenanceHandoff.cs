using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// What to do about a <c>MOVING</c> notification.
/// </summary>
internal enum HandoffAction
{
    /// <summary>Nothing useful is available; let the server close the socket and reconnect then.</summary>
    None,

    /// <summary>Drop our connections so they re-establish against the replacement address.</summary>
    Recycle,

    /// <summary>
    /// The server named where to go; point the next connection at it.
    /// </summary>
    /// <remarks>
    /// Not "add an endpoint and retire this one": the endpoint keeps its identity and its TLS host, and only
    /// the socket target changes. See <c>ServerEndPoint.HandoffTarget</c>.
    /// </remarks>
    MoveTo,
}

/// <summary>
/// The outcome of deciding how to hand off, and why - the reason is logged either way.
/// </summary>
internal readonly struct HandoffDecision(HandoffAction action, EndPoint? target, string reason)
{
    public HandoffAction Action { get; } = action;

    public EndPoint? Target { get; } = target;

    public string Reason { get; } = reason;

    public override string ToString() => Target is null ? $"{Action}: {Reason}" : $"{Action} -> {Target}: {Reason}";
}

/// <summary>
/// Decides what a <c>MOVING</c> notification means for this connection, and where to go.
/// </summary>
/// <remarks>
/// Separated from the acting so that it can be tested exhaustively without a server: the caller supplies the
/// endpoint, the address it is currently on, the announced window and a resolver, and gets back an action.
/// <para>
/// The dispatch turns on the *form* of the endpoint, which is the thing that decides whether a handoff is even
/// possible - and the answer corrects an earlier assumption that <c>MOVING</c> should reuse the endpoint
/// retirement path:
/// </para>
/// <list type="bullet">
/// <item>
/// A hostname endpoint with no named successor - the case every observed <c>MOVING</c> has been - keeps its
/// <see cref="ServerEndPoint"/>: only the address behind the name moves. Retiring the endpoint would delete our
/// only way of reaching the deployment. So the action is to recycle the connections once DNS has moved, which
/// re-resolves them.
/// </item>
/// <item>
/// An address endpoint with a named successor is genuinely a different endpoint, so the topology has to be
/// re-read. Never observed: eleven routes have all carried an explicit null, including cases where the server
/// had already chosen the replacement node. Treat this branch as code that must exist rather than code that is
/// exercised.
/// </item>
/// <item>
/// An address endpoint with no successor has nothing to re-resolve and nowhere named to go. Doing nothing is
/// correct: the socket closes, the reconnect happens, and the relaxed window covers it.
/// </item>
/// </list>
/// </remarks>
internal static class MaintenanceHandoff
{
    internal static async Task<HandoffDecision> DecideAsync(
        EndPoint endpoint,
        EndPoint? successor,
        IPAddress? currentAddress,
        TimeSpan window,
        TimeSpan pollInterval,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (successor is not null)
        {
            // Straight there: no DNS involved, which is the whole value of the field. Measured on RS 8.0.22,
            // DNS trails a MOVING by 4.4s to 18.7s while the socket closes at 15.7s to 19.1s, so a named
            // successor is the difference between moving immediately and possibly not moving in time at all.
            return new HandoffDecision(HandoffAction.MoveTo, successor, "the server named a replacement endpoint");
        }

        if (endpoint is not DnsEndPoint dns)
        {
            return new HandoffDecision(
                HandoffAction.None,
                null,
                $"{Format.ToString(endpoint)} is an address, not a name, and no replacement was named: nothing to re-resolve");
        }

        if (currentAddress is null)
        {
            // Without knowing where we are, "has it moved" is unanswerable - and guessing would mean recycling
            // onto whatever DNS says right now, which for the first several seconds is the address being retired.
            return new HandoffDecision(HandoffAction.None, null, "the address of the current connection is unknown");
        }

        var replacement = await AdvertisedAddressProbe.ProbeAsync(
            dns, currentAddress, window, pollInterval, resolve, log, cancellationToken).ForAwait();

        return replacement is null
            ? new HandoffDecision(
                HandoffAction.None,
                null,
                $"{dns.Host} still resolved only to {currentAddress} when the window expired")
            : new HandoffDecision(
                HandoffAction.Recycle,
                replacement,
                $"{dns.Host} now resolves to {replacement}");
    }

    /// <summary>
    /// How long to wait before probing, so a fleet does not resolve in lockstep.
    /// </summary>
    /// <remarks>
    /// A *fraction* of the announced window rather than a fixed delay, which is the difference from the refresh
    /// jitter elsewhere. Windows are not always generous - the shard notifications have been measured announcing
    /// two seconds - so a flat one-second jitter could spend half of one, and on a short window the right amount
    /// of jitter is almost none. Capped as well as scaled, because a long window does not justify a long wait
    /// when DNS has been seen moving after four seconds.
    /// </remarks>
    internal static TimeSpan GetJitter(TimeSpan window, Random random)
    {
        if (window <= TimeSpan.Zero) return TimeSpan.Zero;

        var tenth = window.TotalMilliseconds / 10;
        var capped = Math.Min(tenth, 1000);
        return TimeSpan.FromMilliseconds(random.Next(0, (int)Math.Max(capped, 1)));
    }
}
