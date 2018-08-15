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
            : this(name, false, DEFAULT_WORKERS) { }

        /// <summary>
        /// Default / shared socket manager
        /// </summary>
        public static SocketManager Shared
        {
            get
            {
                var shared = _shared;
                if (shared != null) return _shared;
                try
                {
                    // note: we'll allow a higher max thread count on the shared one
                    shared = new SocketManager("DefaultSocketManager", false, DEFAULT_WORKERS * 2);
                    if (Interlocked.CompareExchange(ref _shared, shared, null) == null)
                        shared = null;
                }
                finally { shared?.Dispose(); }
                return Volatile.Read(ref _shared);
            }
        }

        /// <summary>Returns a string that represents the current object.</summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            var scheduler = SchedulerPool;
            var comp = CompletionPool;

            return $"scheduler - queue: {scheduler?.TotalServicedByQueue}, pool: {scheduler?.TotalServicedByPool}; completion - queue: {comp ?.TotalServicedByQueue}, pool: {comp?.TotalServicedByPool}";
        }

        private static SocketManager _shared;

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
            : this(name, useHighPrioritySocketThreads, DEFAULT_WORKERS) { }

        private const int DEFAULT_WORKERS = 5, MINIMUM_SEGMENT_SIZE = 8 * 1024;

        private SocketManager(string name, bool useHighPrioritySocketThreads, int workerCount)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            Name = name;

            const long Receive_PauseWriterThreshold = 4L * 1024 * 1024 * 1024; // let's give it up to 4GiB of buffer for now
            const long Receive_ResumeWriterThreshold = 3L * 1024 * 1024 * 1024;

            var defaultPipeOptions = PipeOptions.Default;
            _schedulerPool = new DedicatedThreadPoolPipeScheduler(name + ":IO",
                workerCount: workerCount,
                priority: useHighPrioritySocketThreads ? ThreadPriority.AboveNormal : ThreadPriority.Normal);
            SendPipeOptions = new PipeOptions(
                pool: defaultPipeOptions.Pool,
                readerScheduler: _schedulerPool,
                writerScheduler: _schedulerPool,
                pauseWriterThreshold: defaultPipeOptions.PauseWriterThreshold,
                resumeWriterThreshold: defaultPipeOptions.ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);
            ReceivePipeOptions = new PipeOptions(
                pool: defaultPipeOptions.Pool,
                readerScheduler: _schedulerPool,
                writerScheduler: _schedulerPool,
                pauseWriterThreshold: Receive_PauseWriterThreshold,
                resumeWriterThreshold: Receive_ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);

            _completionPool = new DedicatedThreadPoolPipeScheduler(name + ":Completion",
                workerCount: workerCount, useThreadPoolQueueLength: 1);
        }

        private DedicatedThreadPoolPipeScheduler _schedulerPool, _completionPool;
        internal readonly PipeOptions SendPipeOptions, ReceivePipeOptions;

        internal DedicatedThreadPoolPipeScheduler SchedulerPool => _schedulerPool;
        internal DedicatedThreadPoolPipeScheduler CompletionPool => _completionPool;

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
            var addressFamily = endpoint.AddressFamily;
            if (addressFamily == AddressFamily.Unspecified && endpoint is DnsEndPoint)
            {   // default DNS to ipv4 if not specified explicitly
                addressFamily = AddressFamily.InterNetwork;
            }

            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
            var socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            SocketConnection.SetRecommendedClientOptions(socket);
            //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            return socket;
        }

        partial void OnDispose();

        internal string GetState()
        {
            var s = _schedulerPool;
            return s == null ? null : $"{s.AvailableCount} of {s.WorkerCount} available";
        }

        internal void ScheduleTask(Action<object> action, object state)
            => _completionPool.Schedule(action, state);
    }
}
