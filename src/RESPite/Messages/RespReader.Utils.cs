using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RESPite.Internal;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Messages;

public ref partial struct RespReader
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnsafeAssertClLf(int offset) => UnsafeAssertClLf(ref UnsafeCurrent, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnsafeAssertClLf(scoped ref byte source, int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)) != RespConstants.CrLfUInt16)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

    private enum LengthPrefixResult
    {
        NeedMoreData,
        Length,
        Null,
        Streaming,
    }

    /// <summary>
    /// Asserts that the current element is a scalar type.
    /// </summary>
    public readonly void DemandScalar()
    {
        if (!IsScalar) Throw(Prefix);
        static void Throw(RespPrefix prefix) => throw new InvalidOperationException($"This operation requires a scalar element, got {prefix}");
    }

    /// <summary>
    /// Asserts that the current element is a scalar type.
    /// </summary>
    public readonly void DemandAggregate()
    {
        if (!IsAggregate) Throw(Prefix);
        static void Throw(RespPrefix prefix) => throw new InvalidOperationException($"This operation requires an aggregate element, got {prefix}");
    }

    private static LengthPrefixResult TryReadLengthPrefix(ReadOnlySpan<byte> bytes, out int value, out int byteCount)
    {
        var end = bytes.IndexOf(RespConstants.CrlfBytes);
        if (end < 0)
        {
            byteCount = value = 0;
            if (bytes.Length >= RespConstants.MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return LengthPrefixResult.NeedMoreData;
        }
        byteCount = end + 2;
        switch (end)
        {
            case 0:
                ThrowProtocolFailure("Length prefix expected");
                goto case default; // not reached, just satisfying definite assignment
            case 1 when bytes[0] == (byte)'?':
                value = 0;
                return LengthPrefixResult.Streaming;
            default:
                if (end > RespConstants.MaxRawBytesInt32 || !(Utf8Parser.TryParse(bytes, out value, out var consumed) && consumed == end))
                {
                    ThrowProtocolFailure("Unable to parse integer");
                    value = 0;
                }
                if (value < 0)
                {
                    if (value == -1)
                    {
                        value = 0;
                        return LengthPrefixResult.Null;
                    }
                    ThrowProtocolFailure("Invalid negative length prefix");
                }
                return LengthPrefixResult.Length;
        }
    }

    private readonly RespReader Clone() => this; // useful for performing streaming operations without moving the primary

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    private static void ThrowProtocolFailure(string message)
        => throw new InvalidOperationException("RESP protocol failure: " + message); // protocol exception?

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    internal static void ThrowEOF() => throw new EndOfStreamException();

    [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
    private static void ThrowFormatException() => throw new FormatException();

    private int RawTryReadByte()
    {
        if (_bufferIndex < CurrentLength || TryMoveToNextSegment())
        {
            var result = UnsafeCurrent;
            _bufferIndex++;
            return result;
        }
        return -1;
    }

    private int RawPeekByte()
    {
        return (CurrentLength < _bufferIndex || TryMoveToNextSegment()) ? UnsafeCurrent : -1;
    }

    private bool RawAssertCrLf()
    {
        if (CurrentAvailable >= 2)
        {
            UnsafeAssertClLf(0);
            _bufferIndex += 2;
            return true;
        }
        else
        {
            int next = RawTryReadByte();
            if (next < 0) return false;
            if (next == '\r')
            {
                next = RawTryReadByte();
                if (next < 0) return false;
                if (next == '\n') return true;
            }
            ThrowProtocolFailure("Expected CR/LF");
            return false;
        }
    }

    private LengthPrefixResult RawTryReadLengthPrefix()
    {
        _length = 0;
        if (!RawTryFindCrLf(out int end))
        {
            if (TotalAvailable >= RespConstants.MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return LengthPrefixResult.NeedMoreData;
        }

        switch (end)
        {
            case 0:
                ThrowProtocolFailure("Length prefix expected");
                goto case default; // not reached, just satisfying definite assignment
            case 1:
                var b = (byte)RawTryReadByte();
                RawAssertCrLf();
                if (b == '?')
                {
                    return LengthPrefixResult.Streaming;
                }
                else
                {
                    _length = ParseSingleDigit(b);
                    return LengthPrefixResult.Length;
                }
            default:
                if (end > RespConstants.MaxRawBytesInt32)
                {
                    ThrowProtocolFailure("Unable to parse integer");
                }
                Span<byte> bytes = stackalloc byte[end];
                RawFillBytes(bytes);
                RawAssertCrLf();
                if (!(Utf8Parser.TryParse(bytes, out _length, out var consumed) && consumed == end))
                {
                    ThrowProtocolFailure("Unable to parse integer");
                }

                if (_length < 0)
                {
                    if (_length == -1)
                    {
                        _length = 0;
                        return LengthPrefixResult.Null;
                    }
                    ThrowProtocolFailure("Invalid negative length prefix");
                }

                return LengthPrefixResult.Length;
        }
    }

    private void RawFillBytes(scoped Span<byte> target)
    {
        do
        {
            var current = CurrentSpan();
            if (current.Length >= target.Length)
            {
                // more than enough, need to trim
                current.Slice(0, target.Length).CopyTo(target);
                _bufferIndex += target.Length;
                return; // we're done
            }
            else
            {
                // take what we can
                current.CopyTo(target);
                target = target.Slice(current.Length);
                // we could move _bufferIndex here, but we're about to trash that in TryMoveToNextSegment
            }
        }
        while (TryMoveToNextSegment());
        ThrowEOF();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseSingleDigit(byte value)
    {
        return value switch
        {
            (byte)'0' or (byte)'1' or (byte)'2' or (byte)'3' or (byte)'4' or (byte)'5' or (byte)'6' or (byte)'7' or (byte)'8' or (byte)'9' => value - (byte)'0',
            _ => Invalid(value),
        };

        [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
        static int Invalid(byte value) => throw new FormatException($"Unable to parse integer: '{(char)value}'");
    }

    private readonly bool RawTryAssertInlineScalarPayloadCrLf()
    {
        Debug.Assert(IsInlineScalar, "should be inline scalar");

        var reader = Clone();
        var len = reader._length;
        if (len == 0) return reader.RawAssertCrLf();

        do
        {
            var current = reader.CurrentSpan();
            if (current.Length >= len)
            {
                reader._bufferIndex += len;
                return reader.RawAssertCrLf(); // we're done
            }
            else
            {
                // take what we can
                len -= current.Length;
                // we could move _bufferIndex here, but we're about to trash that in TryMoveToNextSegment
            }
        }
        while (reader.TryMoveToNextSegment());
        return false; // EOF
    }

    private readonly bool RawTryFindCrLf(out int length)
    {
        length = 0;
        RespReader reader = Clone();
        do
        {
            var span = reader.CurrentSpan();
            var index = span.IndexOf((byte)'\r');
            if (index >= 0)
            {
                checked
                {
                    length += index;
                }
                // move past the CR and assert the LF
                reader._bufferIndex += index + 1;
                var next = reader.RawTryReadByte();
                if (next < 0) break; // we don't know
                if (next != '\n') ThrowProtocolFailure("CR/LF expected");

                return true;
            }
            checked
            {
                length += span.Length;
            }
        }
        while (reader.TryMoveToNextSegment());
        length = 0;
        return false;
    }

    private string GetDebuggerDisplay()
    {
        return ToString();
    }

    internal int GetInitialScanCount(out ushort streamingAggregateDepth)
    {
        // this is *similar* to GetDelta, but: without any discount for attributes
        switch (_flags & (RespFlags.IsAggregate | RespFlags.IsStreaming))
        {
            case RespFlags.IsAggregate:
                streamingAggregateDepth = 0;
                return _length - 1;
            case RespFlags.IsAggregate | RespFlags.IsStreaming:
                streamingAggregateDepth = 1;
                return 0;
            default:
                streamingAggregateDepth = 0;
                return -1;
        }
    }
}
