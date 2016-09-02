using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
#if CORE_CLR
using System.Runtime.InteropServices;
using System.Threading.Tasks;
#endif

namespace StackExchange.Redis
{
    internal enum SocketMode
    {
        Abort,
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
        SocketMode Connected(Stream stream, TextWriter log);
        /// <summary>
        /// Indicates that the socket has signalled an error condition
        /// </summary>
        void Error();

        void OnHeartbeat();

        /// <summary>
        /// Indicates that data is available on the socket, and that the consumer should read synchronously from the socket while there is data
        /// </summary>
        void Read();
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

        private enum CallbackOperation
        {
            Read,
            Error
        }

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

        internal SocketToken BeginConnect(EndPoint endpoint, ISocketCallback callback, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            var family = AddressFamily.InterNetwork;
            var socketType = SocketType.Stream;
            var protocolType = ProtocolType.Tcp;
            var socket = new Socket(family, socketType, protocolType);
            SetFastLoopbackOption(socket);
            socket.NoDelay = true;
            try
            {
                CompletionType connectCompletionType = CompletionType.Any;
                this.ShouldForceConnectCompletionType(ref connectCompletionType);

                var formattedEndpoint = Format.ToString(endpoint);
                var tuple = Tuple.Create(socket, callback);
                if (endpoint is DnsEndPoint)
                {
                    // A work-around for a Mono bug in BeginConnect(EndPoint endpoint, AsyncCallback callback, object state)
                    DnsEndPoint dnsEndpoint = (DnsEndPoint)endpoint;

#if CORE_CLR
                    var stopWatch = System.Diagnostics.Stopwatch.StartNew();

                    // Work around https://github.com/dotnet/corefx/issues/8768 by resolving and picking an IPEndPoint to which we can connect.
                    EndPoint endPointToUse = ResolveWorkingEndpointAsync(dnsEndpoint, family, socketType, protocolType, multiplexer, log).Result;

                    if (endPointToUse == null)
                    {
                        throw new InvalidOperationException(string.Format("BeginConnect: fail to connect to any of the resolved IP endpoints for Dns endpoint '{0}'; ensure Redis service is running at the configured endpoint.", formattedEndpoint));
                    }

                    formattedEndpoint = Format.ToString(endPointToUse);
                    multiplexer.LogLocked(log, "Using resolved IP endpoint: {0}", formattedEndpoint);
                    stopWatch.Stop();
                    multiplexer.LogLocked(log, "Time spent on resolving a working IP endpoint: {0} ms", stopWatch.Elapsed.TotalMilliseconds.ToString());

                    multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);
                    socket.ConnectAsync(endPointToUse).ContinueWith(t =>
                    {
                        multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                        EndConnectImpl(t, multiplexer, log, tuple);
                        multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                    });
#else
                    CompletionTypeHelper.RunWithCompletionType(
                        cb => {
                            multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);
                            return socket.BeginConnect(dnsEndpoint.Host, dnsEndpoint.Port, cb, tuple);
                        },
                        ar => {
                            multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);                            
                            EndConnectImpl(ar, multiplexer, log, tuple);
                            multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                        },
                        connectCompletionType);
#endif
                }
                else
                {
#if CORE_CLR
                    multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);
                    socket.ConnectAsync(endpoint).ContinueWith(t =>
                    {
                        multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                        EndConnectImpl(t, multiplexer, log, tuple);
                        multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                    });
#else
                    CompletionTypeHelper.RunWithCompletionType(
                        cb => {
                            multiplexer.LogLocked(log, "BeginConnect: {0}", formattedEndpoint);
                            return socket.BeginConnect(endpoint, cb, tuple);
                        },
                        ar => {
                            multiplexer.LogLocked(log, "EndConnect: {0}", formattedEndpoint);
                            EndConnectImpl(ar, multiplexer, log, tuple);
                            multiplexer.LogLocked(log, "Connect complete: {0}", formattedEndpoint);
                        },
                        connectCompletionType);
#endif
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

        internal void SetFastLoopbackOption(Socket socket)
        {
            // SIO_LOOPBACK_FAST_PATH (http://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost, 
            // or will be subject to WFP filtering.
            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

#if !CORE_CLR
            // windows only
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Win8/Server2012+ only
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 6 || osVersion.Major == 6 && osVersion.Minor >= 2)
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
#else
            try
            {
                // Ioctl is not supported on other platforms at the moment
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
            catch (SocketException)
            {
            }
#endif
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
                        ThreadPool.QueueUserWorkItem(writeOneQueue, this);
                    }
                }
            }
        }

        internal void Shutdown(SocketToken token)
        {
            Shutdown(token.Socket);
        }

        private void EndConnectImpl(IAsyncResult ar, ConnectionMultiplexer multiplexer, TextWriter log, Tuple<Socket, ISocketCallback> tuple)
        {
            try
            {
                bool ignoreConnect = false;
                ShouldIgnoreConnect(tuple.Item2, ref ignoreConnect);
                if (ignoreConnect) return;
                var socket = tuple.Item1;
                var callback = tuple.Item2;
#if CORE_CLR
                multiplexer.Wait((Task)ar); // make it explode if invalid (note: already complete at this point)
#else
                socket.EndConnect(ar);
#endif
                var netStream = new NetworkStream(socket, false);
                var socketMode = callback?.Connected(netStream, log) ?? SocketMode.Abort;
                switch (socketMode)
                {
                    case SocketMode.Poll:
                        multiplexer.LogLocked(log, "Starting poll");
                        OnAddRead(socket, callback);
                        break;
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
                if (tuple != null)
                {
                    try
                    { tuple.Item2.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
            catch(Exception outer)
            {
                ConnectionMultiplexer.TraceWithoutContext(outer.Message);
                if (tuple != null)
                {
                    try
                    { tuple.Item2.Error(); }
                    catch (Exception inner)
                    {
                        ConnectionMultiplexer.TraceWithoutContext(inner.Message);
                    }
                }
            }
        }

#if CORE_CLR
        private async Task<EndPoint> ResolveWorkingEndpointAsync(DnsEndPoint dnsEndPoint, AddressFamily family, SocketType socketType, ProtocolType protocol, ConnectionMultiplexer multiplexer, TextWriter log)
        {
            IPAddress[] hostAddresses = null;

            if (dnsEndPoint.Host == ".")
            {
                hostAddresses = new[] { IPAddress.Loopback };
            }
            else
            {
                try
                {
                    hostAddresses = await Dns.GetHostAddressesAsync(dnsEndPoint.Host);
                }
                catch (Exception ex)
                {
                    multiplexer.LogLocked(log, "ResolveWorkingEndpointAsync: error occurred when resolving IP endpoints for {0}: {1}", Format.ToString(dnsEndPoint), ex.ToString());
                    return null;
                }
            }

            foreach (var address in hostAddresses)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var endpoint = new IPEndPoint(address, dnsEndPoint.Port);
                var formattedEndpoint = Format.ToString(endpoint);
                try
                {
                    multiplexer.LogLocked(log, "ResolveWorkingEndpointAsync: trying to connect to '{0}'...", formattedEndpoint);
                    await socket.ConnectAsync(endpoint);
                    multiplexer.LogLocked(log, "ResolveWorkingEndpointAsync: connection to '{0}' established", formattedEndpoint);
                    return endpoint;
                }
                catch (Exception ex)
                {
                    multiplexer.LogLocked(log, "ResolveWorkingEndpointAsync: failed to connect to '{0}', {1}", formattedEndpoint, ex.ToString());
                }
            }

            return null;
        }
#endif

        partial void OnDispose();
        partial void OnShutdown(Socket socket);

        partial void ShouldIgnoreConnect(ISocketCallback callback, ref bool ignore);
        
        partial void ShouldForceConnectCompletionType(ref CompletionType completionType);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        private void Shutdown(Socket socket)
        {
            if (socket != null)
            {
                OnShutdown(socket);
                try { socket.Shutdown(SocketShutdown.Both); } catch { }
#if !CORE_CLR
                try { socket.Close(); } catch { }
#endif
                try { socket.Dispose(); } catch { }
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
