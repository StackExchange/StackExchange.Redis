using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using Xunit;

namespace RESP.Core.Tests;

[SuppressMessage(
    "Usage",
    "xUnit1031:Do not use blocking task operations in test method",
    Justification = "This isn't actually async; we're testing an awaitable.")]
public class OperationUnitTests
{
    [Fact]
    public void CanCreateAndCompleteOperation()
    {
        var op = RespOperation.Create(out var remote);
        // initial state
        Assert.False(op.IsCanceled);
        Assert.False(op.IsCompleted);
        Assert.False(op.IsCompletedSuccessfully);
        Assert.False(op.IsFaulted);

        // complete first time
        Assert.True(remote.TrySetResult(default));
        Assert.False(op.IsCanceled);
        Assert.True(op.IsCompleted);
        Assert.True(op.IsCompletedSuccessfully);
        Assert.False(op.IsFaulted);

        // additional completions fail
        Assert.False(remote.TrySetResult(default));
#pragma warning disable xUnit1051
        Assert.False(remote.TrySetCanceled());
#pragma warning restore xUnit1051
        Assert.False(remote.TrySetException(null!));

        // can get result
        op.GetResult();

        // but only once, after that: bad things
        Assert.Throws<NullReferenceException>(() => op.GetResult());
        Assert.Throws<NullReferenceException>(() => op.IsCanceled);
        Assert.Throws<NullReferenceException>(() => op.IsCompleted);
        Assert.Throws<NullReferenceException>(() => op.IsCompletedSuccessfully);
        Assert.Throws<NullReferenceException>(() => op.IsFaulted);

        // additional completions continue to fail
        Assert.False(remote.TrySetResult(default));
#pragma warning disable xUnit1051
        Assert.False(remote.TrySetCanceled());
#pragma warning restore xUnit1051
        Assert.False(remote.TrySetException(null!));
    }
}
