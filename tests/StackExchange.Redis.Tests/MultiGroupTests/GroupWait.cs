using System;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Availability;
using Xunit;

namespace StackExchange.Redis.Tests.MultiGroupTests;

internal static class GroupWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits for the group to report connected, instead of asserting it the instant
    /// <c>ConnectGroupAsync</c> returns.
    /// </summary>
    /// <remarks>
    /// Health-check probes that involve a round trip (<c>Ping</c>, <c>StringSet</c>) can still be in
    /// flight when the connect task completes, so asserting immediately is a race that a slow or
    /// contended machine loses. Waiting costs nothing when it is already connected.
    /// </remarks>
    internal static async Task AssertConnectedAsync(IConnectionGroup conn, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var watch = Stopwatch.StartNew();
        while (!conn.IsConnected && watch.Elapsed < limit)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(conn.IsConnected, $"group did not report connected within {limit.TotalSeconds:0.#}s");
    }
}
