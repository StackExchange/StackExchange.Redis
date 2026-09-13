using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Writing must not assume <see cref="IBufferWriter{T}.GetSpan"/> honoured the size hint.
/// </summary>
/// <remarks>
/// <para>
/// The hint is a hint. <c>CycleBuffer.GetUncommittedMemory</c> caps it at 1k, sizes fresh segments from the
/// committed total rather than the hint, and - the case with no floor at all - hands back a dangling recycled
/// segment as-is, whatever length that happens to be. So a writer may legitimately return less than asked.
/// </para>
/// <para>
/// These use a writer that returns deliberately short spans, which is what the buffer does intermittently
/// under concurrency. Reported as ArgumentOutOfRangeException from MessageWriter.WriteUnifiedSpan.
/// </para>
/// </remarks>
public class BufferWriterHintTests
{
    /// <summary>
    /// An <see cref="IBufferWriter{T}"/> that honours at most <paramref name="max"/> bytes per span, and
    /// detects writes that run past the span it handed out.
    /// </summary>
    /// <remarks>
    /// The span comes from an isolated scratch array with a canary beyond it, checked on every Advance. A
    /// plain growing buffer cannot catch this: an overrun would land harmlessly in the slack that follows,
    /// and the test would pass while the real writer corrupts whatever is next in the pool.
    /// </remarks>
    private sealed class StingyWriter(int max) : IBufferWriter<byte>
    {
        private const int Canary = 64;
        private const byte CanaryByte = 0xCD;

        /// <summary>A generous but finite ceiling, so the "unconstrained" baseline stays allocatable.</summary>
        private const int Unconstrained = 64 * 1024;

        private readonly int _max = Math.Min(max, Unconstrained);
        private byte[] _scratch = [];
        private int _handedOut;
        private readonly List<byte> _written = [];

        /// <summary>Everything advanced so far, with the outstanding span checked first.</summary>
        public byte[] Written
        {
            get
            {
                CheckCanary();
                return _written.ToArray();
            }
        }

        public void Advance(int count)
        {
            if (count < 0 || count > _handedOut)
            {
                throw new InvalidOperationException($"Advance({count}) but only {_handedOut} was handed out");
            }

            CheckCanary();
            for (var i = 0; i < count; i++) _written.Add(_scratch[i]);

            // retire the scratch rather than just zeroing the count: what is left in it is written data, and
            // a later canary check would otherwise read that as corruption
            _scratch = [];
            _handedOut = 0;
        }

        // Two ways this harness is deliberately stricter than a real writer, both of which only matter if a
        // caller misbehaves: a partial Advance discards the unadvanced tail rather than keeping it at the
        // write position (uncommitted bytes, so nothing legitimate can observe the difference), and a second
        // Advance without an intervening hand-out throws rather than silently double-counting.

        /// <summary>
        /// Verify nothing was written past the span we handed out.
        /// </summary>
        /// <remarks>
        /// Called on Advance, on the next hand-out, and on reading the result - not just on Advance, because
        /// a span written past and then abandoned without advancing would otherwise slip through, and this
        /// harness is the load-bearing part of these tests.
        /// </remarks>
        private void CheckCanary()
        {
            for (var i = _handedOut; i < _scratch.Length; i++)
            {
                if (_scratch[i] != CanaryByte)
                {
                    throw new InvalidOperationException(
                        $"buffer overrun: first byte past a span of {_handedOut} is at +{i - _handedOut}");
                }
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => Hand(sizeHint).AsMemory(0, _handedOut);

        public Span<byte> GetSpan(int sizeHint = 0) => Hand(sizeHint).AsSpan(0, _handedOut);

        private byte[] Hand(int sizeHint)
        {
            CheckCanary(); // the previous span, if it was abandoned rather than advanced

            _handedOut = Math.Max(1, Math.Min(_max, sizeHint <= 0 ? _max : sizeHint));
            _scratch = new byte[_handedOut + Canary];
            _scratch.AsSpan(_handedOut).Fill(CanaryByte);
            return _scratch;
        }
    }

    /// <remarks>
    /// Built directly rather than through <c>TestHarness.Write</c>, which pins the database to -1 and so
    /// cannot issue the key-bearing commands this needs.
    /// </remarks>
    private static string Render(int max, string command, params object[] args)
    {
        var writer = new StingyWriter(max);
        var message = new RedisDatabase.ExecuteMessage(CommandMap.Default, 0, CommandFlags.None, command, args);
        message.WriteTo(new MessageWriter(null, CommandMap.Default, writer));
        return Encoding.UTF8.GetString(writer.Written).Replace("\r\n", "|");
    }

    [Theory]
    // 8 is below every fixed-size hint in the writer; 16 and 64 straddle the length-prefix sites; 520 is
    // just under the 5 + MaxInt32TextLen + 512 that the quick-span path asks for
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(520)]
    public void ShortSpansStillProduceCorrectFrames(int max)
    {
        var expected = Render(64 * 1024, "SET", "mykey", "myvalue");
        Assert.Equal("*3|$3|SET|$5|mykey|$7|myvalue|", expected);
        Assert.Equal(expected, Render(max, "SET", "mykey", "myvalue"));
    }

    [Theory]
    [InlineData(8, 500)]
    [InlineData(8, 512)]
    [InlineData(8, 513)]
    // NOTE 520 does NOT stress the string path: a 512-byte string asks for exactly 512, which a 520-cap
    // writer honours, so these pass on main too. Kept as regression cover, not as evidence.
    [InlineData(520, 512)]
    [InlineData(520, 5000)]
    public void ShortSpansStillProduceCorrectFramesForLargeValues(int max, int payload)
    {
        var value = new string('x', payload);
        var expected = Render(64 * 1024, "SET", "mykey", value);
        Assert.Equal(expected, Render(max, "SET", "mykey", value));
    }

    [Theory]
    // The reported crash shape: WriteUnifiedSpan with a sizeable BINARY value, which asks for
    // 5 + MaxInt32TextLen + length - up to 528 - and previously wrote it unchecked. byte[] takes a
    // different route from string, and the value-shapes test only reaches it with four bytes.
    [InlineData(8, 400)]
    [InlineData(400, 400)]     // asks 416, gets 400
    [InlineData(520, 512)]     // asks 528, gets 520 - straddles MaxQuickSpanSize with no slack
    [InlineData(520, 513)]     // one over, so the quick path is skipped and the prefix path runs
    [InlineData(64, 4096)]
    public void ShortSpansStillProduceCorrectFramesForLargeBinaryValues(int max, int payload)
    {
        var value = new byte[payload];
        for (var i = 0; i < value.Length; i++) value[i] = (byte)(i % 251);

        var expected = Render(64 * 1024, "SET", "mykey", value);
        Assert.Equal(expected, Render(max, "SET", "mykey", value));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(40)]
    public void ShortSpansStillProduceCorrectFramesForEveryValueShape(int max)
    {
        // one of each writer path: int64, uint64-range, double, byte[], empty, and a long string
        object[] args =
        [
            "mykey",
            1234567890,
            long.MinValue,
            ulong.MaxValue,
            1.5d,
            new byte[] { 1, 2, 3, 250 },
            "",
            new string('x', 600),
        ];

        Assert.Equal(Render(64 * 1024, "SET", args), Render(max, "SET", args));
    }

    [Fact]
    public void TheBaselineIsItselfCorrect()
    {
        // guards against both sides being equally wrong
        Assert.Equal("*4|$3|SET|$5|mykey|$3|1.5|$2|-1|", Render(64 * 1024, "SET", "mykey", 1.5d, -1));
    }

    // ---- paths the message shapes above do not reach ------------------------------------------------
    // Called directly: these are internal, and routing to them through a message would pin the test to
    // whichever command happens to use them today.

    private static string RenderDirect(int max, Action<IBufferWriter<byte>> write)
    {
        var writer = new StingyWriter(max);
        write(writer);
        return Encoding.UTF8.GetString(writer.Written).Replace("\r\n", "|");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(46)]  // one short of the 47 this path needs, so the fallback is forced
    [InlineData(47)]
    public void Sha1AsHexSurvivesShortSpans(int max)
    {
        // the tightest fit in the writer: 47 requested, exactly 47 written, no slack at all
        var hash = new byte[20];
        for (var i = 0; i < hash.Length; i++) hash[i] = (byte)(i * 11);

        static Action<IBufferWriter<byte>> Write(byte[] hash)
            => w => new MessageWriter(null, CommandMap.Default, w).WriteSha1AsHex(hash);

        var expected = RenderDirect(64 * 1024, Write(hash));
        Assert.Equal(42, expected.Replace("|", "\r\n").Length - 5); // $40 CRLF + 40 hex + CRLF
        Assert.Equal(expected, RenderDirect(max, Write(hash)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void KeyspacePrefixesSurviveShortSpans(int max)
    {
        // WithKeyPrefix / channel prefixes: a real user-facing path, and prefixed writes are two-part
        var prefix = Encoding.UTF8.GetBytes("tenant7:");

        static Action<IBufferWriter<byte>> WriteString(byte[] prefix)
            => w => MessageWriter.WriteUnifiedPrefixedString(w, prefix, "user:1");

        Assert.Equal("$14|tenant7:user:1|", RenderDirect(64 * 1024, WriteString(prefix)));
        Assert.Equal("$14|tenant7:user:1|", RenderDirect(max, WriteString(prefix)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void MultiBulkHeaderWithPrefixSurvivesShortSpans(int max)
    {
        static Action<IBufferWriter<byte>> Write()
            => w => MessageWriter.WriteMultiBulkHeader(w, 4, RespPrefix.Map);

        Assert.Equal("%2|", RenderDirect(64 * 1024, Write()));
        Assert.Equal("%2|", RenderDirect(max, Write()));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void IntegerSurvivesShortSpans(int max)
    {
        // server-side only (toys/StackExchange.Redis.Server), but it is the same shape
        static Action<IBufferWriter<byte>> Write()
            => w => MessageWriter.WriteInteger(w, long.MinValue);

        Assert.Equal(":-9223372036854775808|", RenderDirect(64 * 1024, Write()));
        Assert.Equal(":-9223372036854775808|", RenderDirect(max, Write()));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(22)]  // one short of the int64 width a long count can need
    public void CountPrefixIsSizedForALong(int max)
    {
        // DEFENSIVE, not a fix for a reachable bug. WriteMultiBulkHeader takes a long and hints
        // 3 + MaxInt32TextLen (14) on main, which is short of the 23 a long can need - but nothing can
        // reach it: argument counts come from array lengths, so they are int-bounded in practice. The
        // shared WriteCountPrefix here sizes for the parameter type rather than for today's callers,
        // and this pins that down so a future long-valued caller cannot reintroduce a short hint.
        static Action<IBufferWriter<byte>> Write()
            => w => MessageWriter.WriteMultiBulkHeader(w, long.MaxValue);

        Assert.Equal("*9223372036854775807|", RenderDirect(64 * 1024, Write()));
        Assert.Equal("*9223372036854775807|", RenderDirect(max, Write()));
    }

    // ---- the repo's own writer, with no synthetic writer involved ------------------------------------

    /// <summary>
    /// <c>BlockBuffer</c> under-delivers deterministically, so this needs no contrived writer at all.
    /// </summary>
    /// <remarks>
    /// <c>BlockBuffer.GetBuffer</c> clamps the hint to [16, 128] and then hands back whatever is left in the
    /// block - its own comment says so: "this isn't an actual max, just a max of what we guarantee; we give
    /// the caller whatever is left in the buffer". So any request above 128 is under-served whenever the
    /// block has between 128 and 527 bytes remaining, which covers the quick-span path (up to 528) and the
    /// string encode (up to 512). No concurrency, no recycled segments, no CycleBuffer - just capacity.
    /// <para>
    /// <see cref="TestHarness"/> writes through <c>MessageWriter.BlockBuffer</c>, so this is the shipped
    /// path end to end.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(400)]
    [InlineData(450)]
    [InlineData(500)]
    public void BlockBufferUnderDeliversWithoutAnyContrivedWriter(int size)
    {
        var harness = new TestHarness();
        var value = new string('x', size);

        // several arguments in a row, so the block fills and a later one lands in the 128..527 window
        object[] args = [value, value, value, value, value, value];

        var frame = harness.Write("ECHO", args);
        var text = Encoding.UTF8.GetString(frame).Replace("\r\n", "|");

        Assert.StartsWith("*7|$4|ECHO|", text);
        Assert.Equal(6, CountOccurrences(text, "$" + size + "|" + value + "|"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
