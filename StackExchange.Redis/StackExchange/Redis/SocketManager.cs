using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// A SocketManager monitors multiple sockets for availability of data; this is done using
    /// the Socket.Select API and a dedicated reader-thread, which allows for fast responses
    /// even when the system is under ambient load. 
    /// </summary>
    public sealed class SocketManager : IDisposable
    {
        private readonly string name;

        /// <summary>
        /// Creates a new (optionally named) SocketManager instance
        /// </summary>
        public SocketManager(string name = null)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            this.name = name;

            // we need a dedicated writer, because when under heavy ambient load
            // (a busy asp.net site, for example), workers are not reliable enough
            Thread dedicatedWriter = new Thread(writeAllQueues, 32 * 1024); // don't need a huge stack;
            dedicatedWriter.Priority = ThreadPriority.AboveNormal; // time critical
            dedicatedWriter.Name = name + ":Write";
            dedicatedWriter.IsBackground = true; // should not keep process alive
            dedicatedWriter.Start(this); // will self-exit when disposed
        }

        /// <summary>
        /// Gets the name of this SocketManager instance
        /// </summary>
        public string Name { get { return name; } }

        bool isDisposed;
        private readonly Dictionary<IntPtr, SocketPair> socketLookup = new Dictionary<IntPtr, SocketPair>();
        
        struct SocketPair
        {
            public readonly Socket Socket;
            public readonly ISocketCallback Callback;
            public SocketPair(Socket socket, ISocketCallback callback)
            {
                this.Socket = socket;
                this.Callback = callback;
            }
        }

        /// <summary>
        /// Adds a new socket and callback to the manager
        /// </summary>
        private void AddRead(Socket socket, ISocketCallback callback)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (callback == null) throw new ArgumentNullException("callback");

            lock (socketLookup)
            {
                if (isDisposed) throw new ObjectDisposedException(name);

                var handle = socket.Handle;
                if(handle == IntPtr.Zero) throw new ObjectDisposedException("socket");
                socketLookup.Add(handle, new SocketPair(socket, callback));
                if (socketLookup.Count == 1)
                {
                    Monitor.PulseAll(socketLookup);
                    if (Interlocked.CompareExchange(ref readerCount, 0, 0) == 0)
                        StartReader();
                }
            }
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

        private void StartReader()
        {
            var thread = new Thread(read, 32 * 1024); // don't need a huge stack
            thread.Name = name + ":Read";
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal; // time critical
            thread.Start(this);
        }

        private static ParameterizedThreadStart read = state => ((SocketManager)state).Read();
        private void Read()
        {
            bool weAreReader = false;
            try
            {
                weAreReader = Interlocked.CompareExchange(ref readerCount, 1, 0) == 0;
                if (weAreReader) ReadImpl();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Trace.WriteLine(ex);
            }
            finally
            {
                if (weAreReader) Interlocked.Exchange(ref readerCount, 0);
            }
        }

        readonly Queue<IntPtr> readQueue = new Queue<IntPtr>(), errorQueue = new Queue<IntPtr>();

        static readonly IntPtr[] EmptyPointers = new IntPtr[0];
        private void ReadImpl()
        {
            List<IntPtr> dead = null, active = new List<IntPtr>();
            IntPtr[] readSockets = EmptyPointers, errorSockets = EmptyPointers;
            long lastHeartbeat = Environment.TickCount;
            SocketPair[] allSocketPairs = null;
            while (true)
            {
                active.Clear();
                if (dead != null) dead.Clear();

                // this check is actually a pace-maker; sometimes the Timer callback stalls for
                // extended periods of time, which can cause socket disconnect
                long now = Environment.TickCount;
                if (unchecked(now - lastHeartbeat) >= 15000)
                {
                    lastHeartbeat = now;
                    lock(socketLookup)
                    {
                        if(allSocketPairs == null || allSocketPairs.Length != socketLookup.Count)
                            allSocketPairs = new SocketPair[socketLookup.Count];
                        socketLookup.Values.CopyTo(allSocketPairs, 0);
                    }
                    foreach(var pair in allSocketPairs)
                    {
                        var callback = pair.Callback;
                        if (callback != null) try { callback.OnHeartbeat(); } catch { }
                    }
                }


                lock (socketLookup)
                {
                    if (isDisposed) return;

                    if (socketLookup.Count == 0)
                    {
                        // if empty, give it a few seconds chance before exiting
                        Monitor.Wait(socketLookup, TimeSpan.FromSeconds(20));
                        if (socketLookup.Count == 0) return; // nothing new came in, so exit
                    }
                    foreach (var pair in socketLookup)
                    {
                        var socket = pair.Value.Socket;
                        if(socket.Handle == pair.Key && socket.Connected)
                        if (pair.Value.Socket.Connected)
                        {
                            active.Add(pair.Key);
                        }
                        else
                        {
                            (dead ?? (dead = new List<IntPtr>())).Add(pair.Key);
                        }
                    }
                    if (dead != null && dead.Count != 0)
                    {
                        foreach (var socket in dead) socketLookup.Remove(socket);
                    }
                }
                int pollingSockets = active.Count;
                if (pollingSockets == 0)
                {
                    // nobody had actual sockets; just sleep
                    Thread.Sleep(10);
                    continue;
                }

                if (readSockets.Length < active.Count + 1)
                {
                    ConnectionMultiplexer.TraceWithoutContext("Resizing socket array for " + active.Count + " sockets");
                    readSockets = new IntPtr[active.Count + 6]; // leave so space for growth
                    errorSockets = new IntPtr[active.Count + 6];
                }
                readSockets[0] = errorSockets[0] = (IntPtr)active.Count;
                active.CopyTo(readSockets, 1);
                active.CopyTo(errorSockets, 1);
                int ready;
                try
                {
                    var timeout = new TimeValue(1000);
                    ready = select(0, readSockets, null, errorSockets, ref timeout);
                    if (ready <= 0)
                    {
                        continue; // -ve typically means a socket was disposed just before; just retry
                    }

                    ConnectionMultiplexer.TraceWithoutContext((int)readSockets[0] != 0, "Read sockets: " + (int)readSockets[0]);
                    ConnectionMultiplexer.TraceWithoutContext((int)errorSockets[0] != 0, "Error sockets: " + (int)errorSockets[0]);
                }
                catch (Exception ex)
                { // this typically means a socket was disposed just before; just retry
                    Trace.WriteLine(ex.Message);
                    continue;
                }

                int queueCount = (int)readSockets[0];
                lock(readQueue)
                {
                    for (int i = 1; i <= queueCount; i++)
                    {
                        readQueue.Enqueue(readSockets[i]);
                    }
                }
                queueCount = (int)errorSockets[0];
                lock (errorQueue)
                {
                    for (int i = 1; i <= queueCount; i++)
                    {
                        errorQueue.Enqueue(errorSockets[i]);
                    }
                }


                if (ready >= 5) // number of sockets we should attempt to process by ourself before asking for help
                {
                    // seek help, work in parallel, then synchronize
                    lock (QueueDrainSyncLock)
                    {
                        ThreadPool.QueueUserWorkItem(HelpProcessItems, this);
                        ProcessItems();
                        Monitor.Wait(QueueDrainSyncLock);
                    }
                }
                else
                {
                    // just do it ourself
                    ProcessItems();
                }
            }
        }

        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern int select([In] int ignoredParameter, [In, Out] IntPtr[] readfds, [In, Out] IntPtr[] writefds, [In, Out] IntPtr[] exceptfds, [In] ref TimeValue timeout);

        [StructLayout(LayoutKind.Sequential)]
        internal struct TimeValue
        {
            public int Seconds;
            public int Microseconds;
            public TimeValue(int microSeconds)
            {
                Seconds = (int)(microSeconds / 1000000L);
                Microseconds = (int)(microSeconds % 1000000L);
            }
        }

        private void Shutdown(Socket socket)
        {
            if (socket != null)
            {
                lock (socketLookup)
                {
                    socketLookup.Remove(socket.Handle);
                }
                try { socket.Shutdown(SocketShutdown.Both); } catch { }
                try { socket.Close(); } catch { }
                try { socket.Dispose(); } catch { }
            }
        }
        internal void Shutdown(SocketToken token)
        {
            Shutdown(token.Socket);
        }

        private readonly object QueueDrainSyncLock = new object();
        static readonly WaitCallback HelpProcessItems = state =>
        {
            var mgr = (SocketManager)state;
            mgr.ProcessItems();
            lock (mgr.QueueDrainSyncLock)
            {
                Monitor.PulseAll(mgr.QueueDrainSyncLock);
            }
        };

        private void ProcessItems()
        {
            ProcessItems(socketLookup, readQueue, CallbackOperation.Read);
            ProcessItems(socketLookup, errorQueue, CallbackOperation.Error);
        }

        private static void ProcessItems(Dictionary<IntPtr, SocketPair> socketLookup, Queue<IntPtr> queue, CallbackOperation operation)

        {
            if (queue == null) return;
            while (true)
            {
                // get the next item (note we could be competing with a worker here, hence lock)
                IntPtr handle;
                lock (queue)
                {
                    if (queue.Count == 0) break;
                    handle = queue.Dequeue();
                }
                SocketPair pair;
                lock (socketLookup)
                {
                    if (!socketLookup.TryGetValue(handle, out pair)) continue;
                }
                var callback = pair.Callback;
                if (callback != null)
                {
#if VERBOSE
                    var watch = Stopwatch.StartNew();
#endif
                    try
                    {
                        switch (operation)
                        {
                            case CallbackOperation.Read: callback.Read(); break;
                            case CallbackOperation.Error: callback.Error(); break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                    }
#if VERBOSE
                    watch.Stop();
                    ConnectionMultiplexer.TraceWithoutContext(string.Format("{0}: {1}ms on {2}", operation, watch.ElapsedMilliseconds, callback));
#endif
                }
            }
        }

        private enum CallbackOperation
        {
            Read,
            Error
        }

        private int readerCount;

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
            lock (socketLookup)
            {
                isDisposed = true;
                socketLookup.Clear();
                Monitor.PulseAll(socketLookup);
            }
        }

        private readonly Queue<PhysicalBridge> writeQueue = new Queue<PhysicalBridge>();

        private static readonly ParameterizedThreadStart writeAllQueues = context =>
        {
            try { ((SocketManager)context).WriteAllQueues(); } catch { }
        };
        private static readonly WaitCallback writeOneQueue = context =>
        {

            try { ((SocketManager)context).WriteOneQueue(); } catch { }
        };

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
                    case WriteResult.QueueEmpty:
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
                        keepGoing = true;
                        break;
                    case WriteResult.QueueEmpty:
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

        internal SocketToken BeginConnect(EndPoint endpoint, ISocketCallback callback)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            socket.BeginConnect(endpoint, EndConnect, Tuple.Create(socket, callback));
            return new SocketToken(socket);
        }
        private void EndConnect(IAsyncResult ar)
        {
            Tuple<Socket, ISocketCallback> tuple = null;
            try
            {
                tuple = (Tuple<Socket, ISocketCallback>)ar.AsyncState;
                var socket = tuple.Item1;
                var callback = tuple.Item2;
                socket.EndConnect(ar);
                var netStream = new NetworkStream(socket, false);
                var socketMode = callback == null ? SocketMode.Abort : callback.Connected(netStream);
                switch (socketMode)
                {
                    case SocketMode.Poll:
                        AddRead(socket, callback);
                        break;
                    case SocketMode.Async:
                        try { callback.StartReading(); }
                        catch { Shutdown(socket); }
                        break;
                    default:
                        Shutdown(socket);
                        break;
                }
            }
            catch
            {
                if (tuple != null)
                {
                    try { tuple.Item2.Error(); }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(ex);
                    }
                }
            }
        }
    }
    /// <summary>
    /// Allows callbacks from SocketManager as work is discovered
    /// </summary>
    internal interface ISocketCallback
    {
        /// <summary>
        /// Indicates that a socket has connected
        /// </summary>
        SocketMode Connected(Stream stream);
        /// <summary>
        /// Indicates that data is available on the socket, and that the consumer should read synchronously from the socket while there is data
        /// </summary>
        void Read();
        /// <summary>
        /// Indicates that we cannot know whether data is available, and that the consume should commence reading asynchronously
        /// </summary>
        void StartReading();
        /// <summary>
        /// Indicates that the socket has signalled an error condition
        /// </summary>
        void Error();
        void OnHeartbeat();
    }

    internal enum SocketMode
    {
        Abort,
        Poll,
        Async
    }

    internal struct SocketToken
    {
        internal readonly Socket Socket;
        public SocketToken(Socket socket)
        {
            this.Socket = socket;
        }
        public int Available {  get {  return Socket == null ? 0 : Socket.Available; } }

        public bool HasValue { get { return Socket != null; } }
    }
}
