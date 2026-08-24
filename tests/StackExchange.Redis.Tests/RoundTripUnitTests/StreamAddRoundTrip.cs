using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Pins the exact <c>XADD</c> argument order, which the server is strict about:
/// <c>[NOMKSTREAM] [MAXLEN|MINID [~] threshold] [LIMIT n] [KEEPREF|DELREF|ACKED] [IDMP...] &lt;*|id&gt;</c>.
/// </summary>
public class StreamAddRoundTrip(ITestOutputHelper log)
{
    private const string Reply = "$3\r\n1-0\r\n";

    [Fact]
    public Task NoOptions() => AssertPairAsync(
        default,
        "*5\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    [Fact]
    public Task NoMkStream() => AssertPairAsync(
        new() { CreateStream = false },
        "*6\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    /// <summary>NOMKSTREAM comes before the trim options, not after them.</summary>
    [Fact]
    public Task NoMkStreamPrecedesMaxLen() => AssertPairAsync(
        new() { CreateStream = false, MaxLength = 10, Approximate = true, Limit = 5 },
        "*11\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$6\r\nMAXLEN\r\n$1\r\n~\r\n$2\r\n10\r\n$5\r\nLIMIT\r\n$1\r\n5\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    [Fact]
    public Task MaxLenOnly() => AssertPairAsync(
        new() { MaxLength = 10 },
        "*7\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$6\r\nMAXLEN\r\n$2\r\n10\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    [Fact]
    public Task MinIdExact() => AssertPairAsync(
        new() { MinId = "1526919030474-55" },
        "*7\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$5\r\nMINID\r\n$16\r\n1526919030474-55\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    [Fact]
    public Task MinIdApproximateWithLimitAndTrimMode() => AssertPairAsync(
        new() { MinId = "5-5", Approximate = true, Limit = 3, TrimMode = StreamTrimMode.DeleteReferences },
        "*11\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$5\r\nMINID\r\n$1\r\n~\r\n$3\r\n5-5\r\n$5\r\nLIMIT\r\n$1\r\n3\r\n$6\r\nDELREF\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    /// <summary>An explicit id replaces the "*", and everything else keeps its place.</summary>
    [Fact]
    public Task ExplicitMessageId() => AssertPairAsync(
        new() { MessageId = "5-5", CreateStream = false },
        "*6\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$3\r\n5-5\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    /// <summary>The idempotency arguments sit between the trim options and the entry id.</summary>
    [Fact]
    public Task NoMkStreamWithIdempotentId() => AssertPairAsync(
        new() { CreateStream = false, IdempotentId = new("producer", "item-1") },
        "*9\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$4\r\nIDMP\r\n$8\r\nproducer\r\n$6\r\nitem-1\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    [Fact]
    public Task NoMkStreamWithAutoIdempotentIdAndMaxLen() => AssertPairAsync(
        new() { CreateStream = false, IdempotentId = new("producer"), MaxLength = 10 },
        "*10\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$6\r\nMAXLEN\r\n$2\r\n10\r\n$8\r\nIDMPAUTO\r\n$8\r\nproducer\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n");

    /// <summary>The multi-pair builder is a separate code path, and needs its own arithmetic checked.</summary>
    [Fact]
    public async Task PairsArray()
    {
        var db = new RedisDatabase(null!, 0, null);
        NameValueEntry[] pairs = [new("f1", "v1"), new("f2", "v2")];
        var options = new StreamAddOptions { CreateStream = false, MinId = "5-5", Approximate = true };
        var message = db.GetStreamAddMessage("stream", in options, pairs, CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(
            message,
            ResultProcessor.RedisValue,
            "*11\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$5\r\nMINID\r\n$1\r\n~\r\n$3\r\n5-5\r\n$1\r\n*\r\n$2\r\nf1\r\n$2\r\nv1\r\n$2\r\nf2\r\n$2\r\nv2\r\n",
            Reply,
            log: log);
        Assert.Equal("1-0", result);
    }

    /// <summary>With NOMKSTREAM and no stream, the server replies nil - which must surface as a null value.</summary>
    [Fact]
    public async Task NilReplyIsNullValue()
    {
        var db = new RedisDatabase(null!, 0, null);
        var options = new StreamAddOptions { CreateStream = false };
        var message = db.GetStreamAddMessage("stream", in options, new NameValueEntry("field", "value"), CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(
            message,
            ResultProcessor.RedisValue,
            "*6\r\n$4\r\nXADD\r\n$6\r\nstream\r\n$10\r\nNOMKSTREAM\r\n$1\r\n*\r\n$5\r\nfield\r\n$5\r\nvalue\r\n",
            "$-1\r\n",
            log: log);
        Assert.True(result.IsNull);
    }

    [Theory]
    [InlineData(nameof(StreamAddOptions.MaxLength))]
    [InlineData(nameof(StreamAddOptions.MessageId))]
    [InlineData("LimitWithoutThreshold")]
    [InlineData("LimitWithoutApproximate")]
    public void InvalidCombinationsAreRejected(string scenario)
    {
        var db = new RedisDatabase(null!, 0, null);
        var options = scenario switch
        {
            nameof(StreamAddOptions.MaxLength) => new StreamAddOptions { MaxLength = 10, MinId = "5-5" },
            nameof(StreamAddOptions.MessageId) => new StreamAddOptions { MessageId = "5-5", IdempotentId = new("producer") },
            "LimitWithoutThreshold" => new StreamAddOptions { Limit = 5, Approximate = true },
            _ => new StreamAddOptions { MaxLength = 10, Limit = 5 },
        };

        // the validation is on the public entry-points, not the message builder: the shipped positional
        // overloads have always passed odd-but-legal-looking combinations to the server, and still do
        var ex = Assert.Throws<ArgumentException>(() => db.StreamAdd("stream", "field", "value", options));
        log.WriteLine(ex.Message);
        Assert.Equal("options", ex.ParamName);
    }

    private async Task AssertPairAsync(StreamAddOptions options, string requestResp)
    {
        var db = new RedisDatabase(null!, 0, null);
        var message = db.GetStreamAddMessage("stream", in options, new NameValueEntry("field", "value"), CommandFlags.None);

        var result = await TestConnection.ExecuteAsync(message, ResultProcessor.RedisValue, requestResp, Reply, log: log);
        Assert.Equal("1-0", result);
    }
}
