using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Caching;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The ad-hoc escape hatch: run a command this library does not model, get the raw reply back.
/// </summary>
/// <remarks>
/// <para>
/// <b>This path is internal, and that is the point.</b> It exists to satisfy
/// <see cref="IDatabase.ExecuteResp(string, System.ReadOnlyMemory{RedisKeyOrValue}, CommandFlags)" /> and
/// the other shipped signatures that hand over a pre-built argument collection. New code does not need
/// one: <c>db.SendAsync&lt;RedisValue&gt;($"JSON.GET {key} {path}")</c> writes the same request with
/// nothing to allocate and nothing to wrap, so exposing the collection form on the new surface would have
/// been offering the worse of the two.
/// </para>
/// <para>
/// The argument type is still the interesting part - see <see cref="ArgumentsKeepTheirKeyNess"/>.
/// </para>
/// </remarks>
public class RespAdHocExecuteTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        internal System.Collections.Generic.List<string> Sent { get; } = [];

        /// <summary>The keys the writer marked, captured at send time - the request is recycled after.</summary>
        internal System.Collections.Generic.List<string> Keys { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent.Add(Encoding.UTF8.GetString(request.Span.ToArray()).Replace("\r\n", "|"));

            var count = request.KeyCount;
            if (count > 0)
            {
                var ranges = new KeyRange[count];
                if (request.TryGetKeys(ranges) == count)
                {
                    foreach (var range in ranges) Keys.Add(Encoding.UTF8.GetString(request.GetKey(range).ToArray()));
                }
            }

            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>The same name the collection form passes as a string, prepared once.</summary>
    /// <remarks>
    /// SER309 is what pushes this out of the interpolation - a literal there is tokenized and encoded on
    /// every call, and the analyzer says so rather than letting the slower spelling look equivalent.
    /// </remarks>
    private static readonly RespCommand SomeCommand = "SOME.COMMAND".Command(preform: true);

    private static RespDatabaseContext Context(FakeExecutor executor, RespClientCache? cache = null)
        => new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

    [Fact]
    public async Task AnUnmodelledCommandRoundTrips()
    {
        var executor = new FakeExecutor("*2\r\n$3\r\ndoc\r\n:1\r\n");
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("idx"), RedisKeyOrValue.FromValue("@title:hello")];

        using var result = await Context(executor).Raw.ExecuteAsync("FT.SEARCH", args);

        Assert.Equal("*3|$9|FT.SEARCH|$3|idx|$12|@title:hello|", Assert.Single(executor.Sent));
        Assert.Equal(RespPrefix.Array, result.Prefix);
    }

    [Fact]
    public async Task ArgumentsKeepTheirKeyNess()
    {
        // the reason for RedisKeyOrValue rather than object[]: boxing loses key-ness, and with it routing,
        // invalidation, and any chance of caching an ad-hoc command correctly
        var executor = new FakeExecutor("+OK\r\n");
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("thekey"), RedisKeyOrValue.FromValue("thevalue")];

        using var _ = await Context(executor).Raw.ExecuteAsync("JSON.SET", args);

        // exactly one of the two arguments was marked as a key, and it was the right one
        Assert.Equal("thekey", Assert.Single(executor.Keys));
    }

    /// <summary>The collection form and the interpolated form render the same bytes.</summary>
    /// <remarks>
    /// Which is what makes dropping the collection form from the public surface a simplification rather
    /// than a loss: the caller who would have built the array writes the holes instead.
    /// </remarks>
    [Fact]
    public async Task TheInterpolatedFormRendersTheSameRequest()
    {
        var viaCollection = new FakeExecutor("$3\r\nabc\r\n");
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("k"), RedisKeyOrValue.FromValue("x")];
        (await Context(viaCollection).Raw.ExecuteAsync("SOME.COMMAND", args)).Dispose();

        var viaHoles = new FakeExecutor("$3\r\nabc\r\n");
        RedisKey key = "k";
        var result = await Context(viaHoles).SendAsync<string?>($"{SomeCommand}{key}{"x"}");

        Assert.Equal(Assert.Single(viaCollection.Sent), Assert.Single(viaHoles.Sent));
        Assert.Equal(Assert.Single(viaCollection.Keys), Assert.Single(viaHoles.Keys)); // and key-ness survives both
        Assert.Equal("abc", result);
    }

    [Fact]
    public async Task AnAdHocReadCanBeCachedAndInvalidated()
    {
        // the payoff of keeping key-ness: an unmodelled command participates in the cache like any other
        using var cache = new RespClientCache();
        var executor = new FakeExecutor("$3\r\nabc\r\n", "$3\r\nxyz\r\n");
        var context = Context(executor, cache);
        RedisKeyOrValue[] args = [RedisKeyOrValue.FromKey("k")];

        (await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly)).Dispose();
        (await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly)).Dispose();
        Assert.Single(executor.Sent);   // served from cache

        Assert.True(cache.OnInvalidate(Encoding.UTF8.GetBytes("k")));

        using var fresh = await context.Raw.ExecuteAsync("MODULE.GET", args, CommandFlags.CommandRetryReadOnly);
        Assert.Equal(2, executor.Sent.Count);   // invalidated by key, and re-fetched
        Assert.Equal("xyz", fresh.ReadScalar().ReadString());
    }

    [Fact]
    public async Task TheLegacyInterfaceReachesTheSamePath()
    {
        // ExecuteResp's signature and the context method agree exactly, RedisKeyOrValue included - so the
        // adapter is a pass-through, and an IDatabase caller gets the key-marking behaviour for free
        var executor = new FakeExecutor("$3\r\nabc\r\n");
        IDatabase db = Context(executor).AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        using var result = await db.ExecuteRespAsync("MODULE.GET", new[] { RedisKeyOrValue.FromKey("k") });

        Assert.Equal("abc", result.ReadScalar().ReadString());
        Assert.Equal("k", Assert.Single(executor.Keys));
    }

    [Fact]
    public async Task NoArgumentsIsFine()
    {
        var executor = new FakeExecutor("+PONG\r\n");
        using var result = await Context(executor).Raw.ExecuteAsync("PING", default);

        Assert.Equal("*1|$4|PING|", Assert.Single(executor.Sent));
        Assert.Equal("PONG", result.ReadScalar().ReadString());
    }

    // ---- the object-argument Execute ---------------------------------------------------------------

    private static IDatabase LegacyDatabase(FakeExecutor executor)
        => Context(executor).AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

    /// <summary>
    /// The three-way argument branch survives the move: keys, channels and values each take their own
    /// path.
    /// </summary>
    /// <remarks>
    /// The shipped <c>ExecuteMessage.WriteImpl</c> tested each argument's runtime type for exactly this,
    /// and it is the one thing an <see cref="object"/> collection can still express. A key that stopped
    /// being written as a key would lose its prefix, its slot and its cache identity - and nothing about
    /// the bytes would look wrong.
    /// </remarks>
    [Fact]
    public async Task LegacyExecuteKeepsKeysChannelsAndValuesApart()
    {
        var executor = new FakeExecutor("$3\r\nabc\r\n");
        var db = LegacyDatabase(executor);

        var result = await db.ExecuteAsync("MODULE.DO", (RedisKey)"k", RedisChannel.Literal("c"), 42);

        Assert.Equal("*4|$9|MODULE.DO|$1|k|$1|c|$2|42|", Assert.Single(executor.Sent));
        Assert.Equal("k", Assert.Single(executor.Keys)); // the key, and only the key
        Assert.Equal("abc", (string?)result);
    }

    /// <summary>Both prefixes reach an ad-hoc command, which is what writing them as key and channel buys.</summary>
    [Fact]
    public async Task LegacyExecuteAppliesBothPrefixes()
    {
        var executor = new FakeExecutor("$3\r\nabc\r\n");
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor))
            .AppendKeyPrefix("app:")
            .AppendChannelPrefix(RedisChannel.Literal("ch:"));
        IDatabase db = context.AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        (await db.ExecuteAsync("MODULE.DO", (RedisKey)"k", RedisChannel.Literal("c"))).ToString();

        Assert.Equal("*3|$9|MODULE.DO|$5|app:k|$4|ch:c|", Assert.Single(executor.Sent));
    }

    /// <summary>A known command still goes through the command map.</summary>
    [Fact]
    public void LegacyExecuteHonoursADisabledCommand()
    {
        var executor = new FakeExecutor("+OK\r\n");
        var context = new RespDatabaseContext(
            new RespContext(CommandMap.Create(new System.Collections.Generic.HashSet<string> { "GET" }, available: false))
                .WithExecutor(executor));
        IDatabase db = context.AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        Assert.Throws<RedisCommandException>(() => db.Execute("GET", (RedisKey)"k"));
        Assert.Empty(executor.Sent);
    }

    /// <summary>
    /// A command name with a space is refused, on this path and on <c>ExecuteResp</c>.
    /// </summary>
    /// <remarks>
    /// The guard used to live on <c>ExecuteMessage</c>, so <c>ExecuteResp</c> - which builds a different
    /// message - let <c>"ACL SETUSER x"</c> through as one unknown token and got an opaque server error.
    /// It moved into the builder's string constructor, which both ad-hoc routes share, so the two now
    /// agree. <c>AdhocMessageRoundTrip.CommandWithWhitespaceThrows</c> pins the classic path.
    /// </remarks>
    [Theory]
    [InlineData("ACL SETUSER x")]
    [InlineData("GET ")]
    public void AWhitespaceCommandIsRefusedOnBothAdHocRoutes(string command)
    {
        var executor = new FakeExecutor("+OK\r\n");
        var context = Context(executor);
        IDatabase db = context.AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        Assert.Contains("whitespace", Assert.Throws<RedisCommandException>(() => db.Execute(command)).Message);
        Assert.Contains("whitespace", Assert.Throws<RedisCommandException>(() => db.ExecuteResp(command, default)).Message);
        Assert.Empty(executor.Sent);
    }
}
