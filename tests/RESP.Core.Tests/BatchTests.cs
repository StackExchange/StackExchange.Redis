using System.Threading.Tasks;
using RESPite;
using Xunit;

namespace RESP.Core.Tests;

public partial class BatchTests
{
    [Fact]
    public async Task TestInfrastructure()
    {
        using var server = new TestServer();
        var pending = FooAsync(server.Context);
        server.AssertSent("*1\r\n$3\r\nfoo\r\n"u8);
        Assert.False(pending.IsCompleted);
        server.Respond(":42\r\n"u8);
        Assert.Equal(42, await pending);
    }
    [Fact]
    public async Task SimpleBatching()
    {
        // todo: create a manually controlled data pipe
        var parentContext = RespContext.Null.WithCancellationToken(TestContext.Current.CancellationToken);
        using (var batch = parentContext.CreateBatch())
        {
            var ctx = batch.Context;
            var a = FooAsync(ctx);
            var b = FooAsync(ctx);
            var c = FooAsync(ctx);

            await batch.FlushAsync();

            // todo: supply :1\r\n:2\r\n:3\r\n
            Assert.Equal(1, await a);
            Assert.Equal(2, await b);
            Assert.Equal(3, await c);
        }
    }

    [RespCommand]
    private static partial int Foo(in RespContext ctx);
}
