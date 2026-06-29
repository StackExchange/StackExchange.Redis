using System;
using System.Buffers;

namespace StackExchange.Redis.Tests;

internal sealed class FragmentedSegment<T> : ReadOnlySequenceSegment<T>
{
    public FragmentedSegment(long runningIndex, ReadOnlyMemory<T> memory)
    {
        RunningIndex = runningIndex;
        Memory = memory;
    }

    public new FragmentedSegment<T>? Next
    {
        get => (FragmentedSegment<T>?)base.Next;
        set => base.Next = value;
    }

    /// <summary>
    /// Builds a (deliberately) multi-segment <see cref="ReadOnlySequence{T}"/> from the supplied chunks,
    /// one segment per chunk. Note that single-segment sequences may be collapsed by consumers.
    /// </summary>
    public static ReadOnlySequence<T> Create(params ReadOnlyMemory<T>[] chunks)
    {
        if (chunks is null || chunks.Length == 0) return ReadOnlySequence<T>.Empty;

        FragmentedSegment<T>? head = null, tail = null;
        long runningIndex = 0;
        foreach (var chunk in chunks)
        {
            var next = new FragmentedSegment<T>(runningIndex, chunk);
            if (tail is null)
            {
                head = next;
            }
            else
            {
                tail.Next = next;
            }
            tail = next;
            runningIndex += chunk.Length;
        }
        return new ReadOnlySequence<T>(head!, 0, tail!, tail!.Memory.Length);
    }
}
