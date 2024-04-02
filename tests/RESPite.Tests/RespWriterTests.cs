using RESPite.Resp;

namespace RESPite;

public class RespWriterTests
{
    [Theory]
    [InlineData((int)0, "$1\r\n0\r\n")]
    [InlineData((int)-1, "$2\r\n-1\r\n")]
    [InlineData((int)-10, "$3\r\n-10\r\n")]
    [InlineData((int)-100, "$4\r\n-100\r\n")]
    [InlineData((int)-1000, "$5\r\n-1000\r\n")]
    [InlineData((int)-10000, "$6\r\n-10000\r\n")]
    [InlineData((int)-100000, "$7\r\n-100000\r\n")]
    [InlineData((int)-1000000, "$8\r\n-1000000\r\n")]
    [InlineData((int)-10000000, "$9\r\n-10000000\r\n")]
    [InlineData((int)-100000000, "$10\r\n-100000000\r\n")]
    [InlineData((int)-1000000000, "$1\r\n-1000000000\r\n")]
    [InlineData(int.MinValue, "$1\r\n-1000000000\r\n")]

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
