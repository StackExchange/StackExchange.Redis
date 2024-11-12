using RESPite.Resp.Writers;

namespace RESPite;

public class RespWriterTests
{
    [Theory]
    [InlineData(0, "$1\r\n0\r\n")]
    [InlineData(-1, "$2\r\n-1\r\n")]
    [InlineData(-12, "$3\r\n-12\r\n")]
    [InlineData(-123, "$4\r\n-123\r\n")]
    [InlineData(-1234, "$5\r\n-1234\r\n")]
    [InlineData(-12345, "$6\r\n-12345\r\n")]
    [InlineData(-123456, "$7\r\n-123456\r\n")]
    [InlineData(-1234567, "$8\r\n-1234567\r\n")]
    [InlineData(-12345678, "$9\r\n-12345678\r\n")]
    [InlineData(-123456789, "$10\r\n-123456789\r\n")]
    [InlineData(-1234567890, "$11\r\n-1234567890\r\n")]
    [InlineData(int.MinValue, "$11\r\n-2147483648\r\n")]
    [InlineData(1, "$1\r\n1\r\n")]
    [InlineData(12, "$2\r\n12\r\n")]
    [InlineData(123, "$3\r\n123\r\n")]
    [InlineData(1234, "$4\r\n1234\r\n")]
    [InlineData(12345, "$5\r\n12345\r\n")]
    [InlineData(123456, "$6\r\n123456\r\n")]
    [InlineData(1234567, "$7\r\n1234567\r\n")]
    [InlineData(12345678, "$8\r\n12345678\r\n")]
    [InlineData(123456789, "$9\r\n123456789\r\n")]
    [InlineData(1234567890, "$10\r\n1234567890\r\n")]
    [InlineData(int.MaxValue, "$10\r\n2147483647\r\n")]

    public void BulkStringInteger(int value, string expected)
    {
        using var aw = new TestBuffer();
        var writer = new RespWriter(aw);
        writer.WriteBulkString(value);
        writer.Flush();
        var actual = aw.ToString();
        Assert.Equal(expected, actual);
    }
}
