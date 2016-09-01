using Channels.Networking.Libuv;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Channels;
using System.Threading;
//#if CORE_CLR
//using System.Runtime.InteropServices;
//using System.Threading.Tasks;
//#endif

namespace StackExchange.Redis
{
        //internal enum SocketMode
        //{
        //    Abort,
        //    Poll,
        //    Async
        //}

        /// <summary>
        /// Allows callbacks from SocketManager as work is discovered
        /// </summary>
        internal partial interface ISocketCallback
        {
            /// <summary>
            /// Indicates that a socket has connected
            /// </summary>
            bool Connected(Stream stream, TextWriter log);
            /// <summary>
            /// Indicates that the socket has signalled an error condition
            /// </summary>
            void Error();

            void OnHeartbeat();
            void EndConnect(ref SocketToken socket);
        void SetLastRead();
        void OnClosed();
        void OnResult(ref RawResult result);
        RawResult TryParseResult(byte[] arr, ref int offset, ref int count);

        //        /// <summary>
        //        /// Indicates that data is available on the socket, and that the consumer should read synchronously from the socket while there is data
        //        /// </summary>
        //        void Read();
        //        /// <summary>
        //        /// Indicates that we cannot know whether data is available, and that the consume should commence reading asynchronously
        //        /// </summary>
        //        void StartReading();

        //        // check for write-read timeout
        //        void CheckForStaleConnection(ref SocketManager.ManagerState state);

        //        bool IsDataAvailable { get; }
    }

        internal struct SocketToken
        {
            internal
#if DEBUG
            readonly
#endif
              UvTcpConnection Connection;
            public SocketToken(UvTcpConnection connection)
            {
                Connection = connection;
            }
            // public int Available => Socket?.Available ?? 0;

            public bool HasValue => Connection != null;
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
        private static readonly ParameterizedThreadStart writeAllQueues = context =>
        {
            try { ((SocketManager)context).WriteAllQueues(); } catch { }
        };

        private static readonly WaitCallback writeOneQueue = context =>
        {

            try { ((SocketManager)context).WriteOneQueue(); } catch { }
        };

        private readonly string name;

        private readonly Queue<PhysicalBridge> writeQueue = new Queue<PhysicalBridge>();

        bool isDisposed;
        private bool useHighPrioritySocketThreads = true;

        /// <summary>
        /// Creates a new (optionally named) SocketManager instance
        /// </summary>
        public SocketManager(string name = null) : this(name, true) { }

        /// <summary>
        /// Creates a new SocketManager instance
        /// </summary>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            this.name = name;
            this.useHighPrioritySocketThreads = useHighPrioritySocketThreads;

            // we need a dedicated writer, because when under heavy ambient load
            // (a busy asp.net site, for example), workers are not reliable enough
#if !CORE_CLR
            Thread dedicatedWriter = new Thread(writeAllQueues, 32 * 1024); // don't need a huge stack;
            dedicatedWriter.Priority = useHighPrioritySocketThreads ? ThreadPriority.AboveNormal : ThreadPriority.Normal;
#else
            Thread dedicatedWriter = new Thread(writeAllQueues);
#endif
            dedicatedWriter.Name = name + ":Write";
            dedicatedWriter.IsBackground = true; // should not keep process alive
            dedicatedWriter.Start(this); // will self-exit when disposed
        }

        //        private enum CallbackOperation
        //        {
        //            Read,
        //            Error
        //        }

        /// <summary>
        /// Gets the name of this SocketManager instance
        /// </summary>
        public string Name => name;

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

        UvThread thread;        
        internal async void BeginConnect(EndPoint endpoint, ISocketCallback callback, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            UvTcpConnection conn = null;
            try
            {
                if (!(endpoint is IPEndPoint))
                {
                    throw new NotSupportedException("IP only (not DNS) implemented right now");
                }
                if (thread == null) thread = new UvThread();
                var client = new UvTcpClient(thread, (IPEndPoint)endpoint);

                conn = await client.ConnectAsync();
                var socket = new SocketToken(conn);

                var formattedEndpoint = Format.ToString(endpoint);

                multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                callback.EndConnect(ref socket);
                EndConnectImpl(ref socket, multiplexer, log, callback);
                multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
            }
            catch (Exception ex)
            {
                try { callback.Error(); } catch { }
                Shutdown(conn, ex);
            }
        }
        //        internal void SetFastLoopbackOption(Socket socket)
        //        {
        //            // SIO_LOOPBACK_FAST_PATH (http://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
        //            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost, 
        //            // or will be subject to WFP filtering.
        //            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

        //#if !CORE_CLR
        //            // windows only
        //            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        //            {
        //                // Win8/Server2012+ only
        //                var osVersion = Environment.OSVersion.Version;
        //                if (osVersion.Major > 6 || osVersion.Major == 6 && osVersion.Minor >= 2)
        //                {
        //                    byte[] optionInValue = BitConverter.GetBytes(1);
        //                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
        //                }
        //            }
        //#else
        //            try
        //            {
        //                // Ioctl is not supported on other platforms at the moment
        //                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        //                {
        //                    byte[] optionInValue = BitConverter.GetBytes(1);
        //                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
        //                }
        //            }
        //            catch (SocketException)
        //            {
        //            }
        //#endif
        //        }

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
                        ThreadPool.QueueUserWorkItem(writeOneQueue, this);
                    }
                }
            }
        }

        internal void Shutdown(SocketToken token)
        {
            Shutdown(token.Connection, null);
        }

        private void EndConnectImpl(ref SocketToken socketToken, ConnectionMultiplexer multiplexer, TextWriter log, ISocketCallback callback)
        {
            try
            {
                bool ignoreConnect = false;
                ShouldIgnoreConnect(callback, ref ignoreConnect);
                if (ignoreConnect) return;
                var socket = socketToken.Connection;

                multiplexer.LogLocked(log, "Starting read");
                ReadLoop(socket, callback);
            }
            catch (Exception outer)
            {
                Shutdown(socketToken.Connection, outer);
                ConnectionMultiplexer.TraceWithoutContext(outer.Message);
                if (callback != null)
                {
                    try
                    { callback.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
        }

        private async void ReadLoop(UvTcpConnection connection, ISocketCallback callback)
        {
            try
            {
                while (true)
                {
                    var buffer = await connection.Input;

                    if (buffer.Length == 0 && connection.Input.IsCompleted)
                        break;
                    callback.SetLastRead();
                    RawResult result;
                    while (TryParseResult(callback, ref buffer, out result))
                    {
                        callback.OnResult(ref result);
                    }
                    buffer.Consumed(buffer.Start);
                }
                callback.OnClosed();
                Shutdown(connection, null);
            }
            catch (Exception ex)
            {
                try { callback.Error(); } catch { }
                Shutdown(connection, ex);
            }
        }

        private bool TryParseResult(ISocketCallback callback, ref ReadableBuffer buffer, out RawResult result)
        {
            if (!buffer.IsSingleSpan) throw new NotImplementedException("Multi-span parsing");
            var span = buffer.FirstSpan;
            var arr = span.Array;
            int offset = span.Offset, count = span.Length;
            result = callback.TryParseResult(arr, ref offset, ref count);
            if(result.HasValue)
            {
                buffer = buffer.Slice(offset - span.Offset); // move forwards
                return true;
            }
            return false;
        }

        partial void OnDispose();
        partial void OnShutdown(UvTcpConnection socket);

        partial void ShouldIgnoreConnect(ISocketCallback callback, ref bool ignore);

        private void Shutdown(UvTcpConnection connection, Exception error)
        {
            if (connection != null)
            {
                OnShutdown(connection);
                try { connection.Output.CompleteWriting(error); } catch { }
                try { connection.Input.CompleteReading(error); } catch { }
            }
        }

        private void WriteAllQueues()
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

                switch (bridge.WriteQueue(200))
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

        private void WriteOneQueue()
        {
            PhysicalBridge bridge;
            lock (writeQueue)
            {
                bridge = writeQueue.Count == 0 ? null : writeQueue.Dequeue();
            }
            if (bridge == null) return;
            bool keepGoing;
            do
            {
                switch (bridge.WriteQueue(-1))
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
