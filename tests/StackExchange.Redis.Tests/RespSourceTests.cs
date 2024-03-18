using StackExchange.Redis.Configuration;
using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;
#pragma warning disable SERED001, SERED002 // Type is for evaluation purposes only

public class RespSourceTests
{
    [Fact]
    public async Task ParseTest() // WILL NOT WORK WELL with concurrent load, due to global counters; isolated test only
    {
#if DEBUG
        var beforeOutstanding = RefCountedSequenceSegment<byte>.DebugOutstanding;
        var beforeTotal = RefCountedSequenceSegment<byte>.DebugTotalLeased;
#endif
        await using (DebugRespSource source = new(16))
        {
            source.Add("*1\r\n$4\r\nPING\r\n+PONG\r\n*2\r\n$4\r\nPING\r\n$6\r\ncustom\r\n+custom\r\n"u8);

            Assert.Equal(14 + 2 + 5 + 11 + 15 + 1 + 8, source.PeekBuffer().Length); // see fragment lengths below for numbers
            AssertChunks(source.PeekBuffer(), 16, 16, 16, 8);

            using var ping = await source.ReadNextAsync();
            Assert.Equal("*1\r\n$4\r\nPING\r\n", GetString(ping));
            AssertChunks(ping, 14);

            using var pong = await source.ReadNextAsync();
            Assert.Equal("+PONG\r\n", GetString(pong));
            AssertChunks(pong, 2, 5);

            using var pingCustom = await source.ReadNextAsync();
            Assert.Equal("*2\r\n$4\r\nPING\r\n$6\r\ncustom\r\n", GetString(pingCustom));
            AssertChunks(pingCustom, 11, 15);

            using var pongCustom = await source.ReadNextAsync();
            Assert.Equal("+custom\r\n", GetString(pongCustom));
            AssertChunks(pongCustom, 1, 8);

            using var nil = await source.ReadNextAsync();
            AssertChunks(nil);
            Assert.Equal("", GetString(nil));
        }
#if DEBUG
        Assert.Equal(beforeOutstanding, RefCountedSequenceSegment<byte>.DebugOutstanding);
        Assert.Equal(beforeTotal + 5, RefCountedSequenceSegment<byte>.DebugTotalLeased); // not 4, because there's a min-retained-buffer optimization
#endif
    }

    [Fact]
    public async Task ReplayTest() // WILL NOT WORK WELL with concurrent load, due to global counters; isolated test only
    {
#if DEBUG
        var beforeOutstanding = RefCountedSequenceSegment<byte>.DebugOutstanding;
        var beforeTotal = RefCountedSequenceSegment<byte>.DebugTotalLeased;
#endif
        int callbackCount = 0;
        LoggingTunnel.Message callback = msg => callbackCount++;
        var returnCount = await LoggingTunnel.ReplayAsync(new MemoryStream(Encoding.UTF8.GetBytes("*1\r\n$4\r\nPING\r\n+PONG\r\n*2\r\n$4\r\nPING\r\n$6\r\ncustom\r\n+custom\r\n")), callback, true);
        Assert.Equal(4, returnCount);
        Assert.Equal(4, callbackCount);
#if DEBUG
        Assert.Equal(beforeOutstanding, RefCountedSequenceSegment<byte>.DebugOutstanding);
        Assert.Equal(beforeTotal + 1, RefCountedSequenceSegment<byte>.DebugTotalLeased);
#endif

    }


    private void AssertChunks(ReadOnlySequence<byte> value, params int[] expected)
    {
        int index = 0;
        foreach (var chunk in value)
        {
            Assert.Equal(expected[index++], chunk.Length);
        }
        Assert.Equal(expected.Length, index);
    }
    private static string GetString(ReadOnlySequence<byte> value)
    {
        // don't care about efficiency
        var len = checked((int)value.Length);
        var arr = ArrayPool<byte>.Shared.Rent(len);
        value.CopyTo(arr);
        var s = Encoding.UTF8.GetString(arr, 0, len);
        ArrayPool<byte>.Shared.Return(arr);
        return s;
    }
    private static int CountChunks(in ReadOnlySequence<byte> value)
    {
        int count = 0;
        foreach (var chunk in value)
        {
            count++;
        }
        return count;
    }

    public class DebugRespSource : RespSource
    {
        public void Add(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var available = _buffer.GetWritableTail().Span;
                Assert.NotEqual(0, available.Length);
                if (data.Length <= available.Length)
                {
                    data.CopyTo(available);
                    _buffer.Commit(data.Length);
                    break;
                }

                data.Slice(0, available.Length).CopyTo(available);
                _buffer.Commit(available.Length);
                data = data.Slice(available.Length);
            }
        }
        public DebugRespSource(int blockSize) => _buffer = new(blockSize);
        private RotatingBufferCore _buffer;
        protected override ReadOnlySequence<byte> Take(long bytes) => _buffer.DetachRotating(bytes);
        public override ValueTask DisposeAsync()
        {
            _buffer.Dispose();
            return default;
        }
        public ReadOnlySequence<byte> PeekBuffer() => GetBuffer();
        protected override ReadOnlySequence<byte> GetBuffer() => _buffer.GetBuffer();

        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken) => default;
    }
}
