using System.Buffers;
using System.Text;
using RESPite;
using RESPite.Messages;
using RESPite.Resp;

namespace StackExchange.Redis;

internal sealed class LeasedRespResult : IDisposable
{
    public override string ToString()
    {
        var tmp = _buffer;
        return tmp is null ? "(disposed)" : Encoding.UTF8.GetString(tmp, 0, _length);
    }

    private byte[]? _buffer;
    private readonly int _length;

    public ReadOnlySpan<byte> Span
    {
        get
        {
            var tmp = _buffer;
            return tmp is null ? ThrowDisposed() : new(tmp, 0, _length);

            static ReadOnlySpan<byte> ThrowDisposed() => throw new ObjectDisposedException(nameof(LeasedRespResult));
        }
    }

    public LeasedRespResult(in ReadOnlySequence<byte> content)
    {
        _length = checked((int)content.Length);
        _buffer = ArrayPool<byte>.Shared.Rent(_length);
        content.CopyTo(_buffer);
    }

    public void Dispose()
    {
        var old = _buffer;
        _buffer = null;
        if (old is not null)
        {
            ArrayPool<byte>.Shared.Return(old);
        }
    }

    public static IReader<Empty, LeasedRespResult> Reader => RawReader.Instance;
    private sealed class RawReader : IReader<Empty, LeasedRespResult>
    {
        public static RawReader Instance = new();
        private RawReader() { }

        public LeasedRespResult Read(in Empty request, in ReadOnlySequence<byte> content)
            => new(content);
    }
}
