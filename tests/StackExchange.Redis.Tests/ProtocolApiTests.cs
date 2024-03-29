using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;
#pragma warning disable SERED002
namespace StackExchange.Redis.Tests;

public class ProtocolApiTests
{

    [Fact]
    public void ApiUsageTest()
    {
        var cmdMap = NewCommandMap.Default;
        IRespWriter writer = cmdMap.RawCommands.Ping;

        var target = Resp2Writer.Create();
        writer.Write(ref target);
        var payload = target.Detach();

        payload.DebugValidateCommand();
        Assert.Equal("ping", payload.GetCommand());
        payload.Recycle();
    }



    private static RequestBuffer CreatePingChunk(string? value, int preambleBytes, SlabManager? slabManager = null)
    {
        var obj = new PingRequest(value);
        var writer = Resp2Writer.Create(slabManager, preambleReservation: preambleBytes);
        try
        {
            obj.Write(ref writer);
            writer.AssertFullyWritten();
            return writer.Detach();
        }
        finally
        {
            writer.Recycle();
        }
    }


    [Fact]
    public void SlabManagerTests()
    {
        var mgr = new SlabManager();
        var memory = new Memory<byte>[30];
        var handles = new IDisposable[memory.Length];
        for (int i = 0; i < 30; i++)
        {
            handles[i] = mgr.GetChunk(out memory[i]);
        }
        Assert.True(mgr.TryExpandChunk(handles[29], ref memory[29]));
        Assert.True(mgr.TryExpandChunk(handles[29], ref memory[29]));
        Assert.False(mgr.TryExpandChunk(handles[29], ref memory[29]));

        // we expect 64k in 4k chunks
        Assert.True(Assert.IsType<SlabManager.Slab>(handles[0]).IsAlive);
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory[0], out var firstSegment));
        Assert.NotNull(firstSegment.Array);
        for (int i = 0; i < 16; i++)
        {
            Assert.Same(handles[0], handles[i]);
            Assert.True(MemoryMarshal.TryGetArray<byte>(memory[i], out var next));
            Assert.Same(firstSegment.Array, next.Array);
            Assert.Equal(i * 4096, next.Offset);
            Assert.Equal(4096, next.Count);
            handles[i].Dispose();
        }
        Assert.True(Assert.IsType<SlabManager.Slab>(handles[16]).IsAlive);
        Assert.NotSame(handles[0], handles[16]);
        Assert.True(MemoryMarshal.TryGetArray<byte>(memory[16], out var secondSegment));
        Assert.NotNull(secondSegment.Array);
        for (int i = 16; i < 30; i++)
        {
            Assert.Same(handles[16], handles[i]);
            Assert.True(MemoryMarshal.TryGetArray<byte>(memory[i], out var next));
            Assert.Same(secondSegment.Array, next.Array);
            Assert.Equal((i - 16) * 4096, next.Offset);
            Assert.Equal(i == 29 ? 4096 * 3 : 4096, next.Count);
            handles[i].Dispose();
        }

        Assert.False(Assert.IsType<SlabManager.Slab>(handles[0]).IsAlive);
        Assert.True(Assert.IsType<SlabManager.Slab>(handles[16]).IsAlive);

        mgr.Dispose();

        Assert.False(Assert.IsType<SlabManager.Slab>(handles[0]).IsAlive);
        Assert.False(Assert.IsType<SlabManager.Slab>(handles[16]).IsAlive);

        Assert.Throws<ObjectDisposedException>(() => mgr.GetChunk(out _));
    }

    [Theory]
    [InlineData(null, "*1\r\n$4\r\nping\r\n")]
    [InlineData("", "*2\r\n$4\r\nping\r\n$0\r\n\r\n")]
    [InlineData("abc", "*2\r\n$4\r\nping\r\n$3\r\nabc\r\n")]
    [InlineData("aaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz", "*2\r\n$4\r\nping\r\n$104\r\naaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz\r\n")]
    public void CustomPing(string? value, string expected)
    {
        RequestBuffer chunk = CreatePingChunk(value, 0);
        try
        {
            Assert.Equal(expected, chunk.ToString());

            var reader = new RespReader(chunk.GetBuffer());
            Assert.True(reader.ReadNext());
            Assert.Equal(RespPrefix.Array, reader.Prefix);
            var expectedLength = value is null ? 1 : 2;
            Assert.Equal(expectedLength, reader.ChildCount);

            Assert.True(reader.ReadNext());
            Assert.Equal(RespPrefix.BulkString, reader.Prefix);
            Assert.Equal(4, reader.ScalarLength);
            Assert.Equal("ping", reader.ReadString());

            if (value is not null)
            {
                Assert.True(reader.ReadNext());
                Assert.Equal(RespPrefix.BulkString, reader.Prefix);
                Assert.Equal(value.Length, reader.ScalarLength);
                Assert.Equal(value, reader.ReadString());
            }

            Assert.False(reader.ReadNext());
            Assert.Equal(RespPrefix.None, reader.Prefix);
        }
        finally
        {
            chunk.Recycle();
        }
    }

    [Theory]
    [InlineData(null, "*1\r\n$4\r\nping\r\n")]
    [InlineData("", "*2\r\n$4\r\nping\r\n$0\r\n\r\n")]
    [InlineData("abc", "*2\r\n$4\r\nping\r\n$3\r\nabc\r\n")]
    [InlineData("aaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz", "*2\r\n$4\r\nping\r\n$104\r\naaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz\r\n")]
    public void CustomPingWithUnusedPreamble(string? value, string expected)
    {
        RequestBuffer chunk = CreatePingChunk(value, 128);
        try
        {
            Assert.Equal(expected, chunk.ToString());
        }
        finally
        {
            chunk.Recycle();
        }
    }

    private static ReadOnlySpan<byte> Select4 => "*2\r\n$6\r\nselect\r\n$1\r\n4\r\n"u8;
    
    private const string Select4String = "*2\r\n$6\r\nselect\r\n$1\r\n4\r\n";

    [Theory]
    [InlineData(null, "*1\r\n$4\r\nping\r\n")]
    [InlineData("", "*2\r\n$4\r\nping\r\n$0\r\n\r\n")]
    [InlineData("abc", "*2\r\n$4\r\nping\r\n$3\r\nabc\r\n")]
    [InlineData("aaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz", "*2\r\n$4\r\nping\r\n$104\r\naaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz\r\n")]
    public async Task CustomPingWithSelectPreamble(string? value, string expected)
    {
        using var mgr = new SlabManager();
        RequestBuffer chunk = CreatePingChunk(value, 64, mgr);
        try
        {
            // check before prepending
            Assert.Equal(expected, chunk.ToString());

            // prepend valid and check
            chunk = chunk.WithPreamble(Select4);
            Assert.Equal(Select4String + expected, chunk.ToString());

            chunk = chunk.WithPreamble(Select4);
            Assert.Equal(Select4String + Select4String + expected, chunk.ToString());

            // check attempting to add over-long fails
            var before = chunk;
            Assert.Throws<InvalidOperationException>(() => chunk.WithPreamble(Select4));
            Assert.Equal(before, chunk); // checking no side-effects


            await using var source = RespSource.Create(chunk.GetBuffer());
            using (var msg = await source.ReadNextAsync()) // select 4
            {
                Assert.Equal("*2\r\n$6\r\nselect\r\n$1\r\n4\r\n", GetString(msg));
            }
            using (var msg = await source.ReadNextAsync()) // select 4
            {
                Assert.Equal("*2\r\n$6\r\nselect\r\n$1\r\n4\r\n", GetString(msg));
            }
            using (var msg = await source.ReadNextAsync()) // ping
            {
                Assert.Equal(expected, GetString(msg));
            }
            using (var msg = await source.ReadNextAsync()) // natural EOF
            {
                Assert.Equal("", GetString(msg));
            }

            // and take it away again
            chunk = chunk.WithoutPreamble();
            Assert.Equal(expected, chunk.ToString());
        }
        finally
        {
            chunk.Recycle();
        }
    }

    private static string GetString(in ReadOnlySequence<byte> data)
    {
#if NET6_0_OR_GREATER
        return Resp2Writer.UTF8.GetString(data);
#else
        var len = checked((int)data.Length);
        if (len == 0) return "";
        var arr = ArrayPool<byte>.Shared.Rent(len);
        data.CopyTo(arr);
        var result = Resp2Writer.UTF8.GetString(arr, 0, len);
        ArrayPool<byte>.Shared.Return(arr);
        return result;
#endif
    }

    public class PingRequest : RespRequest
    {
        private readonly string? _value;
        public PingRequest(string? value)
        {
            _value = value;
        }
        public override void Write(ref Resp2Writer writer)
        {
            writer.WriteCommand("ping"u8, _value is null ? 0 : 1);
            if (_value is not null)
            {
                writer.WriteValue(_value);
            }
        }
    }
}
