using System;
using System.Collections.Generic;
using System.Text;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Transition plan: can the EXISTING Message infrastructure feed the new frame/cache pipeline without
/// rewriting the command surface? These drive real Message objects through MessageWriter into a
/// RespFrameWriter and check the result is indistinguishable from what the interpolated writer produces.
/// </summary>
public class MessageToRespFrameTests
{
    private static RespFrame Render(Message message, int slot = ServerSelectionStrategy.NoSlot)
    {
        var writer = new RespFrameWriter();
        message.WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        return writer.Complete(slot);
    }

    private static string Text(ReadOnlySpan<byte> value) =>
        Encoding.UTF8.GetString(value.ToArray()).Replace("\r\n", "|");

    private static string[] Keys(in RespFrame frame)
    {
        var count = frame.KeyCount;
        Assert.True(count >= 0);
        var ranges = new KeyRange[count];
        Assert.Equal(count, frame.TryGetKeys(ranges));
        var keys = new string[count];
        for (var i = 0; i < count; i++) keys[i] = Encoding.UTF8.GetString(frame.GetKey(ranges[i]).ToArray());
        return keys;
    }

    [Fact]
    public void MessageRendersTheSameBytesAsTheInterpolatedWriter()
    {
        using var viaMessage = Render(Message.Create(0, CommandFlags.None, RedisCommand.GET, (RedisKey)"mykey"));

        var ctx = new RespContext();
        using var viaInterpolation = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"mykey"}");

        // byte-identical rendering is not a nicety here: the frame IS the cache key, so two routes that
        // disagree would cache the same logical command twice
        Assert.Equal(Text(viaInterpolation.Span), Text(viaMessage.Span));
        Assert.Equal("*2|$3|GET|$5|mykey|", Text(viaMessage.Span));
    }

    [Fact]
    public void KeysAreRecoveredFromAMessageRender()
    {
        using var frame = Render(Message.Create(0, CommandFlags.None, RedisCommand.SET, (RedisKey)"k", (RedisValue)"v"));

        Assert.Equal(3, frame.ArgCount);
        Assert.Equal(new[] { "k" }, Keys(frame));   // the key, and NOT the value
        Assert.False(frame.KeysNeedScan);
    }

    [Fact]
    public void TwoKeyMessagesUseTheInlineOffsets()
    {
        using var frame = Render(Message.Create(0, CommandFlags.None, RedisCommand.RENAME, (RedisKey)"src", (RedisKey)"dst"));

        Assert.False(frame.KeysNeedScan);
        Assert.Equal(new[] { "src", "dst" }, Keys(frame));
    }

    [Fact]
    public void ManyKeyMessagesFallToTheBitmapAndStillResolve()
    {
        RedisKey[] keys = ["a", "b", "c", "d"];
        using var frame = Render(Message.Create(0, CommandFlags.None, RedisCommand.DEL, keys));

        // beyond two, offsets do not fit, so RespFrameWriter derives argument indices by walking once
        Assert.True(frame.KeysNeedScan);
        Assert.Equal(new[] { "a", "b", "c", "d" }, Keys(frame));
    }

    [Fact]
    public void InterleavedKeysAndValuesMarkOnlyTheKeys()
    {
        KeyValuePair<RedisKey, RedisValue>[] pairs =
        [
            new("k1", "v1"),
            new("k2", "v2"),
            new("k3", "v3"),
        ];

        using var frame = Render(Message.Create(
            0, CommandFlags.None, RedisCommand.MSET, pairs, Expiration.Default, When.Always));

        var keys = Keys(frame);
        Assert.Equal(new[] { "k1", "k2", "k3" }, keys);
        Assert.DoesNotContain("v1", keys);
        frame.Dispose();
    }

    [Fact]
    public void TheSlotComesFromTheMessageRatherThanBeingFolded()
    {
        var message = Message.Create(0, CommandFlags.None, RedisCommand.GET, (RedisKey)"{tag}:x");

        // a standalone strategy legitimately reports NoSlot - routing is a cluster concern - so this pins
        // the plumbing rather than the hashing: whatever Message computes is what the frame carries
        var standalone = new ServerSelectionStrategy(null!);
        Assert.Equal(ServerSelectionStrategy.NoSlot, message.GetHashSlot(standalone));

        // and a real slot survives the trip. Message already computes this, so unlike the interpolated
        // writer there is nothing to fold during the write - one less thing to reimplement
        var slot = ServerSelectionStrategy.GetHashSlot((RedisKey)"{tag}:x");
        Assert.NotEqual(ServerSelectionStrategy.NoSlot, slot);

        using var frame = Render(message, slot);
        Assert.Equal(slot, frame.Slot);
    }

    [Fact]
    public void AMessageRenderCanBeCachedAndInvalidated()
    {
        using var cache = new RespClientCache();
        var frame = Render(Message.Create(0, CommandFlags.None, RedisCommand.GET, (RedisKey)"mykey"));

        Assert.True(cache.TryBeginFill(ref frame, 0, out var fill));
        var payload = RespPayload.Create(Encoding.UTF8.GetBytes("$5\r\nhello\r\n"));
        Assert.True(cache.TryComplete(fill, payload));
        payload.Release();

        // and a render from the OTHER route finds it - the two are interchangeable as cache keys
        var ctx = new RespContext();
        using var probe = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"mykey"}");
        Assert.True(cache.TryGet(probe.AsLookupKey(), 0, out var hit));
        Assert.Equal("$5|hello|", Text(hit.Span));
        hit.Release();

        Assert.True(cache.OnInvalidate(Encoding.UTF8.GetBytes("mykey")));
        Assert.False(cache.TryGet(probe.AsLookupKey(), 0, out _));
    }
}
