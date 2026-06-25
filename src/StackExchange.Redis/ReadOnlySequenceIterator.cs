using System;
using System.Buffers;

namespace StackExchange.Redis;

internal ref struct ReadOnlySequenceIterator<T>(ReadOnlySequenceSegment<T> segment, int startIndex, int length)
{
    private int _index = startIndex;
    private int _length = length;
    private ReadOnlySequenceSegment<T> _segment = segment;

    public readonly int Length => _length;

    public bool TryNext(out ReadOnlyMemory<T> memory)
    {
        if (_length > 0)
        {
            memory = _segment.Memory;

            // first
            if (_index > 0)
            {
                memory = memory.Slice(_index);
                _index = 0;
            }

            // end
            if (_length <= memory.Length)
            {
                memory = memory.Slice(0, _length);
                _length = 0;
                _segment = null!;
            }
            else
            {
                _length -= memory.Length;
                _segment = _segment.Next ?? throw new InvalidOperationException("EndSegment is null");
            }
            return true;
        }
        memory = default;
        return false;
    }
}
