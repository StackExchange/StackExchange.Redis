using System;
using System.Diagnostics;
using System.IO.Pipelines;
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
    public sealed partial class SocketManager : IDisposable
    {
        /// <summary>
        /// Gets the name of this SocketManager instance.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        public SocketManager(string name)
            : this(name, DEFAULT_WORKERS, SocketManagerOptions.None) { }

        /// <summary>
        /// Creates a new <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, bool useHighPrioritySocketThreads)
            : this(name, DEFAULT_WORKERS, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) { }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">the number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="useHighPrioritySocketThreads">Whether this <see cref="SocketManager"/> should use high priority sockets.</param>
        public SocketManager(string name, int workerCount, bool useHighPrioritySocketThreads)
            : this(name, workerCount, UseHighPrioritySocketThreads(useHighPrioritySocketThreads)) { }

        private static SocketManagerOptions UseHighPrioritySocketThreads(bool value)
            => value ? SocketManagerOptions.UseHighPrioritySocketThreads : SocketManagerOptions.None;

        /// <summary>
        /// Additional options for configuring the socket manager.
        /// </summary>
        [Flags]
        public enum SocketManagerOptions
        {
            /// <summary>
            /// No additional options.
            /// </summary>
            None = 0,

            /// <summary>
            /// Whether the <see cref="SocketManager"/> should use high priority sockets.
            /// </summary>
            UseHighPrioritySocketThreads = 1 << 0,

            /// <summary>
            /// Use the regular thread-pool for all scheduling.
            /// </summary>
            UseThreadPool = 1 << 1,
        }

        /// <summary>
        /// Creates a new (optionally named) <see cref="SocketManager"/> instance.
        /// </summary>
        /// <param name="name">The name for this <see cref="SocketManager"/>.</param>
        /// <param name="workerCount">The number of dedicated workers for this <see cref="SocketManager"/>.</param>
        /// <param name="options">Options to use when creating the socket manager.</param>
        public SocketManager(string? name = null, int workerCount = 0, SocketManagerOptions options = SocketManagerOptions.None)
        {
            if (name.IsNullOrWhiteSpace()) name = GetType().Name;
            if (workerCount <= 0) workerCount = DEFAULT_WORKERS;
            Name = name;

            const long Receive_PauseWriterThreshold = 4L * 1024 * 1024 * 1024; // receive: let's give it up to 4GiB of buffer for now
            const long Receive_ResumeWriterThreshold = 3L * 1024 * 1024 * 1024; // (large replies get crazy big)

            var defaultPipeOptions = PipeOptions.Default;

            long send_PauseWriterThreshold = Math.Max(
                512 * 1024, // send: let's give it up to 0.5MiB
                defaultPipeOptions.PauseWriterThreshold); // or the default, whichever is bigger
            long send_ResumeWriterThreshold = Math.Max(
                send_PauseWriterThreshold / 2,
                defaultPipeOptions.ResumeWriterThreshold);

            Scheduler = PipeScheduler.ThreadPool;
            SendPipeOptions = new PipeOptions(
                pool: defaultPipeOptions.Pool,
                readerScheduler: Scheduler,
                writerScheduler: Scheduler,
                pauseWriterThreshold: send_PauseWriterThreshold,
                resumeWriterThreshold: send_ResumeWriterThreshold,
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
        /// Default / shared socket manager using a dedicated thread-pool.
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
        /// Shared socket manager using the main thread-pool.
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

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => Name;

        private static SocketManager? s_shared, s_threadPool;

        private const int DEFAULT_WORKERS = 5, MINIMUM_SEGMENT_SIZE = 8 * 1024;

        internal readonly PipeOptions SendPipeOptions, ReceivePipeOptions;

        internal PipeScheduler Scheduler { get; private set; }

        private enum CallbackOperation
        {
            Read,
            Error,
        }

        /// <summary>
        /// Releases all resources associated with this instance.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            OnDispose();
        }

        internal static Socket CreateSocket(EndPoint endpoint)
        {
            var addressFamily = endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;

            var socket = addressFamily == AddressFamily.Unspecified
                ? new Socket(SocketType.Stream, protocolType)
                : new Socket(addressFamily, SocketType.Stream, protocolType);
            SetRecommendedClientOptions(socket);
            // socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
            return socket;
        }

        private static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }

        partial void OnDispose();
    }
}
