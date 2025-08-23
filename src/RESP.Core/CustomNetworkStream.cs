#if NETCOREAPP3_0_OR_GREATER

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Resp;

internal sealed class CustomNetworkStream(Socket socket) : Stream
{
    private SocketAwaitableEventArgs _readArgs = new(), _writeArgs = new();
    private SocketAwaitableEventArgs ReadArgs() => _readArgs.Next();
    private SocketAwaitableEventArgs WriteArgs() => _writeArgs.Next();

    public override void Close()
    {
        socket.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            socket.Dispose();
            _readArgs.Dispose();
            _writeArgs.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) =>
        socket.Receive(buffer, offset, count, SocketFlags.None);

    public override void Write(byte[] buffer, int offset, int count) =>
        socket.Send(buffer, offset, count, SocketFlags.None);

    public override int Read(Span<byte> buffer) => socket.Receive(buffer);

    public override void Write(ReadOnlySpan<byte> buffer) => socket.Send(buffer);

    private static void ThrowCancellable() => throw new NotSupportedException(
        "Cancellable operations are not supported on this stream; cancellation should be handled at the message level, not the IO level.");

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled) ThrowCancellable();
        var args = ReadArgs();
        args.SetBuffer(buffer, offset, count);
        if (socket.ReceiveAsync(args)) return args.Pending().AsTask();
        return Task.FromResult(args.GetInlineResult());
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled) ThrowCancellable();
        var args = WriteArgs();
        args.SetBuffer(buffer, offset, count);
        if (socket.SendAsync(args)) return args.Pending().AsTask();
        args.GetInlineResult(); // check for socket errors
        return Task.CompletedTask;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled) ThrowCancellable();
        var args = ReadArgs();
        args.SetBuffer(buffer);
        if (socket.ReceiveAsync(args)) return args.Pending();
        return new(args.GetInlineResult());
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled) ThrowCancellable();
        var args = WriteArgs();
        args.SetBuffer(MemoryMarshal.AsMemory(buffer));
        if (socket.SendAsync(args)) return args.PendingNoValue();
        args.GetInlineResult(); // check for socket errors
        return default;
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        int count = socket.Receive(buffer);
        return count <= 0 ? -1 : buffer[0];
    }

    public override void WriteByte(byte value)
    {
        ReadOnlySpan<byte> buffer = [value];
        socket.Send(buffer);
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        var args = ReadArgs();
        args.SetBuffer(buffer, offset, count);
        args.CompletedSynchronously = false;
        if (socket.SendAsync(args))
        {
            args.OnCompleted(callback, state);
        }
        else
        {
            args.CompletedSynchronously = true;
            callback?.Invoke(args);
        }

        return args;
    }

    public override int EndRead(IAsyncResult asyncResult) => ((SocketAwaitableEventArgs)asyncResult).GetInlineResult();

    public override IAsyncResult BeginWrite(
        byte[] buffer,
        int offset,
        int count,
        AsyncCallback? callback,
        object? state)
    {
        var args = WriteArgs();
        args.SetBuffer(buffer, offset, count);
        args.CompletedSynchronously = false;
        if (socket.SendAsync(args))
        {
            args.OnCompleted(callback, state);
        }
        else
        {
            args.CompletedSynchronously = true;
            callback?.Invoke(args);
        }

        return args;
    }

    public override void EndWrite(IAsyncResult asyncResult) =>
        ((SocketAwaitableEventArgs)asyncResult).GetInlineResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override bool CanTimeout => socket.ReceiveTimeout != 0 || socket.SendTimeout != 0;

    public override int ReadTimeout
    {
        get => socket.ReceiveTimeout;
        set => socket.ReceiveTimeout = value;
    }

    public override int WriteTimeout
    {
        get => socket.SendTimeout;
        set => socket.SendTimeout = value;
    }

    // inspired from Pipelines.Sockets.Unofficial and Kestrel's SocketAwaitableEventArgs; extended to support more scenarios
    private sealed class SocketAwaitableEventArgs : SocketAsyncEventArgs,
        IValueTaskSource<int>, IValueTaskSource, IAsyncResult
    {
#if NET5_0_OR_GREATER
        public SocketAwaitableEventArgs() : base(unsafeSuppressExecutionContextFlow: true) { }
#else
        public SocketAwaitableEventArgs() { }
#endif
        private static readonly Action<object?> ContinuationCompleted = _ => { };

        public WaitHandle AsyncWaitHandle => throw new NotSupportedException();
        public bool CompletedSynchronously { get; set; }
        private volatile Action<object?>? _continuation;

        private object? _asyncCallbackState; // need an additional state here, unless we introduce type-check overhead
        object? IAsyncResult.AsyncState => _asyncCallbackState;
        private Action<object?>? _reusedAsyncCallback;
        private Action<object?> AsyncCallback => _reusedAsyncCallback ??= OnAsyncCallback;

        public ValueTask<int> Pending() => new(this, _token);
        public ValueTask PendingNoValue() => new(this, _token);
        private short _token;

        public SocketAwaitableEventArgs Next()
        {
            unchecked { _token++; }

            return this;
        }

        private void ThrowToken() => throw new InvalidOperationException("Invalid token - overlapped IO error?");

        private void OnAsyncCallback(object? state)
        {
            if (state is WaitCallback wc)
            {
                wc(_asyncCallbackState);
            }
        }

        protected override void OnCompleted(SocketAsyncEventArgs args)
        {
            Debug.Assert(ReferenceEquals(args, this), "Incorrect SocketAsyncEventArgs");
            var c = _continuation;

            if (c != null || (c = Interlocked.CompareExchange(ref _continuation, ContinuationCompleted, null)) != null)
            {
                var continuationState = UserToken;
                UserToken = null;
                _continuation = ContinuationCompleted; // in case someone's polling IsCompleted

                c(continuationState); // note: inline continuation
            }
        }

        public int GetInlineResult()
        {
            _continuation = null;
            if (SocketError != SocketError.Success)
            {
                ThrowSocketError(SocketError);
            }

            return BytesTransferred;
        }

        void IValueTaskSource.GetResult(short token) => GetResult(token);

        public int GetResult(short token)
        {
            if (token != _token) ThrowToken();
            _continuation = null;

            if (SocketError != SocketError.Success)
            {
                ThrowSocketError(SocketError);
            }

            return BytesTransferred;
        }

        private static void ThrowSocketError(SocketError e) => throw new SocketException((int)e);

        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (token != _token) ThrowToken();
            return !ReferenceEquals(_continuation, ContinuationCompleted) ? ValueTaskSourceStatus.Pending :
                SocketError == SocketError.Success ? ValueTaskSourceStatus.Succeeded :
                ValueTaskSourceStatus.Faulted;
        }

        public bool IsCompleted => ReferenceEquals(_continuation, ContinuationCompleted);

        public void OnCompleted(AsyncCallback? callback, object? state)
        {
            _asyncCallbackState = state;
            OnCompleted(AsyncCallback, callback, _token, ValueTaskSourceOnCompletedFlags.None);
        }

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            if (token != _token) ThrowToken();
            UserToken = state;
            var prevContinuation = Interlocked.CompareExchange(ref _continuation, continuation, null);
            if (ReferenceEquals(prevContinuation, ContinuationCompleted))
            {
                UserToken = null;
                ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
            }
        }
    }
}
#endif
