#if NET461 || NET472 || NETSTANDARD2_0
using System;
using System.Buffers;
using System.Text;

namespace StackExchange.Redis;

internal static class EncodingExtensions
{
    public static int GetChars(this Encoding encoding, in ReadOnlySequence<byte> bytes, Span<char> chars)
    {
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));

        if (bytes.IsSingleSegment)
        {
            return encoding.GetChars(bytes.First.Span, chars);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
#endif
