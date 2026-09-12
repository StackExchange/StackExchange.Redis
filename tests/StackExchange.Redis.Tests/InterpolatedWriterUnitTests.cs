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
    public void DatabaseIsNotPartOfTheRenderedFrame()
    {
        // SELECT is a separate command on the connection, so the same logical command renders IDENTICALLY
        // on different databases. Cache identity therefore needs (frame, database) - the frame alone is not
        // enough, which is easy to miss because everything else that matters (prefix, renamed command,
        // arguments) IS in the bytes.
        using var a = new RespContext(database: 0).Execute($"{RedisCommand.GET}{(RedisKey)"k"}");
        using var b = new RespContext(database: 3).Execute($"{RedisCommand.GET}{(RedisKey)"k"}");

        Assert.True(a.Span.SequenceEqual(b.Span));
        Assert.Equal(0, new RespContext(database: 0).Database);
        Assert.Equal(3, new RespContext(database: 3).Database);
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

    // ---- deferred composition: optional / contextual arguments -------------------------------------

    [Theory]
    [InlineData(false, false, "SET|k|v")]
    [InlineData(true, false, "SET|k|v|EX|300")]
    [InlineData(false, true, "SET|k|v|NX")]
    [InlineData(true, true, "SET|k|v|EX|300|NX")]
    public void ComposeThenConditionallyAppend(bool withTtl, bool withNx, string expected)
    {
        var ctx = new RespContext();
        var cmd = ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        try
        {
            if (withTtl)
            {
                cmd.AppendFormatted((RedisValue)"EX");
                cmd.AppendFormatted((RedisValue)300);
            }

            if (withNx) cmd.AppendFormatted((RedisValue)"NX");

            using var frame = ctx.Execute(ref cmd);
            Assert.Equal(expected, string.Join("|", Parse(frame.Span)));
            Assert.Equal(expected.Split('|').Length, frame.ArgCount);
        }
        catch
        {
            cmd.Dispose(); // Execute takes ownership on success; this covers the throwing window
            throw;
        }
    }

    [Fact]
    public void ComposedKeysStillTrackAndRoute()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        var cmd = ctx.Compose($"{RedisCommand.SMOVE}{(RedisKey)"{u}:src"}");
        cmd.AppendFormatted((RedisKey)"{u}:dst");   // second key arrives AFTER the interpolation
        cmd.AppendFormatted((RedisValue)"m");
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal(new[] { "SMOVE", "{u}:src", "{u}:dst", "m" }, Parse(frame.Span));
        Assert.Equal(new[] { "{u}:src", "{u}:dst" }, Keys(frame));
        Assert.Equal(ServerSelectionStrategy.GetHashSlot((RedisKey)"{u}:src"), frame.Slot);
    }

    [Fact]
    public void ComposedHeaderGrowsWithLateArguments()
    {
        // 3 args at the call site, 12 by the time it is executed: '*3' would have been wrong
        var ctx = new RespContext();
        var cmd = ctx.Compose($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");
        for (int i = 0; i < 9; i++) cmd.AppendFormatted((RedisValue)i);
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal(12, frame.ArgCount);
        Assert.StartsWith("*12\r\n", Encoding.UTF8.GetString(frame.Span.ToArray()));
        Assert.Equal(new[] { "k" }, Keys(frame)); // offset survived the two-digit header
    }

    // ---- initializing with the command as a real argument -----------------------------------------

    [Fact]
    public void ComposeWithCommandArgument()
    {
        var ctx = new RespContext();
        var cmd = ctx.Compose(RedisCommand.SET, $"{(RedisKey)"k"}{(RedisValue)"v"}");
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal(new[] { "SET", "k", "v" }, Parse(frame.Span));
        Assert.Equal(new[] { "k" }, Keys(frame));
    }

    [Fact]
    public void ExecuteWithCommandArgument()
    {
        var ctx = new RespContext(serverType: ServerType.Cluster);
        using var frame = ctx.Execute(RedisCommand.GET, $"{(RedisKey)"foo"}");

        Assert.Equal(new[] { "GET", "foo" }, Parse(frame.Span));
        Assert.Equal(12182, frame.Slot);
    }

    [Fact]
    public void ComposeWithNoInterpolationAtAll()
    {
        // fully dynamic argument list - variadic DEL over a runtime-sized set of keys
        var keys = new RedisKey[] { "a", "b", "c" };
        var ctx = new RespContext();
        var cmd = ctx.Compose(RedisCommand.DEL, keys.Length);
        foreach (var key in keys) cmd.AppendFormatted(key);
        using var frame = ctx.Execute(ref cmd);

        Assert.Equal(new[] { "DEL", "a", "b", "c" }, Parse(frame.Span));
        Assert.Equal(4, frame.ArgCount);
        Assert.True(frame.KeysNeedScan); // three keys exceeds the two inline offsets
    }

    [Fact]
    public void DisabledCommandThrowsFromBothInitializerForms()
    {
        // Note the command-as-argument form resolves the map before renting, while the hole form rents
        // first and discovers it on the first append - but a throwing interpolation abandoning its buffer
        // is accepted behaviour either way; DefaultInterpolatedStringHandler does exactly the same.
        var map = CommandMap.Create(new Dictionary<string, string?> { ["get"] = null });
        var ctx = new RespContext(map);

        Assert.Throws<RedisCommandException>(() => ctx.Compose(RedisCommand.GET, 0).Dispose());
        Assert.Throws<RedisCommandException>(() => ctx.Execute($"{RedisCommand.GET}{(RedisKey)"k"}").Dispose());
    }

    // ---- the single-space relaxation ---------------------------------------------------------------

    [Fact]
    public void SingleSpacesAreAllowedAndDiscarded()
    {
        var ctx = new RespContext();
        using var spaced = ctx.Execute($"{RedisCommand.SET} {(RedisKey)"k"} {(RedisValue)"v"}");
        using var tight = ctx.Execute($"{RedisCommand.SET}{(RedisKey)"k"}{(RedisValue)"v"}");

        // identical bytes: the space is a literal segment, not an argument
        Assert.True(spaced.Span.SequenceEqual(tight.Span));
        Assert.Equal(3, spaced.ArgCount);
        Assert.Equal(new[] { "SET", "k", "v" }, Parse(spaced.Span));
        Assert.Equal(new[] { "k" }, Keys(spaced));
    }

    [Fact]
    public void OtherLiteralsAreRejected()
    {
        var ctx = new RespContext();

        // two spaces look identical to one on the page; this is why the analyzer has to carry the rule
        Assert.Throws<ArgumentException>(() => ctx.Execute($"{RedisCommand.GET}  {(RedisKey)"k"}").Dispose());
        Assert.Throws<ArgumentException>(() => ctx.Execute($"{RedisCommand.GET}-{(RedisKey)"k"}").Dispose());
        Assert.Throws<ArgumentException>(() => ctx.Execute($"SET {(RedisKey)"k"}").Dispose());
    }
}
