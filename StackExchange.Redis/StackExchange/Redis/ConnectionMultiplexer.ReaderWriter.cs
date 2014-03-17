using System;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Redis
{
    partial class ConnectionMultiplexer
    {
        internal SocketManager SocketManager {  get {  return socketManager; } }
        private SocketManager socketManager;
        private bool ownsSocketManager;

        partial void OnCreateReaderWriter(ConfigurationOptions configuration)
        {
            this.ownsSocketManager = configuration.SocketManager == null;
            this.socketManager = configuration.SocketManager ?? new SocketManager(configuration.ClientName);

            // we need a dedicated writer, because when under heavy ambient load
            // (a busy asp.net site, for example), workers are not reliable enough
            Thread dedicatedWriter = new Thread(writeAllQueues);
            dedicatedWriter.Name = socketManager.Name + ":Write";
            dedicatedWriter.IsBackground = true; // should not keep process alive
            dedicatedWriter.Start(this); // will self-exit when disposed
        }

        partial void OnCloseReaderWriter()
        {
            lock (writeQueue)
            { // make sure writer threads know to exit
                Monitor.PulseAll(writeQueue);
            }
            if (ownsSocketManager) socketManager.Dispose();
            socketManager = null;
        }

        private readonly Queue<PhysicalBridge> writeQueue = new Queue<PhysicalBridge>();

        internal void RequestWrite(PhysicalBridge bridge, bool forced)
        {
            if (bridge == null) return;
            Trace("Requesting write: " + bridge.Name);
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
        partial void OnWriterCreated();

        private static readonly ParameterizedThreadStart writeAllQueues = context =>
        {
            try { ((ConnectionMultiplexer)context).WriteAllQueues(); } catch { }
        };
        private static readonly WaitCallback writeOneQueue = context =>
        {

            try { ((ConnectionMultiplexer)context).WriteOneQueue(); } catch { }
        };

        private void WriteAllQueues()
        {
            OnWriterCreated();
            while (true)
            {
                PhysicalBridge bridge;
                lock (writeQueue)
                {
                    if (writeQueue.Count == 0)
                    {
                        if (isDisposed) break; // <========= exit point
                        Monitor.Wait(writeQueue, 500);
                        continue;
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
            OnWriterCreated();
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

        //private void Read()
        //{
        //    throw new NotImplementedException();
        //}
    }
}
