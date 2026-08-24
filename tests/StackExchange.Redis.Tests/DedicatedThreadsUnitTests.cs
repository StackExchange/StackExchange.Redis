using RESPite.Streams;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The <c>DedicatedThreads</c> feature flag, which takes this library's reader and writer off the global
/// thread-pool and onto threads it owns.
/// </summary>
/// <remarks>
/// Exercised through <see cref="PhysicalConnection.ResolveWriteMode"/> rather than by connecting: the flag is
/// process-wide static, so a test that set it and then connected would be racing every other test in the run.
/// The policy is the part worth pinning anyway - that sync-mode is what owns the threads is a fact about
/// <c>SwitchableBufferedStreamWriter</c>, and is covered by <c>BufferedStreamWriterTests</c>.
/// </remarks>
public class DedicatedThreadsUnitTests
{
    [Theory]
    [InlineData(ConnectionType.Interactive)]
    [InlineData(ConnectionType.Subscription)]
    public void WithoutTheFlag_NothingChanges(ConnectionType connectionType)
    {
        var expected = connectionType is ConnectionType.Subscription
            ? BufferedStreamWriter.WriteMode.Async
            : BufferedStreamWriter.WriteMode.Default;

        Assert.Equal(expected, PhysicalConnection.ResolveWriteMode(connectionType, BufferedStreamWriter.WriteMode.Default, dedicatedThreads: false));
    }

    [Fact]
    public void WithTheFlag_InteractiveConnectionsOwnTheirThreads()
        => Assert.Equal(
            BufferedStreamWriter.WriteMode.Sync,
            PhysicalConnection.ResolveWriteMode(ConnectionType.Interactive, BufferedStreamWriter.WriteMode.Default, dedicatedThreads: true));

    /// <summary>
    /// Pub/sub keeps its own rule: the flag is not a licence to reverse a deliberate policy.
    /// </summary>
    [Fact]
    public void WithTheFlag_SubscriptionsAreUnaffected()
        => Assert.Equal(
            BufferedStreamWriter.WriteMode.Async,
            PhysicalConnection.ResolveWriteMode(ConnectionType.Subscription, BufferedStreamWriter.WriteMode.Default, dedicatedThreads: true));

    /// <summary>
    /// The flag promotes an <em>unstated</em> preference, so an explicit choice still wins - otherwise enabling
    /// it under support guidance would silently undo whatever had been configured to get that far.
    /// </summary>
    /// <remarks>
    /// Takes the test project's public <see cref="WriteMode"/> mirror rather than the internal enum, which a
    /// public test signature cannot name (CS0051) - the same reason that mirror exists at all.
    /// </remarks>
    [Theory]
    [InlineData(WriteMode.Async)]
    [InlineData(WriteMode.Pipe)]
    [InlineData(WriteMode.Sync)]
    public void WithTheFlag_AnExplicitModeIsKept(WriteMode configured)
    {
        var expected = (BufferedStreamWriter.WriteMode)configured;
        Assert.Equal(expected, PhysicalConnection.ResolveWriteMode(ConnectionType.Interactive, expected, dedicatedThreads: true));
    }

    /// <summary>
    /// The flag is reachable by name, case-insensitively, exactly as <c>preventthreadtheft</c> is - which is
    /// how it will actually be typed into an application's startup under support guidance.
    /// </summary>
    [Fact]
    public void TheFlagIsSettableByName()
    {
        Assert.False(ConnectionMultiplexer.GetFeatureFlag("DedicatedThreads"));
        try
        {
            ConnectionMultiplexer.SetFeatureFlag("dedicatedthreads", true);
            Assert.True(ConnectionMultiplexer.GetFeatureFlag("DedicatedThreads"));
        }
        finally
        {
            ConnectionMultiplexer.SetFeatureFlag("DedicatedThreads", false);
        }

        Assert.False(ConnectionMultiplexer.GetFeatureFlag("DedicatedThreads"));
    }
}
