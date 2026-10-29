using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A command that fails because its connection is gone must fail with a <see cref="RedisConnectionException"/>:
/// never the raw transport exception, and never a <see cref="TaskCanceledException"/> when no caller token was
/// involved (#3283, main #3287) - and a batch whose connection is gone must complete every one of its commands
/// that way rather than leaving any of them pending (#3286). The shipped core's tests drove its bridge and
/// physical connection directly; this core is driven from the public surface, where the same facts are
/// observable.
/// </summary>
public class ConnectionDroppedExceptionTests(ITestOutputHelper log)
{
    private static async Task<Exception> AwaitFailure(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(task, completed);
        return await Assert.ThrowsAnyAsync<Exception>(() => task);
    }

    private async Task<(InProcessTestServer Server, ConnectionMultiplexer Connection)> ConnectThenLoseTheServerAsync()
    {
        var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(withPubSub: false, defaultOnly: true);
        config.BacklogPolicy = BacklogPolicy.FailFast;
        config.AbortOnConnectFail = false;
        config.AllowSimulateConnectionFailure = true;
        var conn = await ConnectionMultiplexer.ConnectAsync(config);

        // no connection, and no way to make one: what a command meets when the server has gone away
        conn.AllowConnect = false;
        conn.GetServerSnapshot()[0].SimulateConnectionFailure(SimulatedFailureType.All);
        Assert.False(conn.IsConnected);
        return (server, conn);
    }

    [Fact]
    public async Task ACommandWithNoConnectionFailsWithRedisConnectionExceptionNotCancellation()
    {
        var (server, conn) = await ConnectThenLoseTheServerAsync();
        using var _ = server;
        await using var __ = conn;

        var task = conn.GetDatabase().PingAsync();
        var ex = await AwaitFailure(task);
        log.WriteLine(ex.ToString());
        Assert.IsType<RedisConnectionException>(ex);
        Assert.False(task.IsCanceled);
    }

    [Fact]
    public async Task ABatchWithNoConnectionCompletesEveryCommandWithRedisConnectionException()
    {
        var (server, conn) = await ConnectThenLoseTheServerAsync();
        using var _ = server;
        await using var __ = conn;

        var batch = conn.GetDatabase().CreateBatch();
        var tasks = Enumerable.Range(0, 20).Select(i => batch.StringGetAsync("k" + i)).ToArray();
        batch.Execute();

        // every command, not most: a batch's tail used to be left pending when its connection was poisoned
        var all = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(all, completed);
        foreach (var task in tasks)
        {
            Assert.True(task.IsFaulted, "every command must fault, not cancel or hang");
            Assert.IsType<RedisConnectionException>(task.Exception!.InnerException);
        }
    }
}
