using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Unit tests for the experimental interpolated-string RESP writer spike; see
/// design/interpolated-resp-writer.md. Pure formatting/routing - no server involved.
/// </summary>
public class InterpolatedWriterUnitTests
{
    /// <summary>Parse a rendered frame back into its arguments, demanding exact consumption.</summary>
    private static string[] Parse(ReadOnlySpan<byte> frame)
    {
        var reader = new RespReader(frame);
        reader.MoveNext();
        Assert.Equal(RespPrefix.Array, reader.Prefix);
        var count = reader.AggregateLength();
        var args = new string[count];
        for (int i = 0; i < count; i++)
        {
            reader.MoveNext();
            Assert.Equal(RespPrefix.BulkString, reader.Prefix);
            args[i] = reader.ReadString() ?? "";
        }

        reader.DemandEnd(); // no over- or under-run
        return args;
    }

    private static string[] Keys(in RespFrame frame)
    {
        Span<KeyRange> ranges = stackalloc KeyRange[2];
        var count = frame.TryGetKeys(ranges);
        if (count < 0) return null!; // caller must scan
        var keys = new string[count];
        for (int i = 0; i < count; i++) keys[i] = Encoding.UTF8.GetString(frame.GetKey(ranges[i]).ToArray());
        return keys;
    }

    [Fact]
    public void RendersCommandKeyAndValue()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"mykey"}{(RedisValue)"myvalue"}");

        Assert.Equal(3, frame.ArgCount);
        Assert.Equal(new[] { "SET", "mykey", "myvalue" }, Parse(frame.Span));
    }

    [Fact]
    public void RendersExactBytes()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"abc"}");

        Assert.Equal("*2\r\n$3\r\nGET\r\n$3\r\nabc\r\n", Encoding.UTF8.GetString(frame.Span.ToArray()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]   // '*NN' - two digits, so the frame start moves
    [InlineData(120)]  // three
    public void HeaderBackfillIsRightAligned(int extraArgs)
    {
        var ctx = new RespContext();
        var handler = new RespCommandHandler(0, extraArgs + 2, ctx);
        handler.AppendFormatted(RedisCommand.SET);
        handler.AppendFormatted((RedisKey)"mykey");
        for (int i = 0; i < extraArgs; i++) handler.AppendFormatted((RedisValue)i);
        using var frame = handler.Complete();

        var args = Parse(frame.Span);
        Assert.Equal(extraArgs + 2, args.Length);
        Assert.Equal("SET", args[0]);

        // the key offset is buffer-absolute, so it survives the header growing
        Assert.Equal(new[] { "mykey" }, Keys(frame));
    }

    [Fact]
    public void CommandMapRenamesAreApplied()
    {
        var map = CommandMap.Create(new Dictionary<string, string?> { ["set"] = "xset" });
        var ctx = new RespContext(map);
        using var frame = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");

        Assert.Equal(new[] { "XSET", "k", "v" }, Parse(frame.Span));
    }

    [Fact]
    public void DisabledCommandThrows()
    {
        var map = CommandMap.Create(new Dictionary<string, string?> { ["set"] = null });
        var ctx = new RespContext(map);

        Assert.Throws<RedisCommandException>(() => ctx.Execute($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}").Dispose());
    }

    [Fact]
    public void CommandMustComeFirst()
    {
        var ctx = new RespContext();
        Assert.Throws<InvalidOperationException>(() => ctx.Execute($"{(RedisKey)"k"}{RedisCommand.GET}").Dispose());
    }

    [Fact]
    public void KeyPrefixIsAppliedToTheWire()
    {
        var ctx = new RespContext().WithKeyPrefix("tenant7:");
        using var frame = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.Equal(new[] { "GET", "tenant7:user:1" }, Parse(frame.Span));
        Assert.Equal(new[] { "tenant7:user:1" }, Keys(frame));
    }

    [Fact]
    public void KeyPrefixComposesWithAKeyThatAlreadyHasOne()
    {
        // a key that already carries a prefix, as the KeyPrefixed* decorators produce today
        var prefixed = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("inner:"), "user:1");
        var ctx = new RespContext().WithKeyPrefix("outer:");
        using var frame = ctx.Execute($"{RedisCommand.GET}{prefixed}");

        Assert.Equal(new[] { "GET", "outer:inner:user:1" }, Parse(frame.Span));
    }

    [Fact]
    public void BothPrefixMechanismsRenderIdenticalBytes()
    {
        // decorator-applied prefix (rides on the key) vs context-applied prefix (applied at write time).
        // They are different RedisKey VALUES - RedisKey.Equals compares the carried prefix - but they must
        // be indistinguishable on the wire, which is what lets the rendered frame serve as a cache key.
        var viaDecorator = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("tenant7:"), "user:1");
        using var a = new RespContext().Execute($"{RedisCommand.GET}{viaDecorator}");
        using var b = new RespContext(keyPrefix: "tenant7:").Execute($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.True(a.Span.SequenceEqual(b.Span));
        Assert.Equal(new[] { "GET", "tenant7:user:1" }, Parse(a.Span));
    }

#if NET
    [Fact]
    public void ComposingBothPrefixMechanismsDoesNotAllocate()
    {
        // Both mechanisms have to coexist, so this is the permanent hot path - not an interim state.
        // RedisKey.WithPrefix has to allocate to combine two prefixes (its "two prefixes; darn" branch)
        // because it must hand back a RedisKey; the writer only ever needs the combined bytes.
        var decorated = RedisKey.WithPrefix(Encoding.UTF8.GetBytes("inner:"), "user:1");
        var ctx = new RespContext(keyPrefix: "outer:");

        for (int i = 0; i < 64; i++) ctx.Execute($"{RedisCommand.GET}{decorated}").Dispose(); // warm the pool

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) ctx.Execute($"{RedisCommand.GET}{decorated}").Dispose();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0, $"allocated {allocated} bytes over 128 renders");
    }
#endif

    [Fact]
    public void NestedWithKeyPrefixComposes()
    {
        var ctx = new RespContext().WithKeyPrefix("a:").WithKeyPrefix("b:");
        using var frame = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}");

        Assert.Equal(new[] { "GET", "a:b:k" }, Parse(frame.Span));
    }

    [Fact]
    public void ChannelPrefixIsApplied()
    {
        var ctx = new RespContext(channelPrefix: new RedisChannel("app:", RedisChannel.PatternMode.Literal));
        using var frame = ctx.Execute($"{RedisCommand.PUBLISH}{new RedisChannel("news", RedisChannel.PatternMode.Literal)}{(RedisValue)"hi"}");

        Assert.Equal(new[] { "PUBLISH", "app:news", "hi" }, Parse(frame.Span));
    }

    [Fact]
    public void ChannelPrefixIsSkippedWhenTheChannelOptsOut()
    {
        // keyspace notification channels are server-generated names, and opt out of the channel prefix
        var channel = new RedisChannel("__keyevent@0__:set", RedisChannel.RedisChannelOptions.IgnoreChannelPrefix);
        var ctx = new RespContext(channelPrefix: new RedisChannel("app:", RedisChannel.PatternMode.Literal));
        using var frame = ctx.Execute($"{RedisCommand.SUBSCRIBE}{channel}");

        Assert.Equal(new[] { "SUBSCRIBE", "__keyevent@0__:set" }, Parse(frame.Span));
    }

    [Fact]
    public void NoKeysMeansNoSlotAndNoMarks()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Execute($"{RedisCommand.ECHO}{(RedisValue)"hello"}");

        Assert.True(frame.HasNoKeys);
        Assert.Empty(Keys(frame));
        Assert.Equal(ServerSelectionStrategy.NoSlot, frame.Slot);
    }

    [Fact]
    public void OneAndTwoKeysResolveWithoutScanning()
    {
        var ctx = new RespContext();

        using (var one = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k1"}"))
        {
            Assert.False(one.KeysNeedScan);
            Assert.Equal(new[] { "k1" }, Keys(one));
        }

        using var two = ctx.Execute($"{RedisCommand.SMOVE}{(RedisKey)"src"}{(RedisKey)"dst"}{(RedisValue)"m"}");
        Assert.False(two.KeysNeedScan);
        Assert.Equal(new[] { "src", "dst" }, Keys(two));
    }

    [Fact]
    public void ThreeKeysFallBackToScanning()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute($"{RedisCommand.DEL}{(RedisKey)"a"}{(RedisKey)"b"}{(RedisKey)"c"}");

        Assert.True(frame.KeysNeedScan);
        Assert.Null(Keys(frame));
        Assert.Equal(new[] { "DEL", "a", "b", "c" }, Parse(frame.Span)); // still renders correctly
    }

    [Fact]
    public void StandaloneSkipsSlotComputation()
    {
        var ctx = new RespContext(serverType: ServerType.Standalone);
        using var frame = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"foo"}");

        Assert.Equal(ServerSelectionStrategy.NoSlot, frame.Slot);
    }

    [Fact]
    public void ClusterFoldsTheSlotFromTheWrittenBytes()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Execute($"{RedisCommand.GET}{(RedisKey)"foo"}");

        // published CLUSTER KEYSLOT value
        Assert.Equal(12182, frame.Slot);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"foo"), frame.Slot);
    }

    [Fact]
    public void SharedHashTagGivesOneSlot()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Execute($"{RedisCommand.SMOVE}{(RedisKey)"{u1}:a"}{(RedisKey)"{u1}:b"}{(RedisValue)"m"}");

        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"{u1}:a"), frame.Slot);
        Assert.NotEqual(ServerSelectionStrategy.MultipleSlots, frame.Slot);
    }

    [Fact]
    public void CrossSlotKeysAreDetected()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Execute($"{RedisCommand.SMOVE}{(RedisKey)"alpha"}{(RedisKey)"beta"}{(RedisValue)"m"}");

        Assert.Equal(ServerSelectionStrategy.MultipleSlots, frame.Slot);
    }

    [Fact]
    public void SlotIsComputedFromThePrefixedKey()
    {
        var plain = new RespContext(serverType: ServerType.Cluster);
        var prefixed = plain.WithKeyPrefix("tenant7:");

        using var a = plain.Execute($"{RedisCommand.GET}{(RedisKey)"user:1"}");
        using var b = prefixed.Execute($"{RedisCommand.GET}{(RedisKey)"user:1"}");

        Assert.NotEqual(a.Slot, b.Slot);
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"tenant7:user:1"), b.Slot);
    }

    [Fact]
    public void CancellationIsObservedAndTheBufferIsReturned()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = new RespContext().WithCancellationToken(cts.Token);

        Assert.Throws<OperationCanceledException>(() => ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}").Dispose());
    }

    [Fact]
    public void CancellationTokenFlowsThroughWithClones()
    {
        using var cts = new CancellationTokenSource();
        var ctx = new RespContext().WithCancellationToken(cts.Token).WithKeyPrefix("p:").WithDatabase(3);

        Assert.Equal(cts.Token, ctx.CancellationToken);
        Assert.Equal(3, ctx.Database);
        Assert.Equal((RedisKey)"p:", ctx.KeyPrefix);
    }

    [Fact]
    public void MultiByteAndEmptyPayloadsRoundTrip()
    {
        var ctx = new RespContext();
        using var frame = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"naïve☃"}{(RedisValue)""}");

        Assert.Equal(new[] { "SET", "naïve☃", "" }, Parse(frame.Span));
    }

    [Fact]
    public void LargePayloadForcesBufferGrowthMidBuild()
    {
        var big = new string('x', 5000);
        var ctx = new RespContext();
        using var frame = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)big}");

        var args = Parse(frame.Span);
        Assert.Equal(big, args[2]);

        // the reserved prologue and the key offset must survive the regrow
        Assert.Equal(new[] { "k" }, Keys(frame));
    }
}
