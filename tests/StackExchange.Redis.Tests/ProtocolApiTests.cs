using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Collections.Generic;
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
        var writer = cmdMap.RawCommands.Ping;
        Assert.NotNull(writer);

        var target = RespWriter.Create();
        writer.Write(ref target);
        var payload = target.Detach();

        payload.DebugValidateCommand();
        Assert.Equal("PING", payload.GetCommand());
        payload.Recycle();
    }

    [Theory]
    [InlineData("ping", "PING")]
    [InlineData("PING", "PING")]
    [InlineData("PING ", "PING")]
    [InlineData("blah", "BLAH")]
    [InlineData("BLAH", "BLAH")]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void ApiUsageTestMapped(string? mapped, string? expected)
    {
        var map = new Dictionary<string, string?> { { "pInG", mapped } };
        var cmdMap = NewCommandMap.Create(map);
        var writer = cmdMap.RawCommands.Ping;
        if (string.IsNullOrWhiteSpace(mapped))
        {
            Assert.Null(expected);
            Assert.Null(writer);
            Assert.Null(cmdMap.Normalize("pInG", out var known));
            Assert.Equal(RedisCommand.PING, known);
        }
        else
        {
            Assert.NotNull(writer);
            Assert.NotNull(expected);

            var target = RespWriter.Create();
            writer.Write(ref target);
            var payload = target.Detach();

            payload.DebugValidateCommand();
            Assert.Equal(expected, payload.GetCommand());
            payload.Recycle();

            var first = cmdMap.Normalize("pInG", out var known);
            Assert.Equal(RedisCommand.PING, known);
            Assert.Equal(expected, first);
            Assert.Same(first, cmdMap.Normalize("pInG", out _)); // assert non-allocating
        }
    }

    [Theory]
    [InlineData("flibble", "fLiBBle")] // not actually remapped, so: GiGo (avoids alloc per normalize)
    [InlineData("FLIBBLE", "fLiBBle")] // not actually remapped, so: GiGo (avoids alloc per normalize)
    [InlineData("FLIBBLE ", "fLiBBle")] // not actually remapped, so: GiGo (avoids alloc per normalize)
    [InlineData("flibble2", "FLIBBLE2")] // remapped, so: cased during map
    [InlineData("FLIBBLE2", "FLIBBLE2")] // remapped, so: cased during map
    [InlineData("FLIBBLE2 ", "FLIBBLE2")] // remapped, so: cased during map
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void ApiUsageTestUnknownMapped(string? mapped, string? expected)
    {
        var map = new Dictionary<string, string?> { { "fLiBBle", mapped } };
        var cmdMap = NewCommandMap.Create(map);
        if (string.IsNullOrWhiteSpace(mapped))
        {
            Assert.Null(expected);
            Assert.Null(cmdMap.Normalize("fLiBBle", out var known));
            Assert.Equal(RedisCommand.UNKNOWN, known);
        }
        else
        {
            Assert.NotNull(expected);

            var first = cmdMap.Normalize("fLiBBle", out var known);
            Assert.Equal(RedisCommand.UNKNOWN, known);
            Assert.Equal(expected, first);
            Assert.Same(first, cmdMap.Normalize("fLiBBle", out _)); // assert non-allocating
        }
    }

    [Fact]
    public void ExpectOK()
    {
        var reader = RespReaders.OK;
        var resp = new RespReader("+OK\r\n"u8);
        Assert.True(resp.ReadNext());

        // test simple reader facts
        Assert.Equal(RespPrefix.SimpleString, resp.Prefix);
        Assert.False(resp.IsError); // just a prefix check
        Assert.True(resp.IsScalar); // just a prefix check
        Assert.False(resp.IsAggregate); // just a prefix check
        Assert.Equal(2, resp.ScalarLength); // prefix/length
        Assert.Equal(0, resp.ChildCount); // prefix/length

        // test pre-written validator
        reader.Read(ref resp);

        // should be fully consumed
        Assert.False(resp.ReadNext() && resp.BytesConsumed == 5);
    }

    [Fact]
    public void ReadError()
    {
        var resp = new RespReader("-ERR boom!\r\n"u8);
        Assert.True(resp.ReadNext() && resp.IsError);
        var ex = resp.ReadError();
        Assert.IsType<RedisServerException>(ex);
        Assert.Equal("ERR boom!", ex.Message);
        Assert.False(resp.ReadNext());
    }

    [Fact]
    public void ExpectOK_GotKO()
    {
        var reader = RespReaders.OK;
        var resp = new RespReader("+KO\r\n"u8);
        Assert.True(resp.ReadNext() && !resp.IsError);
        try
        {
            reader.Read(ref resp);
            Assert.Fail(); // can't use Assert.Throws because ref-struct / capture
        }
        catch (Exception ex)
        {
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Equal("Did not receive expected response: '+OK'", ex.Message);
        }
        Assert.False(resp.ReadNext());
    }



    private static RequestBuffer CreatePingChunk(string? value, int preambleBytes, SlabManager? slabManager = null)
    {
        var obj = new PingRequest(value);
        var writer = RespWriter.Create(slabManager, preambleReservation: preambleBytes);
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
        return RespWriter.UTF8.GetString(data);
#else
        var len = checked((int)data.Length);
        if (len == 0) return "";
        var arr = ArrayPool<byte>.Shared.Rent(len);
        data.CopyTo(arr);
        var result = RespWriter.UTF8.GetString(arr, 0, len);
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
        public override void Write(ref RespWriter writer)
        {
            writer.WriteCommand("ping"u8, _value is null ? 0 : 1);
            if (_value is not null)
            {
                writer.WriteValue(_value);
            }
        }
    }
}
