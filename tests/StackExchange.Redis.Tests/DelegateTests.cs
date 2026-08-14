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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(25)]
    public void MatchesGetInvocationList(int count)
    {
        Action? action = null;
        for (int i = 0; i < count; i++)
        {
            action += Noop;
        }

        Assert.NotNull(action);
        var expected = action.GetInvocationList();
        Assert.Equal(count, expected.Length);
        Assert.Equal(count == 1, action.IsSingle());

        int index = 0;
        foreach (var inner in action.AsEnumerable())
        {
            Assert.Same(expected[index++], inner);
        }
        Assert.Equal(count, index);

        // and again, to check that removal keeps things consistent
        action -= Noop;
        if (count == 1)
        {
            Assert.Null(action);
            return;
        }

        Assert.NotNull(action);
        expected = action.GetInvocationList();
        Assert.Equal(count - 1, expected.Length);
        Assert.Equal(count == 2, action.IsSingle());
        index = 0;
        foreach (var inner in action.AsEnumerable())
        {
            Assert.Same(expected[index++], inner);
        }
        Assert.Equal(count - 1, index);

        static void Noop() { }
    }

    [Fact]
    public void ResetRepeatsSequence()
    {
        Action? action = Noop;
        action += Noop;

        var iterator = action.GetEnumerator();
        Assert.True(iterator.MoveNext());
        Assert.True(iterator.MoveNext());
        Assert.False(iterator.MoveNext());

        iterator.Reset();
        int count = 0;
        while (iterator.MoveNext()) count++;
        Assert.Equal(2, count);

        static void Noop() { }
    }
}
