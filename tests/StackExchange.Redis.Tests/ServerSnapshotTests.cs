using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using Xunit;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis.Tests;

public class ServerSnapshotTests
{
    [Fact]
    [SuppressMessage("Assertions", "xUnit2012:Do not use boolean check to check if a value exists in a collection", Justification = "Explicit testing")]
    [SuppressMessage("Assertions", "xUnit2013:Do not use equality check to check for collection size.", Justification = "Explicit testing")]
    [SuppressMessage("Assertions", "xUnit2029:Do not use Empty() to check if a value does not exist in a collection", Justification = "Explicit testing")]
    [SuppressMessage("Performance", "CA1829:Use Length/Count property instead of Count() when available", Justification = "Explicit testing")]
    [SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method", Justification = "Explicit testing")]
    public void EmptyBehaviour()
    {
        var snapshot = ServerSnapshot.Empty;
        Assert.Same(snapshot, snapshot.Add(null!));

        Assert.Equal(0, snapshot.Count);
        Assert.Equal(0, ManualCount(snapshot));
        Assert.Equal(0, ManualCount(snapshot, static _ => true));
        Assert.Equal(0, ManualCount(snapshot, static _ => false));

        Assert.Equal(0, Enumerable.Count(snapshot));
        Assert.Equal(0, Enumerable.Count(snapshot, static _ => true));
        Assert.Equal(0, Enumerable.Count(snapshot, static _ => false));

        Assert.False(Enumerable.Any(snapshot));
        Assert.False(snapshot.Any());

        Assert.False(Enumerable.Any(snapshot, static _ => true));
        Assert.False(snapshot.Any(static _ => true));
        Assert.False(Enumerable.Any(snapshot, static _ => false));
        Assert.False(snapshot.Any(static _ => false));

        Assert.Empty(snapshot);
        Assert.Empty(Enumerable.Where(snapshot, static _ => true));
        Assert.Empty(snapshot.Where(static _ => true));
        Assert.Empty(Enumerable.Where(snapshot, static _ => false));
        Assert.Empty(snapshot.Where(static _ => false));

        Assert.Empty(snapshot.Where(CommandFlags.DemandMaster));
        Assert.Empty(snapshot.Where(CommandFlags.DemandReplica));
        Assert.Empty(snapshot.Where(CommandFlags.None));
        Assert.Empty(snapshot.Where(CommandFlags.FireAndForget | CommandFlags.NoRedirect | CommandFlags.NoScriptCache));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 0)]
    [InlineData(5, 3)]
    [InlineData(5, 5)]
    [SuppressMessage("Assertions", "xUnit2012:Do not use boolean check to check if a value exists in a collection", Justification = "Explicit testing")]
    [SuppressMessage("Assertions", "xUnit2029:Do not use Empty() to check if a value does not exist in a collection", Justification = "Explicit testing")]
    [SuppressMessage("Assertions", "xUnit2030:Do not use Assert.NotEmpty to check if a value exists in a collection", Justification = "Explicit testing")]
    [SuppressMessage("Performance", "CA1829:Use Length/Count property instead of Count() when available", Justification = "Explicit testing")]
    [SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method", Justification = "Explicit testing")]
    public void NonEmptyBehaviour(int count, int replicaCount)
    {
        var snapshot = ServerSnapshot.Empty;
        for (int i = 0; i < count; i++)
        {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
            var dummy = (ServerEndPoint)FormatterServices.GetSafeUninitializedObject(typeof(ServerEndPoint));
#pragma warning restore SYSLIB0050 // Type or member is obsolete
            dummy.IsReplica = i < replicaCount;
            snapshot = snapshot.Add(dummy);
        }

        Assert.Equal(count, snapshot.Count);
        Assert.Equal(count, ManualCount(snapshot));
        Assert.Equal(count, ManualCount(snapshot, static _ => true));
        Assert.Equal(0, ManualCount(snapshot, static _ => false));
        Assert.Equal(replicaCount, ManualCount(snapshot, static s => s.IsReplica));

        Assert.Equal(count, Enumerable.Count(snapshot));
        Assert.Equal(count, Enumerable.Count(snapshot, static _ => true));
        Assert.Equal(0, Enumerable.Count(snapshot, static _ => false));
        Assert.Equal(replicaCount, Enumerable.Count(snapshot, static s => s.IsReplica));

        Assert.True(Enumerable.Any(snapshot));
        Assert.True(snapshot.Any());

        Assert.True(Enumerable.Any(snapshot, static _ => true));
        Assert.True(snapshot.Any(static _ => true));
        Assert.False(Enumerable.Any(snapshot, static _ => false));
        Assert.False(snapshot.Any(static _ => false));

        Assert.NotEmpty(snapshot);
        Assert.NotEmpty(Enumerable.Where(snapshot, static _ => true));
        Assert.NotEmpty(snapshot.Where(static _ => true));
        Assert.Empty(Enumerable.Where(snapshot, static _ => false));
        Assert.Empty(snapshot.Where(static _ => false));

        Assert.Equal(snapshot.Count - replicaCount, snapshot.Where(CommandFlags.DemandMaster).Count());
        Assert.Equal(replicaCount, snapshot.Where(CommandFlags.DemandReplica).Count());
        Assert.Equal(snapshot.Count, snapshot.Where(CommandFlags.None).Count());
        Assert.Equal(snapshot.Count, snapshot.Where(CommandFlags.FireAndForget | CommandFlags.NoRedirect | CommandFlags.NoScriptCache).Count());
    }

    private static int ManualCount(ServerSnapshot snapshot, Func<ServerEndPoint, bool>? predicate = null)
    {
        // ^^^ tests the custom iterator implementation
        int count = 0;
        if (predicate is null)
        {
            foreach (var item in snapshot)
            {
                count++;
            }
        }
        else
        {
            foreach (var item in snapshot.Where(predicate))
            {
                count++;
            }
        }
        return count;
    }
}
