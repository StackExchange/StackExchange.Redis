using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis;

internal static class EncodingExtensions
{
    public static int GetCharCount(this Encoding encoding, in ReadOnlySequence<byte> seq)
    {
        var count = 0;
        foreach (var memory in seq)
        {
            count += encoding.GetCharCount(memory.Span);
        }
        return count;
    }

#if NET461 || NET472 || NETSTANDARD2_0
    public static int GetChars(this Encoding encoding, in ReadOnlySequence<byte> seq, Span<char> chars)
    {
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));

        if (seq.IsSingleSegment)
        {
            return encoding.GetChars(seq.First.Span, chars);
        }
        else
        {
            var count = 0;
            foreach (var memory in seq)
            {
                count += encoding.GetChars(memory.Span, chars);
                chars = chars.Slice(memory.Length);
            }
            return count;
        }
    }
#endif
}
