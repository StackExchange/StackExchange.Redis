using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

// building on array.tcl from the redis tests
[RunPerProtocol]
public class ArrayTests(SharedConnectionFixture fixture, ITestOutputHelper log)
    : TestBase(log, fixture)
{
    [Fact]
    public async Task BasicSetGetTests()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisKey missing = WithSuffix(key, ":missing");
        await db.KeyDeleteAsync([key, missing]);

        Assert.True(await db.ArraySetAsync(key, 0, "hello"));
        Assert.Equal("hello", await db.ArrayGetAsync(key, 0));
        Assert.Equal(RedisValue.Null, await db.ArrayGetAsync(key, 1));

        Assert.False(await db.ArraySetAsync(key, 0, "world"));
        Assert.Equal("world", await db.ArrayGetAsync(key, 0));

        Assert.Equal(RedisValue.Null, await db.ArrayGetAsync(missing, 0));

        Assert.True(await db.ArraySetAsync(key, 10, 12345));
        Assert.Equal("12345", await db.ArrayGetAsync(key, 10));

        Assert.True(await db.ArraySetAsync(key, 11, 3.14159));
        var floatValue = await db.ArrayGetAsync(key, 11);
        Assert.Equal(3.14159, (double)floatValue, precision: 5);

        Assert.True(await db.ArraySetAsync(key, 12, "abc"));
        Assert.Equal("abc", await db.ArrayGetAsync(key, 12));

        var longString = new string('x', 100);
        Assert.True(await db.ArraySetAsync(key, 13, longString));
        Assert.Equal(longString, await db.ArrayGetAsync(key, 13));

        Assert.True(await db.ArraySetAsync(key, 14, RedisValue.EmptyString));
        Assert.Equal(RedisValue.EmptyString, await db.ArrayGetAsync(key, 14));
    }

    [Fact]
    public async Task LengthCountAndSparseGaps()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        AssertIndex(await db.ArrayLengthAsync(key), 0);
        AssertIndex(await db.ArrayCountAsync(key), 0);

        Assert.True(await db.ArraySetAsync(key, 0, "a"));
        AssertIndex(await db.ArrayLengthAsync(key), 1);
        AssertIndex(await db.ArrayCountAsync(key), 1);

        Assert.True(await db.ArraySetAsync(key, 5, "b"));
        AssertIndex(await db.ArrayLengthAsync(key), 6);
        AssertIndex(await db.ArrayCountAsync(key), 2);

        Assert.True(await db.ArraySetAsync(key, 100, "c"));
        AssertIndex(await db.ArrayLengthAsync(key), 101);
        AssertIndex(await db.ArrayCountAsync(key), 3);

        await db.KeyDeleteAsync(key);
        Assert.True(await db.ArraySetAsync(key, 0, "a"));
        Assert.True(await db.ArraySetAsync(key, 10000, "b"));
        Assert.True(await db.ArraySetAsync(key, 1000000, "c"));

        Assert.Equal("a", await db.ArrayGetAsync(key, 0));
        Assert.Equal("b", await db.ArrayGetAsync(key, 10000));
        Assert.Equal("c", await db.ArrayGetAsync(key, 1000000));
        AssertIndex(await db.ArrayCountAsync(key), 3);
        AssertIndex(await db.ArrayLengthAsync(key), 1000001);
    }

    [Fact]
    public async Task DeleteAndDeleteRange()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        Assert.Equal(3, await db.ArraySetAsync(key, 0, ["a", "b", "c"]));
        Assert.True(await db.ArrayDeleteAsync(key, 1));
        Assert.Equal(RedisValue.Null, await db.ArrayGetAsync(key, 1));
        AssertIndex(await db.ArrayCountAsync(key), 2);
        Assert.False(await db.ArrayDeleteAsync(key, 1));

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, 0, ["a", "b", "c", "d"]));
        Assert.Equal(3, await db.ArrayDeleteAsync(key, [0, 1, 2]));
        AssertIndex(await db.ArrayCountAsync(key), 1);

        await db.KeyDeleteAsync(key);
        Assert.True(await db.ArraySetAsync(key, 0, "a"));
        Assert.True(await db.ArrayDeleteAsync(key, 0));
        Assert.False(await db.KeyExistsAsync(key));

        await db.KeyDeleteAsync(key);
        await SetNumericValuesAsync(db, key, 10);
        AssertIndex(await db.ArrayCountAsync(key), 10);
        AssertIndex(await db.ArrayDeleteRangeAsync(key, 2, 6), 5);
        AssertIndex(await db.ArrayCountAsync(key), 5);

        await db.KeyDeleteAsync(key);
        await SetNumericValuesAsync(db, key, 10);
        AssertIndex(await db.ArrayDeleteRangeAsync(key, 6, 2), 5);
        AssertIndex(await db.ArrayCountAsync(key), 5);

        await db.KeyDeleteAsync(key);
        Assert.Equal(6, await db.ArraySetAsync(key, 0, ["a", "b", "c", "d", "e", "f"]));
        AssertIndex(await db.ArrayDeleteRangeAsync(key, [new RedisArrayRange(0, 1), new RedisArrayRange(4, 5)]), 4);
        AssertValues(await db.ArrayGetRangeAsync(key, 0, 5), RedisValue.Null, RedisValue.Null, "c", "d", RedisValue.Null, RedisValue.Null);
    }

    [Fact(Timeout = 10000)]
    public async Task DeleteLastElementPublishesArrayDeleteBeforeKeyDeleteNotifications()
    {
        await using var conn = Create(allowAdmin: true, require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        await AssertArrayKeyspaceNotificationsEnabledAsync(conn);

        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var sub = conn.GetSubscriber();
        var channel = RedisChannel.Pattern($"__key*@{db.Database}__:*");
        var queue = await sub.SubscribeAsync(channel);
        try
        {
            Assert.True(await db.ArraySetAsync(key, 0, "a"));
            Assert.True(await db.ArrayDeleteAsync(key, 0));

            AssertNotification(await ReadNotificationAsync(queue, key), KeyNotificationKind.KeySpace, KeyNotificationType.ArDel);
            AssertNotification(await ReadNotificationAsync(queue, key), KeyNotificationKind.KeyEvent, KeyNotificationType.ArDel);
            AssertNotification(await ReadNotificationAsync(queue, key), KeyNotificationKind.KeySpace, KeyNotificationType.Del);
            AssertNotification(await ReadNotificationAsync(queue, key), KeyNotificationKind.KeyEvent, KeyNotificationType.Del);
        }
        finally
        {
            await queue.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task MultiSetMultiGetAndRanges()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(1, "b"), Entry(2, "c")]));
        Assert.Equal("a", await db.ArrayGetAsync(key, 0));
        Assert.Equal("b", await db.ArrayGetAsync(key, 1));
        Assert.Equal("c", await db.ArrayGetAsync(key, 2));

        await db.KeyDeleteAsync(key);
        Assert.True(await db.ArraySetAsync(key, 0, "a"));
        Assert.Equal(1, await db.ArraySetAsync(key, [Entry(0, "aa"), Entry(1, "b")]));
        Assert.Equal("aa", await db.ArrayGetAsync(key, 0));
        Assert.Equal("b", await db.ArrayGetAsync(key, 1));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(1, "b"), Entry(5, "c")]));
        AssertValues(await db.ArrayGetAsync(key, [0, 1, 5, 3]), "a", "b", "c", RedisValue.Null);

        await db.KeyDeleteAsync(key);
        Assert.Equal(5, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(1, "b"), Entry(2, "c"), Entry(3, "d"), Entry(4, "e")]));
        AssertValues(await db.ArrayGetRangeAsync(key, 1, 3), "b", "c", "d");
        AssertValues(await db.ArrayGetRangeAsync(key, 3, 1), "d", "c", "b");

        await AssertServerErrorAsync("range exceeds maximum", async () => _ = await db.ArrayGetRangeAsync(key, 0, 1000000));
        await AssertServerErrorAsync("range exceeds maximum", async () => _ = await db.ArrayGetRangeAsync(key, 1000000, 0));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, 0, ["a", "b", "c"]));
        Assert.Equal("a", await db.ArrayGetAsync(key, 0));
        Assert.Equal("b", await db.ArrayGetAsync(key, 1));
        Assert.Equal("c", await db.ArrayGetAsync(key, 2));
    }

    [Fact]
    public async Task Scan()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisKey missing = WithSuffix(key, ":missing");
        await db.KeyDeleteAsync([key, missing]);

        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(5, "b"), Entry(9, "c")]));
        AssertEntries(await db.ArrayScanAsync(key, 0, 10), Entry(0, "a"), Entry(5, "b"), Entry(9, "c"));

        await db.KeyDeleteAsync(key);
        Assert.True(await db.ArraySetAsync(key, 500, "x"));
        Assert.Empty(await db.ArrayScanAsync(key, 0, 100));

        await db.KeyDeleteAsync(key);
        Assert.Equal(2, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(5, "b")]));
        AssertEntries(await db.ArrayScanAsync(key, 5, 0), Entry(5, "b"), Entry(0, "a"));

        Assert.Empty(await db.ArrayScanAsync(missing, 0, 100));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "string"), Entry(1, 12345), Entry(2, 3.14)]));
        AssertEntries(await db.ArrayScanAsync(key, 0, 10), Entry(0, "string"), Entry(1, "12345"), Entry(2, "3.14"));
    }

    [Fact]
    public async Task GrepBasics()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisKey missing = WithSuffix(key, ":missing");
        await db.KeyDeleteAsync([key, missing]);

        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "alpha"), Entry(1, "beta"), Entry(2, "alphabet"), Entry(5, "gamma")]));
        AssertIndexEntries(await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Match("alpha"))), 0, 2);

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "alpha"), Entry(1, "beta"), Entry(2, "alphabet"), Entry(3, "delta")]));
        var withValues = CreateGrep(ArrayGrepRequest.Predicate.Match("alpha"));
        withValues.Start = 3;
        withValues.End = 0;
        withValues.IncludeValues = true;
        AssertEntries(await db.ArrayGrepAsync(key, withValues), Entry(2, "alphabet"), Entry(0, "alpha"));

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "RedisArray"), Entry(1, "redis-match"), Entry(2, "array-only"), Entry(3, "plain")]));
        var andNoCase = CreateGrep(ArrayGrepRequest.Predicate.Match("redis"), ArrayGrepRequest.Predicate.Glob("*array*"));
        andNoCase.IsIntersection = true;
        andNoCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, andNoCase), 0);

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "hit-1"), Entry(1, "hit-2"), Entry(2, "miss"), Entry(3, "hit-3")]));
        var limited = CreateGrep(ArrayGrepRequest.Predicate.Match("hit"));
        limited.Limit = 2;
        AssertIndexEntries(await db.ArrayGrepAsync(key, limited), 0, 1);

        Assert.Empty(await db.ArrayGrepAsync(missing, CreateGrep(ArrayGrepRequest.Predicate.Match("foo"))));
    }

    [Fact]
    public async Task GrepRegexAndErrors()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "foo123"), Entry(1, "bar"), Entry(2, "zoo999"), Entry(3, "Foo777")]));
        AssertIndexEntries(await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex("^.*[0-9]{3}$"))), 0, 2, 3);

        var noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("^foo[0-9]+$"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 3);

        await db.KeyDeleteAsync(key);
        var values = new RedisArrayEntry[]
        {
            Entry(0, "foo"), Entry(1, "bar"), Entry(2, "baz"), Entry(3, "foobar"), Entry(4, "BAR"),
            Entry(5, "quxfoo"), Entry(6, "zedbar"), Entry(7, "plain"), Entry(8, "ALPS"), Entry(9, "alphabet"),
        };
        Assert.Equal(10, await db.ArraySetAsync(key, values));

        AssertIndexEntries(await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex("foo|bar"))), 0, 1, 3, 5, 6);
        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("foo|bar"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 1, 3, 4, 5, 6);

        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("^(foo|bar)$"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 1, 4);

        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("^(foo|bar)"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 1, 3, 4);

        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("(foo|bar)$"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 1, 3, 4, 5, 6);

        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("alpha|alps"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 8, 9);

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "item-foo-123"), Entry(1, "ITEM-BAR-456"), Entry(2, "item-baz"), Entry(3, "plain")]));
        noCase = CreateGrep(ArrayGrepRequest.Predicate.Regex("^item-(foo|bar)-[0-9]{3}$"));
        noCase.IsCaseSensitive = true;
        AssertIndexEntries(await db.ArrayGrepAsync(key, noCase), 0, 1);

        await db.KeyDeleteAsync(key);
        var re2048 = new string('a', 2048);
        var re2049 = new string('a', 2049);
        Assert.True(await db.ArraySetAsync(key, 0, re2048));
        AssertIndexEntries(await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex(re2048))), 0);
        await AssertServerErrorAsync("maximum is 2048 bytes", async () => _ = await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex(re2049))));
        await AssertServerErrorAsync("backreferences are not supported", async () => _ = await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex("(a)\\1"))));
        await AssertServerErrorAsync("regular expression is empty", async () => _ = await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex(""))));

        await AssertServerErrorAsync("invalid regular expression", async () => _ = await db.ArrayGrepAsync(key, CreateGrep(ArrayGrepRequest.Predicate.Regex("\\x{1"))));

        await db.KeyDeleteAsync(key);
        Assert.True(await db.ArraySetAsync(key, 0, "foo"));
        var request = new ArrayGrepRequest();
        for (int i = 0; i < 250; i++)
        {
            request.AddPredicate(ArrayGrepRequest.Predicate.Match("foo"));
        }
        AssertIndexEntries(await db.ArrayGrepAsync(key, request), 0);

        request = new ArrayGrepRequest();
        for (int i = 0; i < 251; i++)
        {
            request.AddPredicate(ArrayGrepRequest.Predicate.Match("foo"));
        }
        await AssertServerErrorAsync("maximum is 250", async () => _ = await db.ArrayGrepAsync(key, request));
    }

    [Fact]
    public async Task InsertRingNextSeekAndLastItems()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisKey missing = WithSuffix(key, ":missing");
        await db.KeyDeleteAsync([key, missing]);

        AssertIndex(await db.ArrayInsertAsync(key, "a"), 0);
        AssertIndex(await db.ArrayInsertAsync(key, "b"), 1);
        AssertIndex(await db.ArrayInsertAsync(key, "c"), 2);
        Assert.Equal("a", await db.ArrayGetAsync(key, 0));
        Assert.Equal("b", await db.ArrayGetAsync(key, 1));
        Assert.Equal("c", await db.ArrayGetAsync(key, 2));

        await db.KeyDeleteAsync(key);
        for (int i = 0; i < 10; i++)
        {
            _ = await db.ArrayRingAsync(key, 5, i);
        }
        Assert.Equal("5", await db.ArrayGetAsync(key, 0));
        Assert.Equal("6", await db.ArrayGetAsync(key, 1));
        Assert.Equal("7", await db.ArrayGetAsync(key, 2));
        Assert.Equal("8", await db.ArrayGetAsync(key, 3));
        Assert.Equal("9", await db.ArrayGetAsync(key, 4));
        AssertIndex(await db.ArrayCountAsync(key), 5);

        await db.KeyDeleteAsync(key);
        AssertIndex(await db.ArrayNextAsync(key), 0);
        AssertIndex(await db.ArrayInsertAsync(key, "a"), 0);
        AssertIndex(await db.ArrayNextAsync(key), 1);
        AssertIndex(await db.ArrayInsertAsync(key, "b"), 1);
        AssertIndex(await db.ArrayNextAsync(key), 2);

        Assert.False(await db.ArraySeekAsync(missing, 10));
        Assert.True(await db.ArraySeekAsync(key, 10));
        AssertIndex(await db.ArrayInsertAsync(key, "c"), 10);
        AssertIndex(await db.ArrayNextAsync(key), 11);
        Assert.Equal("c", await db.ArrayGetAsync(key, 10));

        await db.KeyDeleteAsync(key);
        AssertIndex(await db.ArrayInsertAsync(key, "a"), 0);
        Assert.True(await db.ArraySeekAsync(key, RedisArrayIndex.MaxValue));
        Assert.Null(await db.ArrayNextAsync(key));
        await AssertServerErrorAsync("insert index overflow", async () => _ = await db.ArrayInsertAsync(key, "b"));

        await db.KeyDeleteAsync(key);
        for (int i = 0; i < 5; i++)
        {
            _ = await db.ArrayInsertAsync(key, i * 10);
        }
        AssertValues(await db.ArrayLastItemsAsync(key, 3), "20", "30", "40");
        AssertValues(await db.ArrayLastItemsAsync(key, 3, reverse: true), "40", "30", "20");

        Assert.True(await db.ArraySeekAsync(key, 0));
        AssertValues(await db.ArrayLastItemsAsync(key, 3), "20", "30", "40");
        AssertValues(await db.ArrayLastItemsAsync(key, 3, reverse: true), "40", "30", "20");
    }

    [Fact]
    public async Task ArrayOperations()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, 10), Entry(1, 20), Entry(2, 30)]));
        Assert.Equal(60, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Sum));
        await Assert.ThrowsAsync<ArgumentException>(async () => _ = await db.ArrayOperationAsync(key, 0, 2, ArrayOperation.Match));
        await Assert.ThrowsAsync<ArgumentException>(async () => _ = await db.ArrayOperationAsync(key, 0, 2, ArrayOperation.Sum, "value"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => _ = await db.ArrayOperationAsync(key, 0, 2, ArrayOperation.Unknown));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, 30), Entry(1, 10), Entry(2, 20)]));
        Assert.Equal(10, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Min));
        Assert.Equal(30, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Max));

        await db.KeyDeleteAsync(key);
        Assert.Equal(4, await db.ArraySetAsync(key, [Entry(0, "hello"), Entry(1, "world"), Entry(2, "hello"), Entry(3, "foo")]));
        Assert.Equal(2, await ArrayOperationInt64Async(db, key, 0, 3, ArrayOperation.Match, "hello"));
        Assert.Equal(1, await ArrayOperationInt64Async(db, key, 0, 3, ArrayOperation.Match, "world"));
        Assert.Equal(0, await ArrayOperationInt64Async(db, key, 0, 3, ArrayOperation.Match, "bar"));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(2, "b"), Entry(5, "c")]));
        Assert.Equal(3, await ArrayOperationInt64Async(db, key, 0, 10, ArrayOperation.Used));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, 255), Entry(1, 15), Entry(2, 240)]));
        Assert.Equal(0, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.And));
        Assert.Equal(255, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Or));
        Assert.Equal(0, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Xor));

        await db.KeyDeleteAsync(key);
        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, 7.9), Entry(1, 3.2), Entry(2, 1.8)]));
        Assert.Equal(1, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.And));
        Assert.Equal(7, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Or));
        Assert.Equal(5, await ArrayOperationInt64Async(db, key, 0, 2, ArrayOperation.Xor));
    }

    [Fact]
    public async Task InfoTypeEncodingAndWrongType()
    {
        await using var conn = Create(require: RedisFeatures.v8_8_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        RedisKey wrongType = WithSuffix(key, ":wrong");
        await db.KeyDeleteAsync([key, wrongType]);

        Assert.Equal(3, await db.ArraySetAsync(key, [Entry(0, "a"), Entry(1, "b"), Entry(100, "c")]));
        var info = await db.ArrayInfoAsync(key);
        AssertIndex(info.Count, 3);
        AssertIndex(info.Length, 101);
        AssertIndex(info.NextInsertIndex, 0);
        AssertIndex(info.Slices, 1);
        AssertIndex(info.DirectorySize, 1);
        AssertIndex(info.SuperDirEntries, 0);
        AssertIndex(info.SliceSize, 4096);

        Assert.Equal(RedisType.Array, await db.KeyTypeAsync(key));
        Assert.Equal("sliced-array", await db.KeyEncodingAsync(key));

        Assert.True(await db.StringSetAsync(wrongType, "value"));
        await AssertServerErrorAsync("WRONGTYPE", async () => _ = await db.ArrayGetAsync(wrongType, 0));
        await AssertServerErrorAsync("WRONGTYPE", async () => _ = await db.ArraySetAsync(wrongType, 0, "foo"));
        await AssertServerErrorAsync("WRONGTYPE", async () => _ = await db.ArrayLengthAsync(wrongType));
        await AssertServerErrorAsync("WRONGTYPE", async () => _ = await db.ArrayCountAsync(wrongType));
    }

    private static RedisArrayEntry Entry(long index, RedisValue value) => new RedisArrayEntry(index, value);

    private static RedisKey WithSuffix(RedisKey key, string suffix) => (RedisKey)(key.ToString() + suffix);

    private static ArrayGrepRequest CreateGrep(params ArrayGrepRequest.Predicate[] predicates)
    {
        var request = new ArrayGrepRequest();
        foreach (var predicate in predicates)
        {
            request.AddPredicate(predicate);
        }

        return request;
    }

    private static async Task SetNumericValuesAsync(IDatabaseAsync db, RedisKey key, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Assert.True(await db.ArraySetAsync(key, i, i * 10));
        }
    }

    private static async Task<long> ArrayOperationInt64Async(
        IDatabaseAsync db,
        RedisKey key,
        RedisArrayIndex start,
        RedisArrayIndex end,
        ArrayOperation operation,
        RedisValue operand = default)
    {
        var result = await db.ArrayOperationAsync(key, start, end, operation, operand);
        return (long)result;
    }

    private static void AssertIndex(RedisArrayIndex actual, ulong expected)
    {
        Assert.Equal(expected, actual.Value);
    }

    private static void AssertIndex(RedisArrayIndex? actual, ulong expected)
    {
        Assert.True(actual.HasValue);
        Assert.Equal(expected, actual.GetValueOrDefault().Value);
    }

    private static void AssertIndexEntries(RedisArrayEntry[] actual, params ulong[] expected)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i].Index.Value);
            Assert.Equal(RedisValue.Null, actual[i].Value);
        }
    }

    private static void AssertEntries(RedisArrayEntry[] actual, params RedisArrayEntry[] expected)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Index.Value, actual[i].Index.Value);
            Assert.Equal(expected[i].Value, actual[i].Value);
        }
    }

    private static void AssertValues(RedisValue[] actual, params RedisValue[] expected)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    private static async Task<(KeyNotificationKind Kind, KeyNotificationType Type)> ReadNotificationAsync(ChannelMessageQueue queue, RedisKey key)
    {
        for (int i = 0; i < 64; i++)
        {
            var message = await queue.ReadAsync(TestContext.Current.CancellationToken);
            if (message.TryParseKeyNotification(out var notification)
                && notification.GetKey() == key
                && notification.Type is KeyNotificationType.ArDel or KeyNotificationType.Del)
            {
                return (notification.Kind, notification.Type);
            }
        }

        Assert.Fail($"Timed out waiting for array keyspace notifications for '{key}'.");
        return default;
    }

    private static void AssertNotification(
        (KeyNotificationKind Kind, KeyNotificationType Type) actual,
        KeyNotificationKind expectedKind,
        KeyNotificationType expectedType)
    {
        Assert.Equal(expectedKind, actual.Kind);
        Assert.Equal(expectedType, actual.Type);
    }

    private static async Task AssertArrayKeyspaceNotificationsEnabledAsync(IConnectionMultiplexer muxer)
    {
        foreach (var ep in muxer.GetEndPoints())
        {
            var server = muxer.GetServer(ep);
            var config = await server.ConfigGetAsync("notify-keyspace-events");
            var value = config.Length == 0 ? "" : config[0].Value.ToString() ?? "";

            foreach (var token in "AKE")
            {
                Assert.SkipUnless(
                    value.IndexOf(token) >= 0,
                    $"Server {ep} notify-keyspace-events config '{value}' missing required token '{token}' for array keyspace notifications.");
            }
        }
    }

    private static async Task AssertServerErrorAsync(string expectedMessage, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<RedisServerException>(action);
        Assert.Contains(expectedMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
