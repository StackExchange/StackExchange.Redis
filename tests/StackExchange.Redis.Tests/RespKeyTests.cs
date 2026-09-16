using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="RespKey"/>: a key the writer borrows rather than owns.
/// </summary>
/// <remarks>
/// The claim under test is that a borrowed key is treated as a key in <b>every</b> respect an owned one
/// is - same bytes, same prefix, same cluster slot - because anything less would be a silent failure: the
/// frame stays well-formed and the server accepts it, while key-prefix isolation is broken and the
/// command routes to the wrong node.
/// </remarks>
public class RespKeyTests
{
    private static string Text(in RespRequestFrame frame)
        => Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    /// <summary>All three sources produce the same bytes as the owned key they stand in for.</summary>
    [Theory]
    [InlineData("k")]
    [InlineData("some:longer:key")]
    [InlineData("")] // a legal Redis key, and the reason emptiness cannot be the discriminator
    public void EverySourceMatchesTheOwnedKey(string key)
    {
        var ctx = new RespContext();
        var utf8 = Encoding.UTF8.GetBytes(key);

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");
        using var fromChars = ctx.Render($"{RedisCommand.GET}{new RespKey(key.AsSpan())}");
        using var fromBytes = ctx.Render($"{RedisCommand.GET}{new RespKey(utf8.AsSpan())}");
        using var fromMemory = ctx.Render($"{RedisCommand.GET}{new RespKey(utf8.AsMemory())}");

        var expected = Text(owned);
        Assert.Equal(expected, Text(fromChars));
        Assert.Equal(expected, Text(fromBytes));
        Assert.Equal(expected, Text(fromMemory));
    }

    /// <summary>
    /// The <see cref="string"/> constructor exists so the obvious spelling works on <b>every</b> target.
    /// </summary>
    /// <remarks>
    /// This test earns its keep on net481 rather than net10: the implicit conversion from
    /// <see cref="string"/> to <c>ReadOnlySpan&lt;char&gt;</c> arrived in netstandard2.1, so without an
    /// explicit constructor <c>new RespKey("k")</c> compiles on the newer targets and fails on the older
    /// ones. The test project multi-targets, so this failing to compile IS the assertion.
    /// </remarks>
    [Fact]
    public void AStringIsAKeyOnEveryTarget()
    {
        var ctx = new RespContext();

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{new RespKey("k")}");

        Assert.Equal(Text(owned), Text(borrowed));
    }

    /// <summary>A null string is a null key, and matches the owned path.</summary>
    /// <remarks>
    /// The three-state discriminator exists so this need not choose between refusing null and silently
    /// writing to <c>""</c> - a key that is shared, legal, and almost never what a null variable meant.
    /// </remarks>
    [Fact]
    public void ANullStringIsANullKey()
    {
        var ctx = new RespContext();
        string? nothing = null;

        Assert.True(new RespKey(nothing).IsNull);

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)nothing!}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{new RespKey(nothing)}");
        Assert.Equal(Text(owned), Text(borrowed));
    }

    /// <summary>
    /// <c>default(RespKey)</c> is null, as <c>default(RedisKey)</c> is - which a two-state discriminator
    /// could not have managed.
    /// </summary>
    /// <remarks>
    /// With a bool the default would have been an <i>empty</i> key, disagreeing with the owned type for
    /// no reason anyone would ever guess. Pinned because it is the sort of asymmetry that only shows up
    /// in somebody else's bug report.
    /// </remarks>
    [Fact]
    public void TheDefaultIsNullJustAsRedisKeysIs()
    {
        var ctx = new RespContext();

        Assert.True(default(RespKey).IsNull);
        Assert.True(default(RedisKey).IsNull);

        using var owned = ctx.Render($"{RedisCommand.GET}{default(RedisKey)}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{default(RespKey)}");
        Assert.Equal(Text(owned), Text(borrowed));
    }

    /// <summary>An empty key is NOT a null one, though they happen to render alike.</summary>
    /// <remarks>
    /// The distinction is the whole reason emptiness cannot serve as the discriminator: an empty key is
    /// legal in Redis, so "no bytes" cannot be allowed to mean "no key".
    /// </remarks>
    [Fact]
    public void AnEmptyKeyIsNotANullKey()
    {
        Assert.False(new RespKey("").IsNull);
        Assert.False(new RespKey(ReadOnlySpan<byte>.Empty).IsNull);
        Assert.True(new RespKey((string?)null).IsNull);
    }

    /// <summary>Non-ASCII survives the UTF-16 reinterpretation and the encode.</summary>
    /// <remarks>
    /// The char source is stored as its UTF-16 code units reinterpreted as bytes, then encoded to UTF-8
    /// on the way out - so anything that is not one byte per char is where a mistake would show.
    /// </remarks>
    [Theory]
    [InlineData("naïve")]
    [InlineData("键")]
    [InlineData("emoji:\U0001F600")] // a surrogate pair
    public void NonAsciiRoundTrips(string key)
    {
        var ctx = new RespContext();

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)key}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{new RespKey(key.AsSpan())}");

        Assert.Equal(Text(owned), Text(borrowed));
    }

    /// <summary>A borrowed key takes the context's key prefix, as an owned one does.</summary>
    [Fact]
    public void TheKeyPrefixIsApplied()
    {
        var ctx = new RespContext().AppendKeyPrefix("tenant:");

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{new RespKey("k".AsSpan())}");

        using var asValue = ctx.Render($"{RedisCommand.GET}{(RedisValue)"k"}");

        Assert.Equal("*2|$3|GET|$8|tenant:k|", Text(owned));
        Assert.Equal(Text(owned), Text(borrowed));

        // and the discriminating fact: a VALUE hole gets no prefix, so being treated as a key is
        // observable rather than assumed
        Assert.Equal("*2|$3|GET|$1|k|", Text(asValue));
    }

    /// <summary>
    /// It folds into the cluster slot, which is what makes it route like a key rather than a value.
    /// </summary>
    [Fact]
    public void TheClusterSlotIsFolded()
    {
        // slot folding is skipped entirely off-cluster, so the context has to say it is one - otherwise
        // every Slot is -1 and the assertion passes without testing anything
        var ctx = new RespContext(serverType: ServerType.Cluster);

        using var owned = ctx.Render($"{RedisCommand.GET}{(RedisKey)"{hash}:field"}");
        using var borrowed = ctx.Render($"{RedisCommand.GET}{new RespKey("{hash}:field".AsSpan())}");
        using var asValue = ctx.Render($"{RedisCommand.GET}{(RedisValue)"{hash}:field"}");

        Assert.NotEqual(-1, owned.Slot);              // the test would be vacuous otherwise
        Assert.Equal(owned.Slot, borrowed.Slot);

        // and the point of the type: a value hole folds no slot at all
        Assert.Equal(-1, asValue.Slot);
    }

    /// <summary>
    /// A <b>dynamic</b> argument list takes borrowed keys too, via the composed builder.
    /// </summary>
    /// <remarks>
    /// I had claimed a run-time-sized argument list forced the array form - where a ref struct cannot go,
    /// since it cannot live in a <see cref="ReadOnlyMemory{T}"/> - and therefore that spans could not help
    /// a caller building N arguments. Wrong: <c>Compose</c>/<c>Append</c> builds in place, and every
    /// <c>Append</c> is an interpolated hole that consumes immediately, so a borrowed key is as welcome
    /// there as anywhere. The real line is <i>build in place</i> versus <i>hand over a collection</i>, and
    /// only the latter excludes spans.
    /// </remarks>
    [Fact]
    public void ADynamicArgumentListTakesBorrowedKeys()
    {
        var ctx = new RespContext();
        string[] keys = ["a", "bb", "ccc"];

        var cmd = ctx.Compose(RedisCommand.DEL, argHint: 1 + keys.Length);
        try
        {
            foreach (var key in keys)
            {
                cmd.Append($"{new RespKey(key.AsSpan())}");
            }
        }
        catch
        {
            cmd.Dispose();
            throw;
        }

        using var frame = cmd.Complete();
        Assert.Equal("*4|$3|DEL|$1|a|$2|bb|$3|ccc|", Text(frame));
        Assert.Equal(4, frame.ArgCount);
    }

    /// <summary>The argument count is unaffected: one key is one argument, however it was sourced.</summary>
    [Fact]
    public void OneKeyIsOneArgument()
    {
        var ctx = new RespContext();
        using var frame = ctx.Render($"{RedisCommand.MSET}{new RespKey("a".AsSpan())}{(RedisValue)"1"}");

        Assert.Equal(3, frame.ArgCount);
    }
}
