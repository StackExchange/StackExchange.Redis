using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Waiting for something that is *promptly* true rather than synchronously true.
/// </summary>
/// <remarks>
/// The case this exists for: endpoint discovery. A node learned from <c>CLUSTER SLOTS</c> during handshake is
/// registered on the connection's own path, so a test that asserts on <c>GetEndPoints()</c> the instant
/// <c>ConnectAsync</c> returns is asserting an ordering the library never promised - the endpoint collection is
/// a snapshot. It passes on a fast machine and fails on a two-core CI runner, which is the worst kind of test.
/// </remarks>
internal static class Poll
{
    /// <summary>
    /// Polls until the predicate holds, or the timeout expires; returns whether it held.
    /// </summary>
    public static async Task<bool> UntilAsync(Func<bool> predicate, int timeoutMilliseconds = 5000, int pollMilliseconds = 25)
    {
        if (predicate()) return true; // usually already true, so don't pay a delay for the common case

        for (int i = 0; i < timeoutMilliseconds / pollMilliseconds; i++)
        {
            await Task.Delay(pollMilliseconds).ConfigureAwait(false);
            if (predicate()) return true;
        }

        return false;
    }
}
