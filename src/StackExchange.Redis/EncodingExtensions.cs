using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis;

internal static class EncodingExtensions
{
    // Above this length we stream through a Decoder; at or below it we linearize onto the stack and use the
    // contiguous span overloads, avoiding the Decoder heap allocation for the common (small) case.
    private const int MaxStackLinearizeBytes = 128;

    // Note: there is no BCL Encoding.GetCharCount(in ReadOnlySequence<byte>) on any TFM (unlike GetChars,
    // which the BCL provides on modern runtimes), so we supply it for all targets.
    public static int GetCharCount(this Encoding encoding, in ReadOnlySequence<byte> seq)
    {
        // common case: a single segment can be measured directly, with no decoder state to track
        if (seq.IsSingleSegment) return encoding.GetCharCount(seq.FirstSpan);

        // small payloads: linearize onto the stack and measure contiguously - no Decoder allocation, and no
        // glyph-straddles-a-boundary problem once the bytes are contiguous
        long length = seq.Length;
        if (length <= MaxStackLinearizeBytes)
        {
            Span<byte> linear = stackalloc byte[(int)length];
            seq.CopyTo(linear);
            return encoding.GetCharCount(linear);
        }

        // larger multi-segment: a multi-byte glyph can straddle a segment boundary, so we *must* decode with
        // a stateful decoder rather than summing per-segment counts (which would over-count split glyphs).
        // Note we cannot use Decoder.GetCharCount: unlike GetChars/Convert it does not carry partial-glyph
        // state between calls, so it too over-counts. Decoder.Convert reports the chars produced without us
        // having to keep them, so we decode into a small scratch buffer and discard the output, flushing on
        // the final segment.
        var decoder = encoding.GetDecoder();
        Span<char> scratch = stackalloc char[128];
        int count = 0;
        var position = seq.Start;
        bool have = seq.TryGet(ref position, out var current);
        while (have)
        {
            var nextPosition = position;
            bool haveNext = seq.TryGet(ref nextPosition, out var next);
            var bytes = current.Span;
            bool flush = !haveNext;
            bool completed;
            do
            {
                decoder.Convert(bytes, scratch, flush, out int bytesUsed, out int charsUsed, out completed);
                count += charsUsed;
                bytes = bytes.Slice(bytesUsed);
            }
            while (!bytes.IsEmpty || (flush && !completed));
            current = next;
            position = nextPosition;
            have = haveNext;
        }
        return count;
    }

#if NET461 || NET472 || NETSTANDARD2_0
    // modern runtimes have a BCL Encoding.GetChars(in ReadOnlySequence<byte>, Span<char>); we only need to
    // supply it for the older targets. The flush-on-final-segment logic mirrors GetCharCount above, which
    // guarantees the two agree on the char count - so a buffer sized via GetCharCount cannot overflow here.
    public static int GetChars(this Encoding encoding, in ReadOnlySequence<byte> seq, Span<char> chars)
    {
        if (seq.IsSingleSegment) return encoding.GetChars(seq.FirstSpan, chars);

        // small payloads: linearize onto the stack and decode contiguously - no Decoder allocation
        long length = seq.Length;
        if (length <= MaxStackLinearizeBytes)
        {
            Span<byte> linear = stackalloc byte[(int)length];
            seq.CopyTo(linear);
            return encoding.GetChars(linear, chars);
        }

        var decoder = encoding.GetDecoder();
        int total = 0;
        var position = seq.Start;
        bool have = seq.TryGet(ref position, out var current);
        while (have)
        {
            var nextPosition = position;
            bool haveNext = seq.TryGet(ref nextPosition, out var next);
            int written = decoder.GetChars(current.Span, chars, flush: !haveNext);
            chars = chars.Slice(written); // advance by chars *written*, not by bytes consumed
            total += written;
            current = next;
            position = nextPosition;
            have = haveNext;
        }
        return total;
    }
#endif
}
