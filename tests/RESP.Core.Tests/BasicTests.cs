using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
        RespFormatters.String.Format("get"u8, ref writer, "abc");
        writer.Flush();
        Assert.Equal("*2\r\n$3\r\nget\r\n$3\r\nabc\r\n", writer.DebugBuffer());
    }

    [Fact]
    public void Parse()
    {
        ReadOnlySpan<byte> buffer = "$3\r\nabc\r\n"u8;
        var reader = new RespReader(buffer);
        reader.MoveNext();
        var value = RespParsers.String.Parse(ref reader);
        reader.DemandEnd();
        Assert.Equal("abc", value);
    }

    private static string Me([CallerMemberName] string? caller = null) => caller ?? "unknown";

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Ping(int count)
    {
        using var conn = GetConnection();
        for (int i = 0; i < count; i++)
        {
            var s = conn.Strings();
            var key = $"{Me()}{i}";
            s.Set(key, $"def{i}");
            var val = s.Get(key);
            Assert.Equal($"def{i}", val);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task PingAsync(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var conn = GetConnection();
        for (int i = 0; i < count; i++)
        {
            var ctx = new RespContext(conn, cts.Token);
            var s = ctx.Strings();
            var key = $"{Me()}{i}";
            await s.SetAsync(key, $"def{i}");
            var val = await s.GetAsync(key);
            Assert.Equal($"def{i}", val);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task PingPipelinedAsync(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var conn = GetConnection();

        Task<string?>[] tasks = new Task<string?>[count];
        for (int i = 0; i < count; i++)
        {
            RespContext ctx = new(conn, cts.Token);
            var s = ctx.Strings();
            var key = $"{Me()}{i}";
            _ = s.SetAsync(key, $"def{i}");
            tasks[i] = s.GetAsync(key);
        }

        for (int i = 0; i < count; i++)
        {
            var val = await tasks[i];
            Assert.Equal($"def{i}", val);
        }
    }
}
