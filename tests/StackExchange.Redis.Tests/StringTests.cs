using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Tests for <see href="https://redis.io/commands#string"/>.
/// </summary>
[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class StringTests : TestBase
{
    public StringTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task Append()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var server = GetServer(conn);
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        var l0 = server.Features.StringLength ? db.StringLengthAsync(key) : null;

        var s0 = db.StringGetAsync(key);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        var s1 = db.StringGetAsync(key);
        var l1 = server.Features.StringLength ? db.StringLengthAsync(key) : null;

        var result = db.StringAppendAsync(key, Encode("defgh"));
        var s3 = db.StringGetAsync(key);
        var l2 = server.Features.StringLength ? db.StringLengthAsync(key) : null;

        Assert.Null((string?)await s0);
        Assert.Equal("abc", await s1);
        Assert.Equal(8, await result);
        Assert.Equal("abcdefgh", await s3);

        if (server.Features.StringLength)
        {
            Assert.Equal(0, await l0!);
            Assert.Equal(3, await l1!);
            Assert.Equal(8, await l2!);
        }
    }

    [Fact]
    public async Task Set()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        var v1 = db.StringGetAsync(key);

        db.StringSet(key, Encode("def"), flags: CommandFlags.FireAndForget);
        var v2 = db.StringGetAsync(key);

        Assert.Equal("abc", await v1);
        Assert.Equal("def", Decode(await v2));
    }

    [Fact]
    public async Task SetEmpty()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, new byte[] { });
        var exists = await db.KeyExistsAsync(key);
        var val = await db.StringGetAsync(key);

        Assert.True(exists);
        Log("Value: " + val);
        Assert.Equal(0, val.Length());
    }

    [Fact]
    public async Task StringGetSetExpiryNoValue()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        var emptyVal = await db.StringGetSetExpiryAsync(key, TimeSpan.FromHours(1));

        Assert.Equal(RedisValue.Null, emptyVal);
    }

    [Fact]
    public async Task StringGetSetExpiryRelative()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", TimeSpan.FromHours(1));
        var relativeSec = db.StringGetSetExpiryAsync(key, TimeSpan.FromMinutes(30));
        var relativeSecTtl = db.KeyTimeToLiveAsync(key);

        Assert.Equal("abc", await relativeSec);
        var time = await relativeSecTtl;
        Assert.NotNull(time);
        Assert.InRange(time.Value, TimeSpan.FromMinutes(29.8), TimeSpan.FromMinutes(30.2));
    }

    [Fact]
    public async Task StringGetSetExpiryAbsolute()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", TimeSpan.FromHours(1));
        var newDate = DateTime.UtcNow.AddMinutes(30);
        var val = db.StringGetSetExpiryAsync(key, newDate);
        var valTtl = db.KeyTimeToLiveAsync(key);

        Assert.Equal("abc", await val);
        var time = await valTtl;
        Assert.NotNull(time);
        Assert.InRange(time.Value, TimeSpan.FromMinutes(29.8), TimeSpan.FromMinutes(30.2));

        // And ensure our type checking works
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => db.StringGetSetExpiryAsync(key, new DateTime(100, DateTimeKind.Unspecified)));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task StringGetSetExpiryPersist()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", TimeSpan.FromHours(1));
        var val = db.StringGetSetExpiryAsync(key, null);
        var valTtl = db.KeyTimeToLiveAsync(key);

        Assert.Equal("abc", await val);
        Assert.Null(await valTtl);
    }

    [Fact]
    public async Task GetLease()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        using (var v1 = await db.StringGetLeaseAsync(key).ConfigureAwait(false))
        {
            string? s = v1?.DecodeString();
            Assert.Equal("abc", s);
        }
    }

    [Fact]
    public async Task GetLeaseAsStream()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
        var lease = await db.StringGetLeaseAsync(key).ConfigureAwait(false);
        Assert.NotNull(lease);
        using (var v1 = lease.AsStream())
        {
            using (var sr = new StreamReader(v1))
            {
                string s = sr.ReadToEnd();
                Assert.Equal("abc", s);
            }
        }
    }

    [Fact]
    public void GetDelete()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);

        Assert.True(db.KeyExists(prefix + "1"));
        Assert.False(db.KeyExists(prefix + "2"));

        var s0 = db.StringGetDelete(prefix + "1");
        var s2 = db.StringGetDelete(prefix + "2");

        Assert.False(db.KeyExists(prefix + "1"));
        Assert.Equal("abc", s0);
        Assert.Equal(RedisValue.Null, s2);
    }

    [Fact]
    public async Task GetDeleteAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);

        Assert.True(db.KeyExists(prefix + "1"));
        Assert.False(db.KeyExists(prefix + "2"));

        var s0 = db.StringGetDeleteAsync(prefix + "1");
        var s2 = db.StringGetDeleteAsync(prefix + "2");

        Assert.False(db.KeyExists(prefix + "1"));
        Assert.Equal("abc", await s0);
        Assert.Equal(RedisValue.Null, await s2);
    }

    [Fact]
    public async Task SetNotExists()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "3", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "4", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "5", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);

        var x0 = db.StringSetAsync(prefix + "1", "def", when: When.NotExists);
        var x1 = db.StringSetAsync(prefix + "1", Encode("def"), when: When.NotExists);
        var x2 = db.StringSetAsync(prefix + "2", "def", when: When.NotExists);
        var x3 = db.StringSetAsync(prefix + "3", Encode("def"), when: When.NotExists);
        var x4 = db.StringSetAsync(prefix + "4", "def", expiry: TimeSpan.FromSeconds(4), when: When.NotExists);
        var x5 = db.StringSetAsync(prefix + "5", "def", expiry: TimeSpan.FromMilliseconds(4001), when: When.NotExists);

        var s0 = db.StringGetAsync(prefix + "1");
        var s2 = db.StringGetAsync(prefix + "2");
        var s3 = db.StringGetAsync(prefix + "3");

        Assert.False(await x0);
        Assert.False(await x1);
        Assert.True(await x2);
        Assert.True(await x3);
        Assert.True(await x4);
        Assert.True(await x5);
        Assert.Equal("abc", await s0);
        Assert.Equal("def", await s2);
        Assert.Equal("def", await s3);
    }

    [Fact]
    public async Task SetKeepTtl()
    {
        using var conn = Create(require: RedisFeatures.v6_0_0);

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "3", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "2", "abc", expiry: TimeSpan.FromMinutes(5), flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "3", "abc", expiry: TimeSpan.FromMinutes(10), flags: CommandFlags.FireAndForget);

        var x0 = db.KeyTimeToLiveAsync(prefix + "1");
        var x1 = db.KeyTimeToLiveAsync(prefix + "2");
        var x2 = db.KeyTimeToLiveAsync(prefix + "3");

        Assert.Null(await x0);
        Assert.True(await x1 > TimeSpan.FromMinutes(4), "Over 4");
        Assert.True(await x1 <= TimeSpan.FromMinutes(5), "Under 5");
        Assert.True(await x2 > TimeSpan.FromMinutes(9), "Over 9");
        Assert.True(await x2 <= TimeSpan.FromMinutes(10), "Under 10");

        db.StringSet(prefix + "1", "def", keepTtl: true, flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "2", "def", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "3", "def", keepTtl: true, flags: CommandFlags.FireAndForget);

        var y0 = db.KeyTimeToLiveAsync(prefix + "1");
        var y1 = db.KeyTimeToLiveAsync(prefix + "2");
        var y2 = db.KeyTimeToLiveAsync(prefix + "3");

        Assert.Null(await y0);
        Assert.Null(await y1);
        Assert.True(await y2 > TimeSpan.FromMinutes(9), "Over 9");
        Assert.True(await y2 <= TimeSpan.FromMinutes(10), "Under 10");
    }

    [Fact]
    public async Task SetAndGet()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "3", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "4", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "5", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "6", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "7", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "8", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "9", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "10", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "2", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "4", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "6", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "7", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "8", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "9", "abc", flags: CommandFlags.FireAndForget);
        db.StringSet(prefix + "10", "abc", expiry: TimeSpan.FromMinutes(10), flags: CommandFlags.FireAndForget);

        var x0 = db.StringSetAndGetAsync(prefix + "1", RedisValue.Null);
        var x1 = db.StringSetAndGetAsync(prefix + "2", "def");
        var x2 = db.StringSetAndGetAsync(prefix + "3", "def");
        var x3 = db.StringSetAndGetAsync(prefix + "4", "def", when: When.Exists);
        var x4 = db.StringSetAndGetAsync(prefix + "5", "def", when: When.Exists);
        var x5 = db.StringSetAndGetAsync(prefix + "6", "def", expiry: TimeSpan.FromSeconds(4));
        var x6 = db.StringSetAndGetAsync(prefix + "7", "def", expiry: TimeSpan.FromMilliseconds(4001));
        var x7 = db.StringSetAndGetAsync(prefix + "8", "def", expiry: TimeSpan.FromSeconds(4), when: When.Exists);
        var x8 = db.StringSetAndGetAsync(prefix + "9", "def", expiry: TimeSpan.FromMilliseconds(4001), when: When.Exists);

        var y0 = db.StringSetAndGetAsync(prefix + "10", "def", keepTtl: true);
        var y1 = db.KeyTimeToLiveAsync(prefix + "10");
        var y2 = db.StringGetAsync(prefix + "10");

        var s0 = db.StringGetAsync(prefix + "1");
        var s1 = db.StringGetAsync(prefix + "2");
        var s2 = db.StringGetAsync(prefix + "3");
        var s3 = db.StringGetAsync(prefix + "4");
        var s4 = db.StringGetAsync(prefix + "5");

        Assert.Equal("abc", await x0);
        Assert.Equal("abc", await x1);
        Assert.Equal(RedisValue.Null, await x2);
        Assert.Equal("abc", await x3);
        Assert.Equal(RedisValue.Null, await x4);
        Assert.Equal("abc", await x5);
        Assert.Equal("abc", await x6);
        Assert.Equal("abc", await x7);
        Assert.Equal("abc", await x8);

        Assert.Equal("abc", await y0);
        Assert.True(await y1 <= TimeSpan.FromMinutes(10), "Under 10 min");
        Assert.True(await y1 >= TimeSpan.FromMinutes(8), "Over 8 min");
        Assert.Equal("def", await y2);

        Assert.Equal(RedisValue.Null, await s0);
        Assert.Equal("def", await s1);
        Assert.Equal("def", await s2);
        Assert.Equal("def", await s3);
        Assert.Equal(RedisValue.Null, await s4);
    }

    [Fact]
    public async Task SetNotExistsAndGet()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var prefix = Me();
        db.KeyDelete(prefix + "1", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "2", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "3", CommandFlags.FireAndForget);
        db.KeyDelete(prefix + "4", CommandFlags.FireAndForget);
        db.StringSet(prefix + "1", "abc", flags: CommandFlags.FireAndForget);

        var x0 = db.StringSetAndGetAsync(prefix + "1", "def", when: When.NotExists);
        var x1 = db.StringSetAndGetAsync(prefix + "2", "def", when: When.NotExists);
        var x2 = db.StringSetAndGetAsync(prefix + "3", "def", expiry: TimeSpan.FromSeconds(4), when: When.NotExists);
        var x3 = db.StringSetAndGetAsync(prefix + "4", "def", expiry: TimeSpan.FromMilliseconds(4001), when: When.NotExists);

        var s0 = db.StringGetAsync(prefix + "1");
        var s1 = db.StringGetAsync(prefix + "2");

        Assert.Equal("abc", await x0);
        Assert.Equal(RedisValue.Null, await x1);
        Assert.Equal(RedisValue.Null, await x2);
        Assert.Equal(RedisValue.Null, await x3);

        Assert.Equal("abc", await s0);
        Assert.Equal("def", await s1);
    }

    [Fact]
    public async Task Ranges()
    {
        using var conn = Create(require: RedisFeatures.v2_1_8);

        var db = conn.GetDatabase();
        var key = Me();

        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abcdefghi", flags: CommandFlags.FireAndForget);
        db.StringSetRange(key, 2, "xy", CommandFlags.FireAndForget);
        db.StringSetRange(key, 4, Encode("z"), CommandFlags.FireAndForget);

        var val = db.StringGetAsync(key);

        Assert.Equal("abxyzfghi", await val);
    }

    [Fact]
    public async Task IncrDecr()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "2", flags: CommandFlags.FireAndForget);
        var v1 = db.StringIncrementAsync(key);
        var v2 = db.StringIncrementAsync(key, 5);
        var v3 = db.StringIncrementAsync(key, -2);
        var v4 = db.StringDecrementAsync(key);
        var v5 = db.StringDecrementAsync(key, 5);
        var v6 = db.StringDecrementAsync(key, -2);
        var s = db.StringGetAsync(key);

        Assert.Equal(3, await v1);
        Assert.Equal(8, await v2);
        Assert.Equal(6, await v3);
        Assert.Equal(5, await v4);
        Assert.Equal(0, await v5);
        Assert.Equal(2, await v6);
        Assert.Equal("2", await s);
    }

    [Fact]
    public async Task IncrDecrFloat()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "2", flags: CommandFlags.FireAndForget);
        var v1 = db.StringIncrementAsync(key, 1.1);
        var v2 = db.StringIncrementAsync(key, 5.0);
        var v3 = db.StringIncrementAsync(key, -2.0);
        var v4 = db.StringIncrementAsync(key, -1.0);
        var v5 = db.StringIncrementAsync(key, -5.0);
        var v6 = db.StringIncrementAsync(key, 2.0);

        var s = db.StringGetAsync(key);

        Assert.Equal(3.1, await v1, 5);
        Assert.Equal(8.1, await v2, 5);
        Assert.Equal(6.1, await v3, 5);
        Assert.Equal(5.1, await v4, 5);
        Assert.Equal(0.1, await v5, 5);
        Assert.Equal(2.1, await v6, 5);
        Assert.Equal(2.1, (double)await s, 5);
    }

    [Fact]
    public async Task GetRange()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, "abcdefghi", flags: CommandFlags.FireAndForget);
        var s = db.StringGetRangeAsync(key, 2, 4);
        var b = db.StringGetRangeAsync(key, 2, 4);

        Assert.Equal("cde", await s);
        Assert.Equal("cde", Decode(await b));
    }

    [Fact]
    public async Task BitCount()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitCount(key);
        var r2 = db.StringBitCount(key, 0, 0);
        var r3 = db.StringBitCount(key, 1, 1);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(6, r3);

        // Async

        r1 = await db.StringBitCountAsync(key);
        r2 = await db.StringBitCountAsync(key, 0, 0);
        r3 = await db.StringBitCountAsync(key, 1, 1);

        Assert.Equal(26, r1);
        Assert.Equal(4, r2);
        Assert.Equal(6, r3);
    }

    [Fact]
    public async Task BitCountWithBitUnit()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foobar", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitCount(key, 1, 1); // Using default byte
        var r2 = db.StringBitCount(key, 1, 1, StringIndexType.Bit);

        Assert.Equal(6, r1);
        Assert.Equal(1, r2);

        // Async

        r1 = await db.StringBitCountAsync(key, 1, 1); // Using default byte
        r2 = await db.StringBitCountAsync(key, 1, 1, StringIndexType.Bit);

        Assert.Equal(6, r1);
        Assert.Equal(1, r2);
    }

    [Fact]
    public async Task BitOp()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var prefix = Me();
        var key1 = prefix + "1";
        var key2 = prefix + "2";
        var key3 = prefix + "3";
        db.StringSet(key1, new byte[] { 3 }, flags: CommandFlags.FireAndForget);
        db.StringSet(key2, new byte[] { 6 }, flags: CommandFlags.FireAndForget);
        db.StringSet(key3, new byte[] { 12 }, flags: CommandFlags.FireAndForget);

        var len_and = db.StringBitOperationAsync(Bitwise.And, "and", new RedisKey[] { key1, key2, key3 });
        var len_or = db.StringBitOperationAsync(Bitwise.Or, "or", new RedisKey[] { key1, key2, key3 });
        var len_xor = db.StringBitOperationAsync(Bitwise.Xor, "xor", new RedisKey[] { key1, key2, key3 });
        var len_not = db.StringBitOperationAsync(Bitwise.Not, "not", key1);

        Assert.Equal(1, await len_and);
        Assert.Equal(1, await len_or);
        Assert.Equal(1, await len_xor);
        Assert.Equal(1, await len_not);

        var r_and = ((byte[]?)(await db.StringGetAsync("and").ForAwait()))?.Single();
        var r_or = ((byte[]?)(await db.StringGetAsync("or").ForAwait()))?.Single();
        var r_xor = ((byte[]?)(await db.StringGetAsync("xor").ForAwait()))?.Single();
        var r_not = ((byte[]?)(await db.StringGetAsync("not").ForAwait()))?.Single();

        Assert.Equal((byte)(3 & 6 & 12), r_and);
        Assert.Equal((byte)(3 | 6 | 12), r_or);
        Assert.Equal((byte)(3 ^ 6 ^ 12), r_xor);
        Assert.Equal(unchecked((byte)(~3)), r_not);
    }

    [Fact]
    public async Task BitPosition()
    {
        using var conn = Create(require: RedisFeatures.v2_6_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foo", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitPosition(key, true);
        var r2 = db.StringBitPosition(key, true, 10, 10);
        var r3 = db.StringBitPosition(key, true, 1, 3);

        Assert.Equal(1, r1);
        Assert.Equal(-1, r2);
        Assert.Equal(9, r3);

        // Async

        r1 = await db.StringBitPositionAsync(key, true);
        r2 = await db.StringBitPositionAsync(key, true, 10, 10);
        r3 = await db.StringBitPositionAsync(key, true, 1, 3);

        Assert.Equal(1, r1);
        Assert.Equal(-1, r2);
        Assert.Equal(9, r3);
    }

    [Fact]
    public async Task BitPositionWithBitUnit()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, flags: CommandFlags.FireAndForget);
        db.StringSet(key, "foo", flags: CommandFlags.FireAndForget);

        var r1 = db.StringBitPositionAsync(key, true, 1, 3); // Using default byte
        var r2 = db.StringBitPositionAsync(key, true, 1, 3, StringIndexType.Bit);

        Assert.Equal(9, await r1);
        Assert.Equal(1, await r2);
    }

    [Fact]
    public async Task RangeString()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.StringSet(key, "hello world", flags: CommandFlags.FireAndForget);
        var result = db.StringGetRangeAsync(key, 2, 6);
        Assert.Equal("llo w", await result);
    }

    [Fact]
    public async Task HashStringLengthAsync()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string value = "hello world";
        db.HashSet(key, "field", value);
        var resAsync = db.HashStringLengthAsync(key, "field");
        var resNonExistingAsync = db.HashStringLengthAsync(key, "non-existing-field");
        Assert.Equal(value.Length, await resAsync);
        Assert.Equal(0, await resNonExistingAsync);
    }


    [Fact]
    public void HashStringLength()
    {
        using var conn = Create(require: RedisFeatures.v3_2_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string value = "hello world";
        db.HashSet(key, "field", value);
        Assert.Equal(value.Length, db.HashStringLength(key, "field"));
        Assert.Equal(0, db.HashStringLength(key, "non-existing-field"));
    }

    [Fact]
    public void LongestCommonSubsequence()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key1 = Me() + "1";
        var key2 = Me() + "2";
        db.KeyDelete(key1);
        db.KeyDelete(key2);
        db.StringSet(key1, "ohmytext");
        db.StringSet(key2, "mynewtext");

        Assert.Equal("mytext", db.StringLongestCommonSubsequence(key1, key2));
        Assert.Equal(6, db.StringLongestCommonSubsequenceLength(key1, key2));

        var stringMatchResult = db.StringLongestCommonSubsequenceWithMatches(key1, key2);
        Assert.Equal(2, stringMatchResult.Matches.Length); // "my" and "text" are the two matches of the result
        Assert.Equivalent(new LCSMatchResult.LCSMatch(4, 5, length: 4), stringMatchResult.Matches[0]); // the string "text" starts at index 4 in the first string and at index 5 in the second string
        Assert.Equivalent(new LCSMatchResult.LCSMatch(2, 0, length: 2), stringMatchResult.Matches[1]); // the string "my" starts at index 2 in the first string and at index 0 in the second string

        stringMatchResult = db.StringLongestCommonSubsequenceWithMatches(key1, key2, 5);
        Assert.Empty(stringMatchResult.Matches); // no matches longer than 5 characters
        Assert.Equal(6, stringMatchResult.LongestMatchLength);

        // Missing keys
        db.KeyDelete(key1);
        Assert.Equal(string.Empty, db.StringLongestCommonSubsequence(key1, key2));
        db.KeyDelete(key2);
        Assert.Equal(string.Empty, db.StringLongestCommonSubsequence(key1, key2));
        stringMatchResult = db.StringLongestCommonSubsequenceWithMatches(key1, key2);
        Assert.NotNull(stringMatchResult.Matches);
        Assert.Empty(stringMatchResult.Matches);
        Assert.Equal(0, stringMatchResult.LongestMatchLength);

        // Default value
        stringMatchResult = db.StringLongestCommonSubsequenceWithMatches(key1, key2, flags: CommandFlags.FireAndForget);
        Assert.True(stringMatchResult.IsEmpty);
    }

    [Fact]
    public async Task LongestCommonSubsequenceAsync()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var db = conn.GetDatabase();
        var key1 = Me() + "1";
        var key2 = Me() + "2";
        db.KeyDelete(key1);
        db.KeyDelete(key2);
        db.StringSet(key1, "ohmytext");
        db.StringSet(key2, "mynewtext");

        Assert.Equal("mytext", await db.StringLongestCommonSubsequenceAsync(key1, key2));
        Assert.Equal(6, await db.StringLongestCommonSubsequenceLengthAsync(key1, key2));

        var stringMatchResult = await db.StringLongestCommonSubsequenceWithMatchesAsync(key1, key2);
        Assert.Equal(2, stringMatchResult.Matches.Length); // "my" and "text" are the two matches of the result
        Assert.Equivalent(new LCSMatchResult.LCSMatch(4, 5, length: 4), stringMatchResult.Matches[0]); // the string "text" starts at index 4 in the first string and at index 5 in the second string
        Assert.Equivalent(new LCSMatchResult.LCSMatch(2, 0, length: 2), stringMatchResult.Matches[1]); // the string "my" starts at index 2 in the first string and at index 0 in the second string

        stringMatchResult = await db.StringLongestCommonSubsequenceWithMatchesAsync(key1, key2, 5);
        Assert.Empty(stringMatchResult.Matches); // no matches longer than 5 characters
        Assert.Equal(6, stringMatchResult.LongestMatchLength);

        // Missing keys
        db.KeyDelete(key1);
        Assert.Equal(string.Empty, await db.StringLongestCommonSubsequenceAsync(key1, key2));
        db.KeyDelete(key2);
        Assert.Equal(string.Empty, await db.StringLongestCommonSubsequenceAsync(key1, key2));
        stringMatchResult = await db.StringLongestCommonSubsequenceWithMatchesAsync(key1, key2);
        Assert.NotNull(stringMatchResult.Matches);
        Assert.Empty(stringMatchResult.Matches);
        Assert.Equal(0, stringMatchResult.LongestMatchLength);

        // Default value
        stringMatchResult = await db.StringLongestCommonSubsequenceWithMatchesAsync(key1, key2, flags: CommandFlags.FireAndForget);
        Assert.True(stringMatchResult.IsEmpty);
    }

    private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value);
    private static string? Decode(byte[]? value) => value is null ? null : Encoding.UTF8.GetString(value);
}
