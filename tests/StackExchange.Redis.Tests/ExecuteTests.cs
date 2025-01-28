using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class ExecuteTests : TestBase
{
    public ExecuteTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    [Fact]
    public async Task DBExecute()
    {
        using var conn = Create();

        var db = conn.GetDatabase(4);
        RedisKey key = Me();
        db.StringSet(key, "some value");

        var actual = (string?)db.Execute("GET", key);
        Assert.Equal("some value", actual);

        actual = (string?)await db.ExecuteAsync("GET", key).ForAwait();
        Assert.Equal("some value", actual);
    }

    [Fact]
    public async Task ServerExecute()
    {
        using var conn = Create();

        var server = conn.GetServer(conn.GetEndPoints().First());
        var actual = (string?)server.Execute("echo", "some value");
        Assert.Equal("some value", actual);

        actual = (string?)await server.ExecuteAsync("echo", "some value").ForAwait();
        Assert.Equal("some value", actual);
    }

    [Fact]
    public async Task DBExecuteLease()
    {
        using var conn = Create();

        var db = conn.GetDatabase();
        var key = Me();

        // sync tests
        {
            db.StringSet(key, "hello world");

            using var lease1 = db.ExecuteLease("GET", (RedisKey)key);
            Assert.NotNull(lease1);

            var value1 = lease1.DecodeString();
            Assert.Equal("hello world", value1);

            db.StringSet(key, "fizz buzz");

            using var lease2 = db.ExecuteLease("GET", new List<object> { (RedisKey)key });
            Assert.NotNull(lease2);

            var value2 = lease2.DecodeString();
            Assert.Equal("fizz buzz", value2);
        }

        // async tests
        {
            await db.StringSetAsync(key, "foo bar");

            using var lease3 = await db.ExecuteLeaseAsync("GET", (RedisKey)key);
            Assert.NotNull(lease3);

            var value3 = lease3.DecodeString();
            Assert.Equal("foo bar", value3);

            await db.StringSetAsync(key, "abc def");

            using var lease4 = await db.ExecuteLeaseAsync("GET", new List<object> { (RedisKey)key });
            Assert.NotNull(lease4);

            var value4 = lease4.DecodeString();
            Assert.Equal("abc def", value4);
        }
    }

    [Fact]
    public async Task ServerExecuteLease()
    {
        using var conn = Create();

        var server = conn.GetServer(conn.GetEndPoints().First());
        var key = Me();

        // sync tests
        {
            server.Execute("SET", key, "hello world");

            using var lease1 = server.ExecuteLease("GET", (RedisKey)key);
            Assert.NotNull(lease1);

            var value1 = lease1.DecodeString();
            Assert.Equal("hello world", value1);

            server.Execute("SET", key, "fizz buzz");

            using var lease2 = server.ExecuteLease("GET", new List<object> { (RedisKey)key });
            Assert.NotNull(lease2);

            var value2 = lease2.DecodeString();
            Assert.Equal("fizz buzz", value2);
        }

        // async tests
        {
            await server.ExecuteAsync("SET", key, "foo bar");

            using var lease3 = await server.ExecuteLeaseAsync("GET", (RedisKey)key);
            Assert.NotNull(lease3);

            var value3 = lease3.DecodeString();
            Assert.Equal("foo bar", value3);

            await server.ExecuteAsync("SET", key, "abc def");

            using var lease4 = await server.ExecuteLeaseAsync("GET", new List<object> { (RedisKey)key });
            Assert.NotNull(lease4);

            var value4 = lease4.DecodeString();
            Assert.Equal("abc def", value4);
        }
    }
}
