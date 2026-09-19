using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using RESPite.Operations;
using RESPite.Transports;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The new stack against a REAL server, with none of the old pipeline in the path.
/// </summary>
/// <remarks>
/// <para>
/// Everything else that talks to a server goes through <c>RespMessageExecutor</c>, which wraps the
/// rendered frame in a <c>Message</c>. This path is: interpolated writer → context →
/// <c>RespConnectionExecutor</c> → <c>RespConnection</c> → <c>StreamDuplexTransport</c> → socket. No
/// <c>Message</c>, no <c>ResultProcessor</c>, no result box.
/// </para>
/// <para>
/// <b>What is deliberately absent, and why these tests are still honest:</b> no handshake, no
/// <c>SELECT</c>, no reconnect, no backlog. A plain TCP connection to a RESP server speaks RESP2 on
/// database 0 without being asked, which is enough to prove the path carries real bytes both ways. It is
/// <i>not</i> enough to benchmark against - see the queue.
/// </para>
/// </remarks>
public class RespNewStackEndToEndTests(ITestOutputHelper output)
{
    private static async Task<(RespDatabaseContext Context, RespConnection Connection, StreamDuplexTransport Transport)> ConnectAsync()
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(TestConfig.Current.PrimaryServer, TestConfig.Current.PrimaryPort);

        var transport = new StreamDuplexTransport(new NetworkStream(socket, ownsSocket: true));
        var connection = new RespConnection(transport);
        var context = new RespContext().WithExecutor(new RespConnectionExecutor(connection, 0));
        return (new RespDatabaseContext(context), connection, transport);
    }

    [Fact]
    public async Task ASetAndGetTravelTheNewStackToARealServer()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:counter";
        await context.Keys.DeleteAsync(key);
        await context.Strings.SetAsync(key, 41);

        Assert.Equal(42, (long)await context.Strings.IncrementAsync(key));
        Assert.Equal("42", (string?)await context.Strings.GetAsync(key));

        output.WriteLine("round-tripped through socket → transport → connection → operation, no Message");
    }

    [Fact]
    public async Task PipeliningHoldsAgainstARealServer()
    {
        // the reason any of this exists: many requests in flight, each matched to its own caller. A fake
        // transport cannot show this, because it never reorders or coalesces reads the way a socket does.
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:pipeline";
        await context.Keys.DeleteAsync(key);

        const int Count = 500;
        var pending = new ValueTask<long>[Count];
        for (var i = 0; i < Count; i++) pending[i] = context.Strings.IncrementAsync(key);

        // every reply is the increment for its own position, in order; a desynchronised queue shows up
        // here as an off-by-one that no in-memory test would catch
        for (var i = 0; i < Count; i++) Assert.Equal(i + 1, await pending[i]);
    }

    [Fact]
    public async Task AServerErrorArrivesAsARedisServerException()
    {
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, _, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        RedisKey key = "RespNewStackEndToEndTests:wrongtype";
        await context.Keys.DeleteAsync(key);
        await context.Lists.LeftPushAsync(key, "a");

        // INCR against a list: the server says WRONGTYPE, and the caller sees the exception it would
        // have seen from the old pipeline
        var ex2 = await Assert.ThrowsAsync<RedisServerException>(async () => await context.Strings.IncrementAsync(key));
        Assert.StartsWith("WRONGTYPE", ex2.Message);
    }

    [Fact]
    public async Task TheOperationRecordsWhereItWentAndWhatMoved()
    {
        // "not cheating by omitting half the work": status, connection, byte stamps and the write tick,
        // recorded against a real socket rather than asserted against a fake
        RespConnection connection;
        RespDatabaseContext context;
        StreamDuplexTransport transport;
        try
        {
            (context, connection, transport) = await ConnectAsync();
        }
        catch (SocketException ex)
        {
            Assert.Skip($"Unable to connect to server: {ex.Message}");
            return;
        }

        await using var owner = transport;

        await context.Strings.GetAsync("RespNewStackEndToEndTests:diagnostics");

        Assert.True(connection.BytesSent > 0);
        Assert.True(connection.BytesReceived > 0);
        Assert.Equal(0, connection.PendingCount);
        Assert.False(connection.IsClosed);
        output.WriteLine($"sent {connection.BytesSent} bytes, received {connection.BytesReceived}");
    }
}
