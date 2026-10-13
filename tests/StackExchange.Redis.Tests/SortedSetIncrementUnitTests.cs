using System;
using System.Collections.Generic;
using StackExchange.Redis.Tests.RoundTripUnitTests;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SortedSetIncrementUnitTests
{
    [Theory]
    [MemberData(nameof(InvalidValueConditions))]
    public void InvalidValueConditionModesThrow(ValueCondition condition)
    {
        var executor = new RoundTripExecutor("$1\r\n1\r\n");
        var db = RoundTrip.Database(executor);

        Assert.Throws<InvalidOperationException>(() =>
            db.SortedSetIncrement("key", "member", 1, condition, CommandFlags.None));

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = db.SortedSetIncrementAsync("key", "member", 1, condition, CommandFlags.None);
        });

        // refused before anything reached the wire
        Assert.Empty(executor.Frames);
    }

    public static IEnumerable<object[]> InvalidValueConditions()
    {
        yield return [ValueCondition.Equal("value")];
        yield return [ValueCondition.NotEqual("value")];
        yield return [ValueCondition.DigestEqual("value")];
        yield return [ValueCondition.DigestNotEqual("value")];
    }
}
