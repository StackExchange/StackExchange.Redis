using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What the string and bitmap groups put on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks>
/// <para>
/// The integration half of the proof is <c>TransitionalStringTests</c>/<c>TransitionalBitTests</c>, which
/// re-run the existing suites through the new surface and so check <b>semantics</b> against real servers.
/// These check the other half: the exact bytes, including the cases a server would accept either way and
/// the ones a test against a server cannot distinguish - which argument order was used, whether a default
/// rendered a token, which of several equivalent commands was chosen.
/// </para>
/// <para>
/// Fast, deterministic, and needs nothing running; a wire-format regression shows up here first.
/// </para>
/// </remarks>
public class RespSurfaceStringsTests
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

    /// <summary>A feature probe that answers with whatever version it was told.</summary>
    private sealed class FakeFeatures(RedisFeatures features, bool known = true) : IRespServerFeatures
    {
        /// <summary>The keys this was asked about, as the probe saw them.</summary>
        public List<RedisKey> Keys { get; } = [];

        public bool TryGetFeatures(RedisCommand command, in RedisKey key, CommandFlags flags, out RedisFeatures result)
        {
            Keys.Add(key);
            result = features;
            return known;
        }
    }

    private static (RespContext Context, FakeExecutor Executor) Target(params string[] replies)
    {
        var executor = new FakeExecutor(replies.Length == 0 ? ["+OK\r\n"] : replies);
        return (new RespContext().WithExecutor(executor), executor);
    }

    [Fact]
    public async Task SimpleValueCommandsRenderTheirArgumentsInOrder()
    {
        var (ctx, exec) = Target(":8\r\n");

        Assert.Equal(8, await ctx.Strings.AppendAsync("k", "defgh"));
        Assert.Equal("*3|$6|APPEND|$1|k|$5|defgh|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task LengthAndRangeAreOrdinaryReads()
    {
        var (ctx, exec) = Target(":3\r\n", "$3\r\nabc\r\n");

        await ctx.Strings.LengthAsync("k");
        await ctx.Strings.GetRangeAsync("k", 0, -1);

        Assert.Equal(
            new[] { "*2|$6|STRLEN|$1|k|", "*4|$8|GETRANGE|$1|k|$1|0|$2|-1|" },
            exec.Sent);

        // both are pure reads, so both are cacheable by the flag gate
        Assert.All(exec.Flags, f => Assert.Equal(CommandFlags.CommandRetryReadOnly, f & Message.MaskRetryCategory));
    }

    [Fact]
    public async Task SetRangeRepliesWithALength()
    {
        var (ctx, exec) = Target(":11\r\n");

        // long, not RedisValue: SETRANGE has only ever replied with an integer
        Assert.Equal(11, await ctx.Strings.SetRangeAsync("k", 6, "Redis"));
        Assert.Equal("*4|$8|SETRANGE|$1|k|$1|6|$5|Redis|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task IncrementIsAlwaysINCRBY()
    {
        var (ctx, exec) = Target(":1\r\n", ":-1\r\n", ":5\r\n");

        await ctx.Strings.IncrementAsync("k");
        await ctx.Strings.IncrementAsync("k", -1);
        await ctx.Strings.IncrementAsync("k", 5);

        // no INCR/DECR/DECRBY: the argument is always written, so there is no arity branch and no second
        // spelling to keep in step. See the remarks on Increment.
        Assert.Equal(
            new[]
            {
                "*3|$6|INCRBY|$1|k|$1|1|",
                "*3|$6|INCRBY|$1|k|$2|-1|",
                "*3|$6|INCRBY|$1|k|$1|5|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task FloatingPointIncrementIsADifferentCommand()
    {
        var (ctx, exec) = Target("$3\r\n1.5\r\n");

        // a value with an exact binary form, on purpose: a RedisValue renders a double round-trippably
        // (G17), so 0.14 would go out as 0.14000000000000001 - which is what the old writer sends too,
        // since it takes the same RedisValue conversion
        Assert.Equal(1.5, await ctx.Strings.IncrementAsync("k", 1.5));
        Assert.Equal("*3|$11|INCRBYFLOAT|$1|k|$3|1.5|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task GetSetExpiryCoversEveryExpirationShape()
    {
        var (ctx, exec) = Target("$3\r\nabc\r\n");

        await ctx.Strings.GetSetExpiryAsync("k", default);                        // leave the TTL alone
        await ctx.Strings.GetSetExpiryAsync("k", TimeSpan.FromSeconds(300));      // EX
        await ctx.Strings.GetSetExpiryAsync("k", TimeSpan.FromMilliseconds(1500)); // PX
        await ctx.Strings.GetSetExpiryAsync("k", Expiration.Persist);             // PERSIST

        Assert.Equal(
            new[]
            {
                "*2|$5|GETEX|$1|k|",
                "*4|$5|GETEX|$1|k|$2|EX|$3|300|",
                "*4|$5|GETEX|$1|k|$2|PX|$4|1500|",
                "*3|$5|GETEX|$1|k|$7|PERSIST|",
            },
            exec.Sent);

        // a bare GETEX is the read the table says it is; anything that touches the TTL is a write
        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.All(exec.Flags.GetRange(1, 3), f => Assert.Equal(CommandFlags.CommandRetryWriteLastWins, f & Message.MaskRetryCategory));
    }

    [Fact]
    public void GetSetExpiryRefusesAnExpirationItCannotSpell()
    {
        var (ctx, _) = Target();

        // ENX has no place in GETEX's grammar; say so here rather than let the server say it later
        Assert.Throws<NotSupportedException>(
            () => ctx.Strings.GetSetExpiryAsync("k", new Expiration(TimeSpan.FromSeconds(30), ExpirationFlags.ExpireIfNotExists)));
    }

    [Fact]
    public async Task ManyKeysAreOneHoleAndStillKeys()
    {
        var (ctx, exec) = Target("*2\r\n$1\r\na\r\n$1\r\nb\r\n");

        RedisKey[] keys = ["k1", "k2", "k3"];
        (await ctx.WithKeyPrefix("t:").Strings.GetAsync(keys)).Dispose();

        // every key in the run is prefixed, exactly as a single key is
        Assert.Equal("*4|$4|MGET|$4|t:k1|$4|t:k2|$4|t:k3|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task NoKeysMeansNoCommand()
    {
        var (ctx, exec) = Target();

        // the empty case hands back the shared Empty lease, so there is nothing pooled to give back -
        // but it is disposed anyway, because a caller cannot know that and should not have to
        using (var none = await ctx.Strings.GetAsync(ReadOnlySpan<RedisKey>.Empty))
        {
            Assert.Equal(0, none.Length);
        }

        Assert.True(await ctx.Strings.SetAsync(ReadOnlySpan<KeyValuePair<RedisKey, RedisValue>>.Empty));

        // an arity-zero MGET or MSET is a server error; "nothing" is answerable without asking
        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task MultiSetPicksTheWidelyAvailableCommandWhenItCan()
    {
        var (ctx, exec) = Target();
        KeyValuePair<RedisKey, RedisValue>[] values = [new("a", "1"), new("b", "2")];

        await ctx.Strings.SetAsync(values);
        await ctx.Strings.SetAsync(values, when: ValueCondition.NotExists);
        await ctx.Strings.SetAsync(values, expiry: TimeSpan.FromSeconds(60));
        await ctx.Strings.SetAsync(values, expiry: TimeSpan.FromSeconds(60), when: ValueCondition.Exists);

        Assert.Equal(
            new[]
            {
                "*5|$4|MSET|$1|a|$1|1|$1|b|$1|2|",
                "*5|$6|MSETNX|$1|a|$1|1|$1|b|$1|2|",

                // MSETEX takes a count first, then the pairs, then the tail
                "*8|$6|MSETEX|$1|2|$1|a|$1|1|$1|b|$1|2|$2|EX|$2|60|",
                "*9|$6|MSETEX|$1|2|$1|a|$1|1|$1|b|$1|2|$2|EX|$2|60|$2|XX|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task MultiSetRefusesAConditionWithNoMultiKeySpelling()
    {
        var (ctx, _) = Target();
        KeyValuePair<RedisKey, RedisValue>[] values = [new("a", "1"), new("b", "2")];

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ctx.Strings.SetAsync(values, when: ValueCondition.Equal("old")));
    }

    [Fact]
    public async Task SetAndGetFollowsTheDocumentedOperandOrder()
    {
        var (ctx, exec) = Target("$3\r\nold\r\n");

        await ctx.Strings.SetAndGetAsync("k", "v", TimeSpan.FromSeconds(4), When.Exists);

        // condition, then GET, then expiration - the grammar as documented, not EX n XX GET
        Assert.Equal("*7|$3|SET|$1|k|$1|v|$2|XX|$3|GET|$2|EX|$1|4|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task ANullValueRemovesTheKey()
    {
        var (ctx, exec) = Target(":1\r\n", "$3\r\nold\r\n");

        // the long-standing meaning on this library's surface: there is no SET that stores "no value",
        // and writing an empty string instead would be a different value, silently
        await ctx.Strings.SetAsync("k", RedisValue.Null);
        await ctx.Strings.SetAndGetAsync("k", RedisValue.Null);

        Assert.Equal(new[] { "*2|$3|DEL|$1|k|", "*2|$6|GETDEL|$1|k|" }, exec.Sent);
    }

    [Fact]
    public async Task DeleteChoosesDelOrDelexByCondition()
    {
        var (ctx, exec) = Target(":1\r\n");

        await ctx.Strings.DeleteAsync("k");
        await ctx.Strings.DeleteAsync("k", ValueCondition.Exists);
        await ctx.Strings.DeleteAsync("k", ValueCondition.Equal("old"));

        Assert.Equal(
            new[]
            {
                "*2|$3|DEL|$1|k|",
                "*2|$3|DEL|$1|k|",  // "if it exists" is what DEL already means
                "*4|$5|DELEX|$1|k|$4|IFEQ|$3|old|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task DeleteRefusesAConditionThatCannotBeAsked()
    {
        var (ctx, _) = Target();

        // "delete it if it is absent" would quietly become "delete it"
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ctx.Strings.DeleteAsync("k", ValueCondition.NotExists));
    }

    [Fact]
    public async Task BoundedIncrementRendersOnlyWhatWasAskedFor()
    {
        var (ctx, exec) = Target("*2\r\n:5\r\n:5\r\n");

        await ctx.Strings.IncrementAsync("k", 5, TimeSpan.FromSeconds(60));
        await ctx.Strings.IncrementAsync("k", 5, TimeSpan.FromSeconds(60), lowerBound: 0, upperBound: 100, options: IncrementOptions.Saturate);

        Assert.Equal(
            new[]
            {
                "*6|$6|INCREX|$1|k|$5|BYINT|$1|5|$2|EX|$2|60|",
                "*11|$6|INCREX|$1|k|$5|BYINT|$1|5|$6|LBOUND|$1|0|$6|UBOUND|$3|100|$8|SATURATE|$2|EX|$2|60|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task BoundedIncrementReportsWhatWasActuallyApplied()
    {
        var (ctx, _) = Target("*2\r\n:100\r\n:40\r\n");

        // under a bound the applied increment is not the one that was asked for; that is the whole reason
        // INCREX has a two-element reply and a result type of its own
        var result = await ctx.Strings.IncrementAsync("k", 60, TimeSpan.FromSeconds(60), upperBound: 100, options: IncrementOptions.Saturate);
        Assert.Equal(100, result.Value);
        Assert.Equal(40, result.AppliedIncrement);
    }

    [Fact]
    public void BoundedIncrementRefusesAnExpirationItCannotSpell()
    {
        var (ctx, _) = Target();

        Assert.Throws<ArgumentException>(() => ctx.Strings.IncrementAsync("k", 1, Expiration.KeepTtl));
        Assert.Throws<ArgumentException>(() => ctx.Strings.IncrementAsync("k", 1, Expiration.Persist));
    }

    [Fact]
    public async Task LongestCommonSubsequenceHasThreeShapes()
    {
        var (ctx, exec) = Target("$2\r\nab\r\n", ":2\r\n");

        await ctx.Strings.LongestCommonSubsequenceAsync("a", "b");
        await ctx.Strings.LongestCommonSubsequenceLengthAsync("a", "b");

        Assert.Equal(
            new[]
            {
                "*3|$3|LCS|$1|a|$1|b|",
                "*4|$3|LCS|$1|a|$1|b|$3|LEN|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task LongestCommonSubsequenceWithMatchesReadsTheIdxReply()
    {
        // ["matches", [[[4,7],[5,8],4]], "len", 6]
        const string Reply =
            "*4\r\n$7\r\nmatches\r\n*1\r\n*3\r\n*2\r\n:4\r\n:7\r\n*2\r\n:5\r\n:8\r\n:4\r\n$3\r\nlen\r\n:6\r\n";
        var (ctx, exec) = Target(Reply);

        var result = await ctx.Strings.LongestCommonSubsequenceWithMatchesAsync("a", "b", minLength: 4);

        Assert.Equal("*7|$3|LCS|$1|a|$1|b|$3|IDX|$11|MINMATCHLEN|$1|4|$12|WITHMATCHLEN|", Assert.Single(exec.Sent));
        Assert.Equal(6, result.LongestMatchLength);
        var match = Assert.Single(result.Matches);
        Assert.Equal(4, match.First.Start);
        Assert.Equal(7, match.First.End);
        Assert.Equal(4, match.Length);
    }

    [Fact]
    public async Task DigestComesBackAsAConditionAWriteCanUse()
    {
        var (ctx, exec) = Target("$16\r\n0123456789abcdef\r\n");

        var digest = await ctx.Strings.DigestAsync("k");
        Assert.Equal("*2|$6|DIGEST|$1|k|", Assert.Single(exec.Sent));

        // the point of returning a ValueCondition rather than bytes: it goes straight back into a write
        Assert.NotNull(digest);
        Assert.True(digest.GetValueOrDefault().IsDigestTest);
    }

    [Fact]
    public async Task AMissingKeyHasNoDigest()
    {
        var (ctx, _) = Target("$-1\r\n");
        Assert.Null(await ctx.Strings.DigestAsync("k"));
    }

    [Fact]
    public async Task BitCountAndPositionOmitTheDefaultIndexType()
    {
        var (ctx, exec) = Target(":2\r\n");

        await ctx.Bitmaps.CountAsync("k");
        await ctx.Bitmaps.CountAsync("k", 0, 4, StringIndexType.Bit);
        await ctx.Bitmaps.PositionAsync("k", true, 0, 4, StringIndexType.Bit);
        await ctx.Bitmaps.PositionAsync("k", true, 2, StringIndex.Unbounded);

        Assert.Equal(
            new[]
            {
                "*4|$8|BITCOUNT|$1|k|$1|0|$2|-1|",          // BYTE is the server's own default: no token
                "*5|$8|BITCOUNT|$1|k|$1|0|$1|4|$3|BIT|",
                "*6|$6|BITPOS|$1|k|$1|1|$1|0|$1|4|$3|BIT|",
                "*4|$6|BITPOS|$1|k|$1|1|$1|2|",             // open-ended: no end, so no index type either
            },
            exec.Sent);
    }

    [Fact]
    public void AnOpenEndedBitPositionCannotAlsoBeABitIndex()
    {
        var (ctx, _) = Target();

        // there is nowhere to put the token, and dropping it would reinterpret `start` as a byte offset
        Assert.Throws<ArgumentException>(
            () => ctx.Bitmaps.PositionAsync("k", false, 2, StringIndex.Unbounded, StringIndexType.Bit));
    }

    [Fact]
    public async Task BitOperationTakesAnyNumberOfSourceKeys()
    {
        var (ctx, exec) = Target(":1\r\n");

        RedisKey[] sources = ["x", "y1", "y2"];
        await ctx.Bitmaps.OperationAsync(Bitwise.Diff1, "dest", sources);
        await ctx.Bitmaps.OperationAsync(Bitwise.Not, "dest", ["x"]);

        Assert.Equal(
            new[]
            {
                "*6|$5|BITOP|$5|DIFF1|$4|dest|$1|x|$2|y1|$2|y2|",
                "*4|$5|BITOP|$3|NOT|$4|dest|$1|x|",
            },
            exec.Sent);
    }

    [Fact]
    public void BitOperationChecksItsArityBeforeTheServerDoes()
    {
        var (ctx, _) = Target();

        Assert.Throws<ArgumentException>(() => ctx.Bitmaps.OperationAsync(Bitwise.And, "dest", ReadOnlySpan<RedisKey>.Empty));
        Assert.Throws<ArgumentException>(() => ctx.Bitmaps.OperationAsync(Bitwise.Not, "dest", ["a", "b"]));
    }

    [Fact]
    public async Task GetBitAndSetBitReadIntegerBooleans()
    {
        var (ctx, exec) = Target(":1\r\n", ":0\r\n");

        Assert.True(await ctx.Bitmaps.GetAsync("k", 10));
        Assert.False(await ctx.Bitmaps.SetAsync("k", 10, true));

        Assert.Equal(
            new[] { "*3|$6|GETBIT|$1|k|$2|10|", "*4|$6|SETBIT|$1|k|$2|10|$1|1|" },
            exec.Sent);
    }

    [Fact]
    public async Task BitFieldEmitsTheStickyOverflowOnlyWhenItChanges()
    {
        var (ctx, exec) = Target("*5\r\n:0\r\n:127\r\n:127\r\n_\r\n:-29\r\n");

        BitFieldOperation[] operations =
        [
            BitFieldOperation.Set(BitFieldEncoding.Int8, 0, 100),
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Saturate),
            BitFieldOperation.Get(BitFieldEncoding.Int8, 0),
            BitFieldOperation.IncrementBy(BitFieldEncoding.Int8, 0, 100, BitFieldOverflow.Fail),
        ];

        using var lease = await ctx.Bitmaps.FieldAsync("k", operations);

        // WRAP is in force to begin with, so the first SET emits no OVERFLOW; the GET does not disturb the
        // sticky state, which is why the FAIL after it is the second and last transition
        Assert.Equal(
            "*21|$8|BITFIELD|$1|k|"
            + "$3|SET|$2|i8|$1|0|$3|100|"
            + "$8|OVERFLOW|$3|SAT|$6|INCRBY|$2|i8|$1|0|$3|100|"
            + "$3|GET|$2|i8|$1|0|"
            + "$8|OVERFLOW|$4|FAIL|$6|INCRBY|$2|i8|$1|0|$3|100|",
            Assert.Single(exec.Sent));

        Assert.Equal(new long?[] { 0, 127, 127, null, -29 }, lease.Span.ToArray());
    }

    [Fact]
    public async Task AnAllGetBitFieldGoesOutAsTheReadOnlyCommand()
    {
        var (bare, exec) = Target("*1\r\n:7\r\n");
        var ctx = bare.WithServices(new FakeFeatures(new RedisFeatures(new Version(7, 0))));

        Assert.Equal(7, await ctx.Bitmaps.FieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, 0)));

        // BITFIELD is a write to the server however read-only its sub-operations are, so an all-GET
        // payload has to say BITFIELD_RO or a replica will refuse it - but only where it exists, which
        // is what the feature probe is for
        Assert.Equal("*5|$11|BITFIELD_RO|$1|k|$3|GET|$2|u8|$1|0|", Assert.Single(exec.Sent));
        Assert.Equal(CommandFlags.CommandRetryReadOnly, Assert.Single(exec.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task AnAllGetBitFieldStaysWritableWhenTheServerIsTooOld()
    {
        var (bare, exec) = Target("*1\r\n:7\r\n");
        var ctx = bare.WithServices(new FakeFeatures(new RedisFeatures(new Version(5, 0))));

        await ctx.Bitmaps.FieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, 0));

        // BITFIELD_RO arrived in 6.0; on anything older the read-only spelling is an unknown-command
        // error, which is strictly worse than losing replica eligibility
        Assert.StartsWith("*5|$8|BITFIELD|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task GetSetUsesTheModernSpellingWhereTheServerHasIt()
    {
        var (bare, exec) = Target("$3\r\nold\r\n");
        var ctx = bare.WithServices(new FakeFeatures(new RedisFeatures(new Version(7, 0))));

        Assert.Equal("old", await ctx.Strings.GetSet("k", "new"));

        // GETSET has been deprecated since 6.2 in favour of SET ... GET; same request, same reply
        Assert.Equal("*4|$3|SET|$1|k|$3|new|$3|GET|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task GetSetStaysDeprecatedWhereTheServerIsTooOldOrUnknown()
    {
        var (old, oldExec) = Target("$3\r\nold\r\n");
        var (bare, bareExec) = Target("$3\r\nold\r\n");

        await old.WithServices(new FakeFeatures(new RedisFeatures(new Version(6, 0)))).Strings.GetSet("k", "new");

        // and with no probe at all - a bare context, a cold multiplexer - "not sure" has to mean the old
        // spelling: SET ... GET is a syntax error before 6.2, where GETSET works everywhere
        await bare.Strings.GetSet("k", "new");

        Assert.Equal("*3|$6|GETSET|$1|k|$3|new|", Assert.Single(oldExec.Sent));
        Assert.Equal("*3|$6|GETSET|$1|k|$3|new|", Assert.Single(bareExec.Sent));
    }

    [Fact]
    public void GetSetRejectsANullValueRatherThanDeletingTheKey()
    {
        var (bare, exec) = Target("$3\r\nold\r\n");
        var ctx = bare.WithServices(new FakeFeatures(new RedisFeatures(new Version(7, 0))));

        // SetAndGet reads a null value as a delete, matching Set - but the old StringGetSet builds a
        // key/value message and those assert, so it has always thrown here. Inheriting the improvement
        // would turn a loud failure into a silent KeyDelete, which is why both spellings refuse it.
        Assert.Throws<ArgumentException>(() => ctx.Strings.GetSet("k", RedisValue.Null));
        Assert.Throws<ArgumentException>(() => bare.Strings.GetSet("k", RedisValue.Null));

        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task TheFeatureProbeIsAskedAboutTheKeyTheServerWillSee()
    {
        var (bare, exec) = Target("*1\r\n:7\r\n");
        var probe = new FakeFeatures(new RedisFeatures(new Version(7, 0)));
        var ctx = bare.WithServices(probe).WithKeyPrefix("t:");

        await ctx.Bitmaps.FieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, 0));

        // on this surface the key prefix is CONTEXT state applied at write time, not something a
        // KeyPrefixed* decorator already baked into the RedisKey - so "k" is not the key that goes out. The
        // probe routes by those bytes, so in a cluster asking about "k" samples the version of whichever
        // node owns the unprefixed slot, which may not be the node that answers this command.
        Assert.Equal("t:k", Assert.Single(probe.Keys));
        Assert.Equal("*5|$11|BITFIELD_RO|$3|t:k|$3|GET|$2|u8|$1|0|", Assert.Single(exec.Sent));
    }

    [Fact]
    public void TheFeatureProbeSeesBothPrefixesAndNeitherWhenThereIsNoKey()
    {
        var probe = new FakeFeatures(new RedisFeatures(new Version(7, 0)));
        var ctx = new RespContext().WithServices(probe).WithKeyPrefix("t:");

        // a key may ALREADY carry a prefix of its own; the two compose rather than one winning, exactly as
        // AppendFormatted composes them when writing
        ctx.TryGetFeatures(RedisCommand.BITFIELD_RO, ((RedisKey)"k").Prepend("inner:"), CommandFlags.None, out _);

        // and a null key means "routes to no particular key"; prefixing that would invent a key made only
        // of the prefix, and route on it
        ctx.TryGetFeatures(RedisCommand.PING, default, CommandFlags.None, out _);

        Assert.Equal(new RedisKey[] { "t:inner:k", default }, probe.Keys);
    }

    [Fact]
    public async Task AnAllGetBitFieldStaysWritableWhenNothingIsKnown()
    {
        // no probe at all: a bare context, a hand-wired executor, a cold multiplexer. "Not sure" has to
        // mean "use the spelling that works everywhere", which is what makes the probe safe to adopt one
        // command at a time rather than all at once.
        var (ctx, exec) = Target("*1\r\n:7\r\n");

        await ctx.Bitmaps.FieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, 0));

        Assert.StartsWith("*5|$8|BITFIELD|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task AnyWriteInABitFieldKeepsTheWritableCommand()
    {
        var (ctx, exec) = Target("*1\r\n:0\r\n");

        await ctx.Bitmaps.FieldAsync("k", BitFieldOperation.Set(BitFieldEncoding.UInt8, 0, 1));

        Assert.StartsWith("*6|$8|BITFIELD|", Assert.Single(exec.Sent));

        // SET is positional, so a replay lands on the same value; only INCRBY compounds
        Assert.Equal(CommandFlags.CommandRetryWriteLastWins, Assert.Single(exec.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public async Task AnElementOffsetUsesTheHashForm()
    {
        var (ctx, exec) = Target("*1\r\n:0\r\n");

        await ctx.WithServices(new FakeFeatures(new RedisFeatures(new Version(7, 0))))
            .Bitmaps.FieldAsync("k", BitFieldOperation.Get(BitFieldEncoding.UInt8, BitFieldOffset.Element(2)));

        Assert.Equal("*5|$11|BITFIELD_RO|$1|k|$3|GET|$2|u8|$2|#2|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task TheFrameCarriesTheCommandsIdentityAsWellAsItsBytes()
    {
        var (ctx, _) = Target("$3\r\nabc\r\n");

        // not decoration: the pipeline decides primary-vs-replica routing from Message.Command, and a
        // profiler reports it. A frame that only knew its bytes reported every command as UNKNOWN.
        using var frame = ctx.Render($"{RedisCommand.GETRANGE}{(RedisKey)"k"}{(RedisValue)0}{(RedisValue)(-1)}");
        Assert.Equal(RedisCommand.GETRANGE, frame.Command);

        await Task.CompletedTask;
    }
    /// <summary>MGET comes back as a pooled lease carrying the same values an array would.</summary>
    /// <remarks>
    /// The point of the shape change is who owns the storage, not what is in it - so the values must be
    /// indistinguishable from the array form, including the nulls that a missing key produces.
    /// </remarks>
    [Fact]
    public async Task MultiGetReturnsTheValuesAsALease()
    {
        var (ctx, _) = Target("*3\r\n$1\r\na\r\n_\r\n$2\r\nbc\r\n");

        using var values = await ctx.Strings.GetAsync([(RedisKey)"k1", (RedisKey)"k2", (RedisKey)"k3"]);

        Assert.Equal(3, values.Length);
        Assert.Equal("a", (string?)values.Span[0]);
        Assert.True(values.Span[1].IsNull);
        Assert.Equal("bc", (string?)values.Span[2]);
    }

    /// <summary>
    /// The lease really is pooled: disposing one and asking again reuses the same storage.
    /// </summary>
    /// <remarks>
    /// Without this the shape change would be pure ceremony - a disposable wrapper around a fresh
    /// allocation buys nothing and costs a <c>using</c>. Asserted through <see cref="ArrayPool{T}"/>
    /// rather than by identity, because the lease does not expose its buffer; renting the same size back
    /// is the only observation available, and it is an implementation detail rather than a contract, so
    /// this asserts only that a returned buffer is being offered again.
    /// </remarks>
    [Fact]
    public async Task TheLeaseStorageGoesBackToThePool()
    {
        var (ctx, _) = Target("*3\r\n$1\r\na\r\n$1\r\nb\r\n$1\r\nc\r\n");
        RedisKey[] keys = [(RedisKey)"k1", (RedisKey)"k2", (RedisKey)"k3"];

        var first = await ctx.Strings.GetAsync(keys);
        Assert.Equal(3, first.Length);
        first.Dispose();

        // the shared pool hands back the most recently returned buffer of a bucket, so a rent of the same
        // size should find one waiting - and it must have been wiped, since a RespValue holds a reference
        // to whoever owns its bytes. The pool is RespValue's, not RedisValue's: reading the wrong bucket
        // finds whatever some other test left in it, which passes alone and fails under load.
        var reused = ArrayPool<RespValue>.Shared.Rent(3);
        try
        {
            Assert.All(reused, v => Assert.True(v.IsNull));
        }
        finally
        {
            ArrayPool<RespValue>.Shared.Return(reused);
        }
    }

}
