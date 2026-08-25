using System;
using System.Threading.Tasks;
using Xunit;

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
    /// <remarks>
    /// The ambient test cancellation token is passed to each delay, so the framework's own timeout wins and
    /// wins *promptly* - a cancelled test stops inside the current poll interval rather than after it, and a
    /// hung predicate surfaces as the framework's cancellation rather than as this method quietly reporting
    /// false. The local timeout is only a backstop for tests with no ambient limit.
    /// </remarks>
    public static async Task<bool> UntilAsync(Func<bool> predicate, int timeoutMilliseconds = 5000, int pollMilliseconds = 25)
    {
        if (predicate()) return true; // usually already true, so don't pay a delay for the common case

        var cancellationToken = TestContext.Current.CancellationToken;
        for (int i = 0; i < timeoutMilliseconds / pollMilliseconds; i++)
        {
            await Task.Delay(pollMilliseconds, cancellationToken).ConfigureAwait(false);
            if (predicate()) return true;
        }

        return false;
    }
}
