using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using RESPite;
using RESPite.Internal;
using Xunit;

namespace RESP.Core.Tests;

public class BlockBufferTests(ITestOutputHelper log)
{
    private void Log(ReadOnlySpan<byte> span)
    {
#if NET
        log.WriteLine(Encoding.UTF8.GetString(span));
#else
        unsafe
        {
            fixed (byte* p = span)
            {
                log.WriteLine(Encoding.UTF8.GetString(p, span.Length));
            }
        }
#endif
    }

    [Fact]
    public void CanCreateAndWriteSimpleBuffer()
    {
        var buffer = BlockBufferSerializer.Create();
        var a = buffer.Serialize("get"u8, "abc", RespFormatters.Key.String, out var blockA);
        var b = buffer.Serialize("get"u8, "def", RespFormatters.Key.String, out var blockB);
        var c = buffer.Serialize("get"u8, "ghi", RespFormatters.Key.String, out var blockC);
        buffer.Clear();
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(3, buffer.CountMessages);
        Assert.Equal(66, buffer.CountMessageBytes); // contents shown/verified below
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);

        // check the payloads
        Log(a.Span);
        Assert.True(a.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\nabc\r\n"u8));
        Log(a.Span);
        Assert.True(b.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\ndef\r\n"u8));
        Log(c.Span);
        Assert.True(c.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\nghi\r\n"u8));
        blockA?.Dispose();
        blockB?.Dispose();
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        blockC?.Dispose();
        Assert.Equal(1, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
    }

    [Fact]
    public void CanWriteLotsOfBuffers_WithCheapReset() // when messages are consumed before more are added
    {
        var buffer = BlockBufferSerializer.Create();
        Assert.Equal(0, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(0, buffer.CountMessages);
        for (int i = 0; i < 5000; i++)
        {
            var a = buffer.Serialize("get"u8, "abc", RespFormatters.Key.String, out var blockA);
            var b = buffer.Serialize("get"u8, "def", RespFormatters.Key.String, out var blockB);
            var c = buffer.Serialize("get"u8, "ghi", RespFormatters.Key.String, out var blockC);
            blockA?.Dispose();
            blockB?.Dispose();
            blockC?.Dispose();
            Assert.True(MemoryMarshal.TryGetArray(a, out var aSegment));
            Assert.True(MemoryMarshal.TryGetArray(b, out var bSegment));
            Assert.True(MemoryMarshal.TryGetArray(c, out var cSegment));
            Assert.Equal(0, aSegment.Offset);
            Assert.Equal(22, aSegment.Count);
            Assert.Equal(22, bSegment.Offset);
            Assert.Equal(22, bSegment.Count);
            Assert.Equal(44, cSegment.Offset);
            Assert.Equal(22, cSegment.Count);
            Assert.Same(aSegment.Array, bSegment.Array);
            Assert.Same(aSegment.Array, cSegment.Array);
        }
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(15_000, buffer.CountMessages);

        buffer.Clear();
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(1, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(15_000, buffer.CountMessages);
    }

    [Fact]
    public void CanWriteLotsOfBuffers()
    {
        var buffer = BlockBufferSerializer.Create();
        List<IDisposable> blocks = new(15_000);
        Assert.Equal(0, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(0, buffer.CountMessages);
        for (int i = 0; i < 5000; i++)
        {
            _ = buffer.Serialize("get"u8, "abc", RespFormatters.Key.String, out var block);
            if (block is not null) blocks.Add(block);
            _ = buffer.Serialize("get"u8, "def", RespFormatters.Key.String, out block);
            if (block is not null) blocks.Add(block);
            _ = buffer.Serialize("get"u8, "ghi", RespFormatters.Key.String, out block);
            if (block is not null) blocks.Add(block);
        }
        // Each buffer is 2048 by default, so: 93 per buffer; at least 162 buffers.
        // In reality, we apply some round-ups and minimum buffer sizes, which pushes it a little higher, but: not much.
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.Equal(171, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);

        buffer.Clear();
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.Equal(171, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);

        foreach (var block in blocks) block.Dispose();
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.Equal(171, buffer.CountAdded);
        Assert.Equal(171, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
    }
}
