using System;
using System.Collections.Generic;
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
        [Obsolete("just don't", error: true)]
        Poll,
        Async
    }

    /// <summary>
    /// Allows callbacks from SocketManager as work is discovered
    /// </summary>
    internal partial interface ISocketCallback
    {
        /// <summary>
        /// Indicates that a socket has connected
        /// </summary>
        /// <param name="pipe">The network stream for this socket.</param>
        /// <param name="log">A text logger to write to.</param>
        ValueTask<SocketMode> ConnectedAsync(IDuplexPipe pipe, TextWriter log);

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

        private readonly Queue<PhysicalBridge> writeQueue = new Queue<PhysicalBridge>();
        private bool isDisposed;
        private readonly bool useHighPrioritySocketThreads = true;

        /// <summary>
        /// Gets the name of this SocketManager instance
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        public SocketManager(string name = null) : this(name, true) { }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name;
            this.useHighPrioritySocketThreads = useHighPrioritySocketThreads;

            _writeOneQueueAsync = () => WriteOneQueueAsync();

            Task.Run(() => WriteAllQueuesAsync());
        }
        private readonly Func<Task> _writeOneQueueAsync;


        private enum CallbackOperation
        {
            Read,
            Error
        }

        /// <summary>
        /// Releases all resources associated with this instance
        /// </summary>
        public void Dispose()
        {
            lock (writeQueue)
            {
                // make sure writer threads know to exit
                isDisposed = true;
                Monitor.PulseAll(writeQueue);
            }
            OnDispose();
        }

        //static readonly PipeOptions PipeOptions = PipeOptions.Default;
        static readonly PipeOptions PipeOptions = new PipeOptions(
            readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);
        internal SocketToken BeginConnect(EndPoint endpoint, ISocketCallback callback, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ? AddressFamily.InterNetwork : endpoint.AddressFamily;
            var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);

            
            try
            {
                var formattedEndpoint = Format.ToString(endpoint);

                var t = SocketConnection.ConnectAsync(endpoint, PipeOptions,
                    SocketConnectionOptions.SyncReader | SocketConnectionOptions.SyncWriter,
                    // SocketConnectionOptions.None,
                    onConnected: conn => EndConnectAsync(conn, multiplexer, log, callback),
                    socket: socket);
                GC.KeepAlive(t); // make compiler happier
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



        internal void RequestWrite(PhysicalBridge bridge, bool forced)
        {
            if (Interlocked.CompareExchange(ref bridge.inWriteQueue, 1, 0) == 0 || forced)
            {
                lock (writeQueue)
                {
                    writeQueue.Enqueue(bridge);
                    if (writeQueue.Count == 1)
                    {
                        Monitor.PulseAll(writeQueue);
                    }
                    else if (writeQueue.Count >= 2)
                    { // struggling are we? let's have some help dealing with the backlog
                        Task.Run(_writeOneQueueAsync);
                    }
                }
            }
        }
        
        internal void Shutdown(SocketToken token)
        {
            Shutdown(token.Socket);
        }

        private async Task EndConnectAsync(SocketConnection connection, ConnectionMultiplexer multiplexer, TextWriter log, ISocketCallback callback)
        {
            try
            {
                bool ignoreConnect = false;
                var socket = connection?.Socket;
                ShouldIgnoreConnect(callback, ref ignoreConnect);
                if (ignoreConnect) return;

                var socketMode = callback == null ? SocketMode.Abort : await callback.ConnectedAsync(connection, log);
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

        private async Task WriteAllQueuesAsync()
        {
            while (true)
            {
                PhysicalBridge bridge;
                lock (writeQueue)
                {
                    if (writeQueue.Count == 0)
                    {
                        if (isDisposed) break; // <========= exit point
                        Monitor.Wait(writeQueue);
                        if (isDisposed) break; // (woken by Dispose)
                        if (writeQueue.Count == 0) continue; // still nothing...
                    }
                    bridge = writeQueue.Dequeue();
                }

                switch (await bridge.WriteQueueAsync(200))
                {
                    case WriteResult.MoreWork:
                    case WriteResult.QueueEmptyAfterWrite:
                        // back of the line!
                        lock (writeQueue)
                        {
                            writeQueue.Enqueue(bridge);
                        }
                        break;
                    case WriteResult.CompetingWriter:
                        break;
                    case WriteResult.NoConnection:
                        Interlocked.Exchange(ref bridge.inWriteQueue, 0);
                        break;
                    case WriteResult.NothingToDo:
                        if (!bridge.ConfirmRemoveFromWriteQueue())
                        { // more snuck in; back of the line!
                            lock (writeQueue)
                            {
                                writeQueue.Enqueue(bridge);
                            }
                        }
                        break;
                }
            }
        }

        private Task WriteOneQueueAsync()
        {
            PhysicalBridge bridge;
            lock (writeQueue)
            {
                bridge = writeQueue.Count == 0 ? null : writeQueue.Dequeue();
            }
            if (bridge == null) return Task.CompletedTask;
            return WriteOneQueueAsyncImpl(bridge);
        }
        private async Task WriteOneQueueAsyncImpl(PhysicalBridge bridge)
        {
            bool keepGoing;
            do
            {
                switch (await bridge.WriteQueueAsync(-1))
                {
                    case WriteResult.MoreWork:
                    case WriteResult.QueueEmptyAfterWrite:
                        keepGoing = true;
                        break;
                    case WriteResult.NothingToDo:
                        keepGoing = !bridge.ConfirmRemoveFromWriteQueue();
                        break;
                    case WriteResult.CompetingWriter:
                        keepGoing = false;
                        break;
                    case WriteResult.NoConnection:
                        Interlocked.Exchange(ref bridge.inWriteQueue, 0);
                        keepGoing = false;
                        break;
                    default:
                        keepGoing = false;
                        break;
                }
            } while (keepGoing);
        }
    }
}
