using System.Diagnostics;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0282 // There is no defined ordering between fields in multiple declarations of partial struct
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace StackExchange.Redis.Resp;

[DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
internal ref partial struct RespReader
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
}
