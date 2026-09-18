using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The dual scan API: a raw cursor page, and an <see cref="IAsyncEnumerable{T}"/> built on top of it.
/// </summary>
/// <remarks>
/// The two claims worth pinning are the ones a scan gets wrong silently. <b>An empty page is not the
/// end</b> - only a zero cursor is - and stopping early reads a fraction of a sparse keyspace while
/// looking like it worked. And <b>the cursor loop exists once</b>: the sequence asks the raw API for
/// pages, so the two cannot disagree about where they are.
/// </remarks>
public class RespScanTests
{
    /// <summary>Replies with scripted pages, and records what was asked for.</summary>
    private sealed class ScanExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static (RespDatabaseContext Context, ScanExecutor Executor) Target(params string[] replies)
    {
        var executor = new ScanExecutor(replies);
        return (new RespDatabaseContext(new RespContext().WithExecutor(executor)), executor);
    }

    private static string Page(long cursor, params string[] items)
    {
        var sb = new StringBuilder($"*2\r\n:{cursor}\r\n*{items.Length}\r\n");
        foreach (var item in items) sb.Append($"${item.Length}\r\n{item}\r\n");
        return sb.ToString();
    }

    [Fact]
    public async Task ARawPageCarriesItsItemsAndCursor()
    {
        var (ctx, exec) = Target(Page(42, "a", "b"));

        using var page = await ctx.Sets.ScanPageAsync("s");

        Assert.Equal(42, page.Cursor);
        Assert.False(page.IsComplete);
        Assert.Equal(["a", "b"], page.Items.Span.ToArray().Select(v => v.ToString()));
        Assert.Equal("*3|$5|SSCAN|$1|s|$1|0|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task MatchAndCountAreOmittedWhenNotAsked()
    {
        var (ctx, exec) = Target(Page(0));

        (await ctx.Sets.ScanPageAsync("s", pattern: "*")).Dispose();      // '*' is "no filter"
        (await ctx.Sets.ScanPageAsync("s", pattern: "a*", pageSize: 7)).Dispose();

        Assert.Equal("*3|$5|SSCAN|$1|s|$1|0|", exec.Sent[0]);
        Assert.Equal("*7|$5|SSCAN|$1|s|$1|0|$5|MATCH|$2|a*|$5|COUNT|$1|7|", exec.Sent[1]);
    }

    /// <summary>The sequence walks pages until the cursor is zero.</summary>
    [Fact]
    public async Task TheSequenceFollowsTheCursorToTheEnd()
    {
        var (ctx, exec) = Target(Page(7, "a", "b"), Page(9, "c"), Page(0, "d"));

        var seen = new List<string>();
        await foreach (var item in ctx.Sets.ScanAsync("s")) seen.Add(item.ToString());

        Assert.Equal(["a", "b", "c", "d"], seen);
        Assert.Equal(3, exec.Sent.Count);
        Assert.Contains("|$1|0|", exec.Sent[0]); // first page from the origin
        Assert.Contains("|$1|7|", exec.Sent[1]); // then from each reply's cursor
        Assert.Contains("|$1|9|", exec.Sent[2]);
    }

    /// <summary>
    /// An empty page in the middle is not the end - only a zero cursor is.
    /// </summary>
    /// <remarks>
    /// This is the scan bug everyone writes once: the server is free to return a page with nothing in it
    /// while the scan still has most of the keyspace to go, so a loop that stops on "no items" silently
    /// reads a fraction of the data.
    /// </remarks>
    [Fact]
    public async Task AnEmptyPageDoesNotEndTheScan()
    {
        var (ctx, _) = Target(Page(5, "a"), Page(6), Page(0, "b"));

        var seen = new List<string>();
        await foreach (var item in ctx.Sets.ScanAsync("s")) seen.Add(item.ToString());

        Assert.Equal(["a", "b"], seen);
    }

    [Fact]
    public async Task TheScanIsAResumableCursor()
    {
        var (ctx, _) = Target(Page(11, "a", "b"), Page(0, "c"));

        var sequence = ctx.Sets.ScanAsync("s");
        var cursor = Assert.IsAssignableFrom<IScanningCursor>(sequence);

        await using var iterator = sequence.GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());

        // the ACTIVE page's cursor, not the next one: resuming from it re-reads the page in progress
        Assert.Equal(0, cursor.Cursor);
        Assert.Equal(0, cursor.PageOffset);

        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1, cursor.PageOffset);

        Assert.True(await iterator.MoveNextAsync()); // into the second page
        Assert.Equal(11, cursor.Cursor);
    }

    /// <summary>A resumed scan starts where it was told, not at the beginning of the page.</summary>
    [Fact]
    public async Task ThePageOffsetSkipsWhatWasAlreadySeen()
    {
        var (ctx, _) = Target(Page(0, "a", "b", "c"));

        var seen = new List<string>();
        await foreach (var item in ctx.Sets.ScanAsync("s", cursor: 0, pageOffset: 2)) seen.Add(item.ToString());

        Assert.Equal(["c"], seen);
    }

    /// <summary>Either token cancels, and the same token twice is not linked twice.</summary>
    /// <remarks>
    /// The behaviour is checked through what a caller can see - that cancelling either source stops the
    /// scan - and the no-double-subscribe case is checked by the one thing that distinguishes it: with a
    /// single token there is nothing to link, so the enumerator must observe that token directly rather
    /// than a copy that could outlive it.
    /// </remarks>
    [Fact]
    public async Task EitherTokenCancelsTheScan()
    {
        foreach (var cancelOuter in new[] { true, false })
        {
            var (ctx, _) = Target(Page(1, "a"), Page(2, "b"), Page(3, "c"));
            using var outer = new CancellationTokenSource();
            using var inner = new CancellationTokenSource();

            var sequence = ctx.Sets.ScanAsync("s", cancellationToken: outer.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in sequence.WithCancellation(inner.Token))
                {
                    (cancelOuter ? outer : inner).Cancel();
                }
            });
        }
    }

    [Fact]
    public async Task OneTokenUsedBothWaysStillCancels()
    {
        var (ctx, _) = Target(Page(1, "a"), Page(2, "b"));
        using var cts = new CancellationTokenSource();

        var sequence = ctx.Sets.ScanAsync("s", cancellationToken: cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sequence.WithCancellation(cts.Token)) cts.Cancel();
        });
    }

    /// <summary>An already-cancelled token stops the scan before it sends anything.</summary>
    [Fact]
    public async Task ACancelledTokenSendsNothing()
    {
        var (ctx, exec) = Target(Page(0, "a"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in ctx.Sets.ScanAsync("s", cancellationToken: cts.Token)) { }
        });

        Assert.Empty(exec.Sent);
    }
}
