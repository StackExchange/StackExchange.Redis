#if FEATURE_SOCKET_MODE_POLL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis
{
    partial class SocketManager
    {
        internal const SocketMode DefaultSocketMode = SocketMode.Poll;
        static readonly IntPtr[] EmptyPointers = new IntPtr[0];
        static readonly WaitCallback HelpProcessItems = state =>
        {
            var qdsl = state as QueueDrainSyncLock;
            if (qdsl != null && qdsl.Consume())
            {
                var mgr = qdsl.Manager;
                mgr.ProcessItems(false);
                qdsl.Pulse();
            }
        };

        private static ParameterizedThreadStart read = state => ((SocketManager)state).Read();

        readonly Queue<ISocketCallback> readQueue = new Queue<ISocketCallback>(), errorQueue = new Queue<ISocketCallback>();

        private readonly Dictionary<IntPtr, SocketPair> socketLookup = new Dictionary<IntPtr, SocketPair>();

        private int readerCount;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1060:MovePInvokesToNativeMethodsClass")]
        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern int select([In] int ignoredParameter, [In, Out] IntPtr[] readfds, [In, Out] IntPtr[] writefds, [In, Out] IntPtr[] exceptfds, [In] ref TimeValue timeout);

        private static void ProcessItems(Queue<ISocketCallback> queue, CallbackOperation operation)

        {
            if (queue == null) return;
            while (true)
            {
                // get the next item (note we could be competing with a worker here, hence lock)
                ISocketCallback callback;
                lock (queue)
                {
                    if (queue.Count == 0) break;
                    callback = queue.Dequeue();
                }
                if (callback != null)
                {
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
                }
            }
        }

        private void OnAddRead(Socket socket, ISocketCallback callback)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            lock (socketLookup)
            {
                if (isDisposed) throw new ObjectDisposedException(name);

                var handle = socket.Handle;
                if (handle == IntPtr.Zero) throw new ObjectDisposedException("socket");
                socketLookup.Add(handle, new SocketPair(socket, callback));
                if (socketLookup.Count == 1)
                {
                    Monitor.PulseAll(socketLookup);
                    if (Interlocked.CompareExchange(ref readerCount, 0, 0) == 0)
                        StartReader();
                }
            }
        }

        partial void OnDispose()
        {
            lock (socketLookup)
            {
                isDisposed = true;
                socketLookup.Clear();
                Monitor.PulseAll(socketLookup);
            }
        }

        partial void OnShutdown(Socket socket)
        {
            lock (socketLookup)
            {
                socketLookup.Remove(socket.Handle);
            }
        }

        private void ProcessItems(bool setState)
        {
            if(setState) managerState = ManagerState.ProcessReadQueue;
            ProcessItems(readQueue, CallbackOperation.Read);
            if (setState) managerState = ManagerState.ProcessErrorQueue;
            ProcessItems(errorQueue, CallbackOperation.Error);
        }
        private void Read()
        {
            bool weAreReader = false;
            try
            {
                weAreReader = Interlocked.CompareExchange(ref readerCount, 1, 0) == 0;
                if (weAreReader)
                {
                    managerState = ManagerState.Preparing;
                    ReadImpl();
                    managerState = ManagerState.Inactive;
                }
            }
            catch (Exception ex)
            {
                if (weAreReader)
                {
                    managerState = ManagerState.Faulted;
                }
                Debug.WriteLine(ex);
                Trace.WriteLine(ex);
            }
            finally
            {
                if (weAreReader) Interlocked.Exchange(ref readerCount, 0);
            }
        }

        internal ManagerState State => managerState;
        private volatile ManagerState managerState;
        private volatile int lastErrorTicks;
        internal string LastErrorTimeRelative()
        {
            var tmp = lastErrorTicks;
            if (tmp == 0) return "never";
            return unchecked(Environment.TickCount - tmp) + "ms ago";
        }
        private ISocketCallback GetCallback(IntPtr key)
        {
            lock(socketLookup)
            {
                SocketPair pair;
                return socketLookup.TryGetValue(key, out pair) ? pair.Callback : null;
            }
        }
        private void ReadImpl()
        {
            List<IntPtr> dead = null, active = new List<IntPtr>();
            List<ISocketCallback> activeCallbacks = new List<ISocketCallback>();
            IntPtr[] readSockets = EmptyPointers, errorSockets = EmptyPointers;
            long lastHeartbeat = Environment.TickCount;
            SocketPair[] allSocketPairs = null;
            while (true)
            {
                managerState = ManagerState.CheckForHeartbeat;
                active.Clear();
                activeCallbacks.Clear();
                dead?.Clear();

                // this check is actually a pace-maker; sometimes the Timer callback stalls for
                // extended periods of time, which can cause socket disconnect
                long now = Environment.TickCount;
                if (unchecked(now - lastHeartbeat) >= 15000)
                {
                    managerState = ManagerState.ExecuteHeartbeat;
                    lastHeartbeat = now;
                    lock (socketLookup)
                    {
                        if (allSocketPairs == null || allSocketPairs.Length != socketLookup.Count)
                            allSocketPairs = new SocketPair[socketLookup.Count];
                        socketLookup.Values.CopyTo(allSocketPairs, 0);
                    }
                    foreach (var pair in allSocketPairs)
                    {
                        var callback = pair.Callback;
                        if (callback != null) try { callback.OnHeartbeat(); } catch { }
                    }
                }

                managerState = ManagerState.LocateActiveSockets;
                lock (socketLookup)
                {
                    if (isDisposed) return;

                    if (socketLookup.Count == 0)
                    {
                        // if empty, give it a few seconds chance before exiting
                        managerState = ManagerState.NoSocketsPause;
                        Monitor.Wait(socketLookup, TimeSpan.FromSeconds(20));
                        if (socketLookup.Count == 0) return; // nothing new came in, so exit
                    }
                    managerState = ManagerState.PrepareActiveSockets;
                    foreach (var pair in socketLookup)
                    {
                        var socket = pair.Value.Socket;
                        if (socket.Handle == pair.Key && socket.Connected)
                            if (pair.Value.Socket.Connected)
                            {
                                active.Add(pair.Key);
                                activeCallbacks.Add(pair.Value.Callback);
                            }
                            else
                            {
                                (dead ?? (dead = new List<IntPtr>())).Add(pair.Key);
                            }
                    }
                    if (dead != null && dead.Count != 0)
                    {
                        managerState = ManagerState.CullDeadSockets;
                        foreach (var socket in dead) socketLookup.Remove(socket);
                    }
                }
                int pollingSockets = active.Count;
                if (pollingSockets == 0)
                {
                    // nobody had actual sockets; just sleep
                    managerState = ManagerState.NoActiveSocketsPause;
                    Thread.Sleep(10);
                    continue;
                }

                if (readSockets.Length < active.Count + 1)
                {
                    managerState = ManagerState.GrowingSocketArray;
                    ConnectionMultiplexer.TraceWithoutContext("Resizing socket array for " + active.Count + " sockets");
                    readSockets = new IntPtr[active.Count + 6]; // leave so space for growth
                    errorSockets = new IntPtr[active.Count + 6];
                }
                managerState = ManagerState.CopyingPointersForSelect;
                readSockets[0] = errorSockets[0] = (IntPtr)active.Count;
                active.CopyTo(readSockets, 1);
                active.CopyTo(errorSockets, 1);
                int ready;
                try
                {
                    var timeout = new TimeValue(1000);
                    managerState = ManagerState.ExecuteSelect;
                    ready = select(0, readSockets, null, errorSockets, ref timeout);
                    managerState = ManagerState.ExecuteSelectComplete;
                    if (ready <= 0) // -ve typically means a socket was disposed just before; just retry
                    {
                        bool hasWorkToDo = false;
                        if (ready == 0)
                        {
                            managerState = ManagerState.CheckForStaleConnections;
                            foreach (var s in activeCallbacks)
                            {
                                if (s.IsDataAvailable)
                                {
                                    hasWorkToDo = true;
                                }
                                else
                                {
#pragma warning disable 0420
                                    s.CheckForStaleConnection(ref managerState);
#pragma warning restore 0420
                                }
                            }
                            managerState = ManagerState.CheckForStaleConnectionsDone;
                        }
                        else
                        {
                            lastErrorTicks = Environment.TickCount;
                        }
                        if (!hasWorkToDo)
                        {
                            continue; 
                        }
                    }
                    ConnectionMultiplexer.TraceWithoutContext((int)readSockets[0] != 0, "Read sockets: " + (int)readSockets[0]);
                    ConnectionMultiplexer.TraceWithoutContext((int)errorSockets[0] != 0, "Error sockets: " + (int)errorSockets[0]);
                }
                catch (Exception ex)
                { // this typically means a socket was disposed just before; just retry
                    Trace.WriteLine(ex.Message);
                    continue;
                }

                bool haveWork = false;
                int queueCount = (int)readSockets[0];
                if (queueCount != 0)
                {
                    managerState = ManagerState.EnqueueRead;
                    lock (readQueue)
                    {
                        for (int i = 1; i <= queueCount; i++)
                        {
                            var callback = GetCallback(readSockets[i]);
                            if (callback != null)
                            {
                                readQueue.Enqueue(callback);
                                haveWork = true;
                            }
                        }
                    }
                }
                queueCount = (int)errorSockets[0];
                if (queueCount != 0)
                {
                    managerState = ManagerState.EnqueueError;
                    lock (errorQueue)
                    {
                        for (int i = 1; i <= queueCount; i++)
                        {
                            var callback = GetCallback(errorSockets[i]);
                            if (callback != null)
                            {
                                errorQueue.Enqueue(callback);
                                haveWork = true;
                            }
                        }
                    }
                }
                if(!haveWork)
                {
                    // edge case: select is returning 0, but data could still be available
                    managerState = ManagerState.EnqueueReadFallback;
                    lock (readQueue)
                    {
                        foreach (var callback in activeCallbacks)
                        {
                            if(callback.IsDataAvailable)
                            {
                                readQueue.Enqueue(callback);
                            }
                        }
                    }
                }


                if (ready >= 5) // number of sockets we should attempt to process by ourself before asking for help
                {
                    // seek help, work in parallel, then synchronize
                    var obj = new QueueDrainSyncLock(this);
                    lock (obj)
                    {
                        managerState = ManagerState.RequestAssistance;
                        ThreadPool.QueueUserWorkItem(HelpProcessItems, obj);
                        managerState = ManagerState.ProcessQueues;
                        ProcessItems(true);
                        if (!obj.Consume())
                        {   // then our worker arrived and picked up work; we need
                            // to let it finish; note that if it *didn't* get that far
                            // yet, the Consume() call will mean that it never tries
                            Monitor.Wait(obj);
                        }
                    }
                }
                else
                {
                    // just do it ourself
                    managerState = ManagerState.ProcessQueues;
                    ProcessItems(true);
                }
            }
        }

        private void StartReader()
        {
            var thread = new Thread(read, 32*1024) // don't need a huge stack
            {
                Priority = useHighPrioritySocketThreads ? ThreadPriority.AboveNormal : ThreadPriority.Normal,
                Name = name + ":Read",
                IsBackground = true
            };
            thread.Start(this);
        }
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

        struct SocketPair
        {
            public readonly ISocketCallback Callback;
            public readonly Socket Socket;
            public SocketPair(Socket socket, ISocketCallback callback)
            {
                Socket = socket;
                Callback = callback;
            }
        }
        sealed class QueueDrainSyncLock
        {
            private int workers;
            public QueueDrainSyncLock(SocketManager manager)
            {
                Manager = manager;
            }
            public SocketManager Manager { get; }

            internal bool Consume()
            {
                return Interlocked.CompareExchange(ref workers, 1, 0) == 0;
            }

            internal void Pulse()
            {
                lock (this)
                {
                    Monitor.PulseAll(this);
                }
            }
        }
    }
}
#endif