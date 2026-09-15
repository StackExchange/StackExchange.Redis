using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the vector-set group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceVectorSetsTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<string> Sent { get; } = [];

        public List<CommandFlags> Flags { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [":1\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    [Fact]
    public async Task AddWritesTheVectorBeforeTheElement()
    {
        var (ctx, exec) = Target();

        var request = VectorSetAddRequest.Member("m", new[] { 1f, 2f }.AsMemory());
        request.UseFp32 = false; // VALUES, so the bytes are readable here
        await ctx.VectorSets.AddAsync("k", request);

        // the element comes AFTER its vector, which is the one part of this grammar that reads backwards
        // from the way the method is called
        Assert.Equal("*7|$4|VADD|$1|k|$6|VALUES|$1|2|$1|1|$1|2|$1|m|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AddPacksTheVectorWhenItCan()
    {
        var (ctx, exec) = Target();

        var request = VectorSetAddRequest.Member("m", new[] { 1f, 2f }.AsMemory());
        request.UseFp32 = true;
        await ctx.VectorSets.AddAsync("k", request);

        // FP32 is one bulk string of raw little-endian floats rather than a token each: four arguments
        // for any length of vector, and exact rather than round-tripped through text
        var sent = Assert.Single(exec.Sent);
        Assert.StartsWith("*5|$4|VADD|$1|k|$4|FP32|$8|", sent);
    }

    [Fact]
    public async Task AddWritesItsIndexOptionsInTheDocumentedOrder()
    {
        var (ctx, exec) = Target();

        var request = VectorSetAddRequest.Member("m", new[] { 1f }.AsMemory(), "{}");
        request.UseFp32 = false;
        request.ReducedDimensions = 16;
        request.UseCheckAndSet = true;
        request.Quantization = VectorSetQuantization.Binary;
        request.BuildExplorationFactor = 200;
        request.MaxConnections = 8;

        await ctx.VectorSets.AddAsync("k", request);

        // REDUCE before the vector; CAS, quantization and EF after the element; SETATTR then M last
        Assert.Equal(
            "*16|$4|VADD|$1|k|$6|REDUCE|$2|16|$6|VALUES|$1|1|$1|1|$1|m|$3|CAS|$3|BIN|$2|EF|$3|200|$7|SETATTR|$2|{}|$1|M|$1|8|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheDefaultQuantizationSaysNothing()
    {
        var (ctx, exec) = Target();

        var request = VectorSetAddRequest.Member("m", new[] { 1f }.AsMemory());
        request.UseFp32 = false;
        request.Quantization = VectorSetQuantization.Int8;

        await ctx.VectorSets.AddAsync("k", request);

        // int8 is what the server does anyway, so naming it would be three bytes for nothing
        Assert.DoesNotContain("NOQUANT", Assert.Single(exec.Sent));
        Assert.DoesNotContain("BIN", exec.Sent[0]);
    }

    [Fact]
    public async Task SimilaritySearchNamesItsOrigin()
    {
        var (ctx, exec) = Target("*0\r\n");

        using (await ctx.VectorSets.SimilaritySearchAsync("k", VectorSetSimilaritySearchRequest.ByMember("m"))) { }

        var byVector = VectorSetSimilaritySearchRequest.ByVector(new[] { 1f, 2f }.AsMemory());
        byVector.UseFp32 = false;
        using (await ctx.VectorSets.SimilaritySearchAsync("k", byVector)) { }

        Assert.Equal(
            new[]
            {
                "*4|$4|VSIM|$1|k|$3|ELE|$1|m|",
                "*6|$4|VSIM|$1|k|$6|VALUES|$1|2|$1|1|$1|2|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task SimilaritySearchWritesEveryOptionItWasGiven()
    {
        var (ctx, exec) = Target("*0\r\n");

        var query = VectorSetSimilaritySearchRequest.ByMember("m");
        query.WithScores = true;
        query.WithAttributes = true;
        query.Count = 5;
        query.Epsilon = 0.5;
        query.SearchExplorationFactor = 100;
        query.FilterExpression = ".size > 1";
        query.MaxFilteringEffort = 20;
        query.UseExactSearch = true;
        query.DisableThreading = true;

        using (await ctx.VectorSets.SimilaritySearchAsync("k", query)) { }

        Assert.Equal(
            "*18|$4|VSIM|$1|k|$3|ELE|$1|m|$10|WITHSCORES|$11|WITHATTRIBS|$5|COUNT|$1|5|$7|EPSILON|$3|0.5|$2|EF|$3|100|$6|FILTER|$9|.size > 1|$9|FILTER-EF|$2|20|$5|TRUTH|$8|NOTHREAD|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ABlankFilterIsNoFilterAtAll()
    {
        var (ctx, exec) = Target("*0\r\n");

        var query = VectorSetSimilaritySearchRequest.ByMember("m");
        query.FilterExpression = "   ";

        using (await ctx.VectorSets.SimilaritySearchAsync("k", query)) { }

        // the property keeps what it was given, but a blank filter is absent rather than a filter that
        // matches everything - writing one is a syntax error at the server, which is how this was found
        Assert.Equal("*4|$4|VSIM|$1|k|$3|ELE|$1|m|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ScoresAndAttributesAreReadFromWhateverShapeArrives()
    {
        // RESP2: member, score and attributes as three successive elements
        var (flat, _) = Target("*3\r\n$1\r\na\r\n$3\r\n0.5\r\n$2\r\n{}\r\n");

        // RESP3: the same request answers with the pair nested per match
        var (nested, _) = Target("*2\r\n$1\r\na\r\n*2\r\n$3\r\n0.5\r\n$2\r\n{}\r\n");

        foreach (var ctx in new[] { flat, nested })
        {
            var query = VectorSetSimilaritySearchRequest.ByMember("m");
            query.WithScores = true;
            query.WithAttributes = true;

            using var results = await ctx.VectorSets.SimilaritySearchAsync("k", query);

            var only = Assert.Single(results!.Span.ToArray());
            Assert.Equal("a", only.Member);
            Assert.Equal(0.5, only.Score);
            Assert.Equal("{}", only.AttributesJson);
        }
    }

    [Fact]
    public async Task TheSimpleReadsAreKeyAndMember()
    {
        var (ctx, exec) = Target(":3\r\n", ":8\r\n", ":1\r\n", "$2\r\n{}\r\n", "$1\r\nm\r\n");

        Assert.Equal(3, await ctx.VectorSets.LengthAsync("k"));
        Assert.Equal(8, await ctx.VectorSets.DimensionAsync("k"));
        Assert.True(await ctx.VectorSets.ContainsAsync("k", "m"));
        Assert.Equal("{}", await ctx.VectorSets.GetAttributesJsonAsync("k", "m"));
        Assert.Equal("m", await ctx.VectorSets.RandomMemberAsync("k"));

        Assert.Equal(
            new[]
            {
                "*2|$5|VCARD|$1|k|",
                "*2|$4|VDIM|$1|k|",
                "*3|$9|VISMEMBER|$1|k|$1|m|",
                "*3|$8|VGETATTR|$1|k|$1|m|",
                "*2|$11|VRANDMEMBER|$1|k|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task TheWritesAreKeyMemberAndMaybeAPayload()
    {
        var (ctx, exec) = Target(":1\r\n");

        await ctx.VectorSets.RemoveAsync("k", "m");
        await ctx.VectorSets.SetAttributesJsonAsync("k", "m", "{\"a\":1}");

        Assert.Equal(
            new[]
            {
                "*3|$4|VREM|$1|k|$1|m|",
                "*4|$8|VSETATTR|$1|k|$1|m|$7|{\"a\":1}|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task RandomMembersPassesItsCountThrough()
    {
        var (ctx, exec) = Target("*2\r\n$1\r\na\r\n$1\r\nb\r\n");

        using var some = await ctx.VectorSets.RandomMembersAsync("k", 2);
        Assert.Equal(new RedisValue[] { "a", "b" }, System.Array.ConvertAll(some.Span.ToArray(), v => v.AsRedisValue()));

        // negative counts are meaningful here - they allow the same member more than once - so the count
        // is written as given rather than clamped
        using (await ctx.VectorSets.RandomMembersAsync("k", -2)) { }

        Assert.Equal(
            new[] { "*3|$11|VRANDMEMBER|$1|k|$1|2|", "*3|$11|VRANDMEMBER|$1|k|$2|-2|" },
            exec.Sent);
    }

    [Fact]
    public async Task TheApproximateVectorComesBackAsFloats()
    {
        var (ctx, exec) = Target("*3\r\n$3\r\n0.5\r\n$4\r\n-0.5\r\n$1\r\n1\r\n");

        using var vector = await ctx.VectorSets.GetApproximateVectorAsync("k", "m");

        Assert.NotNull(vector);
        Assert.Equal(new[] { 0.5f, -0.5f, 1f }, vector.Span.ToArray());
        Assert.Equal("*3|$4|VEMB|$1|k|$1|m|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AMissingMemberHasNoVectorAtAll()
    {
        var (ctx, _) = Target("*-1\r\n");

        // nil, not empty: "no such member" and "a zero-dimensional vector" are different answers, which is
        // why this result is nullable where most array replies are not
        Assert.Null(await ctx.VectorSets.GetApproximateVectorAsync("k", "m"));
    }

    [Fact]
    public async Task LinksAreFlattenedAcrossTheIndexLayers()
    {
        var (ctx, exec) = Target("*2\r\n*1\r\n$1\r\na\r\n*2\r\n$1\r\nb\r\n$1\r\nc\r\n");

        using var links = await ctx.VectorSets.GetLinksAsync("k", "m");

        // the reply is one array per HNSW layer; the layers are an implementation detail of the index
        // rather than something the caller asked about, so they arrive as one run
        Assert.NotNull(links);
        Assert.Equal(new RedisValue[] { "a", "b", "c" }, links.Span.ToArray());
        Assert.Equal("*3|$6|VLINKS|$1|k|$1|m|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ScoredLinksAreInterleavedRatherThanPaired()
    {
        var (ctx, exec) = Target("*1\r\n*4\r\n$1\r\na\r\n$3\r\n0.5\r\n$1\r\nb\r\n$4\r\n0.25\r\n");

        using var links = await ctx.VectorSets.GetLinksWithScoresAsync("k", "m");

        // WITHSCORES sends a flat run of member/score pairs, NOT an array of two-element arrays - reading
        // it as the latter takes a member for a score
        Assert.NotNull(links);
        var span = links.Span;
        Assert.Equal(2, span.Length);
        Assert.Equal("a", span[0].Member);
        Assert.Equal(0.5, span[0].Score);
        Assert.Equal("b", span[1].Member);
        Assert.Equal(0.25, span[1].Score);

        Assert.Equal("*4|$6|VLINKS|$1|k|$1|m|$10|WITHSCORES|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task InfoReadsTheFieldsItKnowsAndSkipsTheRest()
    {
        var (ctx, exec) = Target(
            "*8\r\n$10\r\nquant-type\r\n$4\r\nint8\r\n$10\r\nvector-dim\r\n:4\r\n$4\r\nsize\r\n:7\r\n$7\r\nfuture!\r\n*1\r\n:1\r\n");

        var info = await ctx.VectorSets.InfoAsync("k");

        Assert.NotNull(info);
        Assert.Equal(4, info!.Value.Dimension);
        Assert.Equal(7, info.Value.Length);

        // an unknown field with an aggregate value is skipped rather than stopping the walk: the reply is
        // a map that the server is free to extend
        Assert.Equal("*2|$5|VINFO|$1|k|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AMissingKeyHasNoInfo()
    {
        var (ctx, _) = Target("*-1\r\n");

        Assert.Null(await ctx.VectorSets.InfoAsync("k"));
    }

    [Fact]
    public async Task RangeWritesItsBoundsWithBrackets()
    {
        var (ctx, exec) = Target("*0\r\n");

        using (await ctx.VectorSets.RangeAsync("k")) { }
        using (await ctx.VectorSets.RangeAsync("k", "a", "z")) { }
        using (await ctx.VectorSets.RangeAsync("k", "a", "z", count: 10, exclude: Exclude.Both)) { }

        // an open end is - or +; a bound is the value behind [ for inclusive or ( for exclusive, which is
        // the same spelling the sorted-set lex ranges use
        Assert.Equal(
            new[]
            {
                "*4|$6|VRANGE|$1|k|$1|-|$1|+|",
                "*4|$6|VRANGE|$1|k|$2|[a|$2|[z|",
                "*5|$6|VRANGE|$1|k|$2|(a|$2|(z|$2|10|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task TheGroupLandsInTheRightRetryCategories()
    {
        var (ctx, exec) = Target(":1\r\n", ":1\r\n");

        await ctx.VectorSets.LengthAsync("k");
        await ctx.VectorSets.RemoveAsync("k", "m");

        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, exec.Flags[1] & Message.MaskRetryCategory);
    }
}
