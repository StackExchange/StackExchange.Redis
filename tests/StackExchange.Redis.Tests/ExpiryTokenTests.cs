using System;
using Xunit;
using static StackExchange.Redis.RedisDatabase;
using static StackExchange.Redis.RedisDatabase.ExpiryToken;
namespace StackExchange.Redis.Tests;

public class ExpiryTokenTests // pure tests, no DB
{
    [Fact]
    public void Persist_Seconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = Persist(time, false);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("EX 5", ex.ToString());
    }

    [Fact]
    public void Persist_Milliseconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5001);
        var ex = Persist(time, false);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("PX 5001", ex.ToString());
    }

    [Fact]
    public void Persist_None_False()
    {
        TimeSpan? time = null;
        var ex = Persist(time, false);
        Assert.Equal(0, ex.Tokens);
        Assert.Equal("", ex.ToString());
    }

    [Fact]
    public void Persist_None_True()
    {
        TimeSpan? time = null;
        var ex = Persist(time, true);
        Assert.Equal(1, ex.Tokens);
        Assert.Equal("PERSIST", ex.ToString());
    }

    [Fact]
    public void Persist_Both()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = Assert.Throws<ArgumentException>(() => Persist(time, true));
        Assert.Equal("persist", ex.ParamName);
        Assert.StartsWith("Cannot specify both expiry and persist", ex.Message);
    }

    [Fact]
    public void KeepTtl_Seconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = KeepTtl(time, false);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("EX 5", ex.ToString());
    }

    [Fact]
    public void KeepTtl_Milliseconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5001);
        var ex = KeepTtl(time, false);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("PX 5001", ex.ToString());
    }

    [Fact]
    public void KeepTtl_None_False()
    {
        TimeSpan? time = null;
        var ex = KeepTtl(time, false);
        Assert.Equal(0, ex.Tokens);
        Assert.Equal("", ex.ToString());
    }

    [Fact]
    public void KeepTtl_None_True()
    {
        TimeSpan? time = null;
        var ex = KeepTtl(time, true);
        Assert.Equal(1, ex.Tokens);
        Assert.Equal("KEEPTTL", ex.ToString());
    }

    [Fact]
    public void KeepTtl_Both()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = Assert.Throws<ArgumentException>(() => KeepTtl(time, true));
        Assert.Equal("keepTtl", ex.ParamName);
        Assert.StartsWith("Cannot specify both expiry and keepTtl", ex.Message);
    }

    [Fact]
    public void DateTime_Seconds()
    {
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc);
        var ex = new ExpiryToken(when);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("EXAT 1753265054", ex.ToString());
    }

    [Fact]
    public void DateTime_Milliseconds()
    {
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc);
        when = when.AddMilliseconds(14);
        var ex = new ExpiryToken(when);
        Assert.Equal(2, ex.Tokens);
        Assert.Equal("PXAT 1753265054014", ex.ToString());
    }
}
