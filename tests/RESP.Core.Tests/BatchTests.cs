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
    [RespCommand]
    private static partial void Bar(in RespContext ctx);
}
