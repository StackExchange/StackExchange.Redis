using System;
using Resp;
using Resp.RedisCommands;
using Xunit;

namespace RESP.Core.Tests;

public class BasicTests(ConnectionFixture fixture, ITestOutputHelper log) : TestBase(fixture, log)
{
    [Fact]
    public void Format()
    {
        Span<byte> buffer = stackalloc byte[128];
        var writer = new RespWriter(buffer);
        StringFormatter.Instance.Format("get"u8, ref writer, "abc");
        writer.Flush();
        Assert.Equal("*2\r\n$3\r\nget\r\n$3\r\nabc\r\n", writer.DebugBuffer());
    }

    [Fact]
    public void Parse()
    {
        ReadOnlySpan<byte> buffer = "$3\r\nabc\r\n"u8;
        var reader = new RespReader(buffer);
        reader.MoveNext();
        var value = StringParser.Instance.Parse(ref reader);
        reader.DemandEnd();
        Assert.Equal("abc", value);
    }

    [Fact]
    public void Ping()
    {
        using var conn = GetConnection();
        var s = conn.String("abc", TimeSpan.FromSeconds(10));
        s.Set("def");
        var val = s.Get();
        Assert.Equal("def", val);
    }
}
