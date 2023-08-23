using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class KeyTests : TestBase
{
    public KeyTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public void TestScan()
    {
        using var conn = Create(allowAdmin: true);

        var dbId = TestConfig.GetDedicatedDB(conn);
        var db = conn.GetDatabase(dbId);
        var server = GetAnyPrimary(conn);
        var prefix = Me();
        server.FlushDatabase(dbId, flags: CommandFlags.FireAndForget);

        const int Count = 1000;
        for (int i = 0; i < Count; i++)
            db.StringSet(prefix + "x" + i, "y" + i, flags: CommandFlags.FireAndForget);

        var count = server.Keys(dbId, prefix + "*").Count();
        Assert.Equal(Count, count);
    }

    [Fact]
    public void FlushFetchRandomKey()
    {
        using var conn = Create(allowAdmin: true);

        var dbId = TestConfig.GetDedicatedDB(conn);
        Skip.IfMissingDatabase(conn, dbId);
        var db = conn.GetDatabase(dbId);
        var prefix = Me();
        conn.GetServer(TestConfig.Current.PrimaryServerAndPort).FlushDatabase(dbId, CommandFlags.FireAndForget);
        string? anyKey = db.KeyRandom();

        Assert.Null(anyKey);
        db.StringSet(prefix + "abc", "def");
        byte[]? keyBytes = db.KeyRandom();

        Assert.NotNull(keyBytes);
        Assert.Equal(prefix + "abc", Encoding.UTF8.GetString(keyBytes));
    }

    [Fact]
    public void Zeros()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, 123, flags: CommandFlags.FireAndForget);
        int k = (int)db.StringGet(key);
        Assert.Equal(123, k);

        db.KeyDelete(key, CommandFlags.FireAndForget);
        int i = (int)db.StringGet(key);
        Assert.Equal(0, i);

        Assert.True(db.StringGet(key).IsNull);
        int? value = (int?)db.StringGet(key);
        Assert.False(value.HasValue);
    }

    [Fact]
    public void PrependAppend()
    {
        {
            // simple
            RedisKey key = "world";
            var ret = key.Prepend("hello");
            Assert.Equal("helloworld", ret);
        }

        {
            RedisKey key1 = "world";
            RedisKey key2 = Encoding.UTF8.GetBytes("hello");
            var key3 = key1.Prepend(key2);
            Assert.True(ReferenceEquals(key1.KeyValue, key3.KeyValue));
            Assert.True(ReferenceEquals(key2.KeyValue, key3.KeyPrefix));
            Assert.Equal("helloworld", key3);
        }

        {
            RedisKey key = "hello";
            var ret = key.Append("world");
            Assert.Equal("helloworld", ret);
        }

        {
            RedisKey key1 = Encoding.UTF8.GetBytes("hello");
            RedisKey key2 = "world";
            var key3 = key1.Append(key2);
            Assert.True(ReferenceEquals(key2.KeyValue, key3.KeyValue));
            Assert.True(ReferenceEquals(key1.KeyValue, key3.KeyPrefix));
            Assert.Equal("helloworld", key3);
        }
    }

    [Fact]
    public void Exists()
    {
        using var conn = Create();

        RedisKey key = Me();
        RedisKey key2 = Me() + "2";
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.KeyDelete(key2, CommandFlags.FireAndForget);

        Assert.False(db.KeyExists(key));
        Assert.False(db.KeyExists(key2));
        Assert.Equal(0, db.KeyExists(new[] { key, key2 }));

        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        Assert.True(db.KeyExists(key));
        Assert.False(db.KeyExists(key2));
        Assert.Equal(1, db.KeyExists(new[] { key, key2 }));

        db.StringSet(key2, "new value", flags: CommandFlags.FireAndForget);
        Assert.True(db.KeyExists(key));
        Assert.True(db.KeyExists(key2));
        Assert.Equal(2, db.KeyExists(new[] { key, key2 }));
    }

    [Fact]
    public async Task ExistsAsync()
    {
        using var conn = Create();

        RedisKey key = Me();
        RedisKey key2 = Me() + "2";
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.KeyDelete(key2, CommandFlags.FireAndForget);
        var a1 = db.KeyExistsAsync(key).ForAwait();
        var a2 = db.KeyExistsAsync(key2).ForAwait();
        var a3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);

        var b1 = db.KeyExistsAsync(key).ForAwait();
        var b2 = db.KeyExistsAsync(key2).ForAwait();
        var b3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

        db.StringSet(key2, "new value", flags: CommandFlags.FireAndForget);

        var c1 = db.KeyExistsAsync(key).ForAwait();
        var c2 = db.KeyExistsAsync(key2).ForAwait();
        var c3 = db.KeyExistsAsync(new[] { key, key2 }).ForAwait();

        Assert.False(await a1);
        Assert.False(await a2);
        Assert.Equal(0, await a3);

        Assert.True(await b1);
        Assert.False(await b2);
        Assert.Equal(1, await b3);

        Assert.True(await c1);
        Assert.True(await c2);
        Assert.Equal(2, await c3);
    }

    [Fact]
    public async Task IdleTime()
    {
        using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = db.KeyIdleTime(key);
        Assert.True(idleTime > TimeSpan.Zero);

        db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
        var idleTime2 = db.KeyIdleTime(key);
        Assert.True(idleTime2 < idleTime);

        db.KeyDelete(key);
        var idleTime3 = db.KeyIdleTime(key);
        Assert.Null(idleTime3);
    }

    [Fact]
    public async Task TouchIdleTime()
    {
        using var conn = Create(require: RedisFeatures.v3_2_1);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = db.KeyIdleTime(key);
        Assert.True(idleTime > TimeSpan.Zero);

        Assert.True(db.KeyTouch(key));
        var idleTime1 = db.KeyIdleTime(key);
        Assert.True(idleTime1 < idleTime);
    }

    [Fact]
    public async Task IdleTimeAsync()
    {
        using var conn = Create();

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero);

        db.StringSet(key, "new value2", flags: CommandFlags.FireAndForget);
        var idleTime2 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime2 < idleTime);

        db.KeyDelete(key);
        var idleTime3 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.Null(idleTime3);
    }

    [Fact]
    public async Task TouchIdleTimeAsync()
    {
        using var conn = Create(require: RedisFeatures.v3_2_1);

        RedisKey key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        await Task.Delay(2000).ForAwait();
        var idleTime = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime > TimeSpan.Zero);

        Assert.True(await db.KeyTouchAsync(key).ForAwait());
        var idleTime1 = await db.KeyIdleTimeAsync(key).ForAwait();
        Assert.True(idleTime1 < idleTime);
    }

    [Fact]
    public async Task KeyEncoding()
    {
        using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);

        Assert.Equal("embstr", db.KeyEncoding(key));
        Assert.Equal("embstr", await db.KeyEncodingAsync(key));

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.ListLeftPush(key, "new value", flags: CommandFlags.FireAndForget);

        // Depending on server version, this is going to vary - we're sanity checking here.
        var listTypes = new [] { "ziplist", "quicklist", "listpack" };
        Assert.Contains(db.KeyEncoding(key), listTypes);
        Assert.Contains(await db.KeyEncodingAsync(key), listTypes);

        var keyNotExists = key + "no-exist";
        Assert.Null(db.KeyEncoding(keyNotExists));
        Assert.Null(await db.KeyEncodingAsync(keyNotExists));
    }

    [Fact]
    public async Task KeyRefCount()
    {
        using var conn = Create();

        var key = Me();
        var db = conn.GetDatabase();
        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);

        Assert.Equal(1, db.KeyRefCount(key));
        Assert.Equal(1, await db.KeyRefCountAsync(key));

        var keyNotExists = key + "no-exist";
        Assert.Null(db.KeyRefCount(keyNotExists));
        Assert.Null(await db.KeyRefCountAsync(keyNotExists));
    }

    [Fact]
    public async Task KeyFrequency()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v4_0_0);

        var key = Me();
        var db = conn.GetDatabase();
        var server = GetServer(conn);

        var serverConfig = server.ConfigGet("maxmemory-policy");
        var maxMemoryPolicy = serverConfig.Length == 1 ? serverConfig[0].Value : "";
        Log($"maxmemory-policy detected as {maxMemoryPolicy}");
        var isLfu = maxMemoryPolicy.Contains("lfu");

        db.KeyDelete(key, CommandFlags.FireAndForget);
        db.StringSet(key, "new value", flags: CommandFlags.FireAndForget);
        db.StringGet(key);

        if (isLfu)
        {
            var count = db.KeyFrequency(key);
            Assert.True(count > 0);

            count = await db.KeyFrequencyAsync(key);
            Assert.True(count > 0);

            // Key not exists
            db.KeyDelete(key, CommandFlags.FireAndForget);
            var res = db.KeyFrequency(key);
            Assert.Null(res);

            res = await db.KeyFrequencyAsync(key);
            Assert.Null(res);
        }
        else
        {
            var ex = Assert.Throws<RedisServerException>(() => db.KeyFrequency(key));
            Assert.Contains("An LFU maxmemory policy is not selected", ex.Message);
            ex = await Assert.ThrowsAsync<RedisServerException>(() => db.KeyFrequencyAsync(key));
            Assert.Contains("An LFU maxmemory policy is not selected", ex.Message);
        }
    }

    private static void TestTotalLengthAndCopyTo(in RedisKey key, int expectedLength)
    {
        var length = key.TotalLength();
        Assert.Equal(expectedLength, length);
        var arr = ArrayPool<byte>.Shared.Rent(length + 20); // deliberately over-sized
        try
        {
            var written = key.CopyTo(arr);
            Assert.Equal(length, written);

            var viaCast = (byte[]?)key;
            ReadOnlySpan<byte> x = viaCast, y = new ReadOnlySpan<byte>(arr, 0, length);
            Assert.True(x.SequenceEqual(y));
            Assert.True(key.IsNull == viaCast is null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }

    [Fact]
    public void NullKeySlot()
    {
        RedisKey key = RedisKey.Null;
        Assert.True(key.TryGetSimpleBuffer(out var buffer));
        Assert.Empty(buffer);
        TestTotalLengthAndCopyTo(key, 0);

        Assert.Equal(-1, GetHashSlot(key));
    }

    private static readonly byte[] KeyPrefix = Encoding.UTF8.GetBytes("abcde");

    private static int GetHashSlot(in RedisKey key)
    {
        var strategy = new ServerSelectionStrategy(null!)
        {
            ServerType = ServerType.Cluster
        };
        return strategy.HashSlot(key);
    }

    [Theory]
    [InlineData(false, null, -1)]
    [InlineData(false, "", 0)]
    [InlineData(false, "f", 3168)]
    [InlineData(false, "abcde", 16097)]
    [InlineData(false, "abcdef", 15101)]
    [InlineData(false, "abcdeffsdkjhsdfgkjh sdkjhsdkjf hsdkjfh skudrfy7 348iu yksef78 dssdhkfh ##$OIU", 5073)]
    [InlineData(false, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Cras lobortis quam ac molestie ultricies. Duis maximus, nunc a auctor faucibus, risus turpis porttitor nibh, sit amet consequat lacus nibh quis nisi. Aliquam ipsum quam, dapibus ut ex eu, efficitur vestibulum dui. Sed a nibh ut felis congue tempor vel vel lectus. Phasellus a neque placerat, blandit massa sed, imperdiet urna. Praesent scelerisque lorem ipsum, non facilisis libero hendrerit quis. Nullam sit amet malesuada velit, ac lacinia lacus. Donec mollis a massa sed egestas. Suspendisse vitae augue quis erat gravida consectetur. Aenean interdum neque id lacinia eleifend.", 4954)]
    [InlineData(true, null, 16097)]
    [InlineData(true, "", 16097)] // note same as false/abcde
    [InlineData(true, "f", 15101)] // note same as false/abcdef
    [InlineData(true, "abcde", 4089)]
    [InlineData(true, "abcdef", 1167)]
    [InlineData(true, "👻👩‍👩‍👦‍👦", 8494)]
    [InlineData(true, "abcdeffsdkjhsdfgkjh sdkjhsdkjf hsdkjfh skudrfy7 348iu yksef78 dssdhkfh ##$OIU", 10923)]
    [InlineData(true, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Cras lobortis quam ac molestie ultricies. Duis maximus, nunc a auctor faucibus, risus turpis porttitor nibh, sit amet consequat lacus nibh quis nisi. Aliquam ipsum quam, dapibus ut ex eu, efficitur vestibulum dui. Sed a nibh ut felis congue tempor vel vel lectus. Phasellus a neque placerat, blandit massa sed, imperdiet urna. Praesent scelerisque lorem ipsum, non facilisis libero hendrerit quis. Nullam sit amet malesuada velit, ac lacinia lacus. Donec mollis a massa sed egestas. Suspendisse vitae augue quis erat gravida consectetur. Aenean interdum neque id lacinia eleifend.", 4452)]
    public void TestStringKeySlot(bool prefixed, string? s, int slot)
    {
        RedisKey key = prefixed ? new RedisKey(KeyPrefix, s) : s;
        if (s is null && !prefixed)
        {
            Assert.True(key.TryGetSimpleBuffer(out var buffer));
            Assert.Empty(buffer);
            TestTotalLengthAndCopyTo(key, 0);
        }
        else
        {
            Assert.False(key.TryGetSimpleBuffer(out var _));
        }
        TestTotalLengthAndCopyTo(key, Encoding.UTF8.GetByteCount(s ?? "") + (prefixed ? KeyPrefix.Length : 0));

        Assert.Equal(slot, GetHashSlot(key));
    }

    [Theory]
    [InlineData(false, -1, -1)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 1, 10242)]
    [InlineData(false, 6, 10015)]
    [InlineData(false, 47, 849)]
    [InlineData(false, 14123, 2356)]
    [InlineData(true, -1, 16097)]
    [InlineData(true, 0, 16097)]
    [InlineData(true, 1, 7839)]
    [InlineData(true, 6, 6509)]
    [InlineData(true, 47, 2217)]
    [InlineData(true, 14123, 6773)]
    public void TestBlobKeySlot(bool prefixed, int count, int slot)
    {
        byte[]? blob = null;
        if (count >= 0)
        {
            blob = new byte[count];
            new Random(count).NextBytes(blob);
            for (int i = 0; i < blob.Length; i++)
            {
                if (blob[i] == (byte)'{') blob[i] = (byte)'!'; // avoid unexpected hash tags
            }
        }
        RedisKey key = prefixed ? new RedisKey(KeyPrefix, blob) : blob;
        if (prefixed)
        {
            Assert.False(key.TryGetSimpleBuffer(out _));
        }
        else
        {
            Assert.True(key.TryGetSimpleBuffer(out var buffer));
            if (blob is null)
            {
                Assert.Empty(buffer);
            }
            else
            {
                Assert.Same(blob, buffer);
            }
        }
        TestTotalLengthAndCopyTo(key, (blob?.Length ?? 0) + (prefixed ? KeyPrefix.Length : 0));

        Assert.Equal(slot, GetHashSlot(key));
    }

    [Theory]
    [MemberData(nameof(KeyEqualityData))]
    public void KeyEquality(RedisKey x, RedisKey y, bool equal)
    {
        if (equal)
        {
            Assert.Equal(x, y);
            Assert.True(x == y);
            Assert.False(x != y);
            Assert.True(x.Equals(y));
            Assert.True(x.Equals((object)y));
            Assert.Equal(x.GetHashCode(), y.GetHashCode());
        }
        else
        {
            Assert.NotEqual(x, y);
            Assert.False(x == y);
            Assert.True(x != y);
            Assert.False(x.Equals(y));
            Assert.False(x.Equals((object)y));
            // note that this last one is not strictly required, but: we pass, so: yay!
            Assert.NotEqual(x.GetHashCode(), y.GetHashCode());
        }
    }

    public static IEnumerable<object[]> KeyEqualityData()
    {
        RedisKey abcString = "abc", abcBytes = Encoding.UTF8.GetBytes("abc");
        RedisKey abcdefString = "abcdef", abcdefBytes = Encoding.UTF8.GetBytes("abcdef");

        yield return new object[] { RedisKey.Null, abcString, false };
        yield return new object[] { RedisKey.Null, abcBytes, false };
        yield return new object[] { abcString, RedisKey.Null, false };
        yield return new object[] { abcBytes, RedisKey.Null, false };
        yield return new object[] { RedisKey.Null, RedisKey.Null, true };
        yield return new object[] { new RedisKey((string?)null), RedisKey.Null, true };
        yield return new object[] { new RedisKey(null, (byte[]?)null), RedisKey.Null, true };
        yield return new object[] { new RedisKey(""), RedisKey.Null, false };
        yield return new object[] { new RedisKey(null, Array.Empty<byte>()), RedisKey.Null, false };

        yield return new object[] { abcString, abcString, true };
        yield return new object[] { abcBytes, abcBytes, true };
        yield return new object[] { abcString, abcBytes, true };
        yield return new object[] { abcBytes, abcString, true };

        yield return new object[] { abcdefString, abcdefString, true };
        yield return new object[] { abcdefBytes, abcdefBytes, true };
        yield return new object[] { abcdefString, abcdefBytes, true };
        yield return new object[] { abcdefBytes, abcdefString, true };

        yield return new object[] { abcString, abcdefString, false };
        yield return new object[] { abcBytes, abcdefBytes, false };
        yield return new object[] { abcString, abcdefBytes, false };
        yield return new object[] { abcBytes, abcdefString, false };

        yield return new object[] { abcdefString, abcString, false };
        yield return new object[] { abcdefBytes, abcBytes, false };
        yield return new object[] { abcdefString, abcBytes, false };
        yield return new object[] { abcdefBytes, abcString, false };

        var x = abcString.Append("def");
        yield return new object[] { abcdefString, x, true };
        yield return new object[] { abcdefBytes, x, true };
        yield return new object[] { x, abcdefBytes, true };
        yield return new object[] { x, abcdefString, true };
        yield return new object[] { abcString, x, false };
        yield return new object[] { abcString, x, false };
        yield return new object[] { x, abcString, false };
        yield return new object[] { x, abcString, false };
    }
}
