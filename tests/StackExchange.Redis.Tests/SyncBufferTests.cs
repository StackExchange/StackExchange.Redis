using Xunit;

namespace StackExchange.Redis.Tests;

public class SyncBufferTests
{
    [Fact]
    public void Foo()
    {
        var pipe = new SyncBufferWriter();
        Assert.True(pipe.Reader.TryRead(out var result));
    }
}
