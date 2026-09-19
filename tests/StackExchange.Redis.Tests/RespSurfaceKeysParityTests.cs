using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Key commands the new surface renders itself, against the bytes the classic path produces.
/// </summary>
/// <remarks>
/// <inheritdoc cref="RespSurfaceStreamsParityTests" path="/remarks/para[1]"/>
/// <para>
/// <c>MIGRATE</c> is the one that most wants this: the key is its <b>third</b> argument rather than its
/// first, and the two optional tokens go last, so an independent rendering agreeing is worth more than a
/// string anyone could have copied from the other implementation.
/// </para>
/// </remarks>
public class RespSurfaceKeysParityTests
{
    private sealed class FakeExecutor(string reply) : RespExecutorBase
    {
        public string? Sent { get; private set; }

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request)
        {
            Sent = Text(request.Span);
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    private static string Classic(Func<RedisDatabase, Message> build)
    {
        var writer = new RespFrameWriter();
        build(new RedisDatabase(null!, 0, null)).WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        using var frame = writer.Complete(ServerSelectionStrategy.NoSlot);
        return Text(frame.Span);
    }

    private static string Modern(Func<RespDatabaseContext, ValueTask> send, string reply = "+OK\r\n")
    {
        var executor = new FakeExecutor(reply);
        var task = send(new RespDatabaseContext(new RespContext().WithExecutor(executor)));
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
        return executor.Sent!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5000)]
    public void RestoreMatches(int? ttlMs)
    {
        byte[] payload = [1, 2, 3, 0xFF];
        var expiry = ttlMs.HasValue ? TimeSpan.FromMilliseconds(ttlMs.GetValueOrDefault()) : (TimeSpan?)null;
        Assert.Equal(
            Classic(db => db.GetRestoreMessage("k", payload, expiry, CommandFlags.None)),
            Modern(ctx => ctx.Keys.RestoreAsync("k", payload, expiry)));
    }

    /// <summary>TimeSpan.MaxValue means "no expiry", not "the largest expiry".</summary>
    [Fact]
    public void RestoreTreatsMaxValueAsNoExpiry()
        => Assert.Equal(
            Classic(db => db.GetRestoreMessage("k", [1], TimeSpan.MaxValue, CommandFlags.None)),
            Modern(ctx => ctx.Keys.RestoreAsync("k", [1], TimeSpan.MaxValue)));

    [Theory]
    [InlineData(MigrateOptions.None)]
    [InlineData(MigrateOptions.Copy)]
    [InlineData(MigrateOptions.Replace)]
    [InlineData(MigrateOptions.Copy | MigrateOptions.Replace)]
    public void MigrateMatches(MigrateOptions options)
    {
        var endpoint = new DnsEndPoint("other-host", 6380);
        Assert.Equal(
            Classic(db => new RedisDatabase.KeyMigrateCommandMessage(0, "k", endpoint, 3, 1500, options, CommandFlags.None)),
            Modern(ctx => ctx.Keys.MigrateAsync("k", "other-host", 6380, 3, TimeSpan.FromMilliseconds(1500), options)));
    }

    [Fact]
    public void DebugObjectMatches()
        => Assert.Equal(
            Classic(db => Message.Create(0, CommandFlags.None, RedisCommand.DEBUG, RedisLiterals.OBJECT, (RedisKey)"k")),
            Modern(ctx => Discard(ctx.Keys.DebugObjectAsync("k")), "$4\r\nsome\r\n"));

    private static async ValueTask Discard<T>(ValueTask<T> pending) => await pending;
}
