using System.Threading.Tasks;
using RESPite;
using Xunit;

namespace RESP.Core.Tests;

public partial class BatchTests
{
    [Fact]
    public async Task TestInfrastructure()
    {
        await TestServer.Execute(ctx => FooAsync(ctx), "*1\r\n$3\r\nfoo\r\n"u8, ":42\r\n"u8, 42);
        await TestServer.Execute(ctx => FooAsync(ctx), "*1\r\n$3\r\nfoo\r\n", ":42\r\n", 42);
        await TestServer.Execute(ctx => BarAsync(ctx), "*1\r\n$3\r\nbar\r\n"u8, "+ok\r\n"u8);
        await TestServer.Execute(ctx => BarAsync(ctx), "*1\r\n$3\r\nbar\r\n", "+OK\r\n");
    }

    [Fact]
    public async Task SimpleBatching()
    {
        using var server = new TestServer();
        // prepare a batch
        var batch = server.Context.CreateBatch();
        var b = TestAsync(batch.Context, 1);
        var c = TestAsync(batch.Context, 2);
        var d = TestAsync(batch.Context, 3);

        // we want to sandwich the batch between two regular operations
        var a = TestAsync(server.Context, 0); // uses SERVER
        Assert.True(a.Unwrap().IsSent);
        Assert.False(d.Unwrap().IsSent);
        await batch.FlushAsync(); // uses BATCH
        Assert.True(d.Unwrap().IsSent);
        var e = TestAsync(server.Context, 4); // uses SERVER again

        // check what was sent
        server.AssertSent("*2\r\n$4\r\ntest\r\n$1\r\n0\r\n"u8);
        server.AssertSent("*2\r\n$4\r\ntest\r\n$1\r\n1\r\n"u8);
        server.AssertSent("*2\r\n$4\r\ntest\r\n$1\r\n2\r\n"u8);
        server.AssertSent("*2\r\n$4\r\ntest\r\n$1\r\n3\r\n"u8);
        server.AssertSent("*2\r\n$4\r\ntest\r\n$1\r\n4\r\n"u8);
        server.AssertAllSent(); // that's everything

        // check what is received (all in one chunk)
        server.Respond(":5\r\n:6\r\n:7\r\n:8\r\n:9\r\n"u8);
        Assert.Equal(5, await a);
        Assert.Equal(6, await b);
        Assert.Equal(7, await c);
        Assert.Equal(8, await d);
        Assert.Equal(9, await e);
    }

    [RespCommand]
    private static partial int Test(in RespContext ctx, int value);

    [RespCommand]
    private static partial int Foo(in RespContext ctx);

    [RespCommand]
    private static partial void Bar(in RespContext ctx);
}
