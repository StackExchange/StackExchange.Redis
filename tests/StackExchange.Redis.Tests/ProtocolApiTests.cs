using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Xunit;
#pragma warning disable SERED002
using static StackExchange.Redis.Protocol.Resp2Writer;
namespace StackExchange.Redis.Tests;

public class ProtocolApiTests
{

    private static RequestBuffer CreatePingChunk(string? value, int preambleBytes)
    {
        var obj = new PingRequest(value);
        var writer = preambleBytes <= 0 ? new() : new Resp2Writer(preambleBytes);
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
        RequestBuffer chunk = CreatePingChunk(value, 64);
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
        RequestBuffer chunk = CreatePingChunk(value, 64);
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

    private static string GetString(ReadOnlySequence<byte> data)
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
