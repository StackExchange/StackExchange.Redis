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

    private readonly Stream _source, _target;
    private readonly bool _closeStreams;
    private BufferCore<byte> _buffer;

    internal StreamTransport(Stream source, Stream target, bool closeStreams)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (!source.CanRead) throw new ArgumentException("Source must allow read", nameof(source));
        if (!target.CanWrite) throw new ArgumentException("Target must allow read", nameof(target));
        _buffer = new(new SlabManager<byte>());
        _source = source;
        _target = target;
        _closeStreams = closeStreams;
    }

    ReadOnlySequence<byte> IByteTransportBase.GetBuffer() => _buffer.GetBuffer();


#if NETCOREAPP3_1_OR_GREATER
    public ValueTask DisposeAsync()
    {
        _buffer.Dispose();
        _buffer.SlabManager.Dispose();
        if (_closeStreams)
        {
            var pending = _source.DisposeAsync();
            if (ReferenceEquals(_source, _target)) return pending;
            if (!pending.IsCompletedSuccessfully) return Awaited(pending, _target);
            pending.GetAwaiter().GetResult();
            return _target.DisposeAsync();
        }
        return default;

        static async ValueTask Awaited(ValueTask pending, Stream target)
        {
            await pending;
            await target.DisposeAsync();
        }
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
        if (_closeStreams)
        {
            _source.Dispose();
            if (!ReferenceEquals(_source, _target)) _target.Dispose();
        }
    }

    public ValueTask<bool> TryReadAsync(int hint, CancellationToken cancellationToken)
    {
        var readBuffer = _buffer.GetWritableTail();
        Debug.Assert(!readBuffer.IsEmpty, "should have space");

        var pending = _source.ReadAsync(readBuffer, cancellationToken);
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
        var bytes = _source.Read(readBuffer);

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
            _target.Write(buffer.First);
        }
        else
        {
            WriteMultiSegment(_target, buffer);

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
            return _target.WriteAsync(buffer.First, token);
        }
        else
        {
            return WriteMultiSegment(_target, buffer, token);

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
