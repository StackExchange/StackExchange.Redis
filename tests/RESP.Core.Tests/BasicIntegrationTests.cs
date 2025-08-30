using System;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Connections;
using RESPite.Messages;
using RESPite.Redis;
using RESPite.Redis.Alt; // needed for AsStrings() etc
using Xunit;

namespace RESP.Core.Tests;

public class BasicIntegrationTests(ConnectionFixture fixture, ITestOutputHelper log) : IntegrationTestBase(fixture, log)
{
    [Fact]
    public void Format()
    {
        Span<byte> buffer = stackalloc byte[128];
        var writer = new RespWriter(buffer);
        RespFormatters.Value.String.Format("get"u8, ref writer, "abc");
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

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Ping(int count)
    {
        using var conn = GetConnection();
        var ctx = conn.Context;
        for (int i = 0; i < count; i++)
        {
            var key = $"{Me()}{i}";
            ctx.AsStrings().Set(key, $"def{i}");
            var val = ctx.AsStrings().Get(key);
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
            var ctx = conn.Context.WithCancellationToken(cts.Token);
            var key = $"{Me()}{i}";
            await ctx.AsStrings().SetAsync(key, $"def{i}");
            var val = await ctx.AsStrings().GetAsync(key);
            Assert.Equal($"def{i}", val);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(100, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(100, true)]
    public async Task PingPipelinedAsync(int count, bool forPipeline)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var conn = forPipeline ? GetConnection().Synchronized() : GetConnection();

        ValueTask<string?>[] tasks = new ValueTask<string?>[count];
        for (int i = 0; i < count; i++)
        {
            RespContext ctx = conn.Context.WithCancellationToken(cts.Token);
            var key = $"{Me()}{i}";
            _ = ctx.AsStrings().SetAsync(key, $"def{i}");
            tasks[i] = ctx.AsStrings().GetAsync(key);
        }

        for (int i = 0; i < count; i++)
        {
            var val = await tasks[i];
            Assert.Equal($"def{i}", val);
        }
    }
}
