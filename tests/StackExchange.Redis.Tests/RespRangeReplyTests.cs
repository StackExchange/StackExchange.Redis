using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// <see cref="Streams.RespRangeReply"/>: an <c>XRANGE</c> reply read as windows over its own buffer.
/// </summary>
/// <remarks>
/// Two claims are worth pinning here above all others. <b>The deferred view and the array shape agree</b>,
/// because they are two call sites of one parse rather than two parses - so a drift between them would be
/// a bug the old test suite could not see. And <b>the windows die with the reply</b>, which is the whole of
/// the lifetime contract and the one thing a caller can get wrong silently.
/// </remarks>
public class RespRangeReplyTests
{
    private sealed class FakeExecutor(params string[] replies) : IRespExecutor
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

    /// <summary>Two entries, two fields each - the nested shape the whole exercise is about.</summary>
    private const string TwoEntries =
        "*2|" +
          "*2|$3|1-1|*4|$1|f|$1|v|$1|g|$1|w|" +
          "*2|$3|2-2|*2|$4|name|$5|value|";

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? [Wire(TwoEntries)] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    private static string Wire(string s) => s.Replace("|", "\r\n");

    private static async Task<Streams.RespRangeReply> RangeAsync(string reply = TwoEntries)
    {
        var (ctx, _) = Target(Wire(reply));
        return await ctx.Streams.RangeAsync("s");
    }

    [Fact]
    public async Task TheCommandRenders()
    {
        var (ctx, exec) = Target();

        (await ctx.Streams.RangeAsync("s")).Dispose();
        (await ctx.Streams.RangeAsync("s", "1-1", "9-9")).Dispose();
        (await ctx.Streams.RangeAsync("s", count: 10)).Dispose();
        (await ctx.Streams.RangeAsync("s", "1-1", "9-9", 10, Order.Descending)).Dispose();

        Assert.Equal(
            new[]
            {
                "*4|$6|XRANGE|$1|s|$1|-|$1|+|",                    // the default bounds
                "*4|$6|XRANGE|$1|s|$3|1-1|$3|9-9|",
                "*6|$6|XRANGE|$1|s|$1|-|$1|+|$5|COUNT|$2|10|",     // COUNT only when asked for
                "*6|$9|XREVRANGE|$1|s|$3|9-9|$3|1-1|$5|COUNT|$2|10|", // descending swaps the bounds
            },
            exec.Sent);
    }

    /// <summary>An absent count writes nothing at all - not an empty argument.</summary>
    /// <remarks>
    /// The argument count in the header is the assertion that matters: a conditional
    /// <see cref="RedisValue"/> hole would keep the frame well-formed while making it a different request,
    /// which nothing would catch at run time.
    /// </remarks>
    [Fact]
    public async Task AnAbsentCountWritesNothing()
    {
        var (ctx, exec) = Target();
        (await ctx.Streams.RangeAsync("s")).Dispose();

        Assert.StartsWith("*4|", exec.Sent[0]);
        Assert.DoesNotContain("COUNT", exec.Sent[0]);
    }

    [Fact]
    public async Task ACountMustBePositive()
    {
        var (ctx, _) = Target();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => (await ctx.Streams.RangeAsync("s", count: 0)).Dispose());
    }

    [Fact]
    public async Task WalksEntriesAndFields()
    {
        using var reply = await RangeAsync();

        Assert.Equal(2, reply.Count);
        Assert.Equal(2, reply.Entries.Count);

        var seen = new List<string>();
        foreach (var entry in reply.Entries)
        {
            var fields = new List<string>();
            foreach (var field in entry.Fields) fields.Add($"{field.Name}={field.Value}");
            seen.Add($"{entry.Id}[{string.Join(",", fields)}]");
        }

        Assert.Equal(["1-1[f=v,g=w]", "2-2[name=value]"], seen);
    }

    /// <summary>
    /// Walking twice gives the same answer: a window carries no parse state, it re-reads the buffer.
    /// </summary>
    [Fact]
    public async Task CanBeWalkedMoreThanOnce()
    {
        using var reply = await RangeAsync();

        static List<string> Ids(Streams.RespRangeReply reply)
        {
            var ids = new List<string>();
            foreach (var entry in reply.Entries) ids.Add(entry.Id.ToString());
            return ids;
        }

        Assert.Equal(Ids(reply), Ids(reply));
    }

    /// <summary>
    /// The deferred view and the array shape are two call sites of <b>one</b> parse, so they cannot drift.
    /// </summary>
    [Fact]
    public async Task ToArrayMatchesTheWalk()
    {
        using var reply = await RangeAsync();

        var array = reply.ToArray();
        Assert.Equal(2, array.Length);

        var walked = new List<StreamEntry>();
        foreach (var entry in reply.Entries) walked.Add(entry.ToStreamEntry());

        for (var i = 0; i < array.Length; i++)
        {
            Assert.Equal(array[i].Id, walked[i].Id);
            Assert.Equal(
                array[i].Values.Select(v => $"{v.Name}={v.Value}"),
                walked[i].Values.Select(v => $"{v.Name}={v.Value}"));
        }
    }

    /// <summary>The old surface's promise, served by materialising the new shape.</summary>
    [Fact]
    public async Task ToArrayIsTheOldShape()
    {
        using var reply = await RangeAsync();
        var entries = reply.ToArray();

        Assert.Equal("1-1", entries[0].Id);
        Assert.Equal(2, entries[0].Values.Length);
        Assert.Equal("f", entries[0].Values[0].Name);
        Assert.Equal("v", entries[0].Values[0].Value);
        Assert.Equal("2-2", entries[1].Id);
        Assert.Equal("name", entries[1].Values[0].Name);
        Assert.Equal("value", entries[1].Values[0].Value);
    }

    /// <summary>An empty reply is empty, not null, and walking it yields nothing.</summary>
    [Fact]
    public async Task AnEmptyRangeIsEmpty()
    {
        using var reply = await RangeAsync("*0|");

        Assert.Equal(0, reply.Count);
        Assert.Empty(reply.ToArray());
        foreach (var entry in reply.Entries) Assert.Fail("no entries expected");
    }

    /// <summary>A nil reply reads as empty, as every other aggregate on this surface does.</summary>
    [Fact]
    public async Task ANilRangeIsEmpty()
    {
        using var reply = await RangeAsync("*-1|");

        Assert.Equal(0, reply.Count);
        Assert.Empty(reply.ToArray());
    }

    /// <summary>The <c>XREADGROUP</c>-with-<c>CLAIM</c> extras, when the server sends them.</summary>
    [Fact]
    public async Task TheClaimExtrasAreRead()
    {
        using var reply = await RangeAsync("*1|*4|$3|1-1|*2|$1|f|$1|v|:1500|:3|");

        foreach (var entry in reply.Entries)
        {
            Assert.Equal("1-1", entry.Id.ToString());
            Assert.Equal(TimeSpan.FromMilliseconds(1500), entry.IdleTime);
            Assert.Equal(3, entry.DeliveryCount);
        }
    }

    /// <summary>Without the extras, the entry reports what the old shape reports: no idle time, zero deliveries.</summary>
    [Fact]
    public async Task TheClaimExtrasAreAbsentWhenNotSent()
    {
        using var reply = await RangeAsync();

        foreach (var entry in reply.Entries)
        {
            Assert.Null(entry.IdleTime);
            Assert.Equal(0, entry.DeliveryCount);
        }
    }

    /// <summary>
    /// A field list of scalars is never mistaken for jagged pairs, whatever the protocol.
    /// </summary>
    /// <remarks>
    /// Worth pinning because the deferred path decides jaggedness from the <b>content</b> and always
    /// permits it, where the eager path gates on the protocol version. That is only safe because a stream
    /// entry's fields are scalars, so they can never look jagged - this is the test that says so.
    /// </remarks>
    [Fact]
    public async Task ScalarFieldsAreNotJagged()
    {
        using var reply = await RangeAsync();

        foreach (var entry in reply.Entries)
        {
            Assert.False(entry.Fields.IsJagged);
        }
    }

    /// <summary>
    /// The jagged policy on the stream path, which nothing pinned before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by mutation while threading policy rather than protocol through these parses: making
    /// <c>AllowJaggedStreamFields</c> return <see langword="false"/> unconditionally broke <b>no</b> test,
    /// because no stream test has ever fed a jagged field list. The policy was real code with no evidence
    /// behind it.
    /// </para>
    /// <para>
    /// What it decides: permitted, <c>[[f,v],[g,w]]</c> is two fields; refused, the parse asks the reader
    /// for a scalar, is handed an array, and <b>throws</b>. So the flag is not a tidying preference -
    /// refusing it is the difference between reading the reply and failing on it. Which also settles the
    /// deferred path's unconditional <c>true</c>: permitting jagged can never turn a working parse into a
    /// differently-valued one, because the alternative was not a different value but an exception.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheJaggedPolicyDecidesWhetherAFieldListCanBeReadAtAll()
    {
        static NameValueEntry[] Parse(RedisProtocol protocol)
        {
            var frame = Encoding.UTF8.GetBytes(Wire("*2|*2|$1|f|$1|v|*2|$1|g|$1|w|"));
            var reader = new RespReader(frame);
            reader.MoveNext();
            return ResultProcessor.ParseStreamEntryValues(ref reader, ResultProcessor.AllowJaggedStreamFields(protocol));
        }

        var permitted = Parse(RedisProtocol.Resp3);
        Assert.Equal(2, permitted.Length);
        Assert.Equal("f", permitted[0].Name);
        Assert.Equal("w", permitted[1].Value);

        Assert.Throws<InvalidOperationException>(() => Parse(RedisProtocol.Resp2));
    }

    /// <summary>
    /// A scalar field list - what a real server actually sends - reads the same either way.
    /// </summary>
    /// <remarks>
    /// This is what makes the deferred path's unconditional <c>allowJaggedFields: true</c> safe despite
    /// the eager path gating on protocol: the policy can only bite on a shape <c>XADD</c> cannot produce.
    /// </remarks>
    [Theory]
    [InlineData(RedisProtocol.Resp3)]
    [InlineData(RedisProtocol.Resp2)]
    public void TheJaggedPolicyCannotAffectARealFieldList(RedisProtocol protocol)
    {
        var frame = Encoding.UTF8.GetBytes(Wire("*4|$1|f|$1|v|$1|g|$1|w|"));
        var reader = new RespReader(frame);
        reader.MoveNext();

        var fields = ResultProcessor.ParseStreamEntryValues(ref reader, ResultProcessor.AllowJaggedStreamFields(protocol));

        Assert.Equal(2, fields.Length);
        Assert.Equal("f", fields[0].Name);
        Assert.Equal("w", fields[1].Value);
    }

    /// <summary>Every route into the reply dies with it - the whole of the lifetime contract.</summary>
    /// <remarks>
    /// The windows themselves are structs and outlive the reply as <i>values</i>; what stops them being
    /// read is that the buffer they point at has gone. So the sweep covers both the reply's own accessors
    /// and a window extracted before disposal.
    /// </remarks>
    [Fact]
    public async Task EveryAccessorDiesWithTheReply()
    {
        var reply = await RangeAsync();

        RespValue escapedId = default;
        RespAggregate<Streams.RespStreamEntry> escapedEntries = reply.Entries;
        foreach (var entry in reply.Entries)
        {
            escapedId = entry.Id;
            break;
        }

        Assert.False(reply.IsDisposed);
        reply.Dispose();
        Assert.True(reply.IsDisposed);

        Assert.Throws<ObjectDisposedException>(() => reply.Entries);
        Assert.Throws<ObjectDisposedException>(() => reply.GetReader());
        Assert.Throws<ObjectDisposedException>(() => reply.ToArray());
        Assert.Throws<ObjectDisposedException>(() => escapedId.AsRedisValue());
        Assert.Throws<ObjectDisposedException>(() =>
        {
            foreach (var entry in escapedEntries) { }
        });
    }

    /// <summary>
    /// Count survives disposal, because it was read from the header rather than from the buffer.
    /// </summary>
    /// <remarks>
    /// Not an oversight: it is the same arrangement <c>RespAggregate&lt;T&gt;.ToString</c> relies on, and
    /// it is what lets a disposed reply still describe itself in a debugger.
    /// </remarks>
    [Fact]
    public async Task CountAndToStringSurviveDisposal()
    {
        var reply = await RangeAsync();
        reply.Dispose();

        Assert.Equal(2, reply.Count);
        Assert.Equal("(disposed)", reply.ToString());
    }

    /// <summary>Disposing twice gives the buffer back once.</summary>
    /// <remarks>
    /// The failure this prevents is invisible from outside until somebody else's data turns up in your
    /// reply, so it is pinned rather than assumed.
    /// </remarks>
    [Fact]
    public async Task DisposingTwiceIsSafe()
    {
        var reply = await RangeAsync();

        reply.Dispose();
        reply.Dispose();
        reply.Dispose();

        Assert.True(reply.IsDisposed);
    }

    /// <summary>A derived reply's own cleanup runs once, before the buffer goes back.</summary>
    private sealed class CountingReply(RespPayload payload) : RespReply(payload)
    {
        public int Disposals { get; private set; }

        protected override void OnDisposed() => Disposals++;
    }

    /// <summary>
    /// The extension point works from outside: derive, take the payload, read it, and let the base give it
    /// back.
    /// </summary>
    [Fact]
    public void ADerivedReplyCanBeBuiltAndDisposed()
    {
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes(Wire("*2|$1|a|$1|b|")));
        var reply = new CountingReply(payload);

        var reader = reply.GetReader();
        reader.MoveNext();
        Assert.Equal(2, reader.AggregateLength());

        reply.Dispose();
        reply.Dispose();

        Assert.Equal(1, reply.Disposals); // once, however many times Dispose is called
        Assert.True(reply.IsDisposed);
    }

    [Fact]
    public void AReplyDemandsAPayload()
        => Assert.Throws<ArgumentNullException>(() => new CountingReply(null!));
}
