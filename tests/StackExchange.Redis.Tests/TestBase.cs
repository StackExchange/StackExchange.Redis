﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using StackExchange.Redis.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

public abstract class TestBase : IDisposable
{
    private ITestOutputHelper Output { get; }
    protected TextWriterOutputHelper Writer { get; }
    protected static bool RunningInCI { get; } = Environment.GetEnvironmentVariable("APPVEYOR") != null;
    protected virtual string GetConfiguration() => GetDefaultConfiguration();
    internal static string GetDefaultConfiguration() => TestConfig.Current.PrimaryServerAndPort;

    private readonly SharedConnectionFixture? _fixture;

    protected bool SharedFixtureAvailable => _fixture != null && _fixture.IsEnabled;

    protected TestBase(ITestOutputHelper output, SharedConnectionFixture? fixture = null)
    {
        Output = output;
        Output.WriteFrameworkVersion();
        Writer = new TextWriterOutputHelper(output, TestConfig.Current.LogToConsole);
        _fixture = fixture;
        ClearAmbientFailures();
    }

    /// <summary>
    /// Useful to temporarily get extra worker threads for an otherwise synchronous test case which will 'block' the thread,
    /// on a synchronous API like <see cref="Task.Wait"/> or <see cref="Task.Result"/>.
    /// </summary>
    /// <note>
    /// Must NOT be used for test cases which *goes async*, as then the inferred return type will become 'async void',
    /// and we will fail to observe the result of  the async part.
    /// </note>
    /// <remarks>See 'ConnectFailTimeout' class for example usage.</remarks>
    protected static Task RunBlockingSynchronousWithExtraThreadAsync(Action testScenario) => Task.Factory.StartNew(testScenario, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

    protected void LogNoTime(string message) => LogNoTime(Writer, message);
    internal static void LogNoTime(TextWriter output, string? message)
    {
        lock (output)
        {
            output.WriteLine(message);
        }
        if (TestConfig.Current.LogToConsole)
        {
            Console.WriteLine(message);
        }
    }
    protected void Log(string? message) => LogNoTime(Writer, message);
    public static void Log(TextWriter output, string message)
    {
        lock (output)
        {
            output?.WriteLine(Time() + ": " + message);
        }
        if (TestConfig.Current.LogToConsole)
        {
            Console.WriteLine(message);
        }
    }
    protected void Log(string message, params object?[] args)
    {
        lock (Output)
        {
            Output.WriteLine(Time() + ": " + message, args);
        }
        if (TestConfig.Current.LogToConsole)
        {
            Console.WriteLine(message, args);
        }
    }

    protected ProfiledCommandEnumerable Log(ProfilingSession session)
    {
        var profile = session.FinishProfiling();
        foreach (var command in profile)
        {
            Writer.WriteLineNoTime(command.ToString());
        }
        return profile;
    }

    protected static void CollectGarbage()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    public void Dispose()
    {
        _fixture?.Teardown(Writer);
        Teardown();
        Writer.Dispose();
        GC.SuppressFinalize(this);
    }

#if VERBOSE
    protected const int AsyncOpsQty = 100, SyncOpsQty = 10;
#else
    protected const int AsyncOpsQty = 2000, SyncOpsQty = 2000;
#endif

    static TestBase()
    {
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Console.WriteLine("Unobserved: " + args.Exception);
            args.SetObserved();
            lock (sharedFailCount)
            {
                if (sharedFailCount != null)
                {
                    sharedFailCount.Value++;
                }
            }
            lock (backgroundExceptions)
            {
                backgroundExceptions.Add(args.Exception.ToString());
            }
        };
        Console.WriteLine("Setup information:");
        Console.WriteLine("  GC IsServer: " + GCSettings.IsServerGC);
        Console.WriteLine("  GC LOH Mode: " + GCSettings.LargeObjectHeapCompactionMode);
        Console.WriteLine("  GC Latency Mode: " + GCSettings.LatencyMode);
    }

    internal static string Time() => DateTime.UtcNow.ToString("HH:mm:ss.ffff");
    protected void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        Interlocked.Increment(ref privateFailCount);
        lock (privateExceptions)
        {
            privateExceptions.Add($"{Time()}: Connection failed ({e.FailureType}): {EndPointCollection.ToString(e.EndPoint)}/{e.ConnectionType}: {e.Exception}");
        }
        Log($"Connection Failed ({e.ConnectionType},{e.FailureType}): {e.Exception}");
    }

    protected void OnInternalError(object? sender, InternalErrorEventArgs e)
    {
        Interlocked.Increment(ref privateFailCount);
        lock (privateExceptions)
        {
            privateExceptions.Add(Time() + ": Internal error: " + e.Origin + ", " + EndPointCollection.ToString(e.EndPoint) + "/" + e.ConnectionType);
        }
    }

    private int privateFailCount;
    private static readonly AsyncLocal<int> sharedFailCount = new AsyncLocal<int>();
    private volatile int expectedFailCount;

    private readonly List<string> privateExceptions = new List<string>();
    private static readonly List<string> backgroundExceptions = new List<string>();

    public void ClearAmbientFailures()
    {
        Interlocked.Exchange(ref privateFailCount, 0);
        lock (sharedFailCount)
        {
            sharedFailCount.Value = 0;
        }
        expectedFailCount = 0;
        lock (privateExceptions)
        {
            privateExceptions.Clear();
        }
        lock (backgroundExceptions)
        {
            backgroundExceptions.Clear();
        }
    }

    public void SetExpectedAmbientFailureCount(int count)
    {
        expectedFailCount = count;
    }

    public void Teardown()
    {
        int sharedFails;
        lock (sharedFailCount)
        {
            sharedFails = sharedFailCount.Value;
            sharedFailCount.Value = 0;
        }
        if (expectedFailCount >= 0 && (sharedFails + privateFailCount) != expectedFailCount)
        {
            lock (privateExceptions)
            {
                foreach (var item in privateExceptions.Take(5))
                {
                    LogNoTime(item);
                }
            }
            lock (backgroundExceptions)
            {
                foreach (var item in backgroundExceptions.Take(5))
                {
                    LogNoTime(item);
                }
            }
            Skip.Inconclusive($"There were {privateFailCount} private and {sharedFailCount.Value} ambient exceptions; expected {expectedFailCount}.");
        }
        var pool = SocketManager.Shared?.SchedulerPool;
        Log($"Service Counts: (Scheduler) Queue: {pool?.TotalServicedByQueue.ToString()}, Pool: {pool?.TotalServicedByPool.ToString()}, Workers: {pool?.WorkerCount.ToString()}, Available: {pool?.AvailableCount.ToString()}");
    }

    protected static IServer GetServer(IConnectionMultiplexer muxer)
    {
        IServer? result = null;
        foreach (var server in muxer.GetServers())
        {
            if (server.IsReplica || !server.IsConnected) continue;
            if (result != null) throw new InvalidOperationException("Requires exactly one primary endpoint (found " + server.EndPoint + " and " + result.EndPoint + ")");
            result = server;
        }
        if (result == null) throw new InvalidOperationException("Requires exactly one primary endpoint (found none)");
        return result;
    }

    protected static IServer GetAnyPrimary(IConnectionMultiplexer muxer)
    {
        foreach (var endpoint in muxer.GetEndPoints())
        {
            var server = muxer.GetServer(endpoint);
            if (!server.IsReplica) return server;
        }
        throw new InvalidOperationException("Requires a primary endpoint (found none)");
    }

    internal virtual IInternalConnectionMultiplexer Create(
        string? clientName = null,
        int? syncTimeout = null,
        bool? allowAdmin = null,
        int? keepAlive = null,
        int? connectTimeout = null,
        string? password = null,
        string? tieBreaker = null,
        TextWriter? log = null,
        bool fail = true,
        string[]? disabledCommands = null,
        string[]? enabledCommands = null,
        bool checkConnect = true,
        string? failMessage = null,
        string? channelPrefix = null,
        Proxy? proxy = null,
        string? configuration = null,
        bool logTransactionData = true,
        bool shared = true,
        int? defaultDatabase = null,
        BacklogPolicy? backlogPolicy = null,
        Version? require = null,
        [CallerMemberName] string? caller = null)
    {
        if (Output == null)
        {
            Assert.True(false, "Failure: Be sure to call the TestBase constructor like this: BasicOpsTests(ITestOutputHelper output) : base(output) { }");
        }

        // Share a connection if instructed to and we can - many specifics mean no sharing
        if (shared
            && _fixture != null && _fixture.IsEnabled
            && enabledCommands == null
            && disabledCommands == null
            && fail
            && channelPrefix == null
            && proxy == null
            && configuration == null
            && password == null
            && tieBreaker == null
            && defaultDatabase == null
            && (allowAdmin == null || allowAdmin == true)
            && expectedFailCount == 0
            && backlogPolicy == null)
        {
            configuration = GetConfiguration();
            // Only return if we match
            if (configuration == _fixture.Configuration)
            {
                ThrowIfBelowMinVersion(_fixture.Connection, require);
                return _fixture.Connection;
            }
        }

        var conn = CreateDefault(
            Writer,
            configuration ?? GetConfiguration(),
            clientName, syncTimeout, allowAdmin, keepAlive,
            connectTimeout, password, tieBreaker, log,
            fail, disabledCommands, enabledCommands,
            checkConnect, failMessage,
            channelPrefix, proxy,
            logTransactionData, defaultDatabase,
            backlogPolicy,
            caller);

        ThrowIfBelowMinVersion(conn, require);

        conn.InternalError += OnInternalError;
        conn.ConnectionFailed += OnConnectionFailed;
        conn.ConnectionRestored += (s, e) => Log($"Connection Restored ({e.ConnectionType},{e.FailureType}): {e.Exception}");
        return conn;
    }

    protected void ThrowIfBelowMinVersion(IConnectionMultiplexer conn, Version? requiredVersion)
    {
        if (requiredVersion is null)
        {
            return;
        }

        var serverVersion = conn.GetServer(conn.GetEndPoints()[0]).Version;
        if (requiredVersion > serverVersion)
        {
            throw new SkipTestException($"Requires server version {requiredVersion}, but server is only {serverVersion}.")
            {
                MissingFeatures = $"Server version >= {requiredVersion}."
            };
        }
    }

    public static ConnectionMultiplexer CreateDefault(
        TextWriter? output,
        string configuration,
        string? clientName = null,
        int? syncTimeout = null,
        bool? allowAdmin = null,
        int? keepAlive = null,
        int? connectTimeout = null,
        string? password = null,
        string? tieBreaker = null,
        TextWriter? log = null,
        bool fail = true,
        string[]? disabledCommands = null,
        string[]? enabledCommands = null,
        bool checkConnect = true,
        string? failMessage = null,
        string? channelPrefix = null,
        Proxy? proxy = null,
        bool logTransactionData = true,
        int? defaultDatabase = null,
        BacklogPolicy? backlogPolicy = null,
        [CallerMemberName] string? caller = null)
    {
        StringWriter? localLog = null;
        if (log == null)
        {
            log = localLog = new StringWriter();
        }
        try
        {
            var config = ConfigurationOptions.Parse(configuration);
            if (disabledCommands != null && disabledCommands.Length != 0)
            {
                config.CommandMap = CommandMap.Create(new HashSet<string>(disabledCommands), false);
            }
            else if (enabledCommands != null && enabledCommands.Length != 0)
            {
                config.CommandMap = CommandMap.Create(new HashSet<string>(enabledCommands), true);
            }

            if (Debugger.IsAttached)
            {
                syncTimeout = int.MaxValue;
            }

            if (channelPrefix != null) config.ChannelPrefix = RedisChannel.Literal(channelPrefix);
            if (tieBreaker != null) config.TieBreaker = tieBreaker;
            if (password != null) config.Password = string.IsNullOrEmpty(password) ? null : password;
            if (clientName != null) config.ClientName = clientName;
            else if (caller != null) config.ClientName = caller;
            if (syncTimeout != null) config.SyncTimeout = syncTimeout.Value;
            if (allowAdmin != null) config.AllowAdmin = allowAdmin.Value;
            if (keepAlive != null) config.KeepAlive = keepAlive.Value;
            if (connectTimeout != null) config.ConnectTimeout = connectTimeout.Value;
            if (proxy != null) config.Proxy = proxy.Value;
            if (defaultDatabase != null) config.DefaultDatabase = defaultDatabase.Value;
            if (backlogPolicy != null) config.BacklogPolicy = backlogPolicy;
            var watch = Stopwatch.StartNew();
            var task = ConnectionMultiplexer.ConnectAsync(config, log);
            if (!task.Wait(config.ConnectTimeout >= (int.MaxValue / 2) ? int.MaxValue : config.ConnectTimeout * 2))
            {
                task.ContinueWith(x =>
                {
                    try
                    {
                        GC.KeepAlive(x.Exception);
                    }
                    catch { /* No boom */ }
                }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Connect timeout");
            }
            watch.Stop();
            if (output != null)
            {
                Log(output, "Connect took: " + watch.ElapsedMilliseconds + "ms");
            }
            var conn = task.Result;
            if (checkConnect && !conn.IsConnected)
            {
                // If fail is true, we throw.
                Assert.False(fail, failMessage + "Server is not available");
                Skip.Inconclusive(failMessage + "Server is not available");
            }
            if (output != null)
            {
                conn.MessageFaulted += (msg, ex, origin) =>
                {
                    output?.WriteLine($"Faulted from '{origin}': '{msg}' - '{(ex == null ? "(null)" : ex.Message)}'");
                    if (ex != null && ex.Data.Contains("got"))
                    {
                        output?.WriteLine($"Got: '{ex.Data["got"]}'");
                    }
                };
                conn.Connecting += (e, t) => output?.WriteLine($"Connecting to {Format.ToString(e)} as {t}");
                if (logTransactionData)
                {
                    conn.TransactionLog += msg => output?.WriteLine("tran: " + msg);
                }
                conn.InfoMessage += msg => output?.WriteLine(msg);
                conn.Resurrecting += (e, t) => output?.WriteLine($"Resurrecting {Format.ToString(e)} as {t}");
                conn.Closing += complete => output?.WriteLine(complete ? "Closed" : "Closing...");
            }
            return conn;
        }
        catch
        {
            if (localLog != null) output?.WriteLine(localLog.ToString());
            throw;
        }
    }

    public static string Me([CallerFilePath] string? filePath = null, [CallerMemberName] string? caller = null) =>
        Environment.Version.ToString() + Path.GetFileNameWithoutExtension(filePath) + "-" + caller;

    protected static TimeSpan RunConcurrent(Action work, int threads, int timeout = 10000, [CallerMemberName] string? caller = null)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        if (threads < 1) throw new ArgumentOutOfRangeException(nameof(threads));
        if (string.IsNullOrWhiteSpace(caller)) caller = Me();
        Stopwatch? watch = null;
        ManualResetEvent allDone = new ManualResetEvent(false);
        object token = new object();
        int active = 0;
        void callback()
        {
            lock (token)
            {
                int nowActive = Interlocked.Increment(ref active);
                if (nowActive == threads)
                {
                    watch = Stopwatch.StartNew();
                    Monitor.PulseAll(token);
                }
                else
                {
                    Monitor.Wait(token);
                }
            }
            work();
            if (Interlocked.Decrement(ref active) == 0)
            {
                watch?.Stop();
                allDone.Set();
            }
        }

        var threadArr = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            var thd = new Thread(callback)
            {
                Name = caller
            };
            threadArr[i] = thd;
            thd.Start();
        }
        if (!allDone.WaitOne(timeout))
        {
            for (int i = 0; i < threads; i++)
            {
                var thd = threadArr[i];
#if !NET6_0_OR_GREATER
                if (thd.IsAlive) thd.Abort();
#endif
            }
            throw new TimeoutException();
        }

        return watch?.Elapsed ?? TimeSpan.Zero;
    }

    private static readonly TimeSpan DefaultWaitPerLoop = TimeSpan.FromMilliseconds(50);
    protected static async Task UntilConditionAsync(TimeSpan maxWaitTime, Func<bool> predicate, TimeSpan? waitPerLoop = null)
    {
        TimeSpan spent = TimeSpan.Zero;
        while (spent < maxWaitTime && !predicate())
        {
            var wait = waitPerLoop ?? DefaultWaitPerLoop;
            await Task.Delay(wait).ForAwait();
            spent += wait;
        }
    }
}
