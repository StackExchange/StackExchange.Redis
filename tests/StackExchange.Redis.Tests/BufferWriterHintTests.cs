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

    // ---- the RESP writer's IBufferWriter paths, called directly ------------------------------------

    private static string RenderDirect(int max, Action<IBufferWriter<byte>> write)
    {
        var writer = new StingyWriter(max);
        write(writer);
        return Encoding.UTF8.GetString(writer.Written).Replace("\r\n", "|");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void KeyspacePrefixesSurviveShortSpans(int max)
    {
        // WithKeyPrefix / channel prefixes: a real user-facing path, and prefixed writes are two-part
        var prefix = Encoding.UTF8.GetBytes("tenant7:");

        static Action<IBufferWriter<byte>> WriteString(byte[] prefix)
            => w => RespWire.WriteUnifiedPrefixedString(w, prefix, "user:1");

        Assert.Equal("$14|tenant7:user:1|", RenderDirect(64 * 1024, WriteString(prefix)));
        Assert.Equal("$14|tenant7:user:1|", RenderDirect(max, WriteString(prefix)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void MultiBulkHeaderWithPrefixSurvivesShortSpans(int max)
    {
        static Action<IBufferWriter<byte>> Write()
            => w => RespWire.WriteMultiBulkHeader(w, 4, RespPrefix.Map);

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
            => w => RespWire.WriteInteger(w, long.MinValue);

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
            => w => RespWire.WriteMultiBulkHeader(w, long.MaxValue);

        Assert.Equal("*9223372036854775807|", RenderDirect(64 * 1024, Write()));
        Assert.Equal("*9223372036854775807|", RenderDirect(max, Write()));
    }

    // ---- the repo's own writer, with no synthetic writer involved ------------------------------------

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
