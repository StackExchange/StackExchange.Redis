using System;
using Resp;
using Xunit;

namespace RESP.Core.Tests;

public class BufferTests
{
    [Fact]
    public void BufferUsage()
    {
        CycleBuffer buffer = default;
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));

        buffer.Write("hello world"u8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(11, buffer.GetCommittedLength());

        Assert.False(buffer.TryGetFirstCommittedSpan(true, out _));
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out var committed));
        Assert.True(committed.SequenceEqual("hello world"u8));
        buffer.DiscardCommitted(11);
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));

        // now partial consume
        buffer.Write("partial consume"u8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(15, buffer.GetCommittedLength());

        Assert.False(buffer.TryGetFirstCommittedSpan(true, out _));
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out committed));
        Assert.True(committed.SequenceEqual("partial consume"u8));
        buffer.DiscardCommitted(8);
        Assert.False(buffer.CommittedIsEmpty);
        Assert.Equal(7, buffer.GetCommittedLength());
        Assert.True(buffer.TryGetFirstCommittedSpan(false, out committed));
        Assert.True(committed.SequenceEqual("consume"u8));
        buffer.DiscardCommitted(7);
        Assert.True(buffer.CommittedIsEmpty);
        Assert.Equal(0, buffer.GetCommittedLength());
        Assert.False(buffer.TryGetFirstCommittedSpan(false, out _));
    }
}
