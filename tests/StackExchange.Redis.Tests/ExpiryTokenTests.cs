using System;
using Xunit;
using static StackExchange.Redis.Expiration;
namespace StackExchange.Redis.Tests;

public class ExpirationTests // pure tests, no DB
{
    [Fact]
    public void Persist_Seconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = CreateOrPersist(time, false);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("EX 5", ex.ToString());
    }

    [Fact]
    public void Persist_Milliseconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5001);
        var ex = CreateOrPersist(time, false);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("PX 5001", ex.ToString());
    }

    [Fact]
    public void Persist_None_False()
    {
        TimeSpan? time = null;
        var ex = CreateOrPersist(time, false);
        Assert.Equal(0, ex.TokenCount);
        Assert.Equal("", ex.ToString());
    }

    [Fact]
    public void Persist_None_True()
    {
        TimeSpan? time = null;
        var ex = CreateOrPersist(time, true);
        Assert.Equal(1, ex.TokenCount);
        Assert.Equal("PERSIST", ex.ToString());
    }

    [Fact]
    public void Persist_Both()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = Assert.Throws<ArgumentException>(() => CreateOrPersist(time, true));
        Assert.Equal("persist", ex.ParamName);
        Assert.StartsWith("Cannot specify both expiry and persist", ex.Message);
    }

    [Fact]
    public void KeepTtl_Seconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = CreateOrKeepTtl(time, false);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("EX 5", ex.ToString());
    }

    [Fact]
    public void KeepTtl_Milliseconds()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5001);
        var ex = CreateOrKeepTtl(time, false);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("PX 5001", ex.ToString());
    }

    [Fact]
    public void KeepTtl_None_False()
    {
        TimeSpan? time = null;
        var ex = CreateOrKeepTtl(time, false);
        Assert.Equal(0, ex.TokenCount);
        Assert.Equal("", ex.ToString());
    }

    [Fact]
    public void KeepTtl_None_True()
    {
        TimeSpan? time = null;
        var ex = CreateOrKeepTtl(time, true);
        Assert.Equal(1, ex.TokenCount);
        Assert.Equal("KEEPTTL", ex.ToString());
    }

    [Fact]
    public void KeepTtl_Both()
    {
        TimeSpan? time = TimeSpan.FromMilliseconds(5000);
        var ex = Assert.Throws<ArgumentException>(() => CreateOrKeepTtl(time, true));
        Assert.Equal("keepTtl", ex.ParamName);
        Assert.StartsWith("Cannot specify both expiry and keepTtl", ex.Message);
    }

    [Fact]
    public void DateTime_Seconds()
    {
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc);
        var ex = new Expiration(when);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("EXAT 1753265054", ex.ToString());
    }

    [Fact]
    public void DateTime_Milliseconds()
    {
        var when = new DateTime(2025, 7, 23, 10, 4, 14, DateTimeKind.Utc);
        when = when.AddMilliseconds(14);
        var ex = new Expiration(when);
        Assert.Equal(2, ex.TokenCount);
        Assert.Equal("PXAT 1753265054014", ex.ToString());
    }
}
