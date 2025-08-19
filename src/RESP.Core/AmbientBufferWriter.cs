using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Resp;

internal sealed class AmbientBufferWriter : IBufferWriter<byte>
{
    private static AmbientBufferWriter? _threadStaticInstance;

    public static AmbientBufferWriter Get(int estimatedSize)
    {
        var obj = _threadStaticInstance ??= new AmbientBufferWriter();
        obj.Init(estimatedSize);
        return obj;
    }

    private byte[] _buffer = [];
    private int _committed;

    private void Init(int size)
    {
        _committed = 0;
        if (size < 0) size = 0;
        if (_buffer.Length < size)
        {
            DemandCapacity(size);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DemandCapacity(int size)
    {
        const int MIN_BUFFER = 32;
        size = Math.Max(size, MIN_BUFFER);

        if (_committed + size > _buffer.Length)
        {
            GrowBy(size);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowBy(int length)
    {
        var newSize = Math.Max(_committed + length, checked((_buffer.Length * 3) / 2));
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize), oldBuffer = _buffer;
        if (_committed != 0)
        {
            new ReadOnlySpan<byte>(oldBuffer, 0, _committed).CopyTo(newBuffer);
        }

        _buffer = newBuffer;
        ArrayPool<byte>.Shared.Return(oldBuffer);
    }

    internal byte[] Detach(out int length)
    {
        length = _committed;
        if (length == 0) return [];
        var result = _buffer;
        _buffer = [];
        _committed = 0;
        return result;
    }

    public void Advance(int count)
    {
        var capacity = _buffer.Length - _committed;
        if (count < 0 || count > capacity) Throw();
        {
            _committed += count;
        }

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(count));
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        DemandCapacity(sizeHint);
        return new(_buffer, _committed, _buffer.Length - _committed);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        DemandCapacity(sizeHint);
        return new(_buffer, _committed, _buffer.Length - _committed);
    }

    internal void Reset() => _committed = 0;
}
