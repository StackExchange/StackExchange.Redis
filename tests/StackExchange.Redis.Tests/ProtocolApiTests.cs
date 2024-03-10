using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ProtocolApiTests
{
    [Theory]
    [InlineData(null, "*1\r\n$4\r\nping\r\n")]
    [InlineData("", "*2\r\n$4\r\nping\r\n$0\r\n\r\n")]
    [InlineData("abc", "*2\r\n$4\r\nping\r\n$3\r\nabc\r\n")]
    [InlineData("aaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz", "*2\r\n$4\r\nping\r\n$104\r\naaaabbbbccccddddeeeeffffgggghhhhiiiijjjjkkkkllllmmmmnnnnooooppppqqqqrrrrssssttttuuuuvvvvwwwwxxxxyyyyzzzz\r\n")]
    public void CustomPing(string? value, string expected)
    {
        var obj = new PingRequest(value);
        var writer = new Resp2Writer();
        try
        {
            obj.Write(ref writer);
            writer.AssertFullyWritten();
            Assert.Equal(expected, writer.ToString());
        }
        finally
        {
            writer.Release();
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

    public class PingRequest : Request
    {
        private readonly string? _value;
        public PingRequest(string? value)
        {
            _value = value;
        }
        public override void Write(ref Resp2Writer writer)
        {
            writer.WriteCommand("ping"u8, _value is null ? 0 : 1, _value?.Length ?? 0);
            if (_value is not null)
            {
                writer.WriteValue(_value);
            }
        }
    }
}
