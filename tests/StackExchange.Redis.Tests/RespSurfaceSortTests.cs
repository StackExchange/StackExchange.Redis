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
/// What SORT puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceSortTests
{
    private sealed class FakeExecutor(params string[] replies) : RespExecutorBase
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public List<CommandFlags> Flags { get; } = [];

        public override int Database => 0;

        public override RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public override ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private sealed class FakeFeatures(RedisFeatures features) : IRespServerFeatures
    {
        public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures result)
        {
            result = features;
            return true;
        }
    }

    private static (RespDatabaseContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? ["*0\r\n"] : replies);
        return (new RespDatabaseContext(new RespContext().WithExecutor(executor)), executor);
    }

    private static RespDatabaseContext Server(in RespDatabaseContext context, int major)
        => context.WithServices(new FakeFeatures(new RedisFeatures(new Version(major, 0))));

    [Fact]
    public async Task TheDefaultsAreNotWritten()
    {
        var (ctx, exec) = Target();

        using (await ctx.Keys.SortAsync("k")) { }

        // ascending, numeric, no limit, no by, no get: the server assumes every one of those, so an
        // untouched call is two arguments and not eight
        Assert.Equal("*2|$4|SORT|$1|k|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheOperandsGoOutInTheDocumentedOrder()
    {
        var (ctx, exec) = Target();

        using (await ctx.Keys.SortAsync(
            "k",
            skip: 1,
            take: 2,
            order: Order.Descending,
            sortType: SortType.Alphabetic,
            by: "w_*",
            get: ["#", "d_*"]))
        {
        }

        // BY, LIMIT, DESC, ALPHA, then one GET per pattern - the grammar Redis documents, rather than the
        // order the parameters happen to be declared in
        Assert.Equal(
            "*13|$4|SORT|$1|k|$2|BY|$3|w_*|$5|LIMIT|$1|1|$1|2|$4|DESC|$5|ALPHA|$3|GET|$1|#|$3|GET|$3|d_*|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ARepeatedGetIsWrittenOncePerPattern()
    {
        var (ctx, exec) = Target();

        using (await ctx.Keys.SortAsync("k", get: ["a", "b", "c"])) { }

        // GET is the one operand that is not a single fragment: it prefixes every pattern, so three
        // patterns are six arguments
        Assert.Equal("*8|$4|SORT|$1|k|$3|GET|$1|a|$3|GET|$1|b|$3|GET|$1|c|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheReadOnlySpellingNeedsAServerThatHasIt()
    {
        var (bare, exec) = Target();

        using (await Server(bare, 7).Keys.SortAsync("k")) { }
        using (await Server(bare, 6).Keys.SortAsync("k")) { }
        using (await bare.Keys.SortAsync("k")) { }

        // SORT_RO is 7.0; before that it is an unknown command, and with no probe "not sure" has to mean
        // the spelling that exists everywhere
        Assert.Equal(
            new[] { "*2|$7|SORT_RO|$1|k|", "*2|$4|SORT|$1|k|", "*2|$4|SORT|$1|k|" },
            exec.Sent);
    }

    [Fact]
    public async Task StoringIsAlwaysTheWritableSpelling()
    {
        var (bare, exec) = Target(":3\r\n");

        await Server(bare, 7).Keys.SortAndStoreAsync("dst", "k");

        // a destination makes this a write, so SORT_RO is not an option however new the server is
        Assert.Equal("*4|$4|SORT|$1|k|$5|STORE|$3|dst|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task StoreIsAWriteAndTheReadIsARead()
    {
        var (bare, exec) = Target("*0\r\n", ":0\r\n");

        using (await Server(bare, 7).Keys.SortAsync("k")) { }
        await Server(bare, 7).Keys.SortAndStoreAsync("dst", "k");

        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);

        // SORT is categorised read-only because that is the common case; the STORE variant has to be
        // raised, or a replay of it would look harmless
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, exec.Flags[1] & Message.MaskRetryCategory);
    }

    [Fact]
    public void RoutingIsLeftToTheCaller()
    {
        var (bare, exec) = Target();

        // SORT is deliberately NOT in the primary-only list - it is one of the writable commands a
        // writable replica may serve - so asking for a replica is honoured rather than overridden
        var pending = Server(bare, 6).Keys.SortAsync("k", flags: CommandFlags.DemandReplica);

        Assert.Equal(CommandFlags.DemandReplica, Message.GetPrimaryReplicaFlags(Assert.Single(exec.Flags)));
        pending.GetAwaiter().GetResult().Dispose();
    }

    [Fact]
    public void ABadEnumThrowsBeforeAnythingIsWritten()
    {
        var (ctx, exec) = Target();

        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Keys.SortAsync("k", order: (Order)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => ctx.Keys.SortAsync("k", sortType: (SortType)42));
        Assert.Throws<ArgumentNullException>(() => ctx.Keys.SortAndStoreAsync(default, "k"));

        Assert.Empty(exec.Sent);
    }
}
