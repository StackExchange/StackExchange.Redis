using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using RESPite;
using RESPite.Internal;
using Xunit;

namespace RESPite.Tests;

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
        var a = buffer.Serialize(null, "get"u8, "abc", RespFormatters.Key.String);
        var b = buffer.Serialize(null, "get"u8, "def", RespFormatters.Key.String);
        var c = buffer.Serialize(null, "get"u8, "ghi", RespFormatters.Key.String);
        buffer.Clear();
#if DEBUG
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(3, buffer.CountMessages);
        Assert.Equal(66, buffer.CountMessageBytes); // contents shown/verified below
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif
        // check the payloads
        Log(a.Span);
        Assert.True(a.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\nabc\r\n"u8));
        Log(a.Span);
        Assert.True(b.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\ndef\r\n"u8));
        Log(c.Span);
        Assert.True(c.Span.SequenceEqual("*2\r\n$3\r\nget\r\n$3\r\nghi\r\n"u8));
        AssertRelease(a);
        AssertRelease(b);
#if DEBUG
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif
        AssertRelease(c);
#if DEBUG
        Assert.Equal(1, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif
    }

    private static void AssertRelease(ReadOnlyMemory<byte> buffer)
    {
        Assert.True(MemoryMarshal.TryGetMemoryManager<byte, BlockBufferSerializer.BlockBuffer>(buffer, out var manager));
        manager.Release();
    }

    [Fact]
    public void CanWriteLotsOfBuffers_WithCheapReset() // when messages are consumed before more are added
    {
        var buffer = BlockBufferSerializer.Create();
#if DEBUG
        Assert.Equal(0, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(0, buffer.CountMessages);
#endif
        for (int i = 0; i < 5000; i++)
        {
            var a = buffer.Serialize(null, "get"u8, "abc", RespFormatters.Key.String);
            var b = buffer.Serialize(null, "get"u8, "def", RespFormatters.Key.String);
            var c = buffer.Serialize(null, "get"u8, "ghi", RespFormatters.Key.String);
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
            AssertRelease(a);
            AssertRelease(b);
            AssertRelease(c);
        }
#if DEBUG
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(15_000, buffer.CountMessages);
#endif
        buffer.Clear();
#if DEBUG
        Assert.Equal(1, buffer.CountAdded);
        Assert.Equal(1, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(15_000, buffer.CountMessages);
#endif
    }

    [Fact]
    public void CanWriteLotsOfBuffers()
    {
        var buffer = BlockBufferSerializer.Create();
        List<ReadOnlyMemory<byte>> blocks = new(15_000);
#if DEBUG
        Assert.Equal(0, buffer.CountAdded);
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
        Assert.Equal(0, buffer.CountMessages);
#endif
        for (int i = 0; i < 5000; i++)
        {
            var block = buffer.Serialize(null, "get"u8, "abc", RespFormatters.Key.String);
            blocks.Add(block);
            block = buffer.Serialize(null, "get"u8, "def", RespFormatters.Key.String);
            blocks.Add(block);
            block = buffer.Serialize(null, "get"u8, "ghi", RespFormatters.Key.String);
            blocks.Add(block);
        }

        // Each buffer is 2048 by default, so: 93 per buffer; at least 162 buffers (looking at CountAdded).
        // In reality, we apply some round-ups and minimum buffer sizes, which pushes it a little higher, but: not much.
        // However, the runtime can also choose to issue bigger leases than we expect, pushing it down! What matters
        // isn't the specific number, but: that it isn't huge.
#if DEBUG
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.True(buffer.CountAdded < 200, "too many buffers used");
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif
        buffer.Clear();
#if DEBUG
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.True(buffer.CountAdded < 200, "too many buffers used");
        Assert.Equal(0, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif

        foreach (var block in blocks) AssertRelease(block);
#if DEBUG
        Assert.Equal(15_000, buffer.CountMessages);
        Assert.True(buffer.CountAdded < 200, "too many buffers used");
        Assert.Equal(buffer.CountAdded, buffer.CountRecycled);
        Assert.Equal(0, buffer.CountLeaked);
#endif
    }
}
