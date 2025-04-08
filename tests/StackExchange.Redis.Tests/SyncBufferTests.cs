using System;
using System.Buffers;
using System.IO.Pipelines;
using Xunit;

namespace StackExchange.Redis.Tests;

public class SyncBufferTests
{
    [Fact]
    public void BasicOperation()
    {
        var writer = new SyncBufferWriter();
        var reader = writer.Reader;
        Assert.True(reader.TryRead(out var result));
        Assert.True(result.Buffer.IsEmpty);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        Assert.False(writer.IsDrained);

        writer.Write("abcdefgh"u8);

        Assert.True(reader.TryRead(out result));
        Assert.Equal(8, result.Buffer.Length);

        Span<byte> span = stackalloc byte[8];
        result.Buffer.CopyTo(span);
        Assert.True(span.SequenceEqual("abcdefgh"u8));
        Assert.False(result.IsCanceled);
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        Assert.False(writer.IsDrained);

        Assert.True(reader.TryRead(out result));
        Assert.True(result.Buffer.IsEmpty);
        Assert.False(result.IsCanceled);
        Assert.False(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        Assert.False(writer.IsDrained);

        writer.Complete();
        Assert.True(reader.TryRead(out result));
        Assert.True(result.Buffer.IsEmpty);
        Assert.False(result.IsCanceled);
        Assert.True(result.IsCompleted);
        reader.AdvanceTo(result.Buffer.End);
        Assert.True(writer.IsDrained);
    }
}
