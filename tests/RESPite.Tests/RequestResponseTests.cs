using System.Text;
using RESPite.Internal;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Transports;
namespace RESPite;

public class RequestResponseTests
{
    private static MemoryStream ResponseStream(ReadOnlySpan<byte> data)
    {
        var ms = new MemoryStream(data.Length);
        ms.Write(data);
        ms.Position = 0;
        return ms;
    }

    private static string GetRequests(MemoryStream ms)
    {
        if (!ms.TryGetBuffer(out var segment))
        {
            segment = new(ms.ToArray());
        }
        var s = Encoding.UTF8.GetString(segment.Array ?? [], segment.Offset, segment.Count);
        ms.Position = 0;
        ms.SetLength(0);
        return s;
    }

    [Fact]
    public void Spoof()
    {
        var source = ResponseStream("+OK\r\n$5\r\nhello\r\n:123\r\n"u8);
        var target = new MemoryStream();
        var transport = source.CreateTransport(target).RequestResponse(RespFrameScanner.Default);
        transport.Send(("abc", 123), TestRespWriters.Set, RespReaders.OK);
        Assert.Equal("*3\r\n$3\r\nSET\r\n$3\r\nabc\r\n$3\r\n123\r\n", GetRequests(target));

        transport.Send<string, Empty>("hello", TestRespWriters.Ping, RespReaders.Echo);
        Assert.Equal("*2\r\n$4\r\nPING\r\n$5\r\nhello\r\n", GetRequests(target));

        var got = transport.Send("abc", TestRespWriters.Get, RespReaders.Int32);
        Assert.Equal(123, got);
        Assert.Equal("*2\r\n$3\r\nGET\r\n$3\r\nabc\r\n", GetRequests(target));
    }
}
