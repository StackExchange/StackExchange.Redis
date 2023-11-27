using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pipelines.Sockets.Unofficial;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents an inter-related group of connections to redis servers.
    /// A reference to this should be held and re-used.
    /// </summary>
    /// <remarks><seealso href="https://stackexchange.github.io/StackExchange.Redis/PipelinesMultiplexers"/></remarks>
    public sealed partial class ConnectionMultiplexer : IInternalConnectionMultiplexer // implies : IConnectionMultiplexer and : IDisposable
    {
        // This gets accessed for every received event; let's make sure we can process it "raw"
        internal readonly byte[]? ConfigurationChangedChannel;
        // Unique identifier used when tracing
        internal readonly byte[] UniqueId = Guid.NewGuid().ToByteArray();

        /// <summary>
        /// Tracks overall connection multiplexer counts.
        /// </summary>
        internal int _connectAttemptCount = 0, _connectCompletedCount = 0, _connectionCloseCount = 0;
        internal long syncOps, asyncOps;
        private long syncTimeouts, fireAndForgets, asyncTimeouts;
        private string? failureMessage, activeConfigCause;
        private TimerToken? pulse;

        private readonly Hashtable servers = new Hashtable();
        private volatile ServerSnapshot _serverSnapshot = ServerSnapshot.Empty;

        private volatile bool _isDisposed;
        internal bool IsDisposed => _isDisposed;
        internal ILogger<ConnectionMultiplexer>? Logger { get; }

        internal CommandMap CommandMap { get; }
        internal EndPointCollection EndPoints { get; }
        internal ConfigurationOptions RawConfig { get; }
        internal ServerSelectionStrategy ServerSelectionStrategy { get; }
        ServerSelectionStrategy IInternalConnectionMultiplexer.ServerSelectionStrategy => ServerSelectionStrategy;
        ConnectionMultiplexer IInternalConnectionMultiplexer.UnderlyingMultiplexer => this;

        internal Exception? LastException { get; set; }

        ConfigurationOptions IInternalConnectionMultiplexer.RawConfig => RawConfig;

        private int lastReconfigiureTicks = Environment.TickCount;
        internal long LastReconfigureSecondsAgo =>
            unchecked(Environment.TickCount - Thread.VolatileRead(ref lastReconfigiureTicks)) / 1000;

        private int _activeHeartbeatErrors, lastHeartbeatTicks;
        internal long LastHeartbeatSecondsAgo =>
            pulse is null
            ? -1
            : unchecked(Environment.TickCount - Thread.VolatileRead(ref lastHeartbeatTicks)) / 1000;

        private static int lastGlobalHeartbeatTicks = Environment.TickCount;
        internal static long LastGlobalHeartbeatSecondsAgo =>
            unchecked(Environment.TickCount - Thread.VolatileRead(ref lastGlobalHeartbeatTicks)) / 1000;

        /// <summary>
        /// Should exceptions include identifiable details? (key names, additional .Data annotations)
        /// </summary>
        [Obsolete($"Please use {nameof(ConfigurationOptions)}.{nameof(ConfigurationOptions.IncludeDetailInExceptions)} instead - this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public bool IncludeDetailInExceptions
        {
            get => RawConfig.IncludeDetailInExceptions;
            set => RawConfig.IncludeDetailInExceptions = value;
        }

        /// <summary>
        /// Should exceptions include performance counter details?
        /// </summary>
        /// <remarks>
        /// CPU usage, etc - note that this can be problematic on some platforms.
        /// </remarks>
        [Obsolete($"Please use {nameof(ConfigurationOptions)}.{nameof(ConfigurationOptions.IncludePerformanceCountersInExceptions)} instead - this will be removed in 3.0.")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public bool IncludePerformanceCountersInExceptions
        {
            get => RawConfig.IncludePerformanceCountersInExceptions;
            set => RawConfig.IncludePerformanceCountersInExceptions = value;
        }

        /// <summary>
        /// Gets the synchronous timeout associated with the connections.
        /// </summary>
        public int TimeoutMilliseconds => RawConfig.SyncTimeout;

        /// <summary>
        /// Gets the asynchronous timeout associated with the connections.
        /// </summary>
        internal int AsyncTimeoutMilliseconds => RawConfig.AsyncTimeout;

        /// <summary>
        /// Gets the client-name that will be used on all new connections.
        /// </summary>
        /// <remarks>
        /// We null coalesce here instead of in Options so that we don't populate it everywhere (e.g. .ToString()), given it's a default.
        /// </remarks>
        public string ClientName => RawConfig.ClientName ?? RawConfig.Defaults.ClientName;

        /// <summary>
        /// Gets the configuration of the connection.
        /// </summary>
        public string Configuration => RawConfig.ToString();

        /// <summary>
        /// Indicates whether any servers are connected.
        /// </summary>
        public bool IsConnected => _serverSnapshot.Any(static s => s.IsConnected);

        /// <summary>
        /// Indicates whether any servers are currently trying to connect.
        /// </summary>
        public bool IsConnecting => _serverSnapshot.Any(static s => s.IsConnecting);

        static ConnectionMultiplexer()
        {
            SetAutodetectFeatureFlags();
        }

        private ConnectionMultiplexer(ConfigurationOptions configuration, ServerType? serverType = null, EndPointCollection? endpoints = null)
        {
            RawConfig = configuration ?? throw new ArgumentNullException(nameof(configuration));
            EndPoints = endpoints ?? RawConfig.EndPoints.Clone();
            EndPoints.SetDefaultPorts(serverType, ssl: RawConfig.Ssl);
            Logger = configuration.LoggerFactory?.CreateLogger<ConnectionMultiplexer>();

            var map = CommandMap = configuration.GetCommandMap(serverType);
            if (!string.IsNullOrWhiteSpace(configuration.Password) && !configuration.TryResp3()) // RESP3 doesn't need AUTH (can issue as part of HELLO)
            {
                map.AssertAvailable(RedisCommand.AUTH);
            }
            if (!map.IsAvailable(RedisCommand.ECHO) && !map.IsAvailable(RedisCommand.PING) && !map.IsAvailable(RedisCommand.TIME))
            {
                // I mean really, give me a CHANCE! I need *something* to check the server is available to me...
                // see also: SendTracer (matching logic)
                map.AssertAvailable(RedisCommand.EXISTS);
            }

            OnCreateReaderWriter(configuration);
            ServerSelectionStrategy = new ServerSelectionStrategy(this);

            var configChannel = configuration.ConfigurationChannel;
            if (!string.IsNullOrWhiteSpace(configChannel))
            {
                ConfigurationChangedChannel = Encoding.UTF8.GetBytes(configChannel);
            }
            lastHeartbeatTicks = Environment.TickCount;
        }

        private static ConnectionMultiplexer CreateMultiplexer(ConfigurationOptions configuration, ILogger? log, ServerType? serverType, out EventHandler<ConnectionFailedEventArgs>? connectHandler, EndPointCollection? endpoints = null)
        {
            var muxer = new ConnectionMultiplexer(configuration, serverType, endpoints);
            connectHandler = null;
            if (log is not null)
            {
                // Create a detachable event-handler to log detailed errors if something happens during connect/handshake
                connectHandler = (_, a) =>
                {
                    try
                    {
                        lock (log) // Keep the outer and any inner errors contiguous
                        {
                            var ex = a.Exception;
                            log?.LogError(ex, $"Connection failed: {Format.ToString(a.EndPoint)} ({a.ConnectionType}, {a.FailureType}): {ex?.Message ?? "(unknown)"}");
                            while ((ex = ex?.InnerException) != null)
                            {
                                log?.LogError(ex, $"> {ex.Message}");
                            }
                        }
                    }
                    catch { }
                };
                muxer.ConnectionFailed += connectHandler;
            }
            return muxer;
        }

        /// <summary>
        /// Get summary statistics associated with all servers in this multiplexer.
        /// </summary>
        public ServerCounters GetCounters()
        {
            var counters = new ServerCounters(null);
            var snapshot = GetServerSnapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                counters.Add(snapshot[i].GetCounters());
            }
            return counters;
        }

        internal async Task MakePrimaryAsync(ServerEndPoint server, ReplicationChangeOptions options, TextWriter? writer)
        {
            _ = server ?? throw new ArgumentNullException(nameof(server));
            var log = Logger.With(writer);

            var cmd = server.GetFeatures().ReplicaCommands ? RedisCommand.REPLICAOF : RedisCommand.SLAVEOF;
            CommandMap.AssertAvailable(cmd);

            if (!RawConfig.AllowAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(RawConfig.IncludeDetailInExceptions, cmd, null, server);
            }
            var srv = new RedisServer(this, server, null);
            if (!srv.IsConnected)
            {
                throw ExceptionFactory.NoConnectionAvailable(this, null, server, GetServerSnapshot(), command: cmd);
            }

            const CommandFlags flags = CommandFlags.NoRedirect;
            Message msg;

            log?.LogInformation($"Checking {Format.ToString(srv.EndPoint)} is available...");
            try
            {
                await srv.PingAsync(flags).ForAwait(); // if it isn't happy, we're not happy
            }
            catch (Exception ex)
            {
                log?.LogError(ex, $"Operation failed on {Format.ToString(srv.EndPoint)}, aborting: {ex.Message}");
                throw;
            }

            var nodes = _serverSnapshot; // same as GetServerSnapshot(), but doesn't force span
            RedisValue newPrimary = Format.ToString(server.EndPoint);

            // try and write this everywhere; don't worry if some folks reject our advances
            if (RawConfig.TryGetTieBreaker(out var tieBreakerKey)
                && options.HasFlag(ReplicationChangeOptions.SetTiebreaker)
                && CommandMap.IsAvailable(RedisCommand.SET))
            {
                foreach (var node in nodes)
                {
                    if (!node.IsConnected || node.IsReplica) continue;
                    log?.LogInformation($"Attempting to set tie-breaker on {Format.ToString(node.EndPoint)}...");
                    msg = Message.Create(0, flags | CommandFlags.FireAndForget, RedisCommand.SET, tieBreakerKey, newPrimary);
                    try
                    {
                        await node.WriteDirectAsync(msg, ResultProcessor.DemandOK).ForAwait();
                    }
                    catch { }
                }
            }

            // stop replicating, promote to a standalone primary
            log?.LogInformation($"Making {Format.ToString(srv.EndPoint)} a primary...");
            try
            {
                await srv.ReplicaOfAsync(null, flags).ForAwait();
            }
            catch (Exception ex)
            {
                log?.LogError(ex, $"Operation failed on {Format.ToString(srv.EndPoint)}, aborting: {ex.Message}");
                throw;
            }

            // also, in case it was a replica a moment ago, and hasn't got the tie-breaker yet, we re-send the tie-breaker to this one
            if (!tieBreakerKey.IsNull && !server.IsReplica)
            {
                log?.LogInformation($"Resending tie-breaker to {Format.ToString(server.EndPoint)}...");
                msg = Message.Create(0, flags | CommandFlags.FireAndForget, RedisCommand.SET, tieBreakerKey, newPrimary);
                try
                {
                    await server.WriteDirectAsync(msg, ResultProcessor.DemandOK).ForAwait();
                }
                catch { }
            }

            // There's an inherent race here in zero-latency environments (e.g. when Redis is on localhost) when a broadcast is specified
            // The broadcast can get back from redis and trigger a reconfigure before we get a chance to get to ReconfigureAsync() below
            // This results in running an outdated reconfiguration and the .CompareExchange() (due to already running a reconfiguration)
            // failing...making our needed reconfiguration a no-op.
            // If we don't block *that* run, then *our* run (at low latency) gets blocked. Then we're waiting on the
            // ConfigurationOptions.ConfigCheckSeconds interval to identify the current (created by this method call) topology correctly.
            var blockingReconfig = Interlocked.CompareExchange(ref activeConfigCause, "Block: Pending Primary Reconfig", null) == null;

            // Try and broadcast the fact a change happened to all members
            // We want everyone possible to pick it up.
            // We broadcast before *and after* the change to remote members, so that they don't go without detecting a change happened.
            // This eliminates the race of pub/sub *then* re-slaving happening, since a method both precedes and follows.
            async Task BroadcastAsync(ServerSnapshot serverNodes)
            {
                if (options.HasFlag(ReplicationChangeOptions.Broadcast)
                    && ConfigurationChangedChannel != null
                    && CommandMap.IsAvailable(RedisCommand.PUBLISH))
                {
                    RedisValue channel = ConfigurationChangedChannel;
                    foreach (var node in serverNodes)
                    {
                        if (!node.IsConnected) continue;
                        log?.LogInformation($"Broadcasting via {Format.ToString(node.EndPoint)}...");
                        msg = Message.Create(-1, flags | CommandFlags.FireAndForget, RedisCommand.PUBLISH, channel, newPrimary);
                        await node.WriteDirectAsync(msg, ResultProcessor.Int64).ForAwait();
                    }
                }
            }

            // Send a message before it happens - because afterwards a new replica may be unresponsive
            await BroadcastAsync(nodes).ForAwait();

            if (options.HasFlag(ReplicationChangeOptions.ReplicateToOtherEndpoints))
            {
                foreach (var node in nodes)
                {
                    if (node == server || node.ServerType != ServerType.Standalone) continue;

                    log?.LogInformation($"Replicating to {Format.ToString(node.EndPoint)}...");
                    msg = RedisServer.CreateReplicaOfMessage(node, server.EndPoint, flags);
                    await node.WriteDirectAsync(msg, ResultProcessor.DemandOK).ForAwait();
                }
            }

            // ...and send one after it happens - because the first broadcast may have landed on a secondary client
            // and it can reconfigure before any topology change actually happened. This is most likely to happen
            // in low-latency environments.
            await BroadcastAsync(nodes).ForAwait();

            // and reconfigure the muxer
            log?.LogInformation("Reconfiguring all endpoints...");
            // Yes, there is a tiny latency race possible between this code and the next call, but it's far more minute than before.
            // The effective gap between 0 and > 0 (likely off-box) latency is something that may never get hit here by anyone.
            if (blockingReconfig)
            {
                Interlocked.Exchange(ref activeConfigCause, null);
            }
            if (!await ReconfigureAsync(first: false, reconfigureAll: true, log, srv.EndPoint, cause: nameof(MakePrimaryAsync)).ForAwait())
            {
                log?.LogInformation("Verifying the configuration was incomplete; please verify");
            }
        }

        internal void CheckMessage(Message message)
        {
            if (!RawConfig.AllowAdmin && message.IsAdmin)
            {
                throw ExceptionFactory.AdminModeNotEnabled(RawConfig.IncludeDetailInExceptions, message.Command, message, null);
            }
            if (message.Command != RedisCommand.UNKNOWN)
            {
                CommandMap.AssertAvailable(message.Command);
            }

            // using >= here because we will be adding 1 for the command itself (which is an argument for the purposes of the multi-bulk protocol)
            if (message.ArgCount >= PhysicalConnection.REDIS_MAX_ARGS)
            {
                throw ExceptionFactory.TooManyArgs(message.CommandAndKey, message.ArgCount);
            }
        }

        internal bool TryResend(int hashSlot, Message message, EndPoint endpoint, bool isMoved)
        {
            // If we're being told to re-send something because the hash slot moved, that means our topology is out of date
            // ...and we should re-evaluate what's what.
            // Allow for a 5-second back-off so we don't hammer this in a loop though
            if (isMoved && LastReconfigureSecondsAgo > 5)
            {
                // Async kickoff a reconfigure
                ReconfigureIfNeeded(endpoint, false, "MOVED encountered");
            }

            return ServerSelectionStrategy.TryResend(hashSlot, message, endpoint, isMoved);
        }

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout).
        /// </summary>
        /// <param name="task">The task to wait on.</param>
        public void Wait(Task task)
        {
            _ = task ?? throw new ArgumentNullException(nameof(task));
            try
            {
                if (!task.Wait(TimeoutMilliseconds))
                {
                    throw new TimeoutException();
                }
            }
            catch (AggregateException aex) when (IsSingle(aex))
            {
                throw aex.InnerExceptions[0];
            }
        }

        /// <summary>
        /// Wait for a given asynchronous operation to complete (or timeout).
        /// </summary>
        /// <typeparam name="T">The type contains in the task to wait on.</typeparam>
        /// <param name="task">The task to wait on.</param>
        public T Wait<T>(Task<T> task)
        {
            _ = task ?? throw new ArgumentNullException(nameof(task));
            try
            {
                if (!task.Wait(TimeoutMilliseconds))
                {
                    throw new TimeoutException();
                }
            }
            catch (AggregateException aex) when (IsSingle(aex))
            {
                throw aex.InnerExceptions[0];
            }
            return task.Result;
        }

        private static bool IsSingle(AggregateException aex)
        {
            try
            {
                return aex?.InnerExceptions.Count == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for the given asynchronous operations to complete (or timeout).
        /// </summary>
        /// <param name="tasks">The tasks to wait on.</param>
        public void WaitAll(params Task[] tasks)
        {
            _ = tasks ?? throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0) return;
            if (!Task.WaitAll(tasks, TimeoutMilliseconds))
            {
                throw new TimeoutException();
            }
        }

        private bool WaitAllIgnoreErrors(Task[] tasks)
        {
            _ = tasks ?? throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0) return true;
            var watch = ValueStopwatch.StartNew();
            try
            {
                // If no error, great
                if (Task.WaitAll(tasks, TimeoutMilliseconds)) return true;
            }
            catch
            { }
            // If we get problems, need to give the non-failing ones time to be fair and reasonable
            for (int i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (!task.IsCanceled && !task.IsCompleted && !task.IsFaulted)
                {
                    var remaining = TimeoutMilliseconds - watch.ElapsedMilliseconds;
                    if (remaining <= 0) return false;
                    try
                    {
                        task.Wait(remaining);
                    }
                    catch
                    { }
                }
            }
            return false;
        }

        private static async Task<bool> WaitAllIgnoreErrorsAsync(string name, Task[] tasks, int timeoutMilliseconds, ILogger? log, [CallerMemberName] string? caller = null, [CallerLineNumber] int callerLineNumber = 0)
        {
            _ = tasks ?? throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0)
            {
                log?.LogInformation("No tasks to await");
                return true;
            }
            if (AllComplete(tasks))
            {
                log?.LogInformation("All tasks are already complete");
                return true;
            }

            static void LogWithThreadPoolStats(ILogger? log, string message, out int busyWorkerCount)
            {
                busyWorkerCount = 0;
                if (log is not null)
                {
                    var sb = new StringBuilder();
                    sb.Append(message);
                    busyWorkerCount = PerfCounterHelper.GetThreadPoolStats(out string iocp, out string worker, out string? workItems);
                    sb.Append(", IOCP: ").Append(iocp).Append(", WORKER: ").Append(worker);
                    if (workItems is not null)
                    {
                        sb.Append(", POOL: ").Append(workItems);
                    }
                    log?.LogInformation(sb.ToString());
                }
            }

            var watch = ValueStopwatch.StartNew();
            LogWithThreadPoolStats(log, $"Awaiting {tasks.Length} {name} task completion(s) for {timeoutMilliseconds}ms", out _);
            try
            {
                // if none error, great
                var remaining = timeoutMilliseconds - watch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    LogWithThreadPoolStats(log, "Timeout before awaiting for tasks", out _);
                    return false;
                }

                var allTasks = Task.WhenAll(tasks).ObserveErrors();
                bool all = await allTasks.TimeoutAfter(timeoutMs: remaining).ObserveErrors().ForAwait();
                LogWithThreadPoolStats(log, all ? $"All {tasks.Length} {name} tasks completed cleanly" : $"Not all {name} tasks completed cleanly (from {caller}#{callerLineNumber}, timeout {timeoutMilliseconds}ms)", out _);
                return all;
            }
            catch
            { }

            // if we get problems, need to give the non-failing ones time to finish
            // to be fair and reasonable
            for (int i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (!task.IsCanceled && !task.IsCompleted && !task.IsFaulted)
                {
                    var remaining = timeoutMilliseconds - watch.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        LogWithThreadPoolStats(log, "Timeout awaiting tasks", out _);
                        return false;
                    }
                    try
                    {
                        await Task.WhenAny(task, Task.Delay(remaining)).ObserveErrors().ForAwait();
                    }
                    catch
                    { }
                }
            }
            LogWithThreadPoolStats(log, "Finished awaiting tasks", out _);
            return false;
        }

        private static bool AllComplete(Task[] tasks)
        {
            for (int i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                if (!task.IsCanceled && !task.IsCompleted && !task.IsFaulted)
                    return false;
            }
            return true;
        }

        internal Exception? AuthException { get; private set; }
        internal void SetAuthSuspect(Exception authException) => AuthException ??= authException;

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The string configuration to use for this multiplexer.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public static Task<ConnectionMultiplexer> ConnectAsync(string configuration, TextWriter? log = null) =>
            ConnectAsync(ConfigurationOptions.Parse(configuration), log);

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The string configuration to use for this multiplexer.</param>
        /// <param name="configure">Action to further modify the parsed configuration options.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public static Task<ConnectionMultiplexer> ConnectAsync(string configuration, Action<ConfigurationOptions> configure, TextWriter? log = null) =>
            ConnectAsync(ConfigurationOptions.Parse(configuration).Apply(configure), log);

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The configuration options to use for this multiplexer.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        /// <remarks>Note: For Sentinel, do <b>not</b> specify a <see cref="ConfigurationOptions.CommandMap"/> - this is handled automatically.</remarks>
        public static Task<ConnectionMultiplexer> ConnectAsync(ConfigurationOptions configuration, TextWriter? log = null)
        {
            SocketConnection.AssertDependencies();
            Validate(configuration);

            return configuration.IsSentinel
                ? SentinelPrimaryConnectAsync(configuration, log)
                : ConnectImplAsync(configuration, log);
        }

        private static async Task<ConnectionMultiplexer> ConnectImplAsync(ConfigurationOptions configuration, TextWriter? writer = null, ServerType? serverType = null)
        {
            IDisposable? killMe = null;
            EventHandler<ConnectionFailedEventArgs>? connectHandler = null;
            ConnectionMultiplexer? muxer = null;
            var configLogger = configuration.LoggerFactory?.CreateLogger<ConnectionMultiplexer>();
            var log = configLogger.With(writer);
            try
            {
                var sw = ValueStopwatch.StartNew();
                log?.LogInformation($"Connecting (async) on {RuntimeInformation.FrameworkDescription} (StackExchange.Redis: v{Utils.GetLibVersion()})");

                muxer = CreateMultiplexer(configuration, log, serverType, out connectHandler);
                killMe = muxer;
                Interlocked.Increment(ref muxer._connectAttemptCount);
                bool configured = await muxer.ReconfigureAsync(first: true, reconfigureAll: false, log, null, "connect").ObserveErrors().ForAwait();
                if (!configured)
                {
                    throw ExceptionFactory.UnableToConnect(muxer, muxer.failureMessage);
                }
                killMe = null;
                Interlocked.Increment(ref muxer._connectCompletedCount);

                if (muxer.ServerSelectionStrategy.ServerType == ServerType.Sentinel)
                {
                    // Initialize the Sentinel handlers
                    muxer.InitializeSentinel(log);
                }

                await configuration.AfterConnectAsync(muxer, s => log?.LogInformation(s)).ForAwait();

                log?.LogInformation($"Total connect time: {sw.ElapsedMilliseconds:n0} ms");

                return muxer;
            }
            finally
            {
                if (connectHandler != null && muxer != null) muxer.ConnectionFailed -= connectHandler;
                if (killMe != null) try { killMe.Dispose(); } catch { }
                if (log is TextWriterLogger twLogger) twLogger.Release();
            }
        }

        private static void Validate([NotNull] ConfigurationOptions? config)
        {
            if (config is null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.EndPoints.Count == 0)
            {
                throw new ArgumentException("No endpoints specified", nameof(config));
            }
        }

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The string configuration to use for this multiplexer.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public static ConnectionMultiplexer Connect(string configuration, TextWriter? log = null) =>
            Connect(ConfigurationOptions.Parse(configuration), log);

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The string configuration to use for this multiplexer.</param>
        /// <param name="configure">Action to further modify the parsed configuration options.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public static ConnectionMultiplexer Connect(string configuration, Action<ConfigurationOptions> configure, TextWriter? log = null) =>
            Connect(ConfigurationOptions.Parse(configuration).Apply(configure), log);

        /// <summary>
        /// Creates a new <see cref="ConnectionMultiplexer"/> instance.
        /// </summary>
        /// <param name="configuration">The configuration options to use for this multiplexer.</param>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        /// <remarks>Note: For Sentinel, do <b>not</b> specify a <see cref="ConfigurationOptions.CommandMap"/> - this is handled automatically.</remarks>
        public static ConnectionMultiplexer Connect(ConfigurationOptions configuration, TextWriter? log = null)
        {
            SocketConnection.AssertDependencies();
            Validate(configuration);

            return configuration.IsSentinel
                ? SentinelPrimaryConnect(configuration, log)
                : ConnectImpl(configuration, log);
        }

        private static ConnectionMultiplexer ConnectImpl(ConfigurationOptions configuration, TextWriter? writer, ServerType? serverType = null, EndPointCollection? endpoints = null)
        {
            IDisposable? killMe = null;
            EventHandler<ConnectionFailedEventArgs>? connectHandler = null;
            ConnectionMultiplexer? muxer = null;
            var configLogger = configuration.LoggerFactory?.CreateLogger<ConnectionMultiplexer>();
            var log = configLogger.With(writer);
            try
            {
                var sw = ValueStopwatch.StartNew();
                log?.LogInformation($"Connecting (sync) on {RuntimeInformation.FrameworkDescription} (StackExchange.Redis: v{Utils.GetLibVersion()})");

                muxer = CreateMultiplexer(configuration, log, serverType, out connectHandler, endpoints);
                killMe = muxer;
                Interlocked.Increment(ref muxer._connectAttemptCount);
                // note that task has timeouts internally, so it might take *just over* the regular timeout
                var task = muxer.ReconfigureAsync(first: true, reconfigureAll: false, log, null, "connect");

                if (!task.Wait(muxer.SyncConnectTimeout(true)))
                {
                    task.ObserveErrors();
                    if (muxer.RawConfig.AbortOnConnectFail)
                    {
                        throw ExceptionFactory.UnableToConnect(muxer, "ConnectTimeout");
                    }
                    else
                    {
                        var ex = ExceptionFactory.UnableToConnect(muxer, "ConnectTimeout");
                        muxer.LastException = ex;
                        muxer.Logger?.LogError(ex, ex.Message);
                    }
                }

                if (!task.Result) throw ExceptionFactory.UnableToConnect(muxer, muxer.failureMessage);
                killMe = null;
                Interlocked.Increment(ref muxer._connectCompletedCount);

                if (muxer.ServerSelectionStrategy.ServerType == ServerType.Sentinel)
                {
                    // Initialize the Sentinel handlers
                    muxer.InitializeSentinel(log);
                }

                configuration.AfterConnectAsync(muxer, s => log?.LogInformation(s)).Wait(muxer.SyncConnectTimeout(true));

                log?.LogInformation($"Total connect time: {sw.ElapsedMilliseconds:n0} ms");

                return muxer;
            }
            finally
            {
                if (connectHandler != null && muxer != null) muxer.ConnectionFailed -= connectHandler;
                if (killMe != null) try { killMe.Dispose(); } catch { }
                if (log is TextWriterLogger twLogger) twLogger.Release();
            }
        }

        ReadOnlySpan<ServerEndPoint> IInternalConnectionMultiplexer.GetServerSnapshot() => _serverSnapshot.AsSpan();
        internal ReadOnlySpan<ServerEndPoint> GetServerSnapshot() => _serverSnapshot.AsSpan();
        internal sealed class ServerSnapshot : IEnumerable<ServerEndPoint>
        {
            public static ServerSnapshot Empty { get; } = new ServerSnapshot(Array.Empty<ServerEndPoint>(), 0);
            private ServerSnapshot(ServerEndPoint[] endpoints, int count)
            {
                _endpoints = endpoints;
                _count = count;
            }
            private readonly ServerEndPoint[] _endpoints;
            private readonly int _count;
            public ReadOnlySpan<ServerEndPoint> AsSpan() => new ReadOnlySpan<ServerEndPoint>(_endpoints, 0, _count);
            public ReadOnlyMemory<ServerEndPoint> AsMemory() => new ReadOnlyMemory<ServerEndPoint>(_endpoints, 0, _count);

            internal ServerSnapshot Add(ServerEndPoint value)
            {
                if (value == null)
                {
                    return this;
                }

                ServerEndPoint[] nextEndpoints;
                if (_endpoints.Length > _count)
                {
                    nextEndpoints = _endpoints;
                }
                else
                {
                    // no more room; need a new array
                    int newLen = _endpoints.Length << 1;
                    if (newLen == 0) newLen = 4;
                    nextEndpoints = new ServerEndPoint[newLen];
                    _endpoints.CopyTo(nextEndpoints, 0);
                }
                nextEndpoints[_count] = value;
                return new ServerSnapshot(nextEndpoints, _count + 1);
            }

            internal EndPoint[] GetEndPoints()
            {
                if (_count == 0) return Array.Empty<EndPoint>();

                var arr = new EndPoint[_count];
                for (int i = 0; i < _count; i++)
                {
                    arr[i] = _endpoints[i].EndPoint;
                }
                return arr;
            }

            public Enumerator GetEnumerator() => new(_endpoints, _count);
            IEnumerator<ServerEndPoint> IEnumerable<ServerEndPoint>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<ServerEndPoint>
            {
                private readonly ServerEndPoint[] _endpoints;
                private readonly Func<ServerEndPoint, bool>? _predicate;
                private readonly int _count;
                private int _index;

                public ServerEndPoint Current { get; private set; }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    while (_index < _count && ++_index < _count)
                    {
                        Current = _endpoints[_index];
                        if (_predicate is null || _predicate(Current))
                        {
                            return true;
                        }
                    }
                    Current = default!;
                    return false;
                }
                void IDisposable.Dispose() { }
                void IEnumerator.Reset()
                {
                    _index = -1;
                    Current = default!;
                }

                public Enumerator(ServerEndPoint[] endpoints, int count, Func<ServerEndPoint, bool>? predicate = null)
                {
                    _index = -1;
                    _endpoints = endpoints;
                    _count = count;
                    _predicate = predicate;
                    Current = default!;
                }
            }

            public int Count => _count;

            public bool Any(Func<ServerEndPoint, bool>? predicate = null)
            {
                if (_count > 0)
                {
                    if (predicate is null) return true;
                    foreach (var item in AsSpan()) // span for bounds elision
                    {
                        if (predicate(item)) return true;
                    }
                }
                return false;
            }

            public ServerSnapshotFiltered Where(CommandFlags flags)
            {
                var effectiveFlags = flags & (CommandFlags.DemandMaster | CommandFlags.DemandReplica);
                return (effectiveFlags) switch
                {
                    CommandFlags.DemandMaster => Where(static s => !s.IsReplica),
                    CommandFlags.DemandReplica => Where(static s => s.IsReplica),
                    _ => Where(null!),
                    // note we don't need to consider "both", since the composition of the flags-enum precludes that
                };
            }

            public ServerSnapshotFiltered Where(Func<ServerEndPoint, bool> predicate)
                => new ServerSnapshotFiltered(_endpoints, _count, predicate);

            public readonly struct ServerSnapshotFiltered : IEnumerable<ServerEndPoint>
            {
                private readonly ServerEndPoint[] _endpoints;
                private readonly Func<ServerEndPoint, bool>? _predicate;
                private readonly int _count;

                public ServerSnapshotFiltered(ServerEndPoint[] endpoints, int count, Func<ServerEndPoint, bool>? predicate)
                {
                    _endpoints = endpoints;
                    _count = count;
                    _predicate = predicate;
                }

                public Enumerator GetEnumerator() => new(_endpoints, _count, _predicate);
                IEnumerator<ServerEndPoint> IEnumerable<ServerEndPoint>.GetEnumerator() => GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }

        ServerEndPoint IInternalConnectionMultiplexer.GetServerEndPoint(EndPoint endpoint) => GetServerEndPoint(endpoint);

        [return: NotNullIfNotNull(nameof(endpoint))]
        internal ServerEndPoint? GetServerEndPoint(EndPoint? endpoint, ILogger? log = null, bool activate = true)
        {
            if (endpoint == null) return null;
            var server = (ServerEndPoint?)servers[endpoint];
            if (server == null)
            {
                bool isNew = false;
                lock (servers)
                {
                    server = (ServerEndPoint?)servers[endpoint];
                    if (server == null)
                    {
                        if (_isDisposed) throw new ObjectDisposedException(ToString());

                        server = new ServerEndPoint(this, endpoint);
                        servers.Add(endpoint, server);
                        isNew = true;
                        _serverSnapshot = _serverSnapshot.Add(server);
                    }
                }
                // spin up the connection if this is new
                if (isNew && activate)
                {
                    server.Activate(ConnectionType.Interactive, log);
                    if (server.SupportsSubscriptions && !server.KnowOrAssumeResp3())
                    {
                        // Intentionally not logging the sub connection
                        server.Activate(ConnectionType.Subscription, null);
                    }
                }
            }
            return server;
        }

        internal void Root() => pulse?.Root(this);

        // note that this also acts (conditionally) as the GC root for the multiplexer
        // when there are in-flight messages; the timer can then acts as the heartbeat
        // to make sure that everything *eventually* completes
        private sealed class TimerToken : IDisposable
        {
            private TimerToken(ConnectionMultiplexer muxer)
            {
                _weakRef = new(muxer);
            }
            private Timer? _timer;
            public void SetTimer(Timer timer) => _timer = timer;

            private readonly WeakReference<ConnectionMultiplexer> _weakRef;

            private object StrongRefSyncLock => _weakRef; // private and readonly? it'll do
            private ConnectionMultiplexer? _strongRef;
            private int _strongRefToken;

            private static readonly TimerCallback Heartbeat = state =>
            {
                var token = (TimerToken)state!;
                if (token._weakRef.TryGetTarget(out var muxer))
                {
                    muxer.OnHeartbeat();
                }
                else
                {
                    // the muxer got disposed from out of us; kill the timer
                    token.Dispose();
                }
            };

            internal static TimerToken Create(ConnectionMultiplexer connection)
            {
                var token = new TimerToken(connection);
                var heartbeatMilliseconds = (int)connection.RawConfig.HeartbeatInterval.TotalMilliseconds;
                var timer = new Timer(Heartbeat, token, heartbeatMilliseconds, heartbeatMilliseconds);
                token.SetTimer(timer);
                return token;
            }

            public void Dispose()
            {
                var tmp = _timer;
                _timer = null;
                if (tmp is not null) try { tmp.Dispose(); } catch { }

                _strongRef = null; // note that this shouldn't be relevant since we've unrooted the TimerToken
            }

            // explanation of rooting model:
            //
            // the timer has a reference to the TimerToken; this *always* has a weak-ref,
            // and *may* sometimes have a strong-ref; this is so that if a consumer
            // drops a multiplexer, it can be garbage collected, i.e. the heartbeat timer
            // doesn't keep the entire thing alive forever; instead, if the heartbeat detects
            // the weak-ref has been collected, it can cancel the timer and *itself* go away;
            // however: this leaves a problem where there is *in flight work* when the consumer
            // drops the multiplexer; in particular, if that happens when disconnected, there
            // could be consumer-visible pending TCS items *in the backlog queue*; we don't want
            // to leave those incomplete, as that fails the contractual expectations of async/await;
            // instead we need to root ourselves. The natural place to do this is by rooting the
            // multiplexer, allowing the heartbeat to keep poking things, so that the usual
            // message-processing and timeout rules apply. This is why we *sometimes* also keep
            // a strong-ref to the same multiplexer.
            //
            // The TimerToken is rooted by the timer callback; this then roots the multiplexer,
            // which keeps our bridges and connections in scope - until we're sure we're done
            // with them.
            //
            // 1) any bridge or connection will trigger rooting by calling Root when
            // they change from "empty" to "non-empty" i.e. whenever there
            // in-flight items; this always changes the token; this includes both the
            // backlog and awaiting-reply queues.
            //
            // 2) the heartbeat is responsible for unrooting, after processing timeouts
            // etc; first it checks whether it is needed (IsRooted), which also gives
            // it the current token.
            //
            // 3) if so, the heartbeat will (outside of the lock) query all sources to
            // see if they still have outstanding work; if everyone reports negatively,
            // then the heartbeat calls UnRoot passing in the old token; if this still
            // matches (i.e. no new work came in while we were looking away), then the
            // strong reference is removed; note that "has outstanding work" ignores
            // internal-call messages; we are only interested in consumer-facing items
            // (but we need to check this *here* rather than when adding, as otherwise
            // the definition of "is empty, should root" becomes more complicated, which
            // impacts the write path, rather than the heartbeat path.
            //
            // This means that the multiplexer (via the timer) lasts as long as there are
            // outstanding messages; if the consumer has dropped the multiplexer, then
            // there will be no new incoming messages, and after timeouts: everything
            // should drop.

            public void Root(ConnectionMultiplexer multiplexer)
            {
                lock (StrongRefSyncLock)
                {
                    _strongRef = multiplexer;
                    _strongRefToken++;
                }
            }

            public bool IsRooted(out int token)
            {
                lock (StrongRefSyncLock)
                {
                    token = _strongRefToken;
                    return _strongRef is not null;
                }
            }

            public void UnRoot(int token)
            {
                lock (StrongRefSyncLock)
                {
                    if (token == _strongRefToken)
                    {
                        _strongRef = null;
                    }
                }
            }
        }

        private void OnHeartbeat()
        {
            try
            {
                int now = Environment.TickCount;
                Interlocked.Exchange(ref lastHeartbeatTicks, now);
                Interlocked.Exchange(ref lastGlobalHeartbeatTicks, now);
                Trace("heartbeat");

                var tmp = GetServerSnapshot();
                int token = 0;
                bool isRooted = pulse?.IsRooted(out token) ?? false, hasPendingCallerFacingItems = false;

                for (int i = 0; i < tmp.Length; i++)
                {
                    tmp[i].OnHeartbeat();
                    if (isRooted && !hasPendingCallerFacingItems)
                    {
                        hasPendingCallerFacingItems = tmp[i].HasPendingCallerFacingItems();
                    }
                }
                if (isRooted && !hasPendingCallerFacingItems)
                {   // release the GC root on the heartbeat *if* the token still matches
                    pulse?.UnRoot(token);
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref _activeHeartbeatErrors, 1, 0) == 0)
                {
                    try
                    {
                        OnInternalError(ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _activeHeartbeatErrors, 0);
                    }
                }
            }
        }

        /// <summary>
        /// Obtain a pub/sub subscriber connection to the specified server.
        /// </summary>
        /// <param name="asyncState">The async state object to pass to the created <see cref="RedisSubscriber"/>.</param>
        public ISubscriber GetSubscriber(object? asyncState = null)
        {
            if (!RawConfig.Proxy.SupportsPubSub())
            {
                throw new NotSupportedException($"The pub/sub API is not available via {RawConfig.Proxy}");
            }
            return new RedisSubscriber(this, asyncState);
        }

        /// <summary>
        /// Applies common DB number defaults and rules.
        /// </summary>
        internal int ApplyDefaultDatabase(int db)
        {
            if (db == -1)
            {
                db = RawConfig.DefaultDatabase.GetValueOrDefault();
            }
            else if (db < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(db));
            }

            if (db != 0 && !RawConfig.Proxy.SupportsDatabases())
            {
                throw new NotSupportedException($"{RawConfig.Proxy} only supports database 0");
            }

            return db;
        }

        /// <summary>
        /// Obtain an interactive connection to a database inside redis.
        /// </summary>
        /// <param name="db">The ID to get a database for.</param>
        /// <param name="asyncState">The async state to pass into the resulting <see cref="RedisDatabase"/>.</param>
        public IDatabase GetDatabase(int db = -1, object? asyncState = null)
        {
            db = ApplyDefaultDatabase(db);

            // if there's no async-state, and the DB is suitable, we can hand out a re-used instance
            return (asyncState == null && db <= MaxCachedDatabaseInstance)
                ? GetCachedDatabaseInstance(db) : new RedisDatabase(this, db, asyncState);
        }

        // DB zero is stored separately, since 0-only is a massively common use-case
        private const int MaxCachedDatabaseInstance = 16; // 17 items - [0,16]
        // Side note: "databases 16" is the default in redis.conf; happy to store one extra to get nice alignment etc
        private IDatabase? dbCacheZero;
        private IDatabase[]? dbCacheLow;
        private IDatabase GetCachedDatabaseInstance(int db) // note that we already trust db here; only caller checks range
        {
            // Note: we don't need to worry about *always* returning the same instance.
            // If two threads ask for db 3 at the same time, it is OK for them to get
            // different instances, one of which (arbitrarily) ends up cached for later use.
            if (db == 0)
            {
                return dbCacheZero ??= new RedisDatabase(this, 0, null);
            }
            var arr = dbCacheLow ??= new IDatabase[MaxCachedDatabaseInstance];
            return arr[db - 1] ??= new RedisDatabase(this, db, null);
        }

        /// <summary>
        /// Compute the hash-slot of a specified key.
        /// </summary>
        /// <param name="key">The key to get a hash slot ID for.</param>
        public int HashSlot(RedisKey key) => ServerSelectionStrategy.HashSlot(key);

        internal ServerEndPoint? AnyServer(ServerType serverType, uint startOffset, RedisCommand command, CommandFlags flags, bool allowDisconnected)
        {
            var tmp = GetServerSnapshot();
            int len = tmp.Length;
            ServerEndPoint? fallback = null;
            for (int i = 0; i < len; i++)
            {
                var server = tmp[(int)(((uint)i + startOffset) % len)];
                if (server != null && server.ServerType == serverType && server.IsSelectable(command, allowDisconnected))
                {
                    if (server.IsReplica)
                    {
                        switch (flags)
                        {
                            case CommandFlags.DemandReplica:
                            case CommandFlags.PreferReplica:
                                return server;
                            case CommandFlags.PreferMaster:
                                fallback = server;
                                break;
                        }
                    }
                    else
                    {
                        switch (flags)
                        {
                            case CommandFlags.DemandMaster:
                            case CommandFlags.PreferMaster:
                                return server;
                            case CommandFlags.PreferReplica:
                                fallback = server;
                                break;
                        }
                    }
                }
            }
            return fallback;
        }

        /// <summary>
        /// Obtain a configuration API for an individual server.
        /// </summary>
        /// <param name="host">The host to get a server for.</param>
        /// <param name="port">The port for <paramref name="host"/> to get a server for.</param>
        /// <param name="asyncState">The async state to pass into the resulting <see cref="RedisServer"/>.</param>
        public IServer GetServer(string host, int port, object? asyncState = null) =>
            GetServer(Format.ParseEndPoint(host, port), asyncState);

        /// <summary>
        /// Obtain a configuration API for an individual server.
        /// </summary>
        /// <param name="hostAndPort">The "host:port" string to get a server for.</param>
        /// <param name="asyncState">The async state to pass into the resulting <see cref="RedisServer"/>.</param>
        public IServer GetServer(string hostAndPort, object? asyncState = null) =>
            Format.TryParseEndPoint(hostAndPort, out var ep)
            ? GetServer(ep, asyncState)
            : throw new ArgumentException($"The specified host and port could not be parsed: {hostAndPort}", nameof(hostAndPort));

        /// <summary>
        /// Obtain a configuration API for an individual server.
        /// </summary>
        /// <param name="host">The host to get a server for.</param>
        /// <param name="port">The port for <paramref name="host"/> to get a server for.</param>
        public IServer GetServer(IPAddress host, int port) => GetServer(new IPEndPoint(host, port));

        /// <summary>
        /// Obtain a configuration API for an individual server.
        /// </summary>
        /// <param name="endpoint">The endpoint to get a server for.</param>
        /// <param name="asyncState">The async state to pass into the resulting <see cref="RedisServer"/>.</param>
        public IServer GetServer(EndPoint? endpoint, object? asyncState = null)
        {
            _ = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            if (!RawConfig.Proxy.SupportsServerApi())
            {
                throw new NotSupportedException($"The server API is not available via {RawConfig.Proxy}");
            }
            var server = servers[endpoint] as ServerEndPoint ?? throw new ArgumentException("The specified endpoint is not defined", nameof(endpoint));
            return new RedisServer(this, server, asyncState);
        }

        /// <summary>
        /// Obtain configuration APIs for all servers in this multiplexer.
        /// </summary>
        public IServer[] GetServers()
        {
            var snapshot = GetServerSnapshot();
            var result = new IServer[snapshot.Length];
            for (var i = 0; i < snapshot.Length; i++)
            {
                result[i] = new RedisServer(this, snapshot[i], null);
            }
            return result;
        }

        /// <summary>
        /// Get the hash-slot associated with a given key, if applicable.
        /// This can be useful for grouping operations.
        /// </summary>
        /// <param name="key">The <see cref="RedisKey"/> to determine the hash slot for.</param>
        public int GetHashSlot(RedisKey key) => ServerSelectionStrategy.HashSlot(key);

        /// <summary>
        /// The number of operations that have been performed on all connections.
        /// </summary>
        public long OperationCount
        {
            get
            {
                long total = 0;
                var snapshot = GetServerSnapshot();
                for (int i = 0; i < snapshot.Length; i++) total += snapshot[i].OperationCount;
                return total;
            }
        }

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration.
        /// </summary>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public bool Configure(TextWriter? log = null)
        {
            // Note we expect ReconfigureAsync to internally allow [n] duration,
            // so to avoid near misses, here we wait 2*[n].
            var task = ReconfigureAsync(first: false, reconfigureAll: true, Logger.With(log), null, "configure");
            if (!task.Wait(SyncConnectTimeout(false)))
            {
                task.ObserveErrors();
                if (RawConfig.AbortOnConnectFail)
                {
                    throw new TimeoutException();
                }
                else
                {
                    LastException = new TimeoutException("ConnectTimeout");
                }
                return false;
            }
            return task.Result;
        }

        /// <summary>
        /// Reconfigure the current connections based on the existing configuration.
        /// </summary>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public async Task<bool> ConfigureAsync(TextWriter? log = null)
        {
            return await ReconfigureAsync(first: false, reconfigureAll: true, Logger.With(log), null, "configure").ObserveErrors().ForAwait();
        }

        internal int SyncConnectTimeout(bool forConnect)
        {
            int retryCount = forConnect ? RawConfig.ConnectRetry : 1;
            if (retryCount <= 0) retryCount = 1;

            int timeout = RawConfig.ConnectTimeout;
            if (timeout >= int.MaxValue / retryCount) return int.MaxValue;

            timeout *= retryCount;
            if (timeout >= int.MaxValue - 500) return int.MaxValue;
            return timeout + Math.Min(500, timeout);
        }

        /// <summary>
        /// Provides a text overview of the status of all connections.
        /// </summary>
        public string GetStatus()
        {
            using var sw = new StringWriter();
            GetStatus(sw);
            return sw.ToString();
        }

        /// <summary>
        /// Provides a text overview of the status of all connections.
        /// </summary>
        /// <param name="log">The <see cref="TextWriter"/> to log to.</param>
        public void GetStatus(TextWriter log) => GetStatus(new TextWriterLogger(log, null));

        internal void GetStatus(ILogger? log)
        {
            if (log == null) return;

            var tmp = GetServerSnapshot();
            log?.LogInformation("Endpoint Summary:");
            foreach (var server in tmp)
            {
                log?.LogInformation("  " + server.Summary());
                log?.LogInformation("  " + server.GetCounters().ToString());
                log?.LogInformation("  " + server.GetProfile());
            }
            log?.LogInformation($"Sync timeouts: {Interlocked.Read(ref syncTimeouts)}; async timeouts: {Interlocked.Read(ref asyncTimeouts)}; fire and forget: {Interlocked.Read(ref fireAndForgets)}; last heartbeat: {LastHeartbeatSecondsAgo}s ago");
        }

        private void ActivateAllServers(ILogger? log)
        {
            foreach (var server in GetServerSnapshot())
            {
                server.Activate(ConnectionType.Interactive, log);
                if (server.SupportsSubscriptions && !server.KnowOrAssumeResp3())
                {
                    // Intentionally not logging the sub connection
                    server.Activate(ConnectionType.Subscription, null);
                }
            }
        }

        internal bool ReconfigureIfNeeded(EndPoint? blame, bool fromBroadcast, string cause, bool publishReconfigure = false, CommandFlags flags = CommandFlags.None)
        {
            if (fromBroadcast)
            {
                OnConfigurationChangedBroadcast(blame!);
            }
            string? activeCause = Volatile.Read(ref activeConfigCause);
            if (activeCause is null)
            {
                bool reconfigureAll = fromBroadcast || publishReconfigure;
                Trace("Configuration change detected; checking nodes", "Configuration");
                ReconfigureAsync(first: false, reconfigureAll, null, blame, cause, publishReconfigure, flags).ObserveErrors();
                return true;
            }
            else
            {
                Trace("Configuration change skipped; already in progress via " + activeCause, "Configuration");
                return false;
            }
        }

        /// <summary>
        /// Triggers a reconfigure of this multiplexer.
        /// This re-assessment of all server endpoints to get the current topology and adjust, the same as if we had first connected.
        /// </summary>
        public Task<bool> ReconfigureAsync(string reason) =>
            ReconfigureAsync(first: false, reconfigureAll: false, log: null, blame: null, cause: reason);

        internal async Task<bool> ReconfigureAsync(bool first, bool reconfigureAll, ILogger? log, EndPoint? blame, string cause, bool publishReconfigure = false, CommandFlags publishReconfigureFlags = CommandFlags.None)
        {
            if (_isDisposed) throw new ObjectDisposedException(ToString());
            bool showStats = log is not null;

            bool ranThisCall = false;
            try
            {
                // Note that we *always* exchange the reason (first one counts) to prevent duplicate runs
                ranThisCall = Interlocked.CompareExchange(ref activeConfigCause, cause, null) == null;

                if (!ranThisCall)
                {
                    log?.LogInformation($"Reconfiguration was already in progress due to: {activeConfigCause}, attempted to run for: {cause}");
                    return false;
                }
                Trace("Starting reconfiguration...");
                Trace(blame != null, "Blaming: " + Format.ToString(blame));
                Interlocked.Exchange(ref lastReconfigiureTicks, Environment.TickCount);

                log?.LogInformation(RawConfig.ToString(includePassword: false));

                if (first)
                {
                    if (RawConfig.ResolveDns && EndPoints.HasDnsEndPoints())
                    {
                        var dns = EndPoints.ResolveEndPointsAsync(this, log).ObserveErrors();
                        if (!await dns.TimeoutAfter(TimeoutMilliseconds).ForAwait())
                        {
                            throw new TimeoutException("Timeout resolving endpoints");
                        }
                    }
                    foreach (var endpoint in EndPoints)
                    {
                        GetServerEndPoint(endpoint, log, false);
                    }
                    ActivateAllServers(log);
                }
                int attemptsLeft = first ? RawConfig.ConnectRetry : 1;

                bool healthy = false;
                do
                {
                    if (first)
                    {
                        attemptsLeft--;
                    }
                    int standaloneCount = 0, clusterCount = 0, sentinelCount = 0;
                    var endpoints = EndPoints;
                    bool useTieBreakers = RawConfig.TryGetTieBreaker(out var tieBreakerKey);
                    log?.LogInformation($"{endpoints.Count} unique nodes specified ({(useTieBreakers ? "with" : "without")} tiebreaker)");

                    if (endpoints.Count == 0)
                    {
                        throw new InvalidOperationException("No nodes to consider");
                    }
                    List<ServerEndPoint> primaries = new List<ServerEndPoint>(endpoints.Count);

                    ServerEndPoint[]? servers = null;
                    bool encounteredConnectedClusterServer = false;
                    ValueStopwatch? watch = null;

                    int iterCount = first ? 2 : 1;
                    // This is fix for https://github.com/StackExchange/StackExchange.Redis/issues/300
                    // auto discoverability of cluster nodes is made synchronous.
                    // We try to connect to endpoints specified inside the user provided configuration
                    // and when we encounter an endpoint to which we are able to successfully connect,
                    // we get the list of cluster nodes from that endpoint and try to proactively connect
                    // to listed nodes instead of relying on auto configure.
                    for (int iter = 0; iter < iterCount; ++iter)
                    {
                        if (endpoints == null) break;

                        var available = new Task<string>[endpoints.Count];
                        servers = new ServerEndPoint[available.Length];

                        for (int i = 0; i < available.Length; i++)
                        {
                            Trace("Testing: " + Format.ToString(endpoints[i]));

                            var server = GetServerEndPoint(endpoints[i]);
                            //server.ReportNextFailure();
                            servers[i] = server;

                            // This awaits either the endpoint's initial connection, or a tracer if we're already connected
                            // (which is the reconfigure case, except second iteration which is only for newly discovered cluster members).
                            var isFirstIteration = iter == 0;
                            available[i] = server.OnConnectedAsync(log, sendTracerIfConnected: isFirstIteration, autoConfigureIfConnected: reconfigureAll);
                        }

                        watch ??= ValueStopwatch.StartNew();
                        var remaining = RawConfig.ConnectTimeout - watch.Value.ElapsedMilliseconds;
                        log?.LogInformation($"Allowing {available.Length} endpoint(s) {TimeSpan.FromMilliseconds(remaining)} to respond...");
                        Trace("Allowing endpoints " + TimeSpan.FromMilliseconds(remaining) + " to respond...");
                        var allConnected = await WaitAllIgnoreErrorsAsync("available", available, remaining, log).ForAwait();

                        if (!allConnected)
                        {
                            // If we failed, log the details so we can debug why per connection
                            for (var i = 0; i < servers.Length; i++)
                            {
                                var server = servers[i];
                                var task = available[i];
                                var bs = server.GetBridgeStatus(ConnectionType.Interactive);

                                log?.LogInformation($"  Server[{i}] ({Format.ToString(server)}) Status: {task.Status} (inst: {bs.MessagesSinceLastHeartbeat}, qs: {bs.Connection.MessagesSentAwaitingResponse}, in: {bs.Connection.BytesAvailableOnSocket}, qu: {bs.MessagesSinceLastHeartbeat}, aw: {bs.IsWriterActive}, in-pipe: {bs.Connection.BytesInReadPipe}, out-pipe: {bs.Connection.BytesInWritePipe}, bw: {bs.BacklogStatus}, rs: {bs.Connection.ReadStatus}. ws: {bs.Connection.WriteStatus})");
                            }
                        }

                        log?.LogInformation("Endpoint summary:");
                        // Log current state after await
                        foreach (var server in servers)
                        {
                            log?.LogInformation($"  {Format.ToString(server.EndPoint)}: Endpoint is (Interactive: {server.InteractiveConnectionState}, Subscription: {server.SubscriptionConnectionState})");
                        }

                        log?.LogInformation("Task summary:");
                        EndPointCollection? updatedClusterEndpointCollection = null;
                        for (int i = 0; i < available.Length; i++)
                        {
                            var task = available[i];
                            var server = servers[i];
                            Trace(Format.ToString(endpoints[i]) + ": " + task.Status);
                            if (task.IsFaulted)
                            {
                                server.SetUnselectable(UnselectableFlags.DidNotRespond);
                                var aex = task.Exception!;
                                foreach (var ex in aex.InnerExceptions)
                                {
                                    log?.LogError(ex, $"  {Format.ToString(server)}: Faulted: {ex.Message}");
                                    failureMessage = ex.Message;
                                }
                            }
                            else if (task.IsCanceled)
                            {
                                server.SetUnselectable(UnselectableFlags.DidNotRespond);
                                log?.LogInformation($"  {Format.ToString(server)}: Connect task canceled");
                            }
                            else if (task.IsCompleted)
                            {
                                if (task.Result != "Disconnected")
                                {
                                    server.ClearUnselectable(UnselectableFlags.DidNotRespond);
                                    log?.LogInformation($"  {Format.ToString(server)}: Returned with success as {server.ServerType} {(server.IsReplica ? "replica" : "primary")} (Source: {task.Result})");

                                    // Count the server types
                                    switch (server.ServerType)
                                    {
                                        case ServerType.Twemproxy:
                                        case ServerType.Envoyproxy:
                                        case ServerType.Standalone:
                                            standaloneCount++;
                                            break;
                                        case ServerType.Sentinel:
                                            sentinelCount++;
                                            break;
                                        case ServerType.Cluster:
                                            clusterCount++;
                                            break;
                                    }

                                    if (clusterCount > 0 && !encounteredConnectedClusterServer && CommandMap.IsAvailable(RedisCommand.CLUSTER))
                                    {
                                        // We have encountered a connected server with a cluster type for the first time.
                                        // so we will get list of other nodes from this server using "CLUSTER NODES" command
                                        // and try to connect to these other nodes in the next iteration
                                        encounteredConnectedClusterServer = true;
                                        updatedClusterEndpointCollection = await GetEndpointsFromClusterNodes(server, log).ForAwait();
                                    }

                                    // Set the server UnselectableFlags and update primaries list
                                    switch (server.ServerType)
                                    {
                                        case ServerType.Twemproxy:
                                        case ServerType.Envoyproxy:
                                        case ServerType.Sentinel:
                                        case ServerType.Standalone:
                                        case ServerType.Cluster:
                                            server.ClearUnselectable(UnselectableFlags.ServerType);
                                            if (server.IsReplica)
                                            {
                                                server.ClearUnselectable(UnselectableFlags.RedundantPrimary);
                                            }
                                            else
                                            {
                                                primaries.Add(server);
                                            }
                                            break;
                                        default:
                                            server.SetUnselectable(UnselectableFlags.ServerType);
                                            break;
                                    }
                                }
                                else
                                {
                                    server.SetUnselectable(UnselectableFlags.DidNotRespond);
                                    log?.LogInformation($"  {Format.ToString(server)}: Returned, but incorrectly");
                                }
                            }
                            else
                            {
                                server.SetUnselectable(UnselectableFlags.DidNotRespond);
                                log?.LogInformation($"  {Format.ToString(server)}: Did not respond (Task.Status: {task.Status})");
                            }
                        }

                        if (encounteredConnectedClusterServer)
                        {
                            endpoints = updatedClusterEndpointCollection;
                        }
                        else
                        {
                            break; // We do not want to repeat the second iteration
                        }
                    }

                    if (clusterCount == 0)
                    {
                        // Set the serverSelectionStrategy
                        if (RawConfig.Proxy == Proxy.Twemproxy)
                        {
                            ServerSelectionStrategy.ServerType = ServerType.Twemproxy;
                        }
                        else if (RawConfig.Proxy == Proxy.Envoyproxy)
                        {
                            ServerSelectionStrategy.ServerType = ServerType.Envoyproxy;
                        }
                        else if (standaloneCount == 0 && sentinelCount > 0)
                        {
                            ServerSelectionStrategy.ServerType = ServerType.Sentinel;
                        }
                        else if (standaloneCount > 0)
                        {
                            ServerSelectionStrategy.ServerType = ServerType.Standalone;
                        }

                        // If multiple primaries are detected, nominate the preferred one
                        // ...but not if the type of server we're connected to supports and expects multiple primaries
                        // ...for those cases, we want to allow sending to any primary endpoint.
                        if (ServerSelectionStrategy.ServerType.HasSinglePrimary())
                        {
                            var preferred = NominatePreferredPrimary(log, servers!, useTieBreakers, primaries);
                            foreach (var primary in primaries)
                            {
                                if (primary == preferred || primary.IsReplica)
                                {
                                    log?.LogInformation($"{Format.ToString(primary)}: Clearing as RedundantPrimary");
                                    primary.ClearUnselectable(UnselectableFlags.RedundantPrimary);
                                }
                                else
                                {
                                    log?.LogInformation($"{Format.ToString(primary)}: Setting as RedundantPrimary");
                                    primary.SetUnselectable(UnselectableFlags.RedundantPrimary);
                                }
                            }
                        }
                    }
                    else
                    {
                        ServerSelectionStrategy.ServerType = ServerType.Cluster;
                        long coveredSlots = ServerSelectionStrategy.CountCoveredSlots();
                        log?.LogInformation($"Cluster: {coveredSlots} of {ServerSelectionStrategy.TotalSlots} slots covered");
                    }
                    if (!first)
                    {
                        // Calling the sync path here because it's all fire and forget
                        long subscriptionChanges = EnsureSubscriptions(CommandFlags.FireAndForget);
                        if (subscriptionChanges == 0)
                        {
                            log?.LogInformation("No subscription changes necessary");
                        }
                        else
                        {
                            log?.LogInformation($"Subscriptions attempting reconnect: {subscriptionChanges}");
                        }
                    }
                    if (showStats)
                    {
                        GetStatus(log);
                    }

                    string? stormLog = GetStormLog();
                    if (!string.IsNullOrWhiteSpace(stormLog))
                    {
                        log?.LogInformation(stormLog);
                    }
                    healthy = standaloneCount != 0 || clusterCount != 0 || sentinelCount != 0;
                    if (first && !healthy && attemptsLeft > 0)
                    {
                        log?.LogInformation("Resetting failing connections to retry...");
                        ResetAllNonConnected();
                        log?.LogInformation($"  Retrying - attempts left: {attemptsLeft}...");
                    }
                    //WTF("?: " + attempts);
                } while (first && !healthy && attemptsLeft > 0);

                if (first && RawConfig.AbortOnConnectFail && !healthy)
                {
                    return false;
                }
                if (first)
                {
                    log?.LogInformation("Starting heartbeat...");
                    pulse = TimerToken.Create(this);
                }
                if (publishReconfigure)
                {
                    try
                    {
                        log?.LogInformation("Broadcasting reconfigure...");
                        PublishReconfigureImpl(publishReconfigureFlags);
                    }
                    catch
                    { }
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace(ex.Message);
                throw;
            }
            finally
            {
                Trace("Exiting reconfiguration...");
                if (ranThisCall) Interlocked.Exchange(ref activeConfigCause, null);
                if (!first && blame is not null) OnConfigurationChanged(blame);
                Trace("Reconfiguration exited");
            }
        }

        /// <summary>
        /// Gets all endpoints defined on the multiplexer.
        /// </summary>
        /// <param name="configuredOnly">Whether to get only the endpoints specified explicitly in the config.</param>
        public EndPoint[] GetEndPoints(bool configuredOnly = false) =>
            configuredOnly
                ? EndPoints.ToArray()
                : _serverSnapshot.GetEndPoints();

        private async Task<EndPointCollection?> GetEndpointsFromClusterNodes(ServerEndPoint server, ILogger? log)
        {
            var message = Message.Create(-1, CommandFlags.None, RedisCommand.CLUSTER, RedisLiterals.NODES);
            try
            {
                var clusterConfig = await ExecuteAsyncImpl(message, ResultProcessor.ClusterNodes, null, server).ForAwait();
                if (clusterConfig is null)
                {
                    return null;
                }
                var clusterEndpoints = new EndPointCollection(clusterConfig.Nodes.Where(node => node.EndPoint is not null).Select(node => node.EndPoint!).ToList());
                // Loop through nodes in the cluster and update nodes relations to other nodes
                ServerEndPoint? serverEndpoint = null;
                foreach (EndPoint endpoint in clusterEndpoints)
                {
                    serverEndpoint = GetServerEndPoint(endpoint);
                    serverEndpoint?.UpdateNodeRelations(clusterConfig);
                }
                return clusterEndpoints;
            }
            catch (Exception ex)
            {
                log?.LogError(ex, $"Encountered error while updating cluster config: {ex.Message}");
                return null;
            }
        }

        private void ResetAllNonConnected()
        {
            var snapshot = GetServerSnapshot();
            foreach (var server in snapshot)
            {
                server.ResetNonConnected();
            }
        }

        private static ServerEndPoint? NominatePreferredPrimary(ILogger? log, ServerEndPoint[] servers, bool useTieBreakers, List<ServerEndPoint> primaries)
        {
            log?.LogInformation("Election summary:");

            Dictionary<string, int>? uniques = null;
            if (useTieBreakers)
            {
                // Count the votes
                uniques = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < servers.Length; i++)
                {
                    var server = servers[i];
                    string? serverResult = server.TieBreakerResult;

                    if (serverResult.IsNullOrWhiteSpace())
                    {
                        log?.LogInformation($"  Election: {Format.ToString(server)} had no tiebreaker set");
                    }
                    else
                    {
                        log?.LogInformation($"  Election: {Format.ToString(server)} nominates: {serverResult}");
                        if (!uniques.TryGetValue(serverResult, out int count)) count = 0;
                        uniques[serverResult] = count + 1;
                    }
                }
            }

            switch (primaries.Count)
            {
                case 0:
                    log?.LogInformation("  Election: No primaries detected");
                    return null;
                case 1:
                    log?.LogInformation($"  Election: Single primary detected: {Format.ToString(primaries[0].EndPoint)}");
                    return primaries[0];
                default:
                    log?.LogInformation("  Election: Multiple primaries detected...");
                    if (useTieBreakers && uniques != null)
                    {
                        switch (uniques.Count)
                        {
                            case 0:
                                log?.LogInformation("  Election: No nominations by tie-breaker");
                                break;
                            case 1:
                                string unanimous = uniques.Keys.Single();
                                log?.LogInformation($"  Election: Tie-breaker unanimous: {unanimous}");
                                var found = SelectServerByElection(servers, unanimous, log);
                                if (found != null)
                                {
                                    log?.LogInformation($"  Election: Elected: {Format.ToString(found.EndPoint)}");
                                    return found;
                                }
                                break;
                            default:
                                log?.LogInformation("  Election is contested:");
                                ServerEndPoint? highest = null;
                                bool arbitrary = false;
                                foreach (var pair in uniques.OrderByDescending(x => x.Value))
                                {
                                    log?.LogInformation($"    Election: {pair.Key} has {pair.Value} votes");
                                    if (highest == null)
                                    {
                                        highest = SelectServerByElection(servers, pair.Key, log);
                                        if (highest != null)
                                        {
                                            // any more with this vote? if so: arbitrary
                                            arbitrary = uniques.Where(x => x.Value == pair.Value).Skip(1).Any();
                                        }
                                    }
                                }
                                if (highest != null)
                                {
                                    if (arbitrary)
                                    {
                                        log?.LogInformation($"  Election: Choosing primary arbitrarily: {Format.ToString(highest.EndPoint)}");
                                    }
                                    else
                                    {
                                        log?.LogInformation($"  Election: Elected: {Format.ToString(highest.EndPoint)}");
                                    }
                                    return highest;
                                }
                                break;
                        }
                    }
                    break;
            }

            log?.LogInformation($"  Election: Choosing primary arbitrarily: {Format.ToString(primaries[0].EndPoint)}");
            return primaries[0];
        }

        private static ServerEndPoint? SelectServerByElection(ServerEndPoint[] servers, string endpoint, ILogger? log)
        {
            if (servers == null || string.IsNullOrWhiteSpace(endpoint)) return null;
            for (int i = 0; i < servers.Length; i++)
            {
                if (string.Equals(Format.ToString(servers[i].EndPoint), endpoint, StringComparison.OrdinalIgnoreCase))
                    return servers[i];
            }
            log?.LogInformation("...but we couldn't find that");
            var deDottedEndpoint = DeDotifyHost(endpoint);
            for (int i = 0; i < servers.Length; i++)
            {
                if (string.Equals(DeDotifyHost(Format.ToString(servers[i].EndPoint)), deDottedEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    log?.LogInformation($"...but we did find instead: {deDottedEndpoint}");
                    return servers[i];
                }
            }
            return null;
        }

        private static string DeDotifyHost(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input; // GIGO

            if (!char.IsLetter(input[0])) return input; // Need first char to be alpha for this to work

            int periodPosition = input.IndexOf('.');
            if (periodPosition <= 0) return input; // No period or starts with a period? Then nothing useful to split

            int colonPosition = input.IndexOf(':');
            if (colonPosition > 0)
            {
                // Has a port specifier
#if NETCOREAPP
                return string.Concat(input.AsSpan(0, periodPosition), input.AsSpan(colonPosition));
#else
                return input.Substring(0, periodPosition) + input.Substring(colonPosition);
#endif
            }
            else
            {
                return input.Substring(0, periodPosition);
            }
        }

        internal void UpdateClusterRange(ClusterConfiguration configuration)
        {
            if (configuration is null)
            {
                return;
            }
            foreach (var node in configuration.Nodes)
            {
                if (node.IsReplica || node.Slots.Count == 0) continue;
                foreach (var slot in node.Slots)
                {
                    if (GetServerEndPoint(node.EndPoint) is ServerEndPoint server)
                    {
                        ServerSelectionStrategy.UpdateClusterRange(slot.From, slot.To, server);
                    }
                }
            }
        }

        internal ServerEndPoint? SelectServer(Message? message) =>
            message == null ? null : ServerSelectionStrategy.Select(message);

        internal ServerEndPoint? SelectServer(RedisCommand command, CommandFlags flags, in RedisKey key) =>
            ServerSelectionStrategy.Select(command, key, flags);

        internal ServerEndPoint? SelectServer(RedisCommand command, CommandFlags flags, in RedisChannel channel) =>
            ServerSelectionStrategy.Select(command, channel, flags);

        private bool PrepareToPushMessageToBridge<T>(Message message, ResultProcessor<T>? processor, IResultBox<T>? resultBox, [NotNullWhen(true)] ref ServerEndPoint? server)
        {
            message.SetSource(processor, resultBox);

            if (server == null)
            {
                // Infer a server automatically
                server = SelectServer(message);

                // If we didn't find one successfully, and we're allowed, queue for any viable server
                if (server == null && RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    server = ServerSelectionStrategy.Select(message, allowDisconnected: true);
                }
            }
            else // A server was specified - do we trust their choice, though?
            {
                if (message.IsPrimaryOnly() && server.IsReplica)
                {
                    throw ExceptionFactory.PrimaryOnly(RawConfig.IncludeDetailInExceptions, message.Command, message, server);
                }

                switch (server.ServerType)
                {
                    case ServerType.Cluster:
                        if (message.GetHashSlot(ServerSelectionStrategy) == ServerSelectionStrategy.MultipleSlots)
                        {
                            throw ExceptionFactory.MultiSlot(RawConfig.IncludeDetailInExceptions, message);
                        }
                        break;
                }

                // If we're not allowed to queue while disconnected, we'll bomb out below.
                if (!server.IsConnected && !RawConfig.BacklogPolicy.QueueWhileDisconnected)
                {
                    // Well, that's no use!
                    server = null;
                }
            }

            if (server != null)
            {
                var profilingSession = _profilingSessionProvider?.Invoke();
                if (profilingSession != null)
                {
                    message.SetProfileStorage(ProfiledCommand.NewWithContext(profilingSession, server));
                }

                if (message.Db >= 0)
                {
                    int availableDatabases = server.Databases;
                    if (availableDatabases > 0 && message.Db >= availableDatabases)
                    {
                        throw ExceptionFactory.DatabaseOutfRange(RawConfig.IncludeDetailInExceptions, message.Db, message, server);
                    }
                }

                Trace("Queuing on server: " + message);
                return true;
            }
            Trace("No server or server unavailable - aborting: " + message);
            return false;
        }

        private ValueTask<WriteResult> TryPushMessageToBridgeAsync<T>(Message message, ResultProcessor<T>? processor, IResultBox<T>? resultBox, [NotNullWhen(true)] ref ServerEndPoint? server)
            => PrepareToPushMessageToBridge(message, processor, resultBox, ref server) ? server.TryWriteAsync(message) : new ValueTask<WriteResult>(WriteResult.NoConnectionAvailable);

        [Obsolete("prefer async")]
        private WriteResult TryPushMessageToBridgeSync<T>(Message message, ResultProcessor<T>? processor, IResultBox<T>? resultBox, [NotNullWhen(true)] ref ServerEndPoint? server)
            => PrepareToPushMessageToBridge(message, processor, resultBox, ref server) ? server.TryWriteSync(message) : WriteResult.NoConnectionAvailable;

        /// <summary>
        /// Gets the client name for this multiplexer.
        /// </summary>
        public override string ToString() => string.IsNullOrWhiteSpace(ClientName) ? GetType().Name : ClientName;

        internal Exception GetException(WriteResult result, Message message, ServerEndPoint? server, PhysicalBridge? bridge = null) => result switch
        {
            WriteResult.Success => throw new ArgumentOutOfRangeException(nameof(result), "Be sure to check result isn't successful before calling GetException."),
            WriteResult.NoConnectionAvailable => ExceptionFactory.NoConnectionAvailable(this, message, server),
            WriteResult.TimeoutBeforeWrite => ExceptionFactory.Timeout(this, null, message, server, result, bridge),
            _ => ExceptionFactory.ConnectionFailure(RawConfig.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "An unknown error occurred when writing the message", server),
        };

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "Intentional observation")]
        internal static void ThrowFailed<T>(TaskCompletionSource<T>? source, Exception unthrownException)
        {
            try
            {
                throw unthrownException;
            }
            catch (Exception ex)
            {
                if (source is not null)
                {
                    source.TrySetException(ex);
                    GC.KeepAlive(source.Task.Exception);
                    GC.SuppressFinalize(source.Task);
                }
            }
        }

        [return: NotNullIfNotNull(nameof(defaultValue))]
        internal T? ExecuteSyncImpl<T>(Message message, ResultProcessor<T>? processor, ServerEndPoint? server, T? defaultValue = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(ToString());

            if (message is null) // Fire-and forget could involve a no-op, represented by null - for example Increment by 0
            {
                return defaultValue;
            }

            Interlocked.Increment(ref syncOps);

            if (message.IsFireAndForget)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                TryPushMessageToBridgeSync(message, processor, null, ref server);
#pragma warning restore CS0618
                Interlocked.Increment(ref fireAndForgets);
                return defaultValue;
            }
            else
            {
                var source = SimpleResultBox<T>.Get();

                bool timeout = false;
                WriteResult result;
                lock (source)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    result = TryPushMessageToBridgeSync(message, processor, source, ref server);
#pragma warning restore CS0618
                    if (result != WriteResult.Success)
                    {
                        throw GetException(result, message, server);
                    }

                    if (Monitor.Wait(source, TimeoutMilliseconds))
                    {
                        Trace("Timely response to " + message);
                    }
                    else
                    {
                        Trace("Timeout performing " + message);
                        timeout = true;
                    }
                }

                if (timeout) // note we throw *outside* of the main lock to avoid deadlock scenarios (#2376)
                {
                    Interlocked.Increment(ref syncTimeouts);
                    // Very important not to return "source" to the pool here
                    // Also note we return "success" when queueing a messages to the backlog, so we need to manually fake it back here when timing out in the backlog
                    throw ExceptionFactory.Timeout(this, null, message, server, message.IsBacklogged ? WriteResult.TimeoutBeforeWrite : result, server?.GetBridge(message.Command, create: false));
                }
                // Snapshot these so that we can recycle the box
                var val = source.GetResult(out var ex, canRecycle: true); // now that we aren't locking it...
                if (ex != null) throw ex;
                Trace(message + " received " + val);
                return val;
            }
        }

        internal Task<T> ExecuteAsyncImpl<T>(Message? message, ResultProcessor<T>? processor, object? state, ServerEndPoint? server, T defaultValue)
        {
            static async Task<T> ExecuteAsyncImpl_Awaited(ConnectionMultiplexer @this, ValueTask<WriteResult> write, TaskCompletionSource<T>? tcs, Message message, ServerEndPoint? server, T defaultValue)
            {
                var result = await write.ForAwait();
                if (result != WriteResult.Success)
                {
                    var ex = @this.GetException(result, message, server);
                    ThrowFailed(tcs, ex);
                }
                return tcs == null ? defaultValue : await tcs.Task.ForAwait();
            }

            if (_isDisposed) throw new ObjectDisposedException(ToString());

            if (message == null)
            {
                return CompletedTask<T>.FromDefault(defaultValue, state);
            }

            Interlocked.Increment(ref asyncOps);

            TaskCompletionSource<T>? tcs = null;
            IResultBox<T>? source = null;
            if (!message.IsFireAndForget)
            {
                source = TaskResultBox<T>.Create(out tcs, state);
            }
            var write = TryPushMessageToBridgeAsync(message, processor, source, ref server);
            if (!write.IsCompletedSuccessfully)
            {
                return ExecuteAsyncImpl_Awaited(this, write, tcs, message, server, defaultValue);
            }

            if (tcs == null)
            {
                return CompletedTask<T>.FromDefault(defaultValue, null); // F+F explicitly does not get async-state
            }
            else
            {
                var result = write.Result;
                if (result != WriteResult.Success)
                {
                    var ex = GetException(result, message, server);
                    ThrowFailed(tcs, ex);
                }
                return tcs.Task;
            }
        }

        internal Task<T?> ExecuteAsyncImpl<T>(Message? message, ResultProcessor<T>? processor, object? state, ServerEndPoint? server)
        {
            [return: NotNullIfNotNull(nameof(tcs))]
            static async Task<T?> ExecuteAsyncImpl_Awaited(ConnectionMultiplexer @this, ValueTask<WriteResult> write, TaskCompletionSource<T?>? tcs, Message message, ServerEndPoint? server)
            {
                var result = await write.ForAwait();
                if (result != WriteResult.Success)
                {
                    var ex = @this.GetException(result, message, server);
                    ThrowFailed(tcs, ex);
                }
                return tcs == null ? default : await tcs.Task.ForAwait();
            }

            if (_isDisposed) throw new ObjectDisposedException(ToString());

            if (message == null)
            {
                return CompletedTask<T?>.Default(state);
            }

            Interlocked.Increment(ref asyncOps);

            TaskCompletionSource<T?>? tcs = null;
            IResultBox<T?>? source = null;
            if (!message.IsFireAndForget)
            {
                source = TaskResultBox<T?>.Create(out tcs, state);
            }
            var write = TryPushMessageToBridgeAsync(message, processor, source!, ref server);
            if (!write.IsCompletedSuccessfully)
            {
                return ExecuteAsyncImpl_Awaited(this, write, tcs, message, server);
            }

            if (tcs == null)
            {
                return CompletedTask<T?>.Default(null); // F+F explicitly does not get async-state
            }
            else
            {
                var result = write.Result;
                if (result != WriteResult.Success)
                {
                    var ex = GetException(result, message, server);
                    ThrowFailed(tcs, ex);
                }
                return tcs.Task;
            }
        }

        internal void OnAsyncTimeout() => Interlocked.Increment(ref asyncTimeouts);

        /// <summary>
        /// Sends request to all compatible clients to reconfigure or reconnect.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher; returns -1 if the operation is pending).</returns>
        public long PublishReconfigure(CommandFlags flags = CommandFlags.None)
        {
            if (ConfigurationChangedChannel is not null)
            {
                return ReconfigureIfNeeded(null, false, "PublishReconfigure", true, flags)
                    ? -1
                    : PublishReconfigureImpl(flags);
            }
            return 0;
        }

        private long PublishReconfigureImpl(CommandFlags flags) =>
            ConfigurationChangedChannel is byte[] channel
                ? GetSubscriber().Publish(RedisChannel.Literal(channel), RedisLiterals.Wildcard, flags)
                : 0;

        /// <summary>
        /// Sends request to all compatible clients to reconfigure or reconnect.
        /// </summary>
        /// <param name="flags">The command flags to use.</param>
        /// <returns>The number of instances known to have received the message (however, the actual number can be higher).</returns>
        public Task<long> PublishReconfigureAsync(CommandFlags flags = CommandFlags.None) =>
            ConfigurationChangedChannel is byte[] channel
                ? GetSubscriber().PublishAsync(RedisChannel.Literal(channel), RedisLiterals.Wildcard, flags)
                : CompletedTask<long>.Default(null);

        /// <summary>
        /// Release all resources associated with this object.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Close(!_isDisposed);
            sentinelConnection?.Dispose();
            var oldTimer = Interlocked.Exchange(ref sentinelPrimaryReconnectTimer, null);
            oldTimer?.Dispose();
        }

        /// <summary>
        /// Release all resources associated with this object.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            await CloseAsync(!_isDisposed).ForAwait();
            if (sentinelConnection is ConnectionMultiplexer sentinel)
            {
                await sentinel.DisposeAsync().ForAwait();
            }
            var oldTimer = Interlocked.Exchange(ref sentinelPrimaryReconnectTimer, null);
            oldTimer?.Dispose();
        }

        /// <summary>
        /// Close all connections and release all resources associated with this object.
        /// </summary>
        /// <param name="allowCommandsToComplete">Whether to allow all in-queue commands to complete first.</param>
        public void Close(bool allowCommandsToComplete = true)
        {
            if (_isDisposed) return;

            OnClosing(false);
            _isDisposed = true;
            _profilingSessionProvider = null;
            using (var tmp = pulse)
            {
                pulse = null;
            }

            if (allowCommandsToComplete)
            {
                var quits = QuitAllServers();
                WaitAllIgnoreErrors(quits);
            }
            DisposeAndClearServers();
            OnCloseReaderWriter();
            OnClosing(true);
            Interlocked.Increment(ref _connectionCloseCount);
        }

        /// <summary>
        /// Close all connections and release all resources associated with this object.
        /// </summary>
        /// <param name="allowCommandsToComplete">Whether to allow all in-queue commands to complete first.</param>
        public async Task CloseAsync(bool allowCommandsToComplete = true)
        {
            _isDisposed = true;
            using (var tmp = pulse)
            {
                pulse = null;
            }

            if (allowCommandsToComplete)
            {
                var quits = QuitAllServers();
                await WaitAllIgnoreErrorsAsync("quit", quits, RawConfig.AsyncTimeout, null).ForAwait();
            }

            DisposeAndClearServers();
        }

        private void DisposeAndClearServers()
        {
            lock (servers)
            {
                var iter = servers.GetEnumerator();
                while (iter.MoveNext())
                {
                    (iter.Value as ServerEndPoint)?.Dispose();
                }
                servers.Clear();
            }
        }

        private Task[] QuitAllServers()
        {
            var quits = new Task[2 * servers.Count];
            lock (servers)
            {
                var iter = servers.GetEnumerator();
                int index = 0;
                while (iter.MoveNext())
                {
                    var server = (ServerEndPoint)iter.Value!;
                    quits[index++] = server.Close(ConnectionType.Interactive);
                    quits[index++] = server.Close(ConnectionType.Subscription);
                }
            }
            return quits;
        }

        long? IInternalConnectionMultiplexer.GetConnectionId(EndPoint endpoint, ConnectionType type)
            => GetServerEndPoint(endpoint)?.GetBridge(type)?.ConnectionId;
    }
}
