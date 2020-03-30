using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
        /// Creates a new <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        public SocketManager(string name)
            : this(name, DEFAULT_WORKERS, SocketManagerOptions.None) { }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
            : this(name, DEFAULT_WORKERS, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) { }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">the number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, int workerCount, bool useHighPrioritySocketThreads)
            : this(name, workerCount, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) {}

        private static SocketManagerOptions UseHighPrioritySocketThreads(bool value)
            => value ? SocketManagerOptions.UseHighPrioritySocketThreads : SocketManagerOptions.None;

        /// <summary>
        /// Additional options for configuring the socket manager
        /// </summary>
        [Flags]
        public enum SocketManagerOptions
        {
            /// <summary>
            /// No additional options
            /// </summary>
            None = 0,
            /// <summary>
            /// Whether the <see cref="SocketManager"/> should use high priority sockets.
            /// </summary>
            UseHighPrioritySocketThreads = 1 << 0,
            /// <summary>
            /// Use the regular thread-pool for all scheduling
            /// </summary>
            UseThreadPool = 1 << 1,
        }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">the number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="options"></param>
        public SocketManager(string name = null, int workerCount = 0, SocketManagerOptions options = SocketManagerOptions.None)
        {
            if (string.IsNullOrWhiteSpace(name)) name = GetType().Name;
            if (workerCount <= 0) workerCount = DEFAULT_WORKERS;
            Name = name;
            bool useHighPrioritySocketThreads = (options & SocketManagerOptions.UseHighPrioritySocketThreads) != 0,
                useThreadPool = (options & SocketManagerOptions.UseThreadPool) != 0;

            const long Receive_PauseWriterThreshold = 4L * 1024 * 1024 * 1024; // receive: let's give it up to 4GiB of buffer for now
            const long Receive_ResumeWriterThreshold = 3L * 1024 * 1024 * 1024; // (large replies get crazy big)

            var defaultPipeOptions = PipeOptions.Default;

            long Send_PauseWriterThreshold = Math.Max(
                512 * 1024,// send: let's give it up to 0.5MiB
                defaultPipeOptions.PauseWriterThreshold); // or the default, whichever is bigger
            long Send_ResumeWriterThreshold = Math.Max(
                Send_PauseWriterThreshold / 2,
                defaultPipeOptions.ResumeWriterThreshold);

            Scheduler = PipeScheduler.ThreadPool;
            if (!useThreadPool)
            {
                Scheduler = new DedicatedThreadPoolPipeScheduler(name + ":IO",
                    workerCount: workerCount,
                    priority: useHighPrioritySocketThreads ? ThreadPriority.AboveNormal : ThreadPriority.Normal);
            }
            SendPipeOptions = new PipeOptions(
                pool: defaultPipeOptions.Pool,
                readerScheduler: Scheduler,
                writerScheduler: Scheduler,
                pauseWriterThreshold: Send_PauseWriterThreshold,
                resumeWriterThreshold: Send_ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);
            ReceivePipeOptions = new PipeOptions(
                pool: defaultPipeOptions.Pool,
                readerScheduler: Scheduler,
                writerScheduler: Scheduler,
                pauseWriterThreshold: Receive_PauseWriterThreshold,
                resumeWriterThreshold: Receive_ResumeWriterThreshold,
                minimumSegmentSize: Math.Max(defaultPipeOptions.MinimumSegmentSize, MINIMUM_SEGMENT_SIZE),
                useSynchronizationContext: false);
        }

        /// <summary>
        /// Default / shared socket manager using a dedicated thread-pool
        /// </summary>
        public static SocketManager Shared
        {
            get
            {
                var shared = s_shared;
                if (shared != null) return shared;
                try
                {
                    // note: we'll allow a higher max thread count on the shared one
                    shared = new SocketManager("DefaultSocketManager", DEFAULT_WORKERS * 2, false);
                    if (Interlocked.CompareExchange(ref s_shared, shared, null) == null)
                        shared = null;
                }
                finally { shared?.Dispose(); }
                return Volatile.Read(ref s_shared);
            }
        }

        /// <summary>
        /// Shared socket manager using the main thread-pool
        /// </summary>
        public static SocketManager ThreadPool
        {
            get
            {
                var shared = s_threadPool;
                if (shared != null) return shared;
                try
                {
                    // note: we'll allow a higher max thread count on the shared one
                    shared = new SocketManager("ThreadPoolSocketManager", options: SocketManagerOptions.UseThreadPool);
                    if (Interlocked.CompareExchange(ref s_threadPool, shared, null) == null)
                        shared = null;
                }
                finally { shared?.Dispose(); }
                return Volatile.Read(ref s_threadPool);
            }
        }

        /// <summary>Returns a string that represents the current object.</summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            var scheduler = SchedulerPool;
            if (scheduler == null) return Name;
            return $"{Name} - queue: {scheduler?.TotalServicedByQueue}, pool: {scheduler?.TotalServicedByPool}";
        }

        private static SocketManager s_shared, s_threadPool;

        private const int DEFAULT_WORKERS = 5, MINIMUM_SEGMENT_SIZE = 8 * 1024;

        internal readonly PipeOptions SendPipeOptions, ReceivePipeOptions;

        internal PipeScheduler Scheduler { get; private set; }

        internal DedicatedThreadPoolPipeScheduler SchedulerPool => Scheduler as DedicatedThreadPoolPipeScheduler;

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
            var tmp = SchedulerPool;
            Scheduler = PipeScheduler.ThreadPool;
            try { tmp?.Dispose(); } catch { }
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
            var s = SchedulerPool;
            return s == null ? null : $"{s.AvailableCount} of {s.WorkerCount} available";
        }
    }
}
