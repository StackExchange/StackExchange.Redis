using RESPite.Internal;
using RESPite.Messages;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Transports;
using Xunit.Abstractions;
namespace RESPite;

public class RespDeframe(ITestOutputHelper log)
{
    private static IRequestResponseTransport CreateTransport(ReadOnlySpan<byte> payload)
    {
        var stream = new MemoryStream();
        stream.Write(payload);
        stream.Position = 0;
        return stream.CreateTransport().RequestResponse(RespFrameScanner.Default, FrameValidation.Disabled); // because we're sending invalid Empty frames
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

    [Fact]
    public void CanReadConfig()
    {
        var reader = new RespReader("*12\r\n$7\r\ntimeout\r\n$1\r\n0\r\n$4\r\nsave\r\n$0\r\n\r\n$10\r\nappendonly\r\n$2\r\nno\r\n$15\r\nslave-read-only\r\n$2\r\nno\r\n$9\r\ndatabases\r\n$2\r\n16\r\n$20\r\ncluster-node-timeout\r\n$2\r\n60\r\n"u8);

        Assert.True(reader.TryReadNext(RespPrefix.Array));
        Assert.Equal(12, reader.ChildCount);
        List<string> actual = new(reader.ChildCount);
        while (reader.TryReadNext(RespPrefix.BulkString))
        {
            var s = reader.ReadString();
            Assert.NotNull(s);
            log.WriteLine(s);
            actual.Add(s);
        }
        Assert.Equal(
            [
            "timeout", "0",
            "save", "",
            "appendonly", "no",
            "slave-read-only", "no",
            "databases", "16",
            "cluster-node-timeout", "60",
            ],
            actual);
    }
}
