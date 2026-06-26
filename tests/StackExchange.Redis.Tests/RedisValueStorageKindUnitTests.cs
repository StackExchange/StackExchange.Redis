using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// On-box unit tests (in-proc server, no external redis) that pin the <see cref="RedisValue.StorageType"/>
/// produced both for values on the inbound read path (<c>RespReader.ReadRedisValue</c>) and for the shared
/// <see cref="RedisLiterals"/> constants.
/// </summary>
/// <remarks>
/// The expected storage kind is compared by name because <see cref="RedisValue.StorageType"/> is internal and
/// so cannot appear as a parameter on a public theory method.
/// </remarks>
public class RedisValueStorageKindUnitTests(ITestOutputHelper output, InProcServerFixture fixture) : TestBase(output, fixture)
{
    /// <summary>
    /// Payloads of &lt;= 8 bytes are kept inline as a short-blob with <em>no</em> eager numeric parse (any
    /// numeric interpretation is deferred to the caller); only <em>longer</em> canonical numeric text is
    /// stored compactly as Int64/Double, and anything else is materialized as a byte[].
    /// </summary>
    [Theory]
    // <= 8 bytes => inline short-blob, whether or not the text looks numeric (numeric parse is deferred)
    [InlineData("1234", "ShortBlob")]
    [InlineData("0", "ShortBlob")]
    [InlineData("-5", "ShortBlob")]
    [InlineData("12345678", "ShortBlob")] // 8 bytes, the inline max
    [InlineData("0.5", "ShortBlob")]
    [InlineData("-2.25", "ShortBlob")]
    [InlineData("01234", "ShortBlob")] // leading zero
    [InlineData("+1234", "ShortBlob")] // leading '+'
    [InlineData("-0", "ShortBlob")] // negative zero text
    [InlineData("1.50", "ShortBlob")] // trailing zero (non-canonical double)
    [InlineData("1e3", "ShortBlob")] // exponent form
    [InlineData("inf", "ShortBlob")] // special token excluded from numeric, but still short
    [InlineData("nan", "ShortBlob")]
    [InlineData("abc", "ShortBlob")] // not numeric
    [InlineData("12abc", "ShortBlob")] // trailing junk
    // > 8 bytes AND a canonical number => compact Int64/Double (also avoids the byte[] alloc)
    [InlineData("123456789", "Int64")] // 9 bytes
    [InlineData("9223372036854775807", "Int64")] // long.MaxValue (19 bytes)
    // canonical, non-negative, and > long.MaxValue => UInt64 (covers the full ulong range on read)
    [InlineData("9223372036854775808", "UInt64")] // long.MaxValue + 1 (19 bytes)
    [InlineData("18446744073709551615", "UInt64")] // ulong.MaxValue (20 bytes)
    [InlineData("1048576.5", "Double")] // 9 bytes, exactly representable => canonical under G17
    [InlineData("-1048576.25", "Double")] // 11 bytes
    // > 8 bytes and not a canonical number => materialized as a byte[]
    [InlineData("99999999999999999999999", "ByteArray")] // oversize numeric
    [InlineData("the quick brown fox", "ByteArray")] // long text
    // > 8 bytes, parses as a double but does NOT round-trip under G17 (IEEE-754 noise), so it must stay a
    // byte[] to preserve byte-for-byte string behaviour rather than being mangled into a reformatted double
    [InlineData("119999.99", "ByteArray")] // a price; G17 => "119999.99000000001"
    [InlineData("2.675000000", "ByteArray")] // classic: the nearest double is < 2.675 => G17 "2.6749999999999998"
    public async Task FetchedValue_StorageKindAndRoundTrip(string stored, string expectedKind)
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        db.KeyDelete(key, CommandFlags.FireAndForget);

        db.StringSet(key, stored);
        var value = db.StringGet(key);

        // the optimization must be invisible: the text projection always matches what was stored
        Assert.Equal(stored, (string?)value);
        Log($"'{stored}' => {value.Type}");
        Assert.Equal(expectedKind, value.Type.ToString());
    }

    /// <summary>
    /// Every <see cref="RedisLiterals"/> constant is built via <c>RedisValue.FromRaw(...u8)</c>, so each is
    /// either an inline short-blob (&lt;= 8 bytes) or a byte[] (&gt; 8 bytes); none are numeric. We assert the
    /// byte[] count (crossing 8 bytes is rare and noteworthy) and that only those two storage flavors appear
    /// (so a numeric/other kind creeping in is caught), while the short-blob count is only logged - so adding
    /// more short literals (the common case) never forces a test edit.
    /// </summary>
    [Fact]
    public void LiteralsResolveToExpectedStorageKinds()
    {
        var literals = typeof(RedisLiterals)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsInitOnly && f.FieldType == typeof(RedisValue))
            .Select(f => ((RedisValue)f.GetValue(null)!).Type)
            .ToArray();

        Assert.NotEmpty(literals);

        var byKind = new Dictionary<RedisValue.StorageType, int>();
        foreach (var kind in literals)
        {
            byKind.TryGetValue(kind, out var n);
            byKind[kind] = n + 1;
        }

        int Count(RedisValue.StorageType kind) => byKind.TryGetValue(kind, out var n) ? n : 0;

        foreach (var kind in byKind.Keys.OrderBy(k => k.ToString()))
        {
            Output.WriteLine($"{kind}: {byKind[kind]}");
        }

        Output.WriteLine($"ShortBlob (logged, not asserted): {Count(RedisValue.StorageType.ShortBlob)}");

        // only two storage flavors should ever appear; a third means something new crept in (e.g. a numeric
        // literal => Int64), which we want to notice
        Assert.Equal(2, byKind.Count);
        Assert.Equal(22, Count(RedisValue.StorageType.ByteArray)); // literals > 8 bytes
    }
}
