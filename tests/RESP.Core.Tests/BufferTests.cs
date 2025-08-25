using System;
using System.Diagnostics;
using Resp;
using Xunit;

namespace RESP.Core.Tests;

public class BufferTests
{
    [Fact]
    public void SimpleUsage()
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

    [Fact]
    public void MultiSegmentUsage()
    {
        byte[] garbage = new byte[1024 * 1024];
        var rand = new Random(Seed: 134521);
        rand.NextBytes(garbage);

        int offset = 0;
        CycleBuffer buffer = default;
        while (offset < garbage.Length)
        {
            var size = rand.Next(1, garbage.Length - offset + 1);
            Debug.Assert(size > 0);
            buffer.Write(new ReadOnlySpan<byte>(garbage, offset, size));
            offset += size;
            Assert.Equal(offset, buffer.GetCommittedLength());
        }

        int total = 0;
        while (buffer.TryGetFirstCommittedSpan(true, out var span))
        {
            var take = rand.Next(0, span.Length + 1);
            var slice = span.Slice(0, take);
            Assert.True(slice.SequenceEqual(new(garbage, total, take)), "data integrity check");
            buffer.DiscardCommitted(take);
            total += take;
        }
        Assert.Equal(garbage.Length, total);
    }
}
