using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using Xunit;

namespace StackExchange.Redis.Tests;

public static class Skip
{
    public static void UnlessLongRunning()
    {
        Assert.SkipUnless(TestConfig.Current.RunLongRunning, "Skipping long-running test");
    }

    public static void IfNoConfig(string prop, [NotNull] string? value)
    {
        Assert.SkipWhen(value.IsNullOrEmpty(), $"Config.{prop} is not set, skipping test.");
    }

    internal static void IfMissingDatabase(IConnectionMultiplexer conn, int dbId)
    {
        var dbCount = conn.GetServer(conn.GetEndPoints()[0]).DatabaseCount;
        Assert.SkipWhen(dbId >= dbCount, $"Database '{dbId}' is not supported on this server.");
    }

    /// <summary>
    /// Skips the test when nothing is listening on <paramref name="host"/>:<paramref name="port"/>.
    /// </summary>
    /// <remarks>
    /// Tests that build their own connection (rather than using the shared fixture) otherwise each
    /// pay a full connect timeout before failing, which is both slow and reported as a failure
    /// instead of a skip. This is a single TCP connect per endpoint, cached for the process, so the
    /// second and subsequent tests needing an absent server skip immediately.
    /// <para>
    /// Note this deliberately only reports whether *something* is accepting connections: if a
    /// server is listening but the client cannot talk to it, that is a real failure and must stay
    /// one, so it is not covered here.
    /// </para>
    /// </remarks>
    public static void IfNoServer(string? host, int port)
    {
        Assert.SkipWhen(!ServerProbe.IsListening(host, port), $"Nothing is listening on {host}:{port}, skipping test.");
    }

    /// <summary>
    /// Skips the test when the cluster nodes are not running.
    /// </summary>
    public static void IfNoCluster()
    {
        var config = TestConfig.Current;
        IfNoServer(config.ClusterServer, config.ClusterStartPort);
    }

    /// <summary>
    /// Skips the test when the sentinel instances are not running.
    /// </summary>
    public static void IfNoSentinel()
    {
        var config = TestConfig.Current;
        IfNoServer(config.SentinelServer, config.SentinelPortA);
    }

    /// <summary>
    /// Skips the test when the failover pair is not running.
    /// </summary>
    public static void IfNoFailoverPair()
    {
        var config = TestConfig.Current;
        IfNoServer(config.FailoverPrimaryServer, config.FailoverPrimaryPort);
        IfNoServer(config.FailoverReplicaServer, config.FailoverReplicaPort);
    }
}

internal static class ServerProbe
{
    // Generous on purpose: a listening server accepts effectively instantly even on a slow or
    // heavily contended machine (the kernel completes the handshake from the backlog), so this
    // only ever waits this long when nothing is there. Being too aggressive here would risk
    // declaring a live-but-busy server absent and silently skipping tests that should have run.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<(string Host, int Port), Lazy<bool>> Cache = new();

    internal static bool IsListening(string? host, int port)
    {
        if (host.IsNullOrEmpty()) return false;

        var probe = Cache.GetOrAdd(
            (host, port),
            static key => new Lazy<bool>(() => Probe(key.Host, key.Port), LazyThreadSafetyMode.ExecutionAndPublication));
        return probe.Value;
    }

    private static bool Probe(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(host, port).Wait(ProbeTimeout);
        }
        catch
        {
            // refused, unresolvable, unreachable: all "no server here"
            return false;
        }
    }
}

public class SkipTestException(string reason) : Exception(reason)
{
    public string? MissingFeatures { get; set; }
}
