using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class LexTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task QueryRangeAndLengthByLex()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("a", 0),
                    new SortedSetEntry("b", 0),
                    new SortedSetEntry("c", 0),
                    new SortedSetEntry("d", 0),
                    new SortedSetEntry("e", 0),
                    new SortedSetEntry("f", 0),
                    new SortedSetEntry("g", 0),
            ],
            CommandFlags.FireAndForget);

        var set = db.SortedSetRangeByValue(key, default(RedisValue), "c");
        var count = db.SortedSetLengthByValue(key, default(RedisValue), "c");
        Equate(set, count, "a", "b", "c");

        set = db.SortedSetRangeByValue(key, default(RedisValue), "c", Exclude.Stop);
        count = db.SortedSetLengthByValue(key, default(RedisValue), "c", Exclude.Stop);
        Equate(set, count, "a", "b");

        set = db.SortedSetRangeByValue(key, "aaa", "g", Exclude.Stop);
        count = db.SortedSetLengthByValue(key, "aaa", "g", Exclude.Stop);
        Equate(set, count, "b", "c", "d", "e", "f");

        set = db.SortedSetRangeByValue(key, "aaa", "g", Exclude.Stop, 1, 3);
        Equate(set, set.Length, "c", "d", "e");

        set = db.SortedSetRangeByValue(key, "aaa", "g", Exclude.Stop, Order.Descending, 1, 3);
        Equate(set, set.Length, "e", "d", "c");

        set = db.SortedSetRangeByValue(key, "g", "aaa", Exclude.Start, Order.Descending, 1, 3);
        Equate(set, set.Length, "e", "d", "c");

        set = db.SortedSetRangeByValue(key, "e", default(RedisValue));
        count = db.SortedSetLengthByValue(key, "e", default(RedisValue));
        Equate(set, count, "e", "f", "g");

        set = db.SortedSetRangeByValue(key, RedisValue.Null, RedisValue.Null, Exclude.None, Order.Descending, 0, 3);    // added to test Null-min- and max-param
        Equate(set, set.Length, "g", "f", "e");
    }

    [Fact]
    public async Task QueryRangeByLexWithNumericPrefix()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("0122", 0),
                    new SortedSetEntry("0123", 0),
                    new SortedSetEntry("0123abc", 0),
                    new SortedSetEntry("0123xyz", 0),
                    new SortedSetEntry("0124", 0),
                    new SortedSetEntry("123abc", 0),
            ],
            CommandFlags.FireAndForget);

        // prefix + '\xff' is the usual autocomplete upper bound; "0123" on its own looks like the number 123 (#1302)
        var set = db.SortedSetRangeByValue(key, "0123", "0123\xff");
        var count = db.SortedSetLengthByValue(key, "0123", "0123\xff");
        Equate(set, count, "0123", "0123abc", "0123xyz");

        set = await db.SortedSetRangeByValueAsync(key, "0123", "0123\xff");
        Equate(set, set.Length, "0123", "0123abc", "0123xyz");

        set = db.SortedSetRangeByValue(key, "0123", "0123\xff", Exclude.None, Order.Descending);
        Equate(set, set.Length, "0123xyz", "0123abc", "0123");

        set = await db.SortedSetRangeByValueAsync(key, "0123", "0123\xff", Exclude.None, Order.Descending);
        Equate(set, set.Length, "0123xyz", "0123abc", "0123");
    }

    [Fact]
    public async Task QueryRangeByLexWithNumericPrefixAndExclude()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("0122", 0),
                    new SortedSetEntry("0123", 0),
                    new SortedSetEntry("0123abc", 0),
                    new SortedSetEntry("0123xyz", 0),
                    new SortedSetEntry("0124", 0),
            ],
            CommandFlags.FireAndForget);

        var set = db.SortedSetRangeByValue(key, "0123", "0123xyz", Exclude.Start);
        var count = db.SortedSetLengthByValue(key, "0123", "0123xyz", Exclude.Start);
        Equate(set, count, "0123abc", "0123xyz");

        set = db.SortedSetRangeByValue(key, "0123", "0123xyz", Exclude.Stop);
        count = db.SortedSetLengthByValue(key, "0123", "0123xyz", Exclude.Stop);
        Equate(set, count, "0123", "0123abc");

        set = db.SortedSetRangeByValue(key, "0123", "0123xyz", Exclude.Start, Order.Descending);
        Equate(set, set.Length, "0123xyz", "0123abc");

        set = db.SortedSetRangeByValue(key, "0123", "0123xyz", Exclude.Stop, Order.Descending);
        Equate(set, set.Length, "0123abc", "0123");
    }

    [Fact]
    public async Task QueryRangeByLexWithDigitOnlyBounds()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("1", 0),
                    new SortedSetEntry("10", 0),
                    new SortedSetEntry("15", 0),
                    new SortedSetEntry("2", 0),
                    new SortedSetEntry("20", 0),
                    new SortedSetEntry("3", 0),
            ],
            CommandFlags.FireAndForget);

        // as text "10" sorts before "2", so the bounds are ordered the way redis orders them
        var set = db.SortedSetRangeByValue(key, "2", "10");
        var raw = (RedisValue[]?)db.Execute("ZRANGEBYLEX", [key, "[10", "[2"]);
        Assert.Equal(raw, set);
        Equate(set, set.Length, "10", "15", "2");

        set = db.SortedSetRangeByValue(key, "2", "10", Exclude.None, Order.Descending);
        raw = (RedisValue[]?)db.Execute("ZREVRANGEBYLEX", [key, "[2", "[10"]);
        Assert.Equal(raw, set);
        Equate(set, set.Length, "2", "15", "10");
    }

    [Fact]
    public async Task QueryRangeByLexWithIntegerBounds()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("1", 0),
                    new SortedSetEntry("10", 0),
                    new SortedSetEntry("15", 0),
                    new SortedSetEntry("2", 0),
                    new SortedSetEntry("20", 0),
                    new SortedSetEntry("3", 0),
            ],
            CommandFlags.FireAndForget);

        // integers are sent as their text, so they are ordered as text too: 10 before 2
        RedisValue min = 2;
        RedisValue max = 10;
        var set = db.SortedSetRangeByValue(key, min, max);
        var raw = (RedisValue[]?)db.Execute("ZRANGEBYLEX", [key, "[10", "[2"]);
        Assert.Equal(raw, set);
        Equate(set, set.Length, "10", "15", "2");

        set = db.SortedSetRangeByValue(key, min, max, Exclude.None, Order.Descending);
        raw = (RedisValue[]?)db.Execute("ZREVRANGEBYLEX", [key, "[2", "[10"]);
        Assert.Equal(raw, set);
        Equate(set, set.Length, "2", "15", "10");
    }

    [Fact]
    public async Task QueryRangeByLexWithNumericPrefixAsBytes()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("0122", 0),
                    new SortedSetEntry("0123", 0),
                    new SortedSetEntry("0123abc", 0),
                    new SortedSetEntry("0123xyz", 0),
                    new SortedSetEntry("0124", 0),
                    new SortedSetEntry("123abc", 0),
            ],
            CommandFlags.FireAndForget);

        // the same bytes the string bounds "0123" and "0123\xff" are sent as
        RedisValue min = Encoding.UTF8.GetBytes("0123");
        RedisValue max = Encoding.UTF8.GetBytes("0123\xff");
        var set = db.SortedSetRangeByValue(key, min, max);
        var count = db.SortedSetLengthByValue(key, min, max);
        Equate(set, count, "0123", "0123abc", "0123xyz");

        set = db.SortedSetRangeByValue(key, min, max, Exclude.None, Order.Descending);
        Equate(set, set.Length, "0123xyz", "0123abc", "0123");

        // a string bound against a byte[] bound
        set = db.SortedSetRangeByValue(key, "0123", max);
        Equate(set, set.Length, "0123", "0123abc", "0123xyz");

        set = db.SortedSetRangeByValue(key, "0123", max, Exclude.None, Order.Descending);
        Equate(set, set.Length, "0123xyz", "0123abc", "0123");
    }

    [Fact]
    public async Task LengthByLexMatchesRangeWithDigitOnlyBounds()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("1", 0),
                    new SortedSetEntry("10", 0),
                    new SortedSetEntry("15", 0),
                    new SortedSetEntry("2", 0),
                    new SortedSetEntry("20", 0),
                    new SortedSetEntry("3", 0),
            ],
            CommandFlags.FireAndForget);

        // the count is of the same range SortedSetRangeByValue returns: "10" up to "2", as text
        var set = db.SortedSetRangeByValue(key, "2", "10");
        var count = db.SortedSetLengthByValue(key, "2", "10");
        Equate(set, count, "10", "15", "2");

        count = await db.SortedSetLengthByValueAsync(key, "2", "10");
        Equate(set, count, "10", "15", "2");

        // swapping the bounds swaps which end is excluded, as it does for the range
        set = db.SortedSetRangeByValue(key, "2", "10", Exclude.Start);
        count = db.SortedSetLengthByValue(key, "2", "10", Exclude.Start);
        Equate(set, count, "10", "15");
    }

    [Fact]
    public async Task RemoveRangeByLexDoesNotReorderBounds()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("1", 0),
                    new SortedSetEntry("10", 0),
                    new SortedSetEntry("15", 0),
                    new SortedSetEntry("2", 0),
                    new SortedSetEntry("20", 0),
                    new SortedSetEntry("3", 0),
            ],
            CommandFlags.FireAndForget);

        // deliberate: unlike range and length, removal is not reordered, so bounds the wrong way round remove
        // nothing - reordering "2" and "10" would remove "10" and "15" as well, which a caller thinking of
        // numbers never asked for
        long removed = db.SortedSetRemoveRangeByValue(key, "2", "10");
        Assert.Equal(0, removed);

        removed = await db.SortedSetRemoveRangeByValueAsync(key, "2", "10");
        Assert.Equal(0, removed);

        var set = db.SortedSetRangeByRank(key);
        Equate(set, set.Length, "1", "10", "15", "2", "20", "3");
    }

    [Fact]
    public async Task RemoveRangeByLex()
    {
        await using var conn = Create();

        var db = conn.GetDatabase();
        RedisKey key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("aaaa", 0),
                    new SortedSetEntry("b", 0),
                    new SortedSetEntry("c", 0),
                    new SortedSetEntry("d", 0),
                    new SortedSetEntry("e", 0),
            ],
            CommandFlags.FireAndForget);
        db.SortedSetAdd(
            key,
            [
                    new SortedSetEntry("foo", 0),
                    new SortedSetEntry("zap", 0),
                    new SortedSetEntry("zip", 0),
                    new SortedSetEntry("ALPHA", 0),
                    new SortedSetEntry("alpha", 0),
            ],
            CommandFlags.FireAndForget);

        var set = db.SortedSetRangeByRank(key);
        Equate(set, set.Length, "ALPHA", "aaaa", "alpha", "b", "c", "d", "e", "foo", "zap", "zip");

        long removed = db.SortedSetRemoveRangeByValue(key, "alpha", "omega");
        Assert.Equal(6, removed);

        set = db.SortedSetRangeByRank(key);
        Equate(set, set.Length, "ALPHA", "aaaa", "zap", "zip");
    }

    private static void Equate(RedisValue[] actual, long count, params string[] expected)
    {
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }
}
