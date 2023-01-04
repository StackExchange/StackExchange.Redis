using System;
using System.Linq;
using System.Runtime.Serialization;
using Xunit;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis.Tests;

public class ServerSnapshotTests
{
    [Fact]
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
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void NonEmptyBehaviour(int count)
    {
        var snapshot = ServerSnapshot.Empty;
        for (int i = 0; i < count; i++)
        {
            var dummy = (ServerEndPoint)FormatterServices.GetSafeUninitializedObject(typeof(ServerEndPoint));
            snapshot = snapshot.Add(dummy);
        }

        Assert.Equal(count, snapshot.Count);
        Assert.Equal(count, ManualCount(snapshot));
        Assert.Equal(count, ManualCount(snapshot, static _ => true));
        Assert.Equal(0, ManualCount(snapshot, static _ => false));

        Assert.Equal(count, Enumerable.Count(snapshot));
        Assert.Equal(count, Enumerable.Count(snapshot, static _ => true));
        Assert.Equal(0, Enumerable.Count(snapshot, static _ => false));

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
