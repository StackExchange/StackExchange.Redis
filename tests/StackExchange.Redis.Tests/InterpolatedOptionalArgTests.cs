using System;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Optional arguments as holes: <c>Expiration</c> and <c>ValueCondition</c> render between zero and three
/// tokens each, so <c>$"{cmd}{key}{value}{when}{expiry}"</c> covers the whole of SET without a branch.
/// </summary>
/// <remarks>
/// These are the first argument types whose token count is not one, and not known until run time. The
/// invariant that matters is that the count they advertise (<c>TokenCount</c>/<c>GetTokenCount</c>) is the
/// count they actually write - a disagreement corrupts the <c>*N</c> header and desynchronises the whole
/// connection, not just the one command.
/// </remarks>
public class InterpolatedOptionalArgTests
{
    private static readonly RespContext Ctx = new();

    private static string Text(in RespRequestFrame frame) =>
        Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    /// <summary>The interpolated writer's rendering of a full SET.</summary>
    private static RespRequestFrame ViaHandler(RedisKey key, RedisValue value, ValueCondition when, Expiration expiry)
    {
        var cmd = Ctx.Compose($"{RedisCommand.SET}{key}{value}{when}{expiry}");
        return cmd.Complete();
    }

    public static TheoryData<string, ValueCondition, Expiration, string> Cases() => new()
    {
        // name                       condition                          expiry                                  expected tail
        { "bare",                     ValueCondition.Always,             Expiration.Default,                     "" },
        { "nx",                       ValueCondition.NotExists,          Expiration.Default,                     "$2|NX|" },
        { "xx",                       ValueCondition.Exists,             Expiration.Default,                     "$2|XX|" },
        { "ifeq",                     ValueCondition.Equal("old"),       Expiration.Default,                     "$4|IFEQ|$3|old|" },
        { "ifne",                     ValueCondition.NotEqual("old"),    Expiration.Default,                     "$4|IFNE|$3|old|" },
        { "ex",                       ValueCondition.Always,             new Expiration(TimeSpan.FromSeconds(300)),      "$2|EX|$3|300|" },
        { "px",                       ValueCondition.Always,             new Expiration(TimeSpan.FromMilliseconds(1500)), "$2|PX|$4|1500|" },
        { "keepttl",                  ValueCondition.Always,             Expiration.KeepTtl,                     "$7|KEEPTTL|" },
        { "persist",                  ValueCondition.Always,             Expiration.Persist,                     "$7|PERSIST|" },
        { "enx",                      ValueCondition.Always,             new Expiration(TimeSpan.FromSeconds(60), ExpirationFlags.ExpireIfNotExists), "$2|EX|$2|60|$3|ENX|" },
        { "nx+ex",                    ValueCondition.NotExists,          new Expiration(TimeSpan.FromSeconds(300)),      "$2|NX|$2|EX|$3|300|" },
        { "ifeq+keepttl",             ValueCondition.Equal("old"),       Expiration.KeepTtl,                     "$4|IFEQ|$3|old|$7|KEEPTTL|" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheFullSetRendersInTheDocumentedOrder(string name, ValueCondition when, Expiration expiry, string tail)
    {
        _ = name;
        using var frame = ViaHandler("k", "v", when, expiry);
        Assert.Equal("*" + frame.ArgCount + "|$3|SET|$1|k|$1|v|" + tail, Text(frame));
    }

    // BothWritersAgreeByteForByte is gone with the legacy MessageWriter it compared against: there is now
    // one writer, and TheFullSetRendersInTheDocumentedOrder pins its bytes directly.

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheAdvertisedTokenCountIsTheCountActuallyWritten(string name, ValueCondition when, Expiration expiry, string tail)
    {
        _ = (name, tail);

        // if these disagree the *N header is a lie, and the NEXT command on the connection is misframed -
        // so this is the invariant worth pinning, not the rendering
        using var frame = ViaHandler("k", "v", when, expiry);
        Assert.Equal(3 + when.TokenCount + expiry.GetTokenCount(allowEnx: true), frame.ArgCount);
    }

    [Fact]
    public void DefaultsContributeNothingAtAll()
    {
        // the property that lets the optional parts be holes rather than branches: an absent argument is
        // not an empty argument, it is no argument
        using var frame = ViaHandler("k", "v", default, default);
        Assert.Equal("*3|$3|SET|$1|k|$1|v|", Text(frame));
        Assert.Equal(3, frame.ArgCount);
    }

    [Fact]
    public void TheKeyIsStillMarkedAfterVariableLengthTails()
    {
        // key marking is by ARGUMENT INDEX, so a multi-token optional argument that miscounted would shift
        // every mark after it; the key is before the tail here, but the count still has to survive
        using var frame = ViaHandler("k", "v", ValueCondition.Equal("old"), new Expiration(TimeSpan.FromSeconds(300)));

        Assert.Equal(1, frame.KeyCount);
        var ranges = new KeyRange[1];
        Assert.Equal(1, frame.TryGetKeys(ranges));
        Assert.Equal("k", Encoding.UTF8.GetString(frame.GetKey(ranges[0]).ToArray()));
    }

    [Fact]
    public void ADigestConditionIsSentAsHexNotAsTheInt64()
    {
        var digest = ValueCondition.DigestEqual("some value");
        using var frame = ViaHandler("k", "v", digest, Expiration.Default);

        // 8 bytes of XXH3, hex-encoded; ToString formats the same digest through the char-based hex writer,
        // which stands in for the byte-parity check against the legacy writer that this used to make
        var hex = digest.ToString().Substring("IFDEQ ".Length);
        Assert.Equal(16, hex.Length);
        Assert.Equal("*5|$3|SET|$1|k|$1|v|$5|IFDEQ|$16|" + hex + "|", Text(frame));
    }
}
