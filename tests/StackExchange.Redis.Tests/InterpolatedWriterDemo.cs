using System;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A worked example of the experimental interpolated RESP writer, covering the shapes a caller would
/// actually use. Doubles as documentation: each case shows the call, the exact frame it renders, and the
/// routing/key metadata folded while writing. See design/interpolated-resp-writer.md.
/// </summary>
public class InterpolatedWriterDemo
{
    /// <summary>Render the frame with CRLF shown as '|', so expectations stay readable.</summary>
    private static string Frame(in RespFrame frame) => Encoding.UTF8.GetString(frame.Span.ToArray()).Replace("\r\n", "|");

    private static string Keys(in RespFrame frame)
    {
        Span<KeyRange> ranges = stackalloc KeyRange[2];
        var count = frame.TryGetKeys(ranges);
        if (count < 0) return "<scan>";
        var parts = new string[count];
        for (int i = 0; i < count; i++) parts[i] = Encoding.UTF8.GetString(frame.GetKey(ranges[i]).ToArray());
        return string.Join(",", parts);
    }

    private static readonly RespContext Cluster = new(serverType: ServerType.Cluster);

    [Fact]
    public void FixedArity()
    {
        using var frame = Cluster.Execute(RedisCommand.GET, $"{(RedisKey)"user:1"}");

        Assert.Equal("*2|$3|GET|$6|user:1|", Frame(frame));
        Assert.Equal("user:1", Keys(frame));
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"user:1"), frame.Slot);
    }

    [Fact]
    public void KeyAndValue()
    {
        using var frame = Cluster.Execute(RedisCommand.SET, $"{(RedisKey)"user:1"}{(RedisValue)"marc"}");

        Assert.Equal("*3|$3|SET|$6|user:1|$4|marc|", Frame(frame));
        Assert.Equal("user:1", Keys(frame)); // the value is not a key, and is not marked as one
    }

    [Fact]
    public void KeyspaceIsolation()
    {
        var tenant = Cluster.WithKeyPrefix("t7:");
        using var frame = tenant.Execute(RedisCommand.GET, $"{(RedisKey)"user:1"}");

        Assert.Equal("*2|$3|GET|$9|t7:user:1|", Frame(frame));
        Assert.Equal("t7:user:1", Keys(frame));

        // the slot follows the PREFIXED key, so tenants do not collide on a slot either
        using var plain = Cluster.Execute(RedisCommand.GET, $"{(RedisKey)"user:1"}");
        Assert.NotEqual(plain.Slot, frame.Slot);
    }

    [Fact]
    public void OptionalArguments()
    {
        var cmd = Cluster.Compose(RedisCommand.SET, $"{(RedisKey)"user:1"}{(RedisValue)"marc"}");
        cmd.AppendFormatted((RedisValue)"EX");
        cmd.AppendFormatted((RedisValue)300);
        using var frame = Cluster.Execute(ref cmd);

        Assert.Equal("*5|$3|SET|$6|user:1|$4|marc|$2|EX|$3|300|", Frame(frame));
        Assert.Equal("user:1", Keys(frame));
    }

    [Fact]
    public void VariadicWithSharedHashTag()
    {
        var keys = new RedisKey[] { "{u}:a", "{u}:b", "{u}:c" };
        var cmd = Cluster.Compose(RedisCommand.DEL, keys.Length);
        foreach (var key in keys) cmd.AppendFormatted(key);
        using var frame = Cluster.Execute(ref cmd);

        Assert.Equal("*4|$3|DEL|$5|{u}:a|$5|{u}:b|$5|{u}:c|", Frame(frame));
        Assert.Equal("<scan>", Keys(frame)); // beyond two keys the inline offsets give out
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"{u}:a"), frame.Slot);
    }

    [Fact]
    public void CrossSlotIsDetected()
    {
        var cmd = Cluster.Compose(RedisCommand.DEL, 2);
        cmd.AppendFormatted((RedisKey)"alpha");
        cmd.AppendFormatted((RedisKey)"beta");
        using var frame = Cluster.Execute(ref cmd);

        Assert.Equal("*3|$3|DEL|$5|alpha|$4|beta|", Frame(frame));
        Assert.Equal(ServerSelectionStrategy.MultipleSlots, frame.Slot);
    }

    [Fact]
    public void ChannelPrefix()
    {
        var pub = Cluster.WithChannelPrefix(new RedisChannel("app:", RedisChannel.PatternMode.Literal));
        var channel = new RedisChannel("news", RedisChannel.PatternMode.Literal);
        using var frame = pub.Execute(RedisCommand.PUBLISH, $"{channel}{(RedisValue)"hi"}");

        Assert.Equal("*3|$7|PUBLISH|$8|app:news|$2|hi|", Frame(frame));
        Assert.Equal("", Keys(frame)); // a channel is not a key
    }
}
