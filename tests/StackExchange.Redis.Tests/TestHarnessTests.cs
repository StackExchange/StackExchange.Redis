using System;
using Xunit;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests;

// who watches the watchers?
public class TestHarnessTests
{
    // this bit isn't required, but: by subclassing TestHarness we can expose the idiomatic test-framework faults.
    private sealed class XUnitTestHarness(CommandMap? commandMap = null, RedisChannel channelPrefix = default, RedisKey keyPrefix = default)
        : TestHarness(commandMap,  channelPrefix, keyPrefix)
    {
        protected override void OnValidateFail(string expected, string actual)
            => Assert.Equal(expected, actual);

        protected override void OnValidateFail(ReadOnlyMemory<byte> expected, ReadOnlyMemory<byte> actual)
            => Assert.Equal(expected, actual);

        protected override void OnValidateFail(in RedisKey expected, in RedisKey actual)
            => Assert.Equal(expected, actual);
    }

    [Fact]
    public void BasicWrite_Bytes()
    {
        var resp = new XUnitTestHarness();
        resp.ValidateRouting(RedisKey.Null, "hello world");
        resp.ValidateResp(
            "*2\r\n$4\r\nECHO\r\n$11\r\nhello world\r\n"u8,
            "echo",
            "hello world");
    }
    [Fact]
    public void BasicWrite_String()
    {
        var resp = new XUnitTestHarness();
        resp.ValidateRouting(RedisKey.Null, "hello world");
        resp.ValidateResp(
            "*2\r\n$4\r\nECHO\r\n$11\r\nhello world\r\n",
            "echo",
            "hello world");
    }

    [Fact]
    public void WithKeyPrefix()
    {
        var map = CommandMap.Create(new() { ["sEt"] = "put" });
        RedisKey key = "mykey";
        var resp = new XUnitTestHarness(keyPrefix: "123/", commandMap: map);
        object[] args = { key, 42 };
        resp.ValidateRouting(key, args);
        resp.ValidateResp("*3\r\n$3\r\nPUT\r\n$9\r\n123/mykey\r\n$2\r\n42\r\n", "set", args);
    }

    [Fact]
    public void WithKeyPrefix_DetectIncorrectUsage()
    {
        string key = "mykey"; // incorrectly not a key
        var resp = new XUnitTestHarness(keyPrefix: "123/");
        object[] args = { key, 42 };
        var ex = Assert.Throws<EqualException>(() => resp.ValidateRouting(key, args));
        Assert.Contains("Expected: 123/mykey", ex.Message);
        Assert.Contains("Actual:   (null)", ex.Message);

        ex = Assert.Throws<EqualException>(() => resp.ValidateResp("*3\r\n$3\r\nSET\r\n$9\r\n123/mykey\r\n$2\r\n42\r\n", "set", args));
        Assert.Contains(@"Expected: ""*3\r\n$3\r\nSET\r\n$9\r\n123/mykey\r\n$2\r\n42\r\n""", ex.Message);
        Assert.Contains(@"Actual:   ""*3\r\n$3\r\nSET\r\n$5\r\nmykey\r\n$2\r\n42\r\n""", ex.Message);
    }

    [Fact]
    public void ParseExample()
    {
        var resp = new XUnitTestHarness();
        var result = resp.Read("*3\r\n:42\r\n#t\r\n$3\r\nabc\r\n"u8);
        Assert.Equal(3, result.Length);
        Assert.Equal(42, (int)result[0]);
        Assert.True((bool)result[1]);
        Assert.Equal("abc", (string?)result[2]);
    }
}
