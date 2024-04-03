//using RESPite.Buffers.Internal;
//using RESPite.Internal;
//using RESPite.Transports;
//using System;
//using System.Buffers;
//using System.Diagnostics;
//using System.Net.Sockets;
//using System.Threading;
//using System.Threading.Tasks;

//namespace RESPite.Gateways.Internal;

//internal sealed class SocketTransport : IByteTransport
//{

//    private readonly Socket _socket;
//    private BufferCore<byte> _buffer;

//    internal SocketTransport(Socket socket)
//    {
//        if (socket is null) throw new ArgumentNullException(nameof(socket));
//        _buffer = new(new SlabManager<byte>());
//        _socket = socket;
//    }

//    ReadOnlySequence<byte> IByteTransportBase.GetBuffer() => _buffer.GetBuffer();

//    public ValueTask DisposeAsync()
//    {
//        Dispose();
//        return default;
//    }

//    public void Dispose()
//    {
//        _buffer.Dispose();
//        _buffer.SlabManager.Dispose();
//        _socket.Dispose();
//    }

//    public ValueTask<bool> TryReadAsync(int hint, CancellationToken cancellationToken)
//    {
//        var readBuffer = _buffer.GetWritableTail();
//        Debug.Assert(!readBuffer.IsEmpty, "should have space");

//        var pending = _socket.ReceiveAsync(readBuffer, SocketFlags.None, cancellationToken);
//        if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);

//        // synchronous happy case
//        var bytes = pending.GetAwaiter().GetResult();
//        if (bytes > 0)
//        {
//            _buffer.Commit(bytes);
//            return new(true);
//        }
//        return default;

//        static async ValueTask<bool> Awaited(SocketTransport @this, ValueTask<int> pending)
//        {
//            var bytes = await pending;
//            if (bytes > 0)
//            {
//                @this._buffer.Commit(bytes);
//                return true;
//            }
//            return false;
//        }
//    }

//    public bool TryRead(int hint)
//    {
//        var readBuffer = _buffer.GetWritableTail();
//        Debug.Assert(!readBuffer.IsEmpty, "should have space");
//        var bytes = _socket.Receive(readBuffer, SocketFlags.None);

//        if (bytes > 0)
//        {
//            _buffer.Commit(bytes);
//            return true;
//        }
//        return false;
//    }

//    public void Advance(long bytes) => _buffer.Advance(bytes);

//    void ISyncByteTransport.Write(in ReadOnlySequence<byte> buffer)
//    {
//        if (buffer.IsSingleSegment)
//        {
//            var first = buffer.First;
//            int bytes = _socket.Send(first, SocketFlags.None);
//            if (bytes != first.Length) ThrowPartialWrite();

//        }
//        else
//        {
//            WriteMultiSegment(_socket, buffer);

//            static void WriteMultiSegment(Socket socket, in ReadOnlySequence<byte> buffer)
//            {
//                foreach (var segment in buffer)
//                {
//                    int actual = socket.Send(segment, SocketFlags.None);
//                    if (actual != buffer.Length) ThrowPartialWrite();

//                }
//            }
//        }
//    }


//    ValueTask IAsyncByteTransport.WriteAsync(in ReadOnlySequence<byte> buffer, CancellationToken token)
//    {
//        if (buffer.IsSingleSegment)
//        {
//            var first = buffer.First;
//            var pending = _socket.SendAsync(first, SocketFlags.None, token);
//            if (!pending.IsCompletedSuccessfully) return Awaited(pending, first.Length);
//            var bytes = pending.GetAwaiter().GetResult();
//            if (bytes != first.Length) ThrowPartialWrite();
//            return default;

//            static async ValueTask Awaited(ValueTask<int> pending, int expected)
//            {
//                var actual = await pending;
//                if (actual != expected) ThrowPartialWrite();
//            }
//        }
//        else
//        {
//            return WriteMultiSegment(_socket, buffer, token);

//            static async ValueTask WriteMultiSegment(Socket socket, ReadOnlySequence<byte> buffer, CancellationToken token)
//            {
//                foreach (var segment in buffer)
//                {
//                    var bytes = await socket.SendAsync(segment, SocketFlags.None, token).ConfigureAwait(false);
//                    if (bytes != segment.Length) ThrowPartialWrite();
//                }
//            }
//        }
        
//    }

//    private static void ThrowPartialWrite() => throw new NotSupportedException("Tell Marc: partial writes");
//}
