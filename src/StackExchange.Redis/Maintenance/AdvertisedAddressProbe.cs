using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// Asks DNS the two questions a handoff needs: what replaces the address we are leaving, and are we still
/// advertised at all.
/// </summary>
/// <remarks>
/// A poll rather than a lookup, and that is the whole point. Measured on Redis Enterprise (2026-08-28), with
/// times relative to the notification: the server-side endpoint moves at +8.6s, DNS follows at +9.7s and +4.4s
/// across two runs, and the sockets close at +18.4s and +16.6s - against a declared 15s grace and a 5s record
/// TTL. So resolving immediately returns <em>the address being retired</em>, in every run observed, and a
/// client that treats the first answer as authoritative hands off to the node it was told to leave.
/// <para>
/// There is no way to observe the intermediate state from outside: the endpoint has moved server-side well
/// before DNS reflects it, and nothing tells a client which of those has happened. Probing until the answer
/// changes is the only mechanism available, and the short TTL is what makes it work - several attempts fit
/// inside the window.
/// </para>
/// <para>
/// The rule is "take any address that is not the one being retired", and that is doing more work than it
/// looks. A Redis Cloud hostname usually carries <em>several</em> A records - measured 2026-08-28: 2 for
/// <c>all-nodes</c>, 3 for <c>all-master-shards</c>, 1 for <c>single</c>, all on a 5s TTL - and the count
/// follows actual proxy *placement* rather than the policy name, so a multi-proxy database whose shards happen
/// to share a node still resolves to one address. With several records the first resolution already names a
/// live sibling proxy, so this returns immediately and steps sideways rather than waiting: any proxy of the
/// same database serves the same data, so a sibling now beats the replacement in nine seconds. The poll only
/// engages when the record names nothing but the address being retired - the single-address case, which is
/// exactly where waiting is the only option available.
/// </para>
/// <para>
/// Deliberately free of jitter, clocks-by-configuration and connection state, so that it can be tested
/// exhaustively without a server: the caller supplies the resolver, the interval and the budget. Jitter
/// belongs at the call site, where the existing refresh jitter lives.
/// </para>
/// </remarks>
internal static class AdvertisedAddressProbe
{
    /// <summary>
    /// Polls DNS until it stops naming <paramref name="retiring"/>, or until the window runs out.
    /// </summary>
    /// <returns>
    /// The replacement endpoint, or <c>null</c> if the window expired without DNS moving - in which case the
    /// caller has learned something useful and should do nothing: the server will close the socket, and the
    /// relaxed timeout window is what covers the reconnect that follows.
    /// </returns>
    internal static async Task<EndPoint?> ProbeAsync(
        DnsEndPoint endpoint,
        IPAddress retiring,
        TimeSpan window,
        TimeSpan pollInterval,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (retiring is null) throw new ArgumentNullException(nameof(retiring));
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));

        // A non-positive window is not an error: a notification can arrive with nothing left of its budget
        // (the shard notifications legitimately carry zero or negative times), and "act now" means one attempt
        // rather than none.
        var deadline = Environment.TickCount + (int)Math.Max(window.TotalMilliseconds, 0);
        int attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            IPAddress[]? addresses = null;
            try
            {
                addresses = await resolve(endpoint.Host, cancellationToken).ForAwait();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // a DNS blip mid-handoff is exactly when we can least afford to give up; keep trying until
                // the window says otherwise
                log?.Invoke($"MOVING: resolve attempt {attempt} for {endpoint.Host} failed: {ex.Message}");
            }

            if (addresses is not null)
            {
                IPAddress? candidate = null;
                bool retiringStillAdvertised = false;
                foreach (var address in addresses)
                {
                    if (address.Equals(retiring)) retiringStillAdvertised = true;
                    else candidate ??= address;
                }

                if (candidate is not null)
                {
                    // Two operationally different outcomes, worth distinguishing in a log somebody reads while
                    // debugging a handoff: we either stepped sideways to a proxy that was already there, or the
                    // record itself has moved on to the replacement.
                    log?.Invoke(retiringStillAdvertised
                        ? $"MOVING: {endpoint.Host} still advertises {retiring}; moving to sibling {candidate} (attempt {attempt})"
                        : $"MOVING: {endpoint.Host} now resolves to {candidate} after {attempt} attempt(s)");
                    return new IPEndPoint(candidate, endpoint.Port);
                }

                log?.Invoke($"MOVING: {endpoint.Host} still resolves only to {retiring} (attempt {attempt}); not yet updated");
            }

            var remaining = unchecked(deadline - Environment.TickCount);
            if (remaining <= 0)
            {
                log?.Invoke($"MOVING: {endpoint.Host} never stopped resolving to {retiring} within the window");
                return null;
            }

            // never sleep past the deadline: the last attempt should land inside the window, not after it
            var delay = (int)Math.Min(pollInterval.TotalMilliseconds, remaining);
            if (delay > 0) await Task.Delay(delay, cancellationToken).ForAwait();
        }
    }

    /// <summary>
    /// Whether the address we are connected on is still one of the addresses the hostname advertises.
    /// </summary>
    /// <returns>
    /// <c>true</c> if still advertised, <c>false</c> if the record no longer names it, and <c>null</c> if DNS
    /// could not be asked - which is *not* the same as "no": a resolution failure is no reason to give up a
    /// working connection.
    /// </returns>
    /// <remarks>
    /// The trigger that needs no notification, and the reason it matters is a measured gap. On a multi-proxy
    /// database, taking a node out for maintenance announced only the data-movement pair - <c>MIGRATING</c> at
    /// +4.3s and <c>MIGRATED</c> at +16.6s, to every proxy - and then dropped the victim from DNS at +21.4s and
    /// closed its socket *silently* at +34.7s, while sibling connections stayed up past +90s. No
    /// <c>MOVING</c> was ever sent.
    /// <para>
    /// So for thirteen seconds the condition was plainly visible to anybody who asked - our address is no
    /// longer advertised - and the client's only other signal was the socket dying with no explanation. Asking
    /// this question when a <c>MIGRATED</c> arrives converts that into a controlled handoff. Note
    /// <c>MIGRATED</c> is also the notification the server *retains* and replays on connect, so a client that
    /// arrives mid-operation gets the same prompt.
    /// </para>
    /// <para>
    /// The same condition is what a support case turned on: a client kept dialling endpoints that no longer
    /// existed for 37 hours, because nothing it could observe told it to stop. Two unrelated failures, one
    /// detection rule.
    /// </para>
    /// </remarks>
    internal static async Task<bool?> IsStillAdvertisedAsync(
        DnsEndPoint endpoint,
        IPAddress current,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));

        IPAddress[] addresses;
        try
        {
            addresses = await resolve(endpoint.Host, cancellationToken).ForAwait();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"{endpoint.Host}: cannot tell whether {current} is still advertised: {ex.Message}");
            return null;
        }

        // an empty answer is "cannot tell" rather than "no": a record that momentarily resolves to nothing is
        // a DNS problem, not an instruction to abandon a connection that is working
        if (addresses is null || addresses.Length == 0)
        {
            log?.Invoke($"{endpoint.Host}: resolved to nothing; treating {current} as still advertised");
            return null;
        }

        foreach (var address in addresses)
        {
            if (address.Equals(current)) return true;
        }

        log?.Invoke($"{endpoint.Host}: no longer advertises {current}; it is being taken out of service");
        return false;
    }
}
