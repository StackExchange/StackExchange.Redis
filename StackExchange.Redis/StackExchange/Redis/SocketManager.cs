using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
        }

        /// <summary>
        /// Gets the name of this SocketManager instance
        /// </summary>
        public string Name { get { return name; } }

        bool isDisposed;
        private readonly Dictionary<Socket, ISocketCallback> socketLookup = new Dictionary<Socket, ISocketCallback>();
        private readonly List<Socket> readQueue = new List<Socket>(), errorQueue = new List<Socket>();

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
                socketLookup.Add(socket, callback);
                if (socketLookup.Count == 1)
                {
                    Monitor.PulseAll(socketLookup);
                    if (Interlocked.CompareExchange(ref readerCount, 0, 0) == 0)
                        StartReader();
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

        private void ReadImpl()
        {
            List<Socket> dead = null;
            while (true)
            {
                readQueue.Clear();
                errorQueue.Clear();
                lock (socketLookup)
                {
                    if (isDisposed) return;

                    if (socketLookup.Count == 0)
                    {
                        // if empty, give it a few seconds chance before exiting
                        Monitor.Wait(socketLookup, TimeSpan.FromSeconds(20));
                        if (socketLookup.Count == 0) return; // nothing new came in, so exit
                    }
                    if (dead != null) dead.Clear();
                    foreach (var pair in socketLookup)
                    {
                        if (pair.Key.Connected)
                        {
                            readQueue.Add(pair.Key);
                            errorQueue.Add(pair.Key);
                        }
                        else
                        {
                            (dead ?? (dead = new List<Socket>())).Add(pair.Key);
                        }
                    }
                    if (dead != null && dead.Count != 0)
                    {
                        foreach (var socket in dead) socketLookup.Remove(socket);
                    }
                }

                int pollingSockets = readQueue.Count;
                if (pollingSockets == 0)
                {
                    // nobody had actual sockets; just sleep
                    Thread.Sleep(10);
                    continue;
                }
                
                try
                {
                    Socket.Select(readQueue, null, errorQueue, 100);
                    ConnectionMultiplexer.TraceWithoutContext(readQueue.Count != 0, "Read sockets: " + readQueue.Count);
                    ConnectionMultiplexer.TraceWithoutContext(errorQueue.Count != 0, "Error sockets: " + errorQueue.Count);
                }
                catch (Exception ex)
                { // this typically means a socket was disposed just before
                    Trace.WriteLine(ex.Message);
                    continue;
                }

                int totalWork = readQueue.Count + errorQueue.Count;
                if (totalWork == 0) continue;

                if (totalWork >= 10) // number of sockets we should attempt to process by ourself before asking for help
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

        internal void Shutdown(SocketToken token)
        {
            var socket = token.Socket;
            if (socket != null)
            {
                lock (socketLookup)
                {
                    socketLookup.Remove(socket);
                }
                try { socket.Shutdown(SocketShutdown.Both); } catch { }
                try { socket.Close(); } catch { }
                try { socket.Dispose(); } catch { }
            }
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

        private static void ProcessItems(Dictionary<Socket, ISocketCallback> socketLookup, List<Socket> list, CallbackOperation operation)

        {
            if (list == null) return;
            while (true)
            {
                // get the next item (note we could be competing with a worker here, hence lock)
                Socket socket;
                lock (list)
                {
                    int index = list.Count - 1;
                    if (index < 0) break;
                    socket = list[index];
                    list.RemoveAt(index); // note: removing from end to avoid moving everything
                }
                ISocketCallback callback;
                lock (socketLookup)
                {
                    if (!socketLookup.TryGetValue(socket, out callback)) callback = null;
                }
                if (callback != null)
                {
#if VERBOSE
                    var watch = Stopwatch.StartNew();
#endif
                    switch (operation)
                    {
                        case CallbackOperation.Read: callback.Read(); break;
                        case CallbackOperation.Error: callback.Error(); break;
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
            lock (socketLookup)
            {
                isDisposed = true;
                socketLookup.Clear();
                Monitor.PulseAll(socketLookup);
            }
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
                AddRead(socket, callback);
                var netStream = new NetworkStream(socket, false);
                callback.Connected(netStream);
            }
            catch
            {
                if (tuple != null)
                {
                    tuple.Item2.Error();
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
        void Connected(Stream stream);
        /// <summary>
        /// Indicates that data is available on the socket, and that the consumer should read from the socket
        /// </summary>
        void Read();
        /// <summary>
        /// Indicates that the socket has signalled an error condition
        /// </summary>
        void Error();
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
