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
/// What the hash group puts on the wire, byte for byte, against a fake executor.
/// </summary>
/// <remarks><inheritdoc cref="RespSurfaceStringsTests" path="/remarks"/></remarks>
public class RespSurfaceHashesTests
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
    public async Task OneFieldAndManyFieldsAreDifferentCommands()
    {
        var (ctx, exec) = Target("$3\r\nabc\r\n", "*2\r\n$1\r\na\r\n$1\r\nb\r\n");

        await ctx.Hashes.GetAsync("k", "f1");
        await ctx.Hashes.GetAsync("k", ["f1", "f2"]);

        Assert.Equal(
            new[] { "*3|$4|HGET|$1|k|$2|f1|", "*4|$5|HMGET|$1|k|$2|f1|$2|f2|" },
            exec.Sent);
    }

    [Fact]
    public async Task FieldsAreValuesAndNotKeys()
    {
        var (ctx, exec) = Target("*1\r\n$1\r\na\r\n");

        await ctx.WithKeyPrefix("t:").Hashes.GetAsync("k", ["f1"]);

        // the key is prefixed; the field is not. Writing a field through the key path would silently
        // prefix it, and nothing downstream could tell
        Assert.Equal("*3|$5|HMGET|$3|t:k|$2|f1|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task NoFieldsMeansNoCommand()
    {
        var (ctx, exec) = Target();

        Assert.Empty((await ctx.Hashes.GetAsync("k", ReadOnlySpan<RedisValue>.Empty)).Span.ToArray());
        Assert.Equal(0, await ctx.Hashes.DeleteAsync("k", ReadOnlySpan<RedisValue>.Empty));
        Assert.Empty((await ctx.Hashes.PersistAsync("k", ReadOnlySpan<RedisValue>.Empty)).Span.ToArray());
        await ctx.Hashes.SetAsync("k", ReadOnlySpan<HashEntry>.Empty);

        Assert.Empty(exec.Sent);
    }

    [Fact]
    public async Task SetPicksBetweenHSetHSetNxAndHDel()
    {
        var (ctx, exec) = Target();

        await ctx.Hashes.SetAsync("k", "f", "v");
        await ctx.Hashes.SetAsync("k", "f", "v", When.NotExists);
        await ctx.Hashes.SetAsync("k", "f", RedisValue.Null);

        Assert.Equal(
            new[]
            {
                "*4|$4|HSET|$1|k|$1|f|$1|v|",
                "*4|$6|HSETNX|$1|k|$1|f|$1|v|",
                "*3|$4|HDEL|$1|k|$1|f|", // a null value removes the field, as on the old surface
            },
            exec.Sent);
    }

    [Fact]
    public async Task AFieldSetIsOneHole()
    {
        var (ctx, exec) = Target("+OK\r\n");

        HashEntry[] entries = [new("f1", "v1"), new("f2", "v2")];
        await ctx.Hashes.SetAsync("k", entries);

        // HashEntry writes its own two arguments, name then value, so a whole field set is one hole
        Assert.Equal("*6|$5|HMSET|$1|k|$2|f1|$2|v1|$2|f2|$2|v2|", Assert.Single(exec.Sent));
    }

    [Fact]
    public async Task RandomFieldHasThreeShapes()
    {
        var (ctx, exec) = Target("$2\r\nf1\r\n", "*1\r\n$2\r\nf1\r\n", "*2\r\n$2\r\nf1\r\n$2\r\nv1\r\n");

        await ctx.Hashes.RandomFieldAsync("k");
        await ctx.Hashes.RandomFieldsAsync("k", -5);
        await ctx.Hashes.RandomFieldsWithValuesAsync("k", 2);

        Assert.Equal(
            new[]
            {
                "*2|$10|HRANDFIELD|$1|k|",
                "*3|$10|HRANDFIELD|$1|k|$2|-5|",
                "*4|$10|HRANDFIELD|$1|k|$1|2|$10|WITHVALUES|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task GetAllReadsBothPairShapes()
    {
        // RESP2 interleaves name/value; RESP3 may send them jagged. The shape is decided from the reply
        // rather than from the negotiated protocol, so both arrive as the same HashEntry[].
        var (interleaved, _) = Target("*4\r\n$2\r\nf1\r\n$2\r\nv1\r\n$2\r\nf2\r\n$2\r\nv2\r\n");
        var (jagged, _) = Target("*2\r\n*2\r\n$2\r\nf1\r\n$2\r\nv1\r\n*2\r\n$2\r\nf2\r\n$2\r\nv2\r\n");

        HashEntry[] expected = [new("f1", "v1"), new("f2", "v2")];
        Assert.Equal(expected, (await interleaved.Hashes.GetAllAsync("k")).Span.ToArray());
        Assert.Equal(expected, (await jagged.Hashes.GetAllAsync("k")).Span.ToArray());
    }

    [Fact]
    public async Task ExpirePicksItsCommandFromTheExpirationsShape()
    {
        var (ctx, exec) = Target("*1\r\n:1\r\n");

        RedisValue[] fields = ["f1"];
        await ctx.Hashes.ExpireAsync("k", fields, TimeSpan.FromSeconds(300));         // relative, whole seconds
        await ctx.Hashes.ExpireAsync("k", fields, TimeSpan.FromMilliseconds(1500));   // relative, milliseconds
        var whole = new DateTime(2101, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await ctx.Hashes.ExpireAsync("k", fields, whole);                    // absolute, whole seconds
        await ctx.Hashes.ExpireAsync("k", fields, whole.AddMilliseconds(1)); // absolute, milliseconds

        // the mode is in the COMMAND NAME here, not in an operand - which is the one thing that makes this
        // group's expiry different from SET's, and the only reason Expiration is read rather than written.
        // Note the ARGUMENT changes units along with the command, so the two have to be chosen together.
        Assert.Equal("*6|$7|HEXPIRE|$1|k|$3|300|$6|FIELDS|$1|1|$2|f1|", exec.Sent[0]);
        Assert.Equal("*6|$8|HPEXPIRE|$1|k|$4|1500|$6|FIELDS|$1|1|$2|f1|", exec.Sent[1]);
        Assert.Equal("*6|$9|HEXPIREAT|$1|k|$10|4133980800|$6|FIELDS|$1|1|$2|f1|", exec.Sent[2]);
        Assert.Equal("*6|$10|HPEXPIREAT|$1|k|$13|4133980800001|$6|FIELDS|$1|1|$2|f1|", exec.Sent[3]);
    }

    [Fact]
    public async Task ExpireRendersItsConditionBeforeTheFields()
    {
        var (ctx, exec) = Target("*1\r\n:1\r\n");

        RedisValue[] fields = ["f1"];
        await ctx.Hashes.ExpireAsync("k", fields, TimeSpan.FromSeconds(60), ExpireWhen.GreaterThanCurrentExpiry);

        Assert.Equal("*7|$7|HEXPIRE|$1|k|$2|60|$2|GT|$6|FIELDS|$1|1|$2|f1|", Assert.Single(exec.Sent));

        // NX/XX/GT/LT make it a conditional write, exactly as for the key-level EXPIRE
        Assert.Equal(CommandFlags.CommandRetryWriteChecked, Assert.Single(exec.Flags) & Message.MaskRetryCategory);
    }

    [Fact]
    public void ExpireRefusesSomethingThatIsNotADeadline()
    {
        var (ctx, _) = Target();
        RedisValue[] fields = ["f1"];

        Assert.Throws<ArgumentException>(() => ctx.Hashes.ExpireAsync("k", fields, Expiration.KeepTtl));
        Assert.Throws<ArgumentException>(() => ctx.Hashes.ExpireAsync("k", fields, Expiration.Persist));
        Assert.Throws<ArgumentException>(() => ctx.Hashes.ExpireAsync("k", fields, default));
    }

    [Fact]
    public async Task LifetimeQueriesAlwaysUseTheMillisecondCommand()
    {
        var (ctx, exec) = Target("*1\r\n:1000\r\n");

        RedisValue[] fields = ["f1", "f2"];
        await ctx.Hashes.GetTimeToLiveAsync("k", fields);
        await ctx.Hashes.GetExpireDateTimeAsync("k", fields);
        await ctx.Hashes.PersistAsync("k", fields);

        Assert.Equal(
            new[]
            {
                "*6|$5|HPTTL|$1|k|$6|FIELDS|$1|2|$2|f1|$2|f2|",
                "*6|$12|HPEXPIRETIME|$1|k|$6|FIELDS|$1|2|$2|f1|$2|f2|",
                "*6|$8|HPERSIST|$1|k|$6|FIELDS|$1|2|$2|f1|$2|f2|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task GetSetExpiryPutsTheExpiryBeforeTheFields()
    {
        var (ctx, exec) = Target("*1\r\n$2\r\nv1\r\n");

        await ctx.Hashes.GetSetExpiryAsync("k", "f1");
        await ctx.Hashes.GetSetExpiryAsync("k", "f1", TimeSpan.FromSeconds(60));
        await ctx.Hashes.GetSetExpiryAsync("k", "f1", Expiration.Persist);

        Assert.Equal(
            new[]
            {
                "*5|$6|HGETEX|$1|k|$6|FIELDS|$1|1|$2|f1|",
                "*7|$6|HGETEX|$1|k|$2|EX|$2|60|$6|FIELDS|$1|1|$2|f1|",
                "*6|$6|HGETEX|$1|k|$7|PERSIST|$6|FIELDS|$1|1|$2|f1|",
            },
            exec.Sent);

        // a bare HGETEX is a read; anything that touches the TTL is a write
        Assert.Equal(CommandFlags.CommandRetryReadOnly, exec.Flags[0] & Message.MaskRetryCategory);
        Assert.All(exec.Flags.GetRange(1, 2), f => Assert.Equal(CommandFlags.CommandRetryWriteLastWins, f & Message.MaskRetryCategory));
    }

    [Fact]
    public async Task SingleFieldRepliesAreUnwrappedFromTheirArray()
    {
        var (ctx, exec) = Target("*1\r\n$2\r\nv1\r\n", "*1\r\n$-1\r\n");

        // the FIELDS commands always reply with an array, one element per field - so asking for one field
        // still gets *1, and the caller still wanted one value
        Assert.Equal("v1", await ctx.Hashes.GetDeleteAsync("k", "f1"));
        Assert.True((await ctx.Hashes.GetDeleteAsync("k", "f1")).IsNull);

        Assert.Equal("*5|$7|HGETDEL|$1|k|$6|FIELDS|$1|1|$2|f1|", exec.Sent[0]);
    }

    [Fact]
    public async Task SetWithExpiryRendersConditionThenExpiryThenFields()
    {
        var (ctx, exec) = Target();

        await ctx.Hashes.SetWithExpiryAsync("k", "f1", "v1");
        await ctx.Hashes.SetWithExpiryAsync("k", "f1", "v1", TimeSpan.FromSeconds(60), When.NotExists);

        Assert.Equal(
            new[]
            {
                "*6|$6|HSETEX|$1|k|$6|FIELDS|$1|1|$2|f1|$2|v1|",

                // FNX, not NX: the key-level token means something else here
                "*9|$6|HSETEX|$1|k|$3|FNX|$2|EX|$2|60|$6|FIELDS|$1|1|$2|f1|$2|v1|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task SetWithExpiryTakesAWholeFieldSet()
    {
        var (ctx, exec) = Target();

        HashEntry[] entries = [new("f1", "v1"), new("f2", "v2")];
        await ctx.Hashes.SetWithExpiryAsync("k", entries, Expiration.KeepTtl, When.Exists);

        Assert.Equal(
            "*10|$6|HSETEX|$1|k|$3|FXX|$7|KEEPTTL|$6|FIELDS|$1|2|$2|f1|$2|v1|$2|f2|$2|v2|",
            Assert.Single(exec.Sent));
    }

    [Fact]
    public void SetWithExpiryRefusesAnExpirationItCannotSpell()
    {
        var (ctx, _) = Target();

        Assert.Throws<NotSupportedException>(
            () => ctx.Hashes.SetWithExpiryAsync("k", "f", "v", new Expiration(TimeSpan.FromSeconds(30), ExpirationFlags.ExpireIfNotExists)));
    }

    [Fact]
    public async Task IncrementHasNoDecrementTwin()
    {
        var (ctx, exec) = Target(":4\r\n", "$3\r\n1.5\r\n");

        await ctx.Hashes.IncrementAsync("k", "f", -6);
        await ctx.Hashes.IncrementAsync("k", "f", 1.5);

        // the server has no HDECRBY, so a negative amount is the only spelling there has ever been
        Assert.Equal(
            new[]
            {
                "*4|$7|HINCRBY|$1|k|$1|f|$2|-6|",
                "*4|$12|HINCRBYFLOAT|$1|k|$1|f|$3|1.5|",
            },
            exec.Sent);
    }

    [Fact]
    public async Task ExpireResultsComeBackAsTheirEnum()
    {
        var (ctx, _) = Target("*3\r\n:1\r\n:0\r\n:-2\r\n");

        RedisValue[] fields = ["a", "b", "c"];
        using var results = await ctx.Hashes.ExpireAsync("k", fields, TimeSpan.FromSeconds(60));

        Assert.Equal(
            new[] { ExpireResult.Success, ExpireResult.ConditionNotMet, ExpireResult.NoSuchField },
            results.Span.ToArray());
    }
}
