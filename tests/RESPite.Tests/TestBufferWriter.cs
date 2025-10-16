using System;
using System.Buffers;
using System.Text;

namespace RESPite.Tests;

// note that ArrayBufferWriter{T} is not available on all target platforms
public sealed class TestBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer = [];
    private int _committed;

    public override string ToString() => Encoding.UTF8.GetString(_buffer, 0, _committed);
    public ReadOnlySpan<byte> Committed => _buffer.AsSpan(0, _committed);

    public void Advance(int count)
    {
        if (count < 0 | count + _committed > _buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
        _committed += count;
    }

    private void Ensure(int sizeHint)
    {
        sizeHint = Math.Max(sizeHint, 128);
        if (_buffer.Length < _committed + sizeHint)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _committed + sizeHint));
            Committed.CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_committed);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_committed);
    }

    public void Dispose()
    {
        _committed = 0;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }
}
