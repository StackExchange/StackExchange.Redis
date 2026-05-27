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
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        db.StringSet(key, 10);

        var result = await db.StringIncrementAsync(key, 2L, TimeSpan.FromSeconds(5), lowerBound: 0, upperBound: 20);

        Assert.Equal(12, result.Value);
        Assert.Equal(2, result.AppliedIncrement);
        Assert.Equal(12, (long)db.StringGet(key));
        Assert.True((await db.KeyTimeToLiveAsync(key)) > TimeSpan.Zero);

        var request = server.LastRequest!;
        Assert.Equal(key, request.Key);
        Assert.False(request.IsFloat);
        Assert.Equal("2", request.Increment);
        Assert.Equal("0", request.LowerBound);
        Assert.Equal("20", request.UpperBound);
        Assert.False(request.Saturate);
        Assert.Equal("EX", request.ExpiryMode);
        Assert.Equal("5", request.ExpiryValue);
        Assert.False(request.Enx);
    }

    [Fact]
    public async Task StringIncrementIncrex_Double_WithAbsoluteExpiryAndEnx()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc).AddMilliseconds(14);
        db.StringSet(key, 3.25, TimeSpan.FromMinutes(10));
        var beforeTtl = await db.KeyTimeToLiveAsync(key);

        var result = await db.StringIncrementAsync(key, 1.25, new Expiration(when, ExpirationFlags.ExpireIfNotExists), lowerBound: -1.5, upperBound: 9.5);

        Assert.Equal(4.5, result.Value);
        Assert.Equal(1.25, result.AppliedIncrement);
        Assert.Equal(4.5, (double)db.StringGet(key));
        var afterTtl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(beforeTtl);
        Assert.NotNull(afterTtl);
        Assert.True(afterTtl <= beforeTtl);
        Assert.True(afterTtl > TimeSpan.FromMinutes(8));

        var request = server.LastRequest!;
        Assert.Equal(key, request.Key);
        Assert.True(request.IsFloat);
        Assert.Equal("1.25", request.Increment);
        Assert.Equal("-1.5", request.LowerBound);
        Assert.Equal("9.5", request.UpperBound);
        Assert.False(request.Saturate);
        Assert.Equal("PXAT", request.ExpiryMode);
        Assert.Equal("1753265054014", request.ExpiryValue);
        Assert.True(request.Enx);
    }

    [Fact]
    [RunPerProtocol]
    public async Task StringIncrementIncrex_ExecuteUsesNumberResultTypes()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = nameof(StringIncrementIncrex_ExecuteUsesNumberResultTypes);
        var expectedFractionalType = TestContext.Current.IsResp3() ? ResultType.Double : ResultType.BulkString;

        var fractional = await db.ExecuteAsync("INCREX", (RedisKey)(key + ":fractional"), "BYFLOAT", 1.5);
        var fractionalItems = (RedisResult[])fractional!;
        Assert.Equal(2, fractionalItems.Length);
        Assert.Equal(expectedFractionalType, fractionalItems[0].Resp3Type);
        Assert.Equal(expectedFractionalType, fractionalItems[1].Resp3Type);
        Assert.Equal(1.5, (double)fractionalItems[0]);
        Assert.Equal(1.5, (double)fractionalItems[1]);

        var integral = await db.ExecuteAsync("INCREX", (RedisKey)(key + ":integral"), "BYFLOAT", 2.0);
        var integralItems = (RedisResult[])integral!;
        Assert.Equal(2, integralItems.Length);
        Assert.Equal(ResultType.Integer, integralItems[0].Resp3Type);
        Assert.Equal(ResultType.Integer, integralItems[1].Resp3Type);
        Assert.Equal(2, (long)integralItems[0]);
        Assert.Equal(2, (long)integralItems[1]);
    }

    [Fact]
    public async Task StringIncrementIncrex_SyncVersion_ParsesResult()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        var result = db.StringIncrement(Me(), 3L, Expiration.Default);

        Assert.Equal(3, result.Value);
        Assert.Equal(3, result.AppliedIncrement);
    }

    [Fact]
    public async Task StringIncrementIncrex_DefaultRejectsWhenBoundExceeded()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();
        db.StringSet(key, 5);

        var result = await db.StringIncrementAsync(key, 1L, TimeSpan.FromSeconds(5), lowerBound: 10);

        Assert.Equal(5, result.Value);
        Assert.Equal(0, result.AppliedIncrement);
        Assert.Equal(5, (long)db.StringGet(key));
        Assert.Null(await db.KeyTimeToLiveAsync(key));
        Assert.False(server.LastRequest!.Saturate);
    }

    [Fact]
    public async Task StringIncrementIncrex_InvalidOptionsThrow()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => db.StringIncrement(Me(), 1L, TimeSpan.FromSeconds(5), options: (IncrementOptions)2));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public async Task StringIncrementIncrex_SaturateClampsToBound()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();
        db.StringSet(key, 8);

        var result = await db.StringIncrementAsync(key, 5L, TimeSpan.FromSeconds(5), upperBound: 10, options: IncrementOptions.Saturate);

        Assert.Equal(10, result.Value);
        Assert.Equal(2, result.AppliedIncrement);
        Assert.Equal(10, (long)db.StringGet(key));
        Assert.True((await db.KeyTimeToLiveAsync(key)) > TimeSpan.Zero);
        Assert.True(server.LastRequest!.Saturate);
    }

    [Fact]
    public async Task StringIncrementIncrex_DefaultRetainsExistingTtl()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();
        db.StringSet(key, 5, TimeSpan.FromMinutes(5));
        var beforeTtl = await db.KeyTimeToLiveAsync(key);

        var result = await db.StringIncrementAsync(key, 2L, Expiration.Default);

        Assert.Equal(7, result.Value);
        Assert.Equal(2, result.AppliedIncrement);
        var afterTtl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(beforeTtl);
        Assert.NotNull(afterTtl);
        Assert.True(afterTtl <= beforeTtl);
        Assert.True(afterTtl > TimeSpan.FromMinutes(4));
    }

    [Theory]
    [InlineData(5L, 2L, null, 10L, IncrementOptions.None, 7L, 2L, true)]
    [InlineData(5L, 1L, 10L, null, IncrementOptions.None, 5L, 0L, false)]
    [InlineData(5L, 2L, null, 10L, IncrementOptions.Saturate, 7L, 2L, true)]
    [InlineData(8L, 5L, null, 10L, IncrementOptions.Saturate, 10L, 2L, true)]
    // [InlineData(10L, 5L, null, 10L, IncrementOptions.Saturate, 10L, 0L, false)]
    [InlineData(10L, 5L, null, 10L, IncrementOptions.Saturate, 10L, 0L, true)]
    // [InlineData(11L, 1L, null, 10L, IncrementOptions.Saturate, 11L, 0L, false)]
    [InlineData(11L, 1L, null, 10L, IncrementOptions.Saturate, 10L, -1L, true)]
    public async Task StringIncrementIncrex_Int64_ExpirationSideEffects(
        long initialValue,
        long increment,
        long? lowerBound,
        long? upperBound,
        IncrementOptions options,
        long expectedValue,
        long expectedAppliedIncrement,
        bool expectExpiryChanged)
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        db.StringSet(key, initialValue, ExistingExpiry);
        var beforeTtl = await db.KeyTimeToLiveAsync(key);

        var result = await db.StringIncrementAsync(key, increment, NewExpiry, lowerBound, upperBound, options);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedAppliedIncrement, result.AppliedIncrement);
        Assert.Equal(expectedValue, (long)db.StringGet(key));
        await AssertExpiryAsync(db, key, beforeTtl, expectExpiryChanged);
    }

    [Theory]
    [InlineData(5.5, 1.25, null, 10.5, IncrementOptions.None, 6.75, 1.25, true)]
    [InlineData(5.5, 1.25, 10.25, null, IncrementOptions.None, 5.5, 0D, false)]
    [InlineData(5.5, 1.25, null, 10.5, IncrementOptions.Saturate, 6.75, 1.25, true)]
    [InlineData(8.25, 5.5, null, 10.5, IncrementOptions.Saturate, 10.5, 2.25, true)]
    // [InlineData(10.5, 5.5, null, 10.5, IncrementOptions.Saturate, 10.5, 0D, false)]
    [InlineData(10.5, 5.5, null, 10.5, IncrementOptions.Saturate, 10.5, 0D, true)]
    // [InlineData(11.5, 1.25, null, 10.5, IncrementOptions.Saturate, 11.5, 0D, false)]
    [InlineData(11.5, 1.25, null, 10.5, IncrementOptions.Saturate, 10.5, -1D, true)]
    public async Task StringIncrementIncrex_Double_ExpirationSideEffects(
        double initialValue,
        double increment,
        double? lowerBound,
        double? upperBound,
        IncrementOptions options,
        double expectedValue,
        double expectedAppliedIncrement,
        bool expectExpiryChanged)
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();
        var key = Me();

        db.StringSet(key, initialValue, ExistingExpiry);
        var beforeTtl = await db.KeyTimeToLiveAsync(key);

        var result = await db.StringIncrementAsync(key, increment, NewExpiry, lowerBound, upperBound, options);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedAppliedIncrement, result.AppliedIncrement);
        Assert.Equal(expectedValue, (double)db.StringGet(key));
        await AssertExpiryAsync(db, key, beforeTtl, expectExpiryChanged);
    }

    [Fact]
    public async Task StringIncrementIncrex_RejectsKeepTtl()
    {
        using var server = new IncrexTestServer(log);
        await using var muxer = await server.ConnectAsync();
        var db = muxer.GetDatabase();

        var ex = Assert.Throws<ArgumentException>(() => db.StringIncrement(Me(), 1L, Expiration.KeepTtl));
        Assert.Equal("expiry", ex.ParamName);
    }

    private static async Task AssertExpiryAsync(IDatabase db, RedisKey key, TimeSpan? beforeTtl, bool expectExpiryChanged)
    {
        var afterTtl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(beforeTtl);
        Assert.NotNull(afterTtl);

        if (expectExpiryChanged)
        {
            Assert.True(afterTtl <= ChangedExpiryUpperBound, $"Expected {key} TTL to use the new expiry, but was {afterTtl}.");
            Assert.True(afterTtl > TimeSpan.Zero, $"Expected {key} TTL to be positive, but was {afterTtl}.");
        }
        else
        {
            Assert.True(afterTtl > UnchangedExpiryLowerBound, $"Expected {key} TTL to retain the original expiry, but was {afterTtl}.");
            Assert.True(afterTtl <= beforeTtl, $"Expected {key} TTL not to grow, but went from {beforeTtl} to {afterTtl}.");
        }
    }

    private static readonly TimeSpan ExistingExpiry = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan NewExpiry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChangedExpiryUpperBound = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UnchangedExpiryLowerBound = TimeSpan.FromMinutes(10);
}
