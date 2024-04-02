using RESPite.Internal;
using RESPite.Transports;
using RESPite.Resp;
using RESPite.Messages;
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
        using var transport = CreateTransport("+pong\r\n*2\r\n+hi\r\n-lo\r\n"u8);
        Assert.Equal("+pong\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
        Assert.Equal("+*2\r\n+hi\r\n-lo\r\n", transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));

        Assert.Throws<EndOfStreamException>(() => transport.Send(Empty.Value, CommonWriters.Empty, CommonReaders.StringUtf8));
    }
}
