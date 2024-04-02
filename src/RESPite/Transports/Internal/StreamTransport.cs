using RESPite.Buffers.Internal;
using RESPite.Internal;
using RESPite.Transports;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RESPite.Gateways.Internal;

internal sealed class StreamTransport : IByteTransport
{

    private readonly Stream _stream;
    private readonly bool _closeStream;
    private BufferCore<byte> _buffer;
    internal StreamTransport(Stream source, bool closeStream)
    {
        _buffer = new(new SlabManager<byte>());
        _stream = source;
        _closeStream = closeStream;
    }

    ReadOnlySequence<byte> IByteTransportBase.GetBuffer() => _buffer.GetBuffer();


#if NETCOREAPP3_1_OR_GREATER
    public ValueTask DisposeAsync()
    {
        _buffer.Dispose();
        _buffer.SlabManager.Dispose();
        return _closeStream ? _stream.DisposeAsync() : default;
    }
#else
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
#endif

    public void Dispose()
    {
        _buffer.Dispose();
        _buffer.SlabManager.Dispose();
        if (_closeStream) _stream.Dispose();
    }

    public ValueTask<bool> TryReadAsync(int hint, CancellationToken cancellationToken)
    {
        var readBuffer = _buffer.GetWritableTail();
        Debug.Assert(!readBuffer.IsEmpty, "should have space");

        var pending = _stream.ReadAsync(readBuffer, cancellationToken);
        if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);

        // synchronous happy case
        var bytes = pending.GetAwaiter().GetResult();
        if (bytes > 0)
        {
            _buffer.Commit(bytes);
            return new(true);
        }
        return default;

        static async ValueTask<bool> Awaited(StreamTransport @this, ValueTask<int> pending)
        {
            var bytes = await pending;
            if (bytes > 0)
            {
                @this._buffer.Commit(bytes);
                return true;
            }
            return false;
        }
    }

    public bool TryRead(int hint)
    {
        var readBuffer = _buffer.GetWritableTail();
        Debug.Assert(!readBuffer.IsEmpty, "should have space");
        var bytes = _stream.Read(readBuffer);

        if (bytes > 0)
        {
            _buffer.Commit(bytes);
            return true;
        }
        return false;
    }

    public void Advance(long bytes) => _buffer.Advance(bytes);

    void ISyncByteTransport.Write(in ReadOnlySequence<byte> buffer)
    {
        if (buffer.IsSingleSegment)
        {
            _stream.Write(buffer.First);
        }
        else
        {
            WriteMultiSegment(_stream, buffer);

            static void WriteMultiSegment(Stream stream, in ReadOnlySequence<byte> buffer)
            {
                foreach (var segment in buffer)
                {
                    stream.Write(segment);
                }
            }
        }
    }


    ValueTask IAsyncByteTransport.WriteAsync(in ReadOnlySequence<byte> buffer, CancellationToken token)
    {
        if (buffer.IsSingleSegment)
        {
            return _stream.WriteAsync(buffer.First, token);
        }
        else
        {
            return WriteMultiSegment(_stream, buffer, token);

            static async ValueTask WriteMultiSegment(Stream stream, ReadOnlySequence<byte> buffer, CancellationToken token)
            {
                foreach (var segment in buffer)
                {
                    await stream.WriteAsync(segment, token).ConfigureAwait(false);
                }
            }
        }
    }
}
