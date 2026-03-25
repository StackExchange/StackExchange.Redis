using System;
using System.Collections.Generic;
using Xunit;

namespace StackExchange.Redis.Tests;

public class DelegateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(25)]
    public void Foo(int count)
    {
        Assert.True(Delegates.IsSupported);
        Action? action = null;
        MulticastDelegate? m = action;
        List<int> captured = [];
        for (int i = 0; i < count; i++)
        {
            action += Add(captured, i);
            static Action Add(List<int> captured, int i) => () => captured.Add(i);
        }

        switch (count)
        {
            case 0:
            Assert.Null(action);
            break;
            case 1:
            Assert.NotNull(action);
            Assert.True(action.IsSingle());
            break;
            default:
            Assert.NotNull(action);
            Assert.False(action.IsSingle());
            break;
        }

        int foreachCount = 0;
        foreach (var inner in action.AsEnumerable())
        {
            inner.Invoke();
            foreachCount++;
        }
        Assert.Equal(count, foreachCount);
        Assert.Equal(count, captured.Count);
        for (int i = 0; i < captured.Count; i++)
        {
            Assert.Equal(i, captured[i]);
        }
    }
}
