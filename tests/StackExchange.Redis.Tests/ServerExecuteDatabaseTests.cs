using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="IServer.Execute(string, object[])"/> names a server but no database, and until this was fixed it
/// refused every recognised command that <see cref="Message.RequiresDatabase"/> claims needs one — which is
/// most of them, since that returns true by default. See #3236.
/// </summary>
/// <remarks>
/// The three routes through <c>ExecuteMessage</c> behave differently and all matter: a recognised command that
/// needs a database, a recognised command on the exclusion list, and one that is not recognised at all and so
/// skips the assertion. Each is covered below.
/// </remarks>
public class ServerExecuteDatabaseTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    /// <summary>CLIENT INFO, used below to ask the server which database a connection is on, arrived in 6.2.</summary>
    private static readonly Version ClientInfoFrom = RedisFeatures.v6_2_0;

    [Theory]
    // recognised, and RequiresDatabase says yes: these are the ones that regressed
    [InlineData("DBSIZE")]
    [InlineData("RANDOMKEY")]
    [InlineData("KEYS")]
    [InlineData("SCAN")]
    [InlineData("DEBUG")]
    [InlineData("OBJECT")]
    // recognised, on the exclusion list: never needed a database, must stay working
    [InlineData("PING")]
    [InlineData("CONFIG")]
    // not recognised, so the assertion is skipped entirely: must stay working
    [InlineData("ACL")]
    [InlineData("FUNCTION")]
    public async Task AdHocCommandsRunWithoutAnExplicitDatabase(string command)
    {
        await using var conn = Create(allowAdmin: true, require: RequiredVersion(command));
        if (command == "DEBUG")
        {
            // DEBUG is disabled by default from Redis 7; the repo's own config turns it on, a stock server does not
            await AssertDebugCommandEnabledAsync(conn);
        }

        var server = GetAnyPrimary(conn);

        // the assertion is simply that this does not throw - before the fix these threw "A target database
        // is required". What the server replies with is not the point, and tightening this into a check on
        // the reply would only couple the test to server versions
        var result = server.Execute(command, ArgsFor(command));
        Assert.NotNull(result);

        var asyncResult = await server.ExecuteAsync(command, ArgsFor(command));
        Assert.NotNull(asyncResult);
    }

    [Fact]
    public async Task AdHocCommandUsesTheDefaultDatabaseRatherThanWhateverWasSelected()
    {
        // The pre-v3 behaviour sent these with no SELECT at all, so they ran against whichever database that
        // physical connection last happened to select, making the result depend on unrelated earlier calls.
        // Pin the predictable behaviour instead: the configured default, as IServer.DatabaseSize already uses.
        //
        // Asserted by asking where the connection ended up, not by comparing key counts: other tests share
        // this server, so any count moves underneath us and two readings of one prove nothing. A private
        // connection, since this is about that connection's own selected database.
        await using var conn = Create(allowAdmin: true, shared: false, require: ClientInfoFrom);
        var server = GetAnyPrimary(conn);

        var otherDb = TestConfig.GetDedicatedDB(conn);
        Skip.IfMissingDatabase(conn, otherDb);
        Assert.NotEqual(0, otherDb); // the default here is 0, so they must differ for this to mean anything

        // leave the connection sitting somewhere other than the default
        await conn.GetDatabase(otherDb).StringSetAsync(Me(), "x");
        Assert.Equal($"db={otherDb}", ReportedDatabase(server));

        try
        {
            // a command needing a database must select the default rather than inherit otherDb
            _ = server.Execute("DBSIZE");
            Assert.Equal("db=0", ReportedDatabase(server));
        }
        finally
        {
            await conn.GetDatabase(otherDb).KeyDeleteAsync(Me());
        }
    }

    [Fact]
    public async Task AdHocCommandFollowsAConfiguredDefaultDatabase()
    {
        var db = TestConfig.GetDedicatedDB();
        await using var conn = Create(allowAdmin: true, defaultDatabase: db, shared: false, require: ClientInfoFrom);
        Skip.IfMissingDatabase(conn, db);
        Assert.NotEqual(0, db);
        var server = GetAnyPrimary(conn);

        // park the connection on database 0, so following the configured default is a visible move
        await conn.GetDatabase(0).StringSetAsync(Me(), "x");
        Assert.Equal("db=0", ReportedDatabase(server));

        try
        {
            _ = server.Execute("DBSIZE");
            Assert.Equal($"db={db}", ReportedDatabase(server));
        }
        finally
        {
            await conn.GetDatabase(0).KeyDeleteAsync(Me());
        }
    }

    [Fact]
    public async Task CommandsNeedingNoDatabaseLeaveTheSelectionAlone()
    {
        // the other half of the fix: only a command that actually requires a database may cause a SELECT.
        // PING is recognised but on the exclusion list; ACL is not recognised at all, so it skips the check
        // entirely - and had the fix simply always passed the default, RemoveDbIfNotRequired would not have
        // stripped it for ACL, so an unrecognised command would have started moving the selection. CLIENT is
        // the third case, and that is what makes it usable as the oracle in the tests above.
        await using var conn = Create(allowAdmin: true, shared: false, require: ClientInfoFrom);
        var server = GetAnyPrimary(conn);

        var otherDb = TestConfig.GetDedicatedDB(conn);
        Skip.IfMissingDatabase(conn, otherDb);
        Assert.NotEqual(0, otherDb); // otherwise "still on otherDb" holds whether or not a SELECT happened

        await conn.GetDatabase(otherDb).StringSetAsync(Me(), "x");

        try
        {
            Assert.Equal($"db={otherDb}", ReportedDatabase(server));

            // exclusion-list command: recognised, and RequiresDatabase says no
            _ = server.Execute("PING");
            Assert.Equal($"db={otherDb}", ReportedDatabase(server));

            // unrecognised command: the assertion never applied to it, and must not start applying.
            // ACL needs 6.0, which the 6.2 this test already requires for CLIENT INFO covers
            _ = server.Execute("ACL", "WHOAMI");
            Assert.Equal($"db={otherDb}", ReportedDatabase(server));

            // ...and the oracle itself does not move it
            Assert.Equal($"db={otherDb}", ReportedDatabase(server));
        }
        finally
        {
            await conn.GetDatabase(otherDb).KeyDeleteAsync(Me());
        }
    }

    /// <summary>Which database the connection is currently on, per the server itself.</summary>
    private static string ReportedDatabase(IServer server)
    {
        var info = (string?)server.Execute("CLIENT", "INFO") ?? "";
        return info.Split(' ').FirstOrDefault(x => x.StartsWith("db=", StringComparison.Ordinal)) ?? "(not reported)";
    }

    /// <summary>The server version each ad-hoc command below needs, where it is not ancient.</summary>
    private static Version? RequiredVersion(string command) => command switch
    {
        "ACL" => RedisFeatures.v6_0_0,
        "FUNCTION" => RedisFeatures.v7_0_0_rc1,
        _ => null,
    };

    private static object[] ArgsFor(string command) => command switch
    {
        "KEYS" => ["zz-server-execute-nonexistent-*"],
        "SCAN" => ["0", "COUNT", "1"],
        "DEBUG" => ["sleep", "0"],
        "OBJECT" => ["help"],
        "CONFIG" => ["get", "timeout"],
        "ACL" => ["whoami"],
        "FUNCTION" => ["list"],
        _ => [],
    };
}
