using System;
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

    [Fact(Timeout = 500)] // this should be very fast unless something is very wrong
    public async Task SimpleBatching()
    {
        // server setup
        using var server = new TestServer();
        var cancellationToken = server.Context.CancellationToken;
        Assert.Equal(TestContext.Current.CancellationToken, cancellationToken); // check server has CT
        Assert.True(cancellationToken.CanBeCanceled);

        // prepare a batch
        ValueTask<int> a, b, c, d, e, f;
        using (var batch = server.Context.CreateBatch())
        {
            Assert.Equal(cancellationToken, batch.Context.CancellationToken); // check the batch inherited CT

            b = TestAsync(batch.Context, 1);
            Assert.Equal(cancellationToken, b.AsRespOperation().CancellationToken); // check batch ops inherit CT
            c = TestAsync(batch.Context, 2);
            d = TestAsync(batch.Context, 3);

            // we want to sandwich the batch between two regular operations
            a = TestAsync(server.Context, 0); // uses SERVER
            Assert.Equal(cancellationToken, a.AsRespOperation().CancellationToken); // check server ops inherit CT
            Assert.True(a.AsRespOperation().IsSent);
            Assert.False(d.AsRespOperation().IsSent);
            await batch.FlushAsync(); // uses BATCH

            // await something not flushed, inside the scope of the batch
            f = TestAsync(batch.Context, 10);

            // Because of https://github.com/dotnet/runtime/issues/119232, we can't detect unsent operations
            // in ValueTask/Task (technically we could for ValueTask[T], but it would break .AsTask()), but
            // we can check the unwrapped handling.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.AsRespOperation());
            Assert.StartsWith("This command has not yet been sent", ex.Message);

            // and try one that escapes the batch (should get disposed)
            f = TestAsync(batch.Context, 10); // never flushed, intentionally
        }
        // we *can* safely await if the batch is disposed
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await f);

        Assert.True(d.AsRespOperation().IsSent);
        e = TestAsync(server.Context, 4); // uses SERVER again

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

        // but can only be awaited once
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await a);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await b);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await c);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await d);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await e);
    }

    [RespCommand]
    private static partial int Test(in RespContext ctx, int value);

    [RespCommand]
    private static partial int Foo(in RespContext ctx);

    [RespCommand]
    private static partial void Bar(in RespContext ctx);
}
