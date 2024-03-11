using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using Xunit;
#pragma warning disable SERED001
using static StackExchange.Redis.Protocol.Resp2Writer;
namespace StackExchange.Redis.Tests;

public class ProtocolApiTests
{

    private static RespChunk CreatePingChunk(string? value, int preambleBytes)
    {
        var obj = new PingRequest(value);
        var writer = preambleBytes <= 0 ? new() : new Resp2Writer(preambleBytes);
        try
        {
            obj.Write(ref writer);
            writer.AssertFullyWritten();
            return writer.Commit();
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
        RespChunk chunk = CreatePingChunk(value, 0);
        try
        {
            Assert.Equal(expected, chunk.ToString());
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
        RespChunk chunk = CreatePingChunk(value, 64);
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
    public void CustomPingWithSelectPreamble(string? value, string expected)
    {
        RespChunk chunk = CreatePingChunk(value, 64);
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

            // and take it away again
            chunk = chunk.WithoutPreamble();
            Assert.Equal(expected, chunk.ToString());
        }
        finally
        {
            chunk.Recycle();
        }
    }

    private static string GetHex(ReadOnlySpan<byte> span)
    {
        var arr = ArrayPool<byte>.Shared.Rent(span.Length);
        span.CopyTo(arr);
        var hex = BitConverter.ToString(arr, 0, span.Length);
        ArrayPool<byte>.Shared.Return(arr);
        return hex;
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
            writer.WriteCommand("ping"u8, _value is null ? 0 : 1,
                _value is null ? 0 : EstimateSize(_value));
            if (_value is not null)
            {
                writer.WriteValue(_value);
            }
        }
    }
}
