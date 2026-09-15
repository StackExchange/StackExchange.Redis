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
/// What the geospatial group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceGeospatialTests
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

    private sealed class FakeFeatures(RedisFeatures features) : IRespServerFeatures
    {
        public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures result)
        {
            result = features;
            return true;
        }
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? ["*0\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    private static RespContext Server(in RespContext context, int major, int minor = 0)
        => context.WithServices(new FakeFeatures(new RedisFeatures(new Version(major, minor))));

    [Fact]
    public async Task AddWritesLongitudeThenLatitudeThenMember()
    {
        var (ctx, exec) = Target(":1\r\n", ":2\r\n");

        await ctx.Geospatial.Add("k", new GeoEntry(1, 2, "a"));
        await ctx.Geospatial.Add("k", [new GeoEntry(1, 2, "a"), new GeoEntry(3, 4, "b")]);

        // longitude first: the order trips people up, and it is the opposite of how coordinates are
        // usually said out loud
        Assert.Equal(
            new[]
            {
                "*5|$6|GEOADD|$1|k|$1|1|$1|2|$1|a|",
                "*8|$6|GEOADD|$1|k|$1|1|$1|2|$1|a|$1|3|$1|4|$1|b|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AddingNothingAsksNothing()
    {
        var (ctx, exec) = Target();

        Assert.Equal(0, await ctx.Geospatial.Add("k", ReadOnlySpan<GeoEntry>.Empty));
        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task RemoveIsTheSortedSetCommand()
    {
        var (ctx, exec) = Target(":1\r\n");

        await ctx.Geospatial.Remove("k", "a");

        // there is no GEOREM and there never was: a geo set is a sorted set
        Assert.Equal("*3|$4|ZREM|$1|k|$1|a|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheSingleReadsNameTheirUnitAndMember()
    {
        var (ctx, exec) = Target("$5\r\n123.4\r\n", "*1\r\n$11\r\nsqc8b49rny0\r\n", "*1\r\n*2\r\n$1\r\n1\r\n$1\r\n2\r\n");

        Assert.Equal(123.4, await ctx.Geospatial.Distance("k", "a", "b", GeoUnit.Kilometers));
        Assert.Equal("sqc8b49rny0", await ctx.Geospatial.Hash("k", "a"));
        Assert.Equal(new GeoPosition(1, 2), await ctx.Geospatial.Position("k", "a"));

        Assert.Equal(
            new[]
            {
                "*5|$7|GEODIST|$1|k|$1|a|$1|b|$2|km|",
                "*3|$7|GEOHASH|$1|k|$1|a|",
                "*3|$6|GEOPOS|$1|k|$1|a|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task AMissingMemberReadsAsNullRatherThanAnEmptyArray()
    {
        var (hash, _) = Target("*1\r\n$-1\r\n");
        var (position, _) = Target("*1\r\n*-1\r\n");

        // GEOPOS and GEOHASH always reply with an array, one element per member asked about; the single
        // forms have to unwrap it, and a nil element is "no such member"
        Assert.Null(await hash.Geospatial.Hash("k", "a"));
        Assert.Null(await position.Geospatial.Position("k", "a"));
    }

    [Fact]
    public async Task SearchNamesItsOriginAndItsShape()
    {
        var (ctx, exec) = Target();

        using (await ctx.Geospatial.Search("k", "a", new GeoSearchCircle(5, GeoUnit.Kilometers), options: GeoRadiusOptions.None)) { }
        using (await ctx.Geospatial.Search("k", 1, 2, new GeoSearchBox(3, 4, GeoUnit.Meters), options: GeoRadiusOptions.None)) { }

        Assert.Equal(
            new[]
            {
                "*7|$9|GEOSEARCH|$1|k|$10|FROMMEMBER|$1|a|$8|BYRADIUS|$1|5|$2|km|",
                "*9|$9|GEOSEARCH|$1|k|$10|FROMLONLAT|$1|1|$1|2|$5|BYBOX|$1|4|$1|3|$1|m|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task SearchWritesItsOptionsInReplyOrder()
    {
        var (ctx, exec) = Target();

        using (await ctx.Geospatial.Search(
            "k",
            "a",
            new GeoSearchCircle(5),
            count: 3,
            order: Order.Descending,
            options: GeoRadiusOptions.Default))
        {
        }

        // WITHCOORD, WITHDIST, WITHHASH is the order the reply comes back in, and the reader depends on
        // it - so the writer states it rather than following the flag values
        Assert.Equal(
            "*12|$9|GEOSEARCH|$1|k|$10|FROMMEMBER|$1|a|$8|BYRADIUS|$1|5|$1|m|$4|DESC|$5|COUNT|$1|3|$9|WITHCOORD|$8|WITHDIST|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AnyOnlyMakesSenseWithACount()
    {
        var (ctx, exec) = Target();

        using (await ctx.Geospatial.Search("k", "a", new GeoSearchCircle(5), count: 2, demandClosest: false, options: GeoRadiusOptions.None)) { }

        Assert.EndsWith("$5|COUNT|$1|2|$3|ANY|", Assert.Single(exec.Sent));

        // and without one the server has nothing to stop at, which the old surface rejects too
        var ex = Assert.Throws<ArgumentException>(
            () => ctx.Geospatial.Search("k", "a", new GeoSearchCircle(5), demandClosest: false));
        Assert.Contains("demandClosest", ex.Message);
    }

    [Fact]
    public async Task StoringNamesTheDestinationFirst()
    {
        var (ctx, exec) = Target(":2\r\n");

        await ctx.Geospatial.SearchAndStore("dst", "k", "a", new GeoSearchCircle(5), storeDistances: true);

        // GEOSEARCHSTORE puts the destination before the source, which is the reverse of the old
        // surface's parameter order
        Assert.Equal(
            "*9|$14|GEOSEARCHSTORE|$3|dst|$1|k|$10|FROMMEMBER|$1|a|$8|BYRADIUS|$1|5|$1|m|$9|STOREDIST|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task RadiusBecomesASearchWhereTheServerHasOne()
    {
        var (bare, exec) = Target();

        using (await Server(bare, 7).Geospatial.Search("k", "a", new GeoSearchCircle(5), options: GeoRadiusOptions.None)) { }
        await Server(bare, 7).Geospatial.RadiusArray("k", "a", double.NaN, double.NaN, 5, GeoUnit.Meters, -1, null, GeoRadiusOptions.None, CommandFlags.None);

        // GEORADIUS is exactly a circular GEOSEARCH, deprecated in its favour since 6.2 - so the old
        // method sends the new command, and the two calls are the same bytes
        Assert.Equal(exec.Sent[0], exec.Sent[1]);
    }

    [Fact]
    public async Task RadiusStaysDeprecatedWhereTheServerIsTooOldOrUnknown()
    {
        var (bare, exec) = Target();

        await Server(bare, 6, 0).Geospatial.RadiusArray("k", "a", double.NaN, double.NaN, 5, GeoUnit.Meters, -1, null, GeoRadiusOptions.None, CommandFlags.None);
        await Server(bare, 6, 0).Geospatial.RadiusArray("k", RedisValue.Null, 1, 2, 5, GeoUnit.Meters, -1, null, GeoRadiusOptions.None, CommandFlags.None);
        await bare.Geospatial.RadiusArray("k", "a", double.NaN, double.NaN, 5, GeoUnit.Meters, -1, null, GeoRadiusOptions.None, CommandFlags.None);

        // and the member form is its own command, not an operand
        Assert.Equal(
            new[]
            {
                "*5|$17|GEORADIUSBYMEMBER|$1|k|$1|a|$1|5|$1|m|",
                "*6|$9|GEORADIUS|$1|k|$1|1|$1|2|$1|5|$1|m|",
                "*5|$17|GEORADIUSBYMEMBER|$1|k|$1|a|$1|5|$1|m|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task ATypedRadiusIsAPureQuery()
    {
        var (bare, exec) = Target();

        await Server(bare, 6, 0).Geospatial.RadiusArray("k", "a", double.NaN, double.NaN, 5, GeoUnit.Meters, -1, null, GeoRadiusOptions.None, CommandFlags.None);

        // GEORADIUS defaults to a write category because STORE/STOREDIST exist and a raw Execute could be
        // using them; this path never emits either, so it says so - and has to say so BEFORE the table is
        // consulted, because the category is first-wins
        Assert.Equal(CommandFlags.CommandRetryReadOnly, Assert.Single(exec.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task SearchIsAReadAndStoringIsAWrite()
    {
        var (ctx, exec) = Target("*0\r\n", ":0\r\n");

        using (await ctx.Geospatial.Search("k", "a", new GeoSearchCircle(5))) { }
        await ctx.Geospatial.SearchAndStore("dst", "k", "a", new GeoSearchCircle(5));

        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, exec.Flags[1] & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task ResultsAreReadAccordingToTheOptionsAskedFor()
    {
        // one result with every extra: [member, distance, hash, [lon, lat]]
        var (ctx, _) = Target("*1\r\n*4\r\n$1\r\na\r\n$5\r\n123.4\r\n:42\r\n*2\r\n$1\r\n1\r\n$1\r\n2\r\n");

        using var results = await ctx.Geospatial.Search(
            "k",
            "a",
            new GeoSearchCircle(5),
            options: GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash | GeoRadiusOptions.WithCoordinates);

        var only = Assert.Single(results.Span.ToArray());
        Assert.Equal("a", only.Member);
        Assert.Equal(123.4, only.Distance);
        Assert.Equal(42, only.Hash);
        Assert.Equal(new GeoPosition(1, 2), only.Position);
    }

    [Fact]
    public async Task WithNoOptionsTheReplyIsAFlatArrayOfMembers()
    {
        var (ctx, _) = Target("*2\r\n$1\r\na\r\n$1\r\nb\r\n");

        using var results = await ctx.Geospatial.Search("k", "a", new GeoSearchCircle(5), options: GeoRadiusOptions.None);

        // the shape changes with the options, which is why the handler has to know them
        Assert.Equal(new RedisValue[] { "a", "b" }, System.Array.ConvertAll(results.Span.ToArray(), x => x.Member));
    }
}
