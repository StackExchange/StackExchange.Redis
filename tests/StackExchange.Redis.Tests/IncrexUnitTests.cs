using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class IncrexUnitTests(ITestOutputHelper log)
{
    private RedisKey Me([CallerMemberName] string callerName = "") => callerName;

    [Fact]
    public async Task StringIncrementIncrex_Int64_WithBoundsAndExpiry()
    {
        using var server = new IncrexTestServer(new("12", "2"), log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        var result = await db.StringIncrementAsync(key, 2L, TimeSpan.FromSeconds(5), lowerBound: 0, upperBound: 20);

        Assert.Equal(12, result.Value);
        Assert.Equal(2, result.AppliedIncrement);

        var request = server.LastRequest!;
        Assert.Equal(key, request.Key);
        Assert.False(request.IsFloat);
        Assert.Equal("2", request.Increment);
        Assert.Equal("0", request.LowerBound);
        Assert.Equal("20", request.UpperBound);
        Assert.Equal("EX", request.ExpiryMode);
        Assert.Equal("5", request.ExpiryValue);
        Assert.False(request.Enx);
    }

    [Fact]
    public async Task StringIncrementIncrex_Double_WithAbsoluteExpiryAndEnx()
    {
        using var server = new IncrexTestServer(new("4.5", "1.25"), log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc).AddMilliseconds(14);

        var result = await db.StringIncrementAsync(key, 1.25, new Expiration(when, ExpirationFlags.ExpireIfNotExists), lowerBound: -1.5, upperBound: 9.5);

        Assert.Equal(4.5, result.Value);
        Assert.Equal(1.25, result.AppliedIncrement);

        var request = server.LastRequest!;
        Assert.Equal(key, request.Key);
        Assert.True(request.IsFloat);
        Assert.Equal("1.25", request.Increment);
        Assert.Equal("-1.5", request.LowerBound);
        Assert.Equal("9.5", request.UpperBound);
        Assert.Equal("PXAT", request.ExpiryMode);
        Assert.Equal("1753265054014", request.ExpiryValue);
        Assert.True(request.Enx);
    }

    [Fact]
    public async Task StringIncrementIncrex_SyncVersion_ParsesResult()
    {
        using var server = new IncrexTestServer(new("10", "0"), log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        var result = db.StringIncrement(Me(), 3L, Expiration.Default);

        Assert.Equal(10, result.Value);
        Assert.Equal(0, result.AppliedIncrement);
    }

    [Fact]
    public async Task StringIncrementIncrex_RejectsKeepTtl()
    {
        using var server = new IncrexTestServer(new("0", "0"), log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        var ex = Assert.Throws<ArgumentException>(() => db.StringIncrement(Me(), 1L, Expiration.KeepTtl));
        Assert.Equal("expiry", ex.ParamName);
    }
}
