#if !NET
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace System.Net.Sockets;

internal static class SocketExtensions
{
    internal static async ValueTask ConnectAsync(this Socket socket, EndPoint remoteEP, CancellationToken cancellationToken = default)
    {
        // this API is only used during handshake, *not* core IO, so: we're not concerned about alloc overhead
        using var args = new SocketAwaitableEventArgs(SocketFlags.None, cancellationToken);
        args.RemoteEndPoint = remoteEP;
        if (!socket.ConnectAsync(args))
        {
            args.Complete();
        }
        await args; // .ConfigureAwait(false) does not apply here
    }

    internal static async ValueTask<int> SendAsync(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
    {
        // this API is only used during handshake, *not* core IO, so: we're not concerned about alloc overhead
        using var args = new SocketAwaitableEventArgs(socketFlags, cancellationToken);
        args.SetBuffer(buffer);
        if (!socket.SendAsync(args))
        {
            args.Complete();
        }

        return await args; // .ConfigureAwait(false) does not apply here
    }

    internal static async ValueTask<int> ReceiveAsync(this Socket socket, Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default)
    {
        // this API is only used during handshake, *not* core IO, so: we're not concerned about alloc overhead
        using var args = new SocketAwaitableEventArgs(socketFlags, cancellationToken);
        args.SetBuffer(buffer);
        if (!socket.ReceiveAsync(args))
        {
            args.Complete();
        }

        return await args; // .ConfigureAwait(false) does not apply here
    }

    /// <summary>
    /// Awaitable SocketAsyncEventArgs, where awaiting the args yields either the BytesTransferred or throws the relevant socket exception,
    /// plus support for cancellation via <see cref="SocketError.TimedOut"/>.
    /// </summary>
    private sealed class SocketAwaitableEventArgs : SocketAsyncEventArgs, ICriticalNotifyCompletion, IDisposable
    {
        public new void Dispose()
        {
            cancelRegistration.Dispose();
            base.Dispose();
        }

        private CancellationTokenRegistration cancelRegistration;
        public SocketAwaitableEventArgs(SocketFlags socketFlags, CancellationToken cancellationToken)
        {
            SocketFlags = socketFlags;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancelRegistration = cancellationToken.Register(Timeout);
            }
        }

        public void SetBuffer(ReadOnlyMemory<byte> buffer)
        {
            if (!MemoryMarshal.TryGetArray(buffer, out var segment)) ThrowNotSupported();
            SetBuffer(segment.Array ?? [], segment.Offset, segment.Count);

            [DoesNotReturn]
            static void ThrowNotSupported() => throw new NotSupportedException("Only array-backed buffers are supported");
        }

        public void Timeout() => Abort(SocketError.TimedOut);

        public void Abort(SocketError error)
        {
            _forcedError = error;
            OnCompleted(this);
        }

        private volatile SocketError _forcedError; // Success = 0, no field init required

        // ReSharper disable once InconsistentNaming
        private static readonly Action _callbackCompleted = () => { };

        private Action? _callback;

        public SocketAwaitableEventArgs GetAwaiter() => this;

        /// <summary>
        /// Indicates whether the current operation is complete; used as part of "await".
        /// </summary>
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        /// <summary>
        /// Gets the result of the async operation is complete; used as part of "await".
        /// </summary>
        public int GetResult()
        {
            Debug.Assert(ReferenceEquals(_callback, _callbackCompleted));

            _callback = null;

            var error = _forcedError;
            if (error is SocketError.Success) error = SocketError;
            if (error is not SocketError.Success) ThrowSocketException(error);

            return BytesTransferred;

            static void ThrowSocketException(SocketError e) => throw new SocketException((int)e);
        }

        /// <summary>
        /// Schedules a continuation for this operation; used as part of "await".
        /// </summary>
        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(Volatile.Read(ref _callback), _callbackCompleted)
                || ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                // this is the rare "kinda already complete" case; push to worker to prevent possible stack dive,
                // but prefer the custom scheduler when possible
                RunOnThreadPool(continuation);
            }
        }

        /// <summary>
        /// Schedules a continuation for this operation; used as part of "await".
        /// </summary>
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        /// <summary>
        /// Marks the operation as complete - this should be invoked whenever a SocketAsyncEventArgs operation returns false.
        /// </summary>
        public void Complete() => OnCompleted(this);

        private static void RunOnThreadPool(Action action)
            => ThreadPool.QueueUserWorkItem(static state => ((Action)state).Invoke(), action);

        /// <summary>
        /// Invoked automatically when an operation completes asynchronously.
        /// </summary>
        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            var continuation = Interlocked.Exchange(ref _callback, _callbackCompleted);
            if (continuation is not null)
            {
                // continue on the thread-pool
                RunOnThreadPool(continuation);
            }
        }
    }
}
#endif
