using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A preamble pair is written to the database it was composed for, whatever database the shared connection
/// was last asked for.
/// </summary>
/// <remarks>
/// The pair path wrote its two frames with no <c>SELECT</c> and no database on either operation, so it ran on
/// whichever database the connection was on. A connection is shared by every database that reaches its
/// endpoint, so under the full suite a default-database <c>HIMPORT PREPARE</c>/<c>SET</c> intermittently ran
/// against another test's database: the SET succeeded, and the key was then missing where it was read
/// (<c>RespHashImportProbeTests.AConnectionLocalPreambleIsTheScriptSeamWithADifferentScope</c>, 3 in 12 runs).
/// </remarks>
public class RespPreambleDatabaseTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task APairLandsInItsOwnDatabaseNotTheConnectionsLast()
    {
        await using var conn = Create();
        var other = TestConfig.GetDedicatedDB(conn);
        Skip.IfMissingDatabase(conn, other);

        var db = conn.GetDatabase();
        Assert.NotEqual(other, db.Database);
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);
        await conn.GetDatabase(other).KeyDeleteAsync(key);

        // leave the one interactive connection SELECTed on the other database
        await conn.GetDatabase(other).StringSetAsync(Me() + ":elsewhere", "x");

        var context = TestMultiplexer.Unwrap(conn).NewCore.GetDatabase(db.Database);
        var preamble = context.Raw.Render($"{RedisCommand.ECHO}{(RedisValue)"preamble"}");
        var request = context.Raw.Render($"{RedisCommand.SET}{key}{(RedisValue)"here"}");
        ValueTask<bool> sent;
        try
        {
            sent = context.Raw.SendWithPreambleAsync(ref preamble, ref request, CommandFlags.None, RespHandlers.Boolean, gate: null);
        }
        finally
        {
            preamble.Dispose();
            request.Dispose();
        }

        Assert.True(await sent);
        Assert.Equal("here", (string?)await db.StringGetAsync(key));
        Assert.False(await conn.GetDatabase(other).KeyExistsAsync(key), "the pair ran against the connection's last database");
    }
}
