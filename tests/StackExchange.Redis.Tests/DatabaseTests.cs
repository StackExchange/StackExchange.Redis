using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class DatabaseTests : TestBase
{
    public DatabaseTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task CommandCount()
    {
        using var conn = Create();
        var server = GetAnyPrimary(conn);
        var count = server.CommandCount();
        Assert.True(count > 100);

        count = await server.CommandCountAsync();
        Assert.True(count > 100);
    }

    [Fact]
    public async Task CommandGetKeys()
    {
        using var conn = Create();
        var server = GetAnyPrimary(conn);

        RedisValue[] command = { "MSET", "a", "b", "c", "d", "e", "f" };

        RedisKey[] keys = server.CommandGetKeys(command);
        RedisKey[] expected = { "a", "c", "e" };
        Assert.Equal(keys, expected);

        keys = await server.CommandGetKeysAsync(command);
        Assert.Equal(keys, expected);
    }

    [Fact]
    public async Task CommandList()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);
        var server = GetAnyPrimary(conn);

        var commands = server.CommandList();
        Assert.True(commands.Length > 100);
        commands = await server.CommandListAsync();
        Assert.True(commands.Length > 100);

        commands = server.CommandList(moduleName: "JSON");
        Assert.Empty(commands);
        commands = await server.CommandListAsync(moduleName: "JSON");
        Assert.Empty(commands);

        commands = server.CommandList(category: "admin");
        Assert.True(commands.Length > 10);
        commands = await server.CommandListAsync(category: "admin");
        Assert.True(commands.Length > 10);

        commands = server.CommandList(pattern: "a*");
        Assert.True(commands.Length > 10);
        commands = await server.CommandListAsync(pattern: "a*");
        Assert.True(commands.Length > 10);

        Assert.Throws<ArgumentException>(() => server.CommandList(moduleName: "JSON", pattern: "a*"));
        await Assert.ThrowsAsync<ArgumentException>(() => server.CommandListAsync(moduleName: "JSON", pattern: "a*"));
    }

    [Fact]
    public async Task CountKeys()
    {
        var db1Id = TestConfig.GetDedicatedDB();
        var db2Id = TestConfig.GetDedicatedDB();
        using (var conn = Create(allowAdmin: true))
        {
            Skip.IfMissingDatabase(conn, db1Id);
            Skip.IfMissingDatabase(conn, db2Id);
            var server = GetAnyPrimary(conn);
            server.FlushDatabase(db1Id, CommandFlags.FireAndForget);
            server.FlushDatabase(db2Id, CommandFlags.FireAndForget);
        }
        using (var conn = Create(defaultDatabase: db2Id))
        {
            Skip.IfMissingDatabase(conn, db1Id);
            Skip.IfMissingDatabase(conn, db2Id);
            RedisKey key = Me();
            var dba = conn.GetDatabase(db1Id);
            var dbb = conn.GetDatabase(db2Id);
            dba.StringSet("abc", "def", flags: CommandFlags.FireAndForget);
            dba.StringIncrement(key, flags: CommandFlags.FireAndForget);
            dbb.StringIncrement(key, flags: CommandFlags.FireAndForget);

            var server = GetAnyPrimary(conn);
            var c0 = server.DatabaseSizeAsync(db1Id);
            var c1 = server.DatabaseSizeAsync(db2Id);
            var c2 = server.DatabaseSizeAsync(); // using default DB, which is db2Id

            Assert.Equal(2, await c0);
            Assert.Equal(1, await c1);
            Assert.Equal(1, await c2);
        }
    }

    [Fact]
    public void DatabaseCount()
    {
        using var conn = Create(allowAdmin: true);

        var server = GetAnyPrimary(conn);
        var count = server.DatabaseCount;
        Log("Count: " + count);
        var configVal = server.ConfigGet("databases")[0].Value;
        Log("Config databases: " + configVal);
        Assert.Equal(int.Parse(configVal), count);
    }

    [Fact]
    public async Task MultiDatabases()
    {
        using var conn = Create();

        RedisKey key = Me();
        var db0 = conn.GetDatabase(TestConfig.GetDedicatedDB(conn));
        var db1 = conn.GetDatabase(TestConfig.GetDedicatedDB(conn));
        var db2 = conn.GetDatabase(TestConfig.GetDedicatedDB(conn));

        db0.KeyDelete(key, CommandFlags.FireAndForget);
        db1.KeyDelete(key, CommandFlags.FireAndForget);
        db2.KeyDelete(key, CommandFlags.FireAndForget);

        db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
        db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);
        db2.StringSet(key, "c", flags: CommandFlags.FireAndForget);

        var a = db0.StringGetAsync(key);
        var b = db1.StringGetAsync(key);
        var c = db2.StringGetAsync(key);

        Assert.Equal("a", await a); // db:0
        Assert.Equal("b", await b); // db:1
        Assert.Equal("c", await c); // db:2
    }

    [Fact]
    public async Task SwapDatabases()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v4_0_0);

        RedisKey key = Me();
        var db0id = TestConfig.GetDedicatedDB(conn);
        var db0 = conn.GetDatabase(db0id);
        var db1id = TestConfig.GetDedicatedDB(conn);
        var db1 = conn.GetDatabase(db1id);

        db0.KeyDelete(key, CommandFlags.FireAndForget);
        db1.KeyDelete(key, CommandFlags.FireAndForget);

        db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
        db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);

        var a = db0.StringGetAsync(key);
        var b = db1.StringGetAsync(key);

        Assert.Equal("a", await a); // db:0
        Assert.Equal("b", await b); // db:1

        var server = GetServer(conn);
        server.SwapDatabases(db0id, db1id);

        var aNew = db1.StringGetAsync(key);
        var bNew = db0.StringGetAsync(key);

        Assert.Equal("a", await aNew); // db:1
        Assert.Equal("b", await bNew); // db:0
    }

    [Fact]
    public async Task SwapDatabasesAsync()
    {
        using var conn = Create(allowAdmin: true, require: RedisFeatures.v4_0_0);

        RedisKey key = Me();
        var db0id = TestConfig.GetDedicatedDB(conn);
        var db0 = conn.GetDatabase(db0id);
        var db1id = TestConfig.GetDedicatedDB(conn);
        var db1 = conn.GetDatabase(db1id);

        db0.KeyDelete(key, CommandFlags.FireAndForget);
        db1.KeyDelete(key, CommandFlags.FireAndForget);

        db0.StringSet(key, "a", flags: CommandFlags.FireAndForget);
        db1.StringSet(key, "b", flags: CommandFlags.FireAndForget);

        var a = db0.StringGetAsync(key);
        var b = db1.StringGetAsync(key);

        Assert.Equal("a", await a); // db:0
        Assert.Equal("b", await b); // db:1

        var server = GetServer(conn);
        _ = server.SwapDatabasesAsync(db0id, db1id).ForAwait();

        var aNew = db1.StringGetAsync(key);
        var bNew = db0.StringGetAsync(key);

        Assert.Equal("a", await aNew); // db:1
        Assert.Equal("b", await bNew); // db:0
    }
}
