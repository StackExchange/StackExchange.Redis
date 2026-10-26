using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SentinelDiscoveryUnitTests
{
    [Fact]
    public async Task FirstAnswerDoesNotWaitForASlowSentinel()
    {
        var never = new TaskCompletionSource<string?>();
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = ConnectionMultiplexer.FirstSentinelResultAsync(
            [never.Task, Task.FromResult<string?>(null), Task.FromException<string?>(new InvalidOperationException()), answer.Task], 10000);
        Assert.False(query.IsCompleted);
        answer.SetResult("primary");
        Assert.Same(query, await Task.WhenAny(query, Task.Delay(5000)));
        Assert.Equal("primary", await query);
        Assert.False(never.Task.IsCompleted);
    }

    [Fact]
    public async Task NoAnswerHasOneDeadlineForTheWholeQuery()
    {
        var never = new TaskCompletionSource<string?>();
        var query = ConnectionMultiplexer.FirstSentinelResultAsync([never.Task, never.Task], 20);
        Assert.Same(query, await Task.WhenAny(query, Task.Delay(5000)));
        Assert.Null(await query);
        Assert.Null(await ConnectionMultiplexer.FirstSentinelResultAsync(Array.Empty<Task<string?>>(), 20));
    }
}
