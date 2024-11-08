using System.Buffers;

namespace RESPite;

internal sealed class TestBuffer : IDisposable, IBufferWriter<byte>
{
    private byte[] _buffer;
    private int _index, _commits, _maxAdvance;

    public int Commits => _commits;
    public TestBuffer()
    {
        _buffer = [];
        _maxAdvance = -1;
    }

    public void Advance(int count)
    {
        if (_maxAdvance < 0) throw new InvalidOperationException();
        if (count < 0 || count > _maxAdvance) throw new ArgumentOutOfRangeException(nameof(count));
        _index += count;
        _commits++;
        _maxAdvance = -1;
    }

    public void Dispose()
    {
        var arr = _buffer;
        _index = _commits = 0;
        _buffer = [];
        ArrayPool<byte>.Shared.Return(arr);
    }

    public override string ToString() => Internal.Constants.UTF8.GetString(_buffer, 0, _index);

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (sizeHint < 16) sizeHint = 16;
        var available = _buffer.Length - _index;
        if (available < sizeHint)
        {
            var newSize = Math.Max(2 * _buffer.Length, sizeHint);
            var newArr = ArrayPool<byte>.Shared.Rent(newSize);

            _buffer.CopyTo(newArr, 0);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newArr;
            available = _buffer.Length - _index;
        }
        _maxAdvance = available;
        return new Memory<byte>(_buffer, _index, available);
    }
    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
}
