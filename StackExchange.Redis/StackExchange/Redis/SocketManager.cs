using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis
{
    internal enum SocketMode
    {
        Abort,
        Async,
    }

    /// <summary>
    /// Allows callbacks from SocketManager as work is discovered
    /// </summary>
    internal partial interface ISocketCallback
    {
        /// <summary>
        /// Indicates that a socket has connected
        /// </summary>
        /// <param name="socket">The socket.</param>
        /// <param name="log">A text logger to write to.</param>
        /// <param name="manager">The manager that will be owning this socket.</param>
        ValueTask<SocketMode> ConnectedAsync(Socket socket, TextWriter log, SocketManager manager);

        /// <summary>
        /// Indicates that the socket has signalled an error condition
        /// </summary>
        void Error();

        void OnHeartbeat();

        ///// <summary>
        ///// Indicates that data is available on the socket, and that the consumer should read synchronously from the socket while there is data
        ///// </summary>
        //void Read();

        /// <summary>
        /// Indicates that we cannot know whether data is available, and that the consume should commence reading asynchronously
        /// </summary>
        void StartReading();

        // check for write-read timeout
        void CheckForStaleConnection(ref SocketManager.ManagerState state);

        bool IsDataAvailable { get; }
    }

    internal struct SocketToken
    {
        internal readonly Socket Socket;
        public SocketToken(Socket socket)
        {
            Socket = socket;
        }

        public int Available => Socket?.Available ?? 0;

        public bool HasValue => Socket != null;
    }

    /// <summary>
    /// A SocketManager monitors multiple sockets for availability of data; this is done using
    /// the Socket.Select API and a dedicated reader-thread, which allows for fast responses
    /// even when the system is under ambient load.
    /// </summary>
    public sealed partial class SocketManager : IDisposable
    {
        internal enum ManagerState
        {
            Inactive,
            Preparing,
            Faulted,
            CheckForHeartbeat,
            ExecuteHeartbeat,
            LocateActiveSockets,
            NoSocketsPause,
            PrepareActiveSockets,
            CullDeadSockets,
            NoActiveSocketsPause,
            GrowingSocketArray,
            CopyingPointersForSelect,
            ExecuteSelect,
            ExecuteSelectComplete,
            CheckForStaleConnections,

            RecordConnectionFailed_OnInternalError,
            RecordConnectionFailed_OnDisconnected,
            RecordConnectionFailed_ReportFailure,
            RecordConnectionFailed_OnConnectionFailed,
            RecordConnectionFailed_FailOutstanding,
            RecordConnectionFailed_ShutdownSocket,

            CheckForStaleConnectionsDone,
            EnqueueRead,
            EnqueueError,
            EnqueueReadFallback,
            RequestAssistance,
            ProcessQueues,
            ProcessReadQueue,
            ProcessErrorQueue,
        }

        /// <summary>
        /// Gets the name of this SocketManager instance
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        public SocketManager(string name = null)
            : this(name, false, DEFAULT_MIN_THREADS, DEFAULT_MAX_THREADS) { }

        internal static SocketManager Shared
        {
            get
            {
                var shared = _shared;
                if (shared != null) return _shared;
                try
                {
                    // note: we'll allow a higher max thread count on the shared one
                    shared = new SocketManager("DefaultSocketManager", false, DEFAULT_MIN_THREADS, DEFAULT_MAX_THREADS * 2);
                    if (Interlocked.CompareExchange(ref _shared, shared, null) == null)
                        shared = null;
                } finally { shared?.Dispose(); }
                return Interlocked.CompareExchange(ref _shared, null, null);
            }
        }
        private static SocketManager _shared;

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
            : this(name, useHighPrioritySocketThreads, DEFAULT_MIN_THREADS, DEFAULT_MAX_THREADS) {}

        const int DEFAULT_MIN_THREADS = 1, DEFAULT_MAX_THREADS = 5;
        private SocketManager(string name, bool useHighPrioritySocketThreads, int minThreads, int maxThreads)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name;
            
            const long Receive_PauseWriterThreshold = 4L * 1024 * 1024 * 1024; // let's give it up to 4GiB of buffer for now
            const long Receive_ResumeWriterThreshold = 3L * 1024 * 1024 * 1024;

            var defaultPipeOptions = PipeOptions.Default;
            _scheduler = new DedicatedThreadPoolPipeScheduler(name,
                minWorkers: minThreads, maxWorkers: maxThreads,
                priority: useHighPrioritySocketThreads ? ThreadPriority.AboveNormal :ThreadPriority.Normal);
            SendPipeOptions = new PipeOptions(
                defaultPipeOptions.Pool, _scheduler, _scheduler,
                pauseWriterThreshold: defaultPipeOptions.PauseWriterThreshold,
                resumeWriterThreshold: defaultPipeOptions.ResumeWriterThreshold,
                defaultPipeOptions.MinimumSegmentSize,
                useSynchronizationContext: false);
            ReceivePipeOptions = new PipeOptions(
                defaultPipeOptions.Pool, _scheduler, _scheduler,
                pauseWriterThreshold: Receive_PauseWriterThreshold,
                resumeWriterThreshold: Receive_ResumeWriterThreshold,
                defaultPipeOptions.MinimumSegmentSize,
                useSynchronizationContext: false);
        }
        DedicatedThreadPoolPipeScheduler _scheduler;
        internal readonly PipeOptions SendPipeOptions, ReceivePipeOptions;

        private enum CallbackOperation
        {
            Read,
            Error
        }

        /// <summary>
        /// Releases all resources associated with this instance
        /// </summary>
        public void Dispose() => Dispose(true);
        private void Dispose(bool disposing)
        {
            // note: the scheduler *can't* be collected by itself - there will
            // be threads, and those threads will be rooting the DedicatedThreadPool;
            // but: we can lend a hand! We need to do this even in the finalizer
            try { _scheduler?.Dispose(); } catch { }
            _scheduler = null;
            if (disposing)
            {
                GC.SuppressFinalize(this);
                OnDispose();
            }
            
        }
        /// <summary>
        /// Releases *appropriate* resources associated with this instance
        /// </summary>
        ~SocketManager() => Dispose(false);
        internal SocketToken BeginConnect(EndPoint endpoint, ISocketCallback callback, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            void RunWithCompletionType(Func<AsyncCallback, IAsyncResult> beginAsync, AsyncCallback asyncCallback)
            {
                void proxyCallback(IAsyncResult ar)
                {
                    if (!ar.CompletedSynchronously)
                    {
                        asyncCallback(ar);
                    }
                }

                var result = beginAsync(proxyCallback);
                if (result.CompletedSynchronously)
                {
                    result.AsyncWaitHandle.WaitOne();
                    asyncCallback(result);
                }
            }


            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
            var socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            SocketConnection.SetRecommendedClientOptions(socket);

            try
            {
                var formattedEndpoint = Format.ToString(endpoint);
                var tuple = Tuple.Create(socket, callback);
                multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);
                // A work-around for a Mono bug in BeginConnect(EndPoint endpoint, AsyncCallback callback, object state)
                if (endpoint is DnsEndPoint dnsEndpoint)
                {
                    RunWithCompletionType(
                        cb => socket.BeginConnect(dnsEndpoint.Host, dnsEndpoint.Port, cb, tuple),
                        ar => {
                            multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                            EndConnectImpl(ar, multiplexer, log, tuple);
                            multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                        });
                }
                else
                {
                    RunWithCompletionType(
                        cb => socket.BeginConnect(endpoint, cb, tuple),
                        ar => {
                            multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                            EndConnectImpl(ar, multiplexer, log, tuple);
                            multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                        });
                }
            }
            catch (NotImplementedException ex)
            {
                if (!(endpoint is IPEndPoint))
                {
                    throw new InvalidOperationException("BeginConnect failed with NotImplementedException; consider using IP endpoints, or enable ResolveDns in the configuration", ex);
                }
                throw;
            }
            var token = new SocketToken(socket);
            return token;
        }

        internal void Shutdown(SocketToken token)
        {
            Shutdown(token.Socket);
        }

        private async void EndConnectImpl(IAsyncResult ar, ConnectionMultiplexer multiplexer, TextWriter log, Tuple<Socket, ISocketCallback> tuple)
        {
            var socket = tuple.Item1;
            var callback = tuple.Item2;
            try
            {
                bool ignoreConnect = false;
                ShouldIgnoreConnect(tuple.Item2, ref ignoreConnect);
                if (ignoreConnect) return;
                socket.EndConnect(ar);

                var socketMode = callback == null ? SocketMode.Abort : await callback.ConnectedAsync(socket, log, this);
                switch (socketMode)
                {
                    case SocketMode.Async:
                        multiplexer.LogLocked(log, "Starting read");
                        try
                        { callback.StartReading(); }
                        catch (Exception ex)
                        {
                            ConnectionMultiplexer.TraceWithoutContext(ex.Message);
                            Shutdown(socket);
                        }
                        break;
                    default:
                        ConnectionMultiplexer.TraceWithoutContext("Aborting socket");
                        Shutdown(socket);
                        break;
                }
            }
            catch (ObjectDisposedException)
            {
                multiplexer.LogLocked(log, "(socket shutdown)");
                if (callback != null)
                {
                    try { callback.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
            catch(Exception outer)
            {
                ConnectionMultiplexer.TraceWithoutContext(outer.Message);
                if (callback != null)
                {
                    try { callback.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
        }

        partial void OnDispose();
        partial void OnShutdown(Socket socket);

        partial void ShouldIgnoreConnect(ISocketCallback callback, ref bool ignore);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        private void Shutdown(Socket socket)
        {
            if (socket != null)
            {
                OnShutdown(socket);
                try { socket.Shutdown(SocketShutdown.Both); } catch { }
                try { socket.Close(); } catch { }
                try { socket.Dispose(); } catch { }
            }
        }

        internal string GetState()
        {
            var s = _scheduler;
            return s == null ? null : $"{s.BusyCount} of {s.WorkerCount} busy ({s.MaxWorkerCount} max)";
        }
    }
}
