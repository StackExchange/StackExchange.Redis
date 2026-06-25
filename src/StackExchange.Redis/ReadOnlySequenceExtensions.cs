using System;
using System.Buffers;
using System.Diagnostics;

namespace StackExchange.Redis;

internal static class ReadOnlySequenceExtensions
{
    public static bool StartsWith(this in ReadOnlySequence<byte> sequence, in ReadOnlySequence<byte> value)
    {
        if (sequence.IsSingleSegment) return sequence.FirstSpan.StartsWith(value);
        if (value.IsSingleSegment) return sequence.StartsWith(value.FirstSpan);
        if (value.Length > sequence.Length) return false;

        return sequence.Slice(0, value.Length).SequenceEqual(value);
    }

    public static bool StartsWith(this ReadOnlySpan<byte> span, in ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment) return span.StartsWith(value.FirstSpan);
        if (value.Length > span.Length) return false;
        foreach (var memory in value)
        {
            if (!memory.Span.SequenceEqual(span.Slice(0, memory.Length)))
                return false;

            span = span.Slice(memory.Length);
        }
        return true;
    }

    public static bool StartsWith(this in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value)
        => StartsWith(sequence, value, sequence.Start);

    public static bool StartsWith(this in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value, SequencePosition start)
    {
        var valueLength = value.Length;
        int valueLengthPart = 0;
        while (sequence.TryGet(ref start, out var memory))
        {
            var spanLength = memory.Length;
            if (spanLength == 0) continue;

            var span = memory.Span;
            if (valueLengthPart > 0)
            {
                Debug.Assert(valueLength > valueLengthPart);

                var remainder = valueLength - valueLengthPart;
                if (remainder > spanLength)
                {
                    if (span.SequenceEqual(value.Slice(valueLengthPart, spanLength)))
                    {
                        valueLengthPart += spanLength;
                        continue;
                    }
                }
                else if (remainder == spanLength)
                {
                    if (span.SequenceEqual(value.Slice(valueLengthPart)))
                    {
                        return true;
                    }
                }
                else if (span.StartsWith(value.Slice(valueLengthPart)))
                {
                    return true;
                }
                return false;
            }

            if (spanLength >= valueLength)
            {
                return span.StartsWith(value);
            }

            if (!value.Slice(0, spanLength).SequenceEqual(span))
                return false;

            valueLengthPart = spanLength;
        }

        return false;
    }

    public static bool SequenceEqual(this in ReadOnlySequence<byte> first, ReadOnlySpan<byte> other)
    {
        if (first.IsSingleSegment) return first.FirstSpan.SequenceEqual(other);
        if (first.Length != other.Length) return false;

        var position = first.Start;
        while (first.TryGet(ref position, out var memory))
        {
            var span = memory.Span;

            if (!span.SequenceEqual(other.Slice(0, span.Length))) return false;

            other = other.Slice(span.Length);
        }

        return other.IsEmpty;
    }

    public static bool SequenceEqual(this in ReadOnlySequence<byte> first, in ReadOnlySequence<byte> other)
    {
        if (first.IsSingleSegment) return other.SequenceEqual(first.FirstSpan);
        if (other.IsSingleSegment) return first.SequenceEqual(other.FirstSpan);
        if (first.Length != other.Length) return false;

        var firstPosition = first.Start;
        var otherPosition = other.Start;
        ReadOnlySpan<byte> firstSpan;
        ReadOnlySpan<byte> otherSpan = default;
        while (first.TryGet(ref firstPosition, out var firstMemory))
        {
            firstSpan = firstMemory.Span;
            if (firstSpan.Length == 0) continue;

            if (otherSpan.Length > 0)
            {
                if (otherSpan.Length >= firstSpan.Length)
                {
                    if (!firstSpan.SequenceEqual(otherSpan.Slice(0, firstSpan.Length))) return false;
                    otherSpan = otherSpan.Slice(firstSpan.Length);
                    continue;
                }

                if (!firstSpan.Slice(0, otherSpan.Length).SequenceEqual(otherSpan)) return false;
                firstSpan = firstSpan.Slice(otherSpan.Length);
            }

            while (other.TryGet(ref otherPosition, out var otherMemory))
            {
                otherSpan = otherMemory.Span;
                if (otherSpan.Length == 0) continue;

                if (otherSpan.Length >= firstSpan.Length)
                {
                    if (!firstSpan.SequenceEqual(otherSpan.Slice(0, firstSpan.Length))) return false;
                    otherSpan = otherSpan.Slice(firstSpan.Length);
                    break;
                }

                if (!firstSpan.Slice(0, otherSpan.Length).SequenceEqual(otherSpan)) return false;
                firstSpan = firstSpan.Slice(otherSpan.Length);
            }
        }

        return true;
    }

    /// <summary>
    /// Lexicographically compares two sequences (matching <see cref="MemoryExtensions.SequenceCompareTo{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
    /// semantics): the first differing byte decides the order, and if one is a prefix of the other, the
    /// shorter sorts first.
    /// </summary>
    public static int SequenceCompareTo(this in ReadOnlySequence<byte> first, in ReadOnlySequence<byte> other)
    {
        if (first.IsSingleSegment && other.IsSingleSegment)
        {
            return first.FirstSpan.SequenceCompareTo(other.FirstSpan);
        }

        // walk both sequences in tandem, comparing the overlapping window each step and short-circuiting on
        // the first non-zero result; empty segments are skipped by the refill loops
        var firstPos = first.Start;
        var otherPos = other.Start;
        ReadOnlySpan<byte> a = default, b = default;
        while (true)
        {
            while (a.IsEmpty && first.TryGet(ref firstPos, out var aNext)) a = aNext.Span;
            while (b.IsEmpty && other.TryGet(ref otherPos, out var bNext)) b = bNext.Span;

            if (a.IsEmpty || b.IsEmpty) break; // at least one sequence is exhausted

            var shared = Math.Min(a.Length, b.Length);
            var cmp = a.Slice(0, shared).SequenceCompareTo(b.Slice(0, shared));
            if (cmp != 0) return cmp;

            a = a.Slice(shared);
            b = b.Slice(shared);
        }

        // everything in the overlap matched, so the longer sequence sorts after the shorter
        return first.Length.CompareTo(other.Length);
    }
}
