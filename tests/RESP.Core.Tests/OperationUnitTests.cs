using System;
using RESPite;
using Xunit;

namespace RESP.Core.Tests;

public class OperationUnitTests
{
    [Fact]
    public void CanCreateAndCompleteOperation()
    {
        var op = RespOperation<int>.Create(null, out var remote);
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
        _ = op.GetResult();

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
