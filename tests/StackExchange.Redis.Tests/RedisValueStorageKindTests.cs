using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Pins the <see cref="RedisValue.StorageType"/> that the inbound read path
/// (<c>RespReader.ReadRedisValue</c>) produces for values fetched from a server: numeric-looking bulk
/// strings are stored compactly as Int64/Double when they round-trip exactly, otherwise as a byte[].
/// </summary>
/// <remarks>
/// The expected storage kind is passed by name because <see cref="RedisValue.StorageType"/> is internal and
/// so cannot appear as a parameter on a public theory method.
/// </remarks>
public class RedisValueStorageKindTests(ITestOutputHelper output, InProcServerFixture fixture) : TestBase(output, fixture)
{
    [Theory]
    // canonical integers => Int64
    [InlineData("1234", "Int64")]
    [InlineData("0", "Int64")]
    [InlineData("-5", "Int64")]
    [InlineData("9223372036854775807", "Int64")] // long.MaxValue
    // canonical finite doubles => Double
    [InlineData("0.5", "Double")]
    [InlineData("-2.25", "Double")]
    // non-canonical / non-numeric / special => kept as bytes
    [InlineData("01234", "ByteArray")] // leading zero
    [InlineData("+1234", "ByteArray")] // leading '+'
    [InlineData("-0", "ByteArray")] // negative zero text
    [InlineData("1.50", "ByteArray")] // trailing zero (non-canonical double)
    [InlineData("1e3", "ByteArray")] // exponent form
    [InlineData("inf", "ByteArray")] // special token excluded
    [InlineData("nan", "ByteArray")]
    [InlineData("99999999999999999999999", "ByteArray")] // oversize
    [InlineData("abc", "ByteArray")] // not numeric
    [InlineData("12abc", "ByteArray")] // trailing junk
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
}
