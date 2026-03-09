using System.Buffers;
using System.Diagnostics;
using System.Text;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace RESPite.Messages;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
public ref partial struct RespReader
{
    internal bool DebugEquals(in RespReader other)
        => _prefix == other._prefix
        && _length == other._length
        && _flags == other._flags
        && _bufferIndex == other._bufferIndex
        && _positionBase == other._positionBase
        && _remainingTailLength == other._remainingTailLength;

    internal new string ToString() => $"{Prefix} ({_flags}); length {_length}, {TotalAvailable} remaining";

    internal void DebugReset()
    {
        _bufferIndex = 0;
        _length = 0;
        _flags = 0;
        _prefix = RespPrefix.None;
    }

#if DEBUG
    internal bool VectorizeDisabled { get; set; }
#endif

    private partial ReadOnlySpan<byte> ActiveBuffer { get; }

    internal readonly string BufferUtf8()
    {
        var clone = Clone();
        var active = clone.ActiveBuffer;
        var totalLen = checked((int)(active.Length + clone._remainingTailLength));
        var oversized = ArrayPool<byte>.Shared.Rent(totalLen);
        Span<byte> target = oversized.AsSpan(0, totalLen);

        while (!target.IsEmpty)
        {
            active.CopyTo(target);
            target = target.Slice(active.Length);
            if (!clone.TryMoveToNextSegment()) break;
            active = clone.ActiveBuffer;
        }
        if (!target.IsEmpty) throw new EndOfStreamException();

        var s = Encoding.UTF8.GetString(oversized, 0, totalLen);
        ArrayPool<byte>.Shared.Return(oversized);
        return s;
    }
}
