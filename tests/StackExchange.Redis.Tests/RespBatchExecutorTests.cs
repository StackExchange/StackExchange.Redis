using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Batching as a decorator on the executor: <c>SendAsync</c> accumulates, and executing sends the run.
/// </summary>
/// <remarks>
/// The property worth holding on to is that <b>no generics were needed</b>. A batch has to hold a
/// heterogeneous queue and complete each caller with its own result type, which looks like it demands an
/// untyped base and a typed proxy - but <c>IRespExecutor</c> has already erased the type: it deals in
/// <see cref="RespPayload"/>, and the handler that turns one into a <c>T</c> is applied a layer above,
/// after the executor has handed the payload back. <c>PayloadsAreCompletedTypedByTheLayerAbove</c> is
/// where that shows.
/// </remarks>
public class RespBatchExecutorTests
{
    /// <summary>Replies in order, and records when each send was actually issued.</summary>
    private sealed class RecordingExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        /// <summary>Set once anything has been sent, so "nothing yet" can be asserted.</summary>
        public bool HasSent => Sent.Count != 0;

        public int Database => 0;

        public RespPayload Send(in RespRequest request) => throw new NotSupportedException();

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return new ValueTask<RespPayload>(
                RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)])));
        }
    }

    private static RespDatabaseContext Source(RecordingExecutor executor)
        => new(new RespContext().WithExecutor(executor));

    [Fact]
    public async Task NothingIsSentUntilTheBatchIsExecuted()
    {
        var executor = new RecordingExecutor("$1\r\na\r\n", "$1\r\nb\r\n");
        using var batch = Source(executor).CreateBatch();

        var first = batch.Context.Strings.GetAsync("k1");
        var second = batch.Context.Strings.GetAsync("k2");

        Assert.False(executor.HasSent);
        Assert.Equal(2, batch.Count);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        await batch.ExecuteAsync();

        Assert.Equal(["*2|$3|GET|$2|k1|", "*2|$3|GET|$2|k2|"], executor.Sent);
        Assert.Equal("a", (string?)await first);
        Assert.Equal("b", (string?)await second);
    }

    /// <summary>
    /// Each caller gets its own result type, from a queue that knows none of them.
    /// </summary>
    /// <remarks>
    /// The queue holds <c>TaskCompletionSource&lt;RespPayload&gt;</c> and nothing else; that these come
    /// back as a <see cref="RedisValue"/>, a <see cref="long"/> and a <see cref="bool"/> is the work of the
    /// handlers above the executor, which the batch never sees. That is why it needs no type parameter.
    /// </remarks>
    [Fact]
    public async Task PayloadsAreCompletedTypedByTheLayerAbove()
    {
        var executor = new RecordingExecutor("$4\r\nmarc\r\n", ":7\r\n", "+OK\r\n");
        using var batch = Source(executor).CreateBatch();

        var value = batch.Context.Strings.GetAsync("k");
        var length = batch.Context.Strings.LengthAsync("k");
        var set = batch.Context.Strings.SetAsync("k", "v");

        await batch.ExecuteAsync();

        Assert.Equal("marc", (string?)await value);
        Assert.Equal(7L, await length);
        Assert.True(await set);
    }

    /// <summary>A command that fails faults only its own caller.</summary>
    [Fact]
    public async Task AFaultIsDeliveredToTheCommandThatCausedIt()
    {
        var executor = new RecordingExecutor("-ERR nope\r\n", "$1\r\nb\r\n");
        using var batch = Source(executor).CreateBatch();

        var bad = batch.Context.Strings.GetAsync("k1");
        var good = batch.Context.Strings.GetAsync("k2");

        await batch.ExecuteAsync();

        await Assert.ThrowsAsync<RespException>(async () => await bad);
        Assert.Equal("b", (string?)await good);
    }

    /// <summary>An empty batch is a no-op rather than an error.</summary>
    [Fact]
    public async Task AnEmptyBatchExecutesCleanly()
    {
        var executor = new RecordingExecutor("+OK\r\n");
        using var batch = Source(executor).CreateBatch();

        await batch.ExecuteAsync();

        Assert.False(executor.HasSent);
    }

    /// <summary>
    /// A batch sends once; executing again, or queueing after, is a bug rather than a no-op.
    /// </summary>
    /// <remarks>
    /// <b>The late command faults its task rather than throwing at the call site</b>, and that is not a
    /// choice this layer gets to make: the send path hands the executor to an <c>async</c> method, so
    /// anything the executor throws is captured into the task it returns. The distinction matters only to
    /// a caller who never awaits - and one who never awaits a command they queued has a larger problem.
    /// </remarks>
    [Fact]
    public async Task ABatchIsOneShot()
    {
        var executor = new RecordingExecutor("$1\r\na\r\n");
        using var batch = Source(executor).CreateBatch();

        _ = batch.Context.Strings.GetAsync("k");
        await batch.ExecuteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await batch.ExecuteAsync());

        var late = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await batch.Context.Strings.GetAsync("k2"));
        Assert.Contains("already been executed", late.Message);
    }

    /// <summary>
    /// Discarding a batch faults what was queued, rather than leaving it outstanding for ever.
    /// </summary>
    /// <remarks>
    /// Not tidiness: the caller awaiting a queued command holds the only reference to its rendered frame
    /// and releases it when that task completes, so a dropped batch would strand a pooled buffer per
    /// command. The fault is what lets those <c>finally</c> blocks run.
    /// </remarks>
    [Fact]
    public async Task DiscardingABatchFaultsWhatWasQueued()
    {
        var executor = new RecordingExecutor("$1\r\na\r\n");
        Task<RedisValue> pending;

        using (var batch = Source(executor).CreateBatch())
        {
            pending = batch.Context.Strings.GetAsync("k").AsTask();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Contains("discarded without being executed", ex.Message);
        Assert.False(executor.HasSent);
    }

    /// <summary>A synchronous send refuses, because there is nothing to hand back yet.</summary>
    [Fact]
    public void ASynchronousSendRefuses()
    {
        var executor = new RecordingExecutor("+OK\r\n");
        using var batch = Source(executor).CreateBatch();

        var ex = Assert.Throws<InvalidOperationException>(
            () => batch.Context.Raw.Send<RedisValue>($"{RedisCommand.GET}{(RedisKey)"k"}", CommandFlags.None));
        Assert.Contains("no synchronous send", ex.Message);
    }

    /// <summary>The context it was built from is untouched, and still sends immediately.</summary>
    [Fact]
    public async Task TheSourceContextIsNotBatched()
    {
        var executor = new RecordingExecutor("$1\r\na\r\n");
        var source = Source(executor);
        using var batch = source.CreateBatch();

        Assert.Equal("a", (string?)await source.Strings.GetAsync("k"));
        Assert.True(executor.HasSent);
        Assert.Equal(0, batch.Count);
    }
}
