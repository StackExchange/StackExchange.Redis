using RESPite.Internal;
using RESPite.Messages;
using RESPite.Resp;
using RESPite.Transports;
namespace RESPite;

public class RespDeframe
{
    private static IRequestResponseTransport CreateTransport(ReadOnlySpan<byte> payload)
    {
        var stream = new MemoryStream();
        stream.Write(payload);
        stream.Position = 0;
        return stream.CreateTransport().RequestResponse(RespFrameScanner.Default);
    }
    [Fact]
    public void CanReadMessage()
    {
        using var transport = CreateTransport("+PONG\r\n:3\r\n:21\r\n+OK\r\n_\r\n*2\r\n+hi\r\n-lo\r\n"u8);
        Assert.Equal("+PONG\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal(":3\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal(":21\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal("+OK\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal("_\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal("*2\r\n+hi\r\n-lo\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Throws<EndOfStreamException>(() => transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
    }
}
