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
        await using var conn = Create(allowAdmin: true);
        var server = GetAnyPrimary(conn);

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
        await using var conn = Create(allowAdmin: true, shared: false);
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
        await using var conn = Create(allowAdmin: true, defaultDatabase: db, shared: false);
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
    public async Task ExclusionListCommandsStillSendNoSelect()
    {
        // CLIENT is on the exclusion list, so it travels with no database and leaves the connection's
        // selection alone - which is exactly what makes it usable as the oracle above.
        await using var conn = Create(allowAdmin: true, shared: false);
        var server = GetAnyPrimary(conn);

        var otherDb = TestConfig.GetDedicatedDB(conn);
        Skip.IfMissingDatabase(conn, otherDb);
        await conn.GetDatabase(otherDb).StringSetAsync(Me(), "x");

        try
        {
            Assert.Equal($"db={otherDb}", ReportedDatabase(server));

            // ...and asking twice does not move it
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
