using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RESPite.Proxy;

/// <summary>
/// A duplex <see cref="Stream"/> over a connected <see cref="Socket"/> that routes socket
/// completions through a <see cref="WorkerPool"/> via <see cref="WorkerSocketAsyncEventArgs"/>,
/// using separate args instances for the read and write legs.
/// </summary>
internal sealed class WorkerNetworkStream : Stream
{
    private readonly Socket _socket;
    private readonly WorkerSocketAsyncEventArgs _readArgs, _writeArgs;
    public WorkerNetworkStream(Socket socket, WorkerPool pool)
    {
        _socket = socket;
        _readArgs = new WorkerSocketAsyncEventArgs(pool);
        _writeArgs = new WorkerSocketAsyncEventArgs(pool) { UserToken = socket };
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _readArgs.SetBuffer(buffer);
        _readArgs.SetForAsync();
        if (cancellationToken.CanBeCanceled) _readArgs.SetCancellation(cancellationToken);
        _ = _socket.ReceiveAsync(_readArgs);
        return _readArgs.AsTypedValueTask();
    }

    public override unsafe int Read(Span<byte> buffer)
    {
        fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
        {
            _readArgs.SetBuffer(ptr, buffer.Length);
            _readArgs.SetForPulse(_readArgs);
            lock (_readArgs)
            {
                if (_socket.ReceiveAsync(_readArgs))
                    _readArgs.WaitForPulse();
            }
        }
        return _readArgs.GetResult();
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        return Read(buffer) is 1 ? buffer[0] : -1;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _readArgs.SetBuffer(buffer, offset, count);
        _readArgs.SetForPulse(_readArgs);
        lock (_readArgs)
        {
            if (_socket.ReceiveAsync(_readArgs))
                _readArgs.WaitForPulse();
        }
        return _readArgs.GetResult();
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _readArgs.SetBuffer(buffer, offset, count);
        _readArgs.SetForAsync();
        if (cancellationToken.CanBeCanceled) _readArgs.SetCancellation(cancellationToken);
        _ = _socket.ReceiveAsync(_readArgs);
        return _readArgs.AsTypedTask();
    }

    public override void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        Write(buffer);
    }

    public override unsafe void Write(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* ptr = &MemoryMarshal.GetReference(buffer))
        {
            _writeArgs.SetBuffer(ptr, buffer.Length);
            _writeArgs.SetForPulse(_writeArgs);
            lock (_writeArgs)
            {
                if (_socket.SendAsync(_writeArgs))
                    _writeArgs.WaitForPulse();
            }
        }
        _ = _writeArgs.GetResult();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _writeArgs.SetBuffer(buffer, offset, count);
        _writeArgs.SetForAsync();
        if (cancellationToken.CanBeCanceled) _writeArgs.SetCancellation(cancellationToken);
        _ = _socket.SendAsync(_writeArgs);
        return _writeArgs.AsUntypedTask();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _writeArgs.SetBuffer(buffer, offset, count);
        _writeArgs.SetForPulse(_writeArgs);
        lock (_writeArgs)
        {
            if (_socket.SendAsync(_writeArgs))
                _writeArgs.WaitForPulse();
        }
        _ = _writeArgs.GetResult();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _writeArgs.SetBuffer(buffer);
        _writeArgs.SetForAsync();
        if (cancellationToken.CanBeCanceled) _writeArgs.SetCancellation(cancellationToken);
        _ = _socket.SendAsync(_writeArgs);
        return _writeArgs.AsUntypedValueTask();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _socket.Dispose(); } // aborts any in-flight op so the SAEAs are free to dispose
            catch { /* already gone */ }
            _readArgs.Dispose();
            _writeArgs.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { } // no-op

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
