using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A command that fails because its connection failed must fail with a <see cref="RedisConnectionException"/>
/// carrying the transport fault as its inner exception: never the raw transport exception, and never a
/// <see cref="TaskCanceledException"/> when no caller token was involved (#3283).
/// </summary>
public class ConnectionFailureExceptionTests(ITestOutputHelper log)
{
    private static (Message Message, Task<bool> Task) CreateTrackedPing()
    {
        var message = Message.Create(-1, CommandFlags.None, RedisCommand.PING);
        var box = TaskResultBox<bool>.Create(out var tcs, null);
        message.SetSource(box, ResultProcessor.DemandOK);
        return (message, tcs.Task);
    }

    private static async Task<Exception> AwaitFailure(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(task, completed);
        return await Assert.ThrowsAnyAsync<Exception>(() => task);
    }

    [Theory]
    [InlineData(false)] // EOF-style report: no exception at all (used to surface as TaskCanceledException)
    [InlineData(true)] // transport fault (used to surface raw when the detailed report was not built)
    public async Task DrainedMessagesFailWithRedisConnectionException(bool withInner)
    {
        // a dummy connection has no bridge, so it is never the bridge's *current* connection and the detailed
        // first-report exception is not built: this is exactly the path that handed out raw/null exceptions
        using var stream = new MemoryStream();
        using var connection = PhysicalConnection.Dummy(stream);
        var (message, task) = CreateTrackedPing();
        connection.EnqueueInsideWriteLock(message, enforceMuxer: false);

        var inner = withInner ? new IOException("simulated transport fault") : null;
        connection.RecordConnectionFailed(ConnectionFailureType.SocketClosed, inner);

        var ex = await AwaitFailure(task);
        log.WriteLine(ex.ToString());
        var conn = Assert.IsType<RedisConnectionException>(ex);
        Assert.Equal(ConnectionFailureType.SocketClosed, conn.FailureType);
        Assert.Same(inner, conn.InnerException);
    }

    [Fact]
    public async Task NoConnectionFailsWithRedisConnectionExceptionNotCancellation()
    {
        using var server = new InProcessTestServer(log);
        var config = server.GetClientConfig(withPubSub: false, defaultOnly: true);
        config.BacklogPolicy = BacklogPolicy.FailFast;
        config.AbortOnConnectFail = false;
        config.AllowSimulateConnectionFailure = true;
        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var endpoint = conn.GetServerSnapshot()[0];
        var bridge = endpoint.GetBridge(ConnectionType.Interactive)!;

        conn.AllowConnect = false;
        endpoint.SimulateConnectionFailure(SimulatedFailureType.All);
        Assert.False(conn.IsConnected);

        // write at the bridge, so we observe the message's own outcome rather than the caller's translation of
        // the write result (which is what hid this for single commands - most of the time)
        var (message, task) = CreateTrackedPing();
        Assert.Equal(WriteResult.NoConnectionAvailable, await bridge.TryWriteAsync(message, isReplica: false));

        var ex = await AwaitFailure(task);
        log.WriteLine(ex.ToString());
        Assert.IsType<RedisConnectionException>(ex);
        Assert.False(task.IsCanceled);
    }
}
