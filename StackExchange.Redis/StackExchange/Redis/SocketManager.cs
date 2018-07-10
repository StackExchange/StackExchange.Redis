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
    /// A SocketManager monitors multiple sockets for availability of data; this is done using
    /// the Socket.Select API and a dedicated reader-thread, which allows for fast responses
    /// even when the system is under ambient load.
    /// </summary>
    public sealed partial class SocketManager : IDisposable
    {
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
                }
                finally { shared?.Dispose(); }
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
            : this(name, useHighPrioritySocketThreads, DEFAULT_MIN_THREADS, DEFAULT_MAX_THREADS) { }

        private const int DEFAULT_MIN_THREADS = 1, DEFAULT_MAX_THREADS = 5, MINIMUM_SEGMENT_SIZE = 8 * 1024;

        private SocketManager(string name, bool useHighPrioritySocketThreads, int minThreads, int maxThreads)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name;

            const long Receive_PauseWriterThreshold = 4L * 1024 * 1024 * 1024; // let's give it up to 4GiB of buffer for now
            const long Receive_ResumeWriterThreshold = 3L * 1024 * 1024 * 1024;

            var defaultPipeOptions = PipeOptions.Default;
            _schedulerPool = new DedicatedThreadPoolPipeScheduler(name + ":IO",
                minWorkers: minThreads, maxWorkers: maxThreads,
                priority: useHighPrioritySocketThreads ? ThreadPriority.AboveNormal : ThreadPriority.Normal);
            SendPipeOptions = new PipeOptions(
                defaultPipeOptions.Pool, _schedulerPool, _schedulerPool,
                pauseWriterThreshold: defaultPipeOptions.PauseWriterThreshold,
                resumeWriterThreshold: defaultPipeOptions.ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);
            ReceivePipeOptions = new PipeOptions(
                defaultPipeOptions.Pool, _schedulerPool, _schedulerPool,
                pauseWriterThreshold: Receive_PauseWriterThreshold,
                resumeWriterThreshold: Receive_ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);

            _completionPool = new DedicatedThreadPoolPipeScheduler(name + ":Completion",
                minWorkers: 1, maxWorkers: maxThreads, useThreadPoolQueueLength: 1);
        }

        private DedicatedThreadPoolPipeScheduler _schedulerPool, _completionPool;
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
            try { _schedulerPool?.Dispose(); } catch { }
            try { _completionPool?.Dispose(); } catch { }
            _schedulerPool = null;
            _completionPool = null;
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

        internal static Socket CreateSocket(EndPoint endpoint)
        {
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
            var socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            SocketConnection.SetRecommendedClientOptions(socket);
            return socket;
        }

        static void ConfigureTimeout(SocketAsyncEventArgs args, int timeoutMilliseconds)
        {
            var timeout = Task.Delay(timeoutMilliseconds);
            timeout.ContinueWith((t, state) =>
            {
                var a = (SocketAsyncEventArgs)state;
                try { Socket.CancelConnectAsync(a); } catch { }
                try
                { ((SocketAwaitable)a.UserToken).Complete(0, SocketError.TimedOut); }
                catch { }
            }, args);
        }
        internal void BeginConnectAsync(EndPoint endpoint, Socket socket, PhysicalConnection physicalConnection, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            var formattedEndpoint = Format.ToString(endpoint);
            multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);

            var awaitable = new SocketAwaitable();
            var args = new SocketAsyncEventArgs();
            args.UserToken = awaitable;
            args.RemoteEndPoint = endpoint;
            args.Completed += SocketAwaitable.Callback;
            try
            {
                if (socket.ConnectAsync(args))
                {
                    ConfigureTimeout(args, multiplexer.RawConfig.ConnectTimeout);
                }
                else
                {
                    SocketAwaitable.OnCompleted(args);
                }

                EndConnectAsync(awaitable, multiplexer, log, socket, physicalConnection);
            }
            catch (NotImplementedException ex)
            {
                if (!(endpoint is IPEndPoint))
                {
                    throw new InvalidOperationException("BeginConnect failed with NotImplementedException; consider using IP endpoints, or enable ResolveDns in the configuration", ex);
                }
                throw;
            }
        }
        private async void EndConnectAsync(SocketAwaitable awaitable, ConnectionMultiplexer multiplexer, TextWriter log, Socket socket, PhysicalConnection connection)
        {
            try
            {
                bool ignoreConnect = false;
                ShouldIgnoreConnect(connection, ref ignoreConnect);
                if (ignoreConnect) return;
                await awaitable;

                var socketMode = connection == null ? SocketMode.Abort : await connection.ConnectedAsync(socket, log, this).ForAwait();
                switch (socketMode)
                {
                    case SocketMode.Async:
                        multiplexer.LogLocked(log, "Starting read");
                        try
                        {
                            connection.StartReading();
                        }
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
                if (connection != null)
                {
                    try { connection.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
            catch (Exception outer)
            {
                ConnectionMultiplexer.TraceWithoutContext(outer.Message);
                if (connection != null)
                {
                    try { connection.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
        }

        partial void OnDispose();
        partial void OnShutdown(Socket socket);

        partial void ShouldIgnoreConnect(PhysicalConnection callback, ref bool ignore);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        internal void Shutdown(Socket socket)
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
            var s = _schedulerPool;
            return s == null ? null : $"{s.BusyCount} of {s.WorkerCount} busy ({s.MaxWorkerCount} max)";
        }

        internal void ScheduleTask(Action<object> action, object state)
            => _completionPool.Schedule(action, state);
    }
}
