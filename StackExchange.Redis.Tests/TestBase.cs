using StackExchange.Redis.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public abstract class TestBase : IDisposable
    {
        private ITestOutputHelper Output { get; }
        protected TextWriterOutputHelper Writer { get; }
        protected static bool RunningInCI { get; } = Environment.GetEnvironmentVariable("APPVEYOR") != null;
        protected virtual string GetConfiguration() => TestConfig.Current.MasterServerAndPort;

        protected TestBase(ITestOutputHelper output)
        {
            Output = output;
            Output.WriteFrameworkVersion();
            Writer = new TextWriterOutputHelper(output, TestConfig.Current.LogToConsole);
            ClearAmbientFailures();
        }

        protected void LogNoTime(string message)
        {
            Output.WriteLine(message);
            if (TestConfig.Current.LogToConsole)
            {
                Console.WriteLine(message);
            }
        }
        protected void Log(string message)
        {
            Output.WriteLine(Time() + ": " + message);
            if (TestConfig.Current.LogToConsole)
            {
                Console.WriteLine(message);
            }
        }
        protected void Log(string message, params object[] args)
        {
            Output.WriteLine(Time() + ": " + message, args);
            if (TestConfig.Current.LogToConsole)
            {
                Console.WriteLine(message, args);
            }
        }

        protected void CollectGarbage()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            Teardown();
        }

#if VERBOSE
        protected const int AsyncOpsQty = 100, SyncOpsQty = 10;
#else
        protected const int AsyncOpsQty = 10000, SyncOpsQty = 10000;
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
        internal static string Time() => DateTime.UtcNow.ToString("HH:mm:ss.fff");
        protected void OnConnectionFailed(object sender, ConnectionFailedEventArgs e)
        {
            Interlocked.Increment(ref privateFailCount);
            lock (privateExceptions)
            {
                privateExceptions.Add($"{Time()}: Connection failed ({e.FailureType}): {EndPointCollection.ToString(e.EndPoint)}/{e.ConnectionType}: {e.Exception}");
            }
        }

        protected void OnInternalError(object sender, InternalErrorEventArgs e)
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
                Assert.True(false, $"There were {privateFailCount} private and {sharedFailCount.Value} ambient exceptions; expected {expectedFailCount}.");
            }
            Log($"Service Counts: (Scheduler) Queue: {SocketManager.Shared?.SchedulerPool?.TotalServicedByQueue.ToString()}, Pool: {SocketManager.Shared?.SchedulerPool?.TotalServicedByPool.ToString()}, (Completion) Queue: {SocketManager.Shared?.CompletionPool?.TotalServicedByQueue.ToString()}, Pool: {SocketManager.Shared?.CompletionPool?.TotalServicedByPool.ToString()}");
        }

        protected IServer GetServer(ConnectionMultiplexer muxer)
        {
            EndPoint[] endpoints = muxer.GetEndPoints();
            IServer result = null;
            foreach (var endpoint in endpoints)
            {
                var server = muxer.GetServer(endpoint);
                if (server.IsSlave || !server.IsConnected) continue;
                if (result != null) throw new InvalidOperationException("Requires exactly one master endpoint (found " + server.EndPoint + " and " + result.EndPoint + ")");
                result = server;
            }
            if (result == null) throw new InvalidOperationException("Requires exactly one master endpoint (found none)");
            return result;
        }

        protected IServer GetAnyMaster(ConnectionMultiplexer muxer)
        {
            foreach (var endpoint in muxer.GetEndPoints())
            {
                var server = muxer.GetServer(endpoint);
                if (!server.IsSlave) return server;
            }
            throw new InvalidOperationException("Requires a master endpoint (found none)");
        }

        private static readonly HashSet<GCHandle> ActiveMultiplexers = new HashSet<GCHandle>();

        protected virtual ConnectionMultiplexer Create(
            string clientName = null, int? syncTimeout = null, bool? allowAdmin = null, int? keepAlive = null,
            int? connectTimeout = null, string password = null, string tieBreaker = null, TextWriter log = null,
            bool fail = true, string[] disabledCommands = null, string[] enabledCommands = null,
            bool checkConnect = true, string failMessage = null,
            string channelPrefix = null, Proxy? proxy = null,
            string configuration = null, bool logTransactionData = true,
            [CallerMemberName] string caller = null)
        {
            StringWriter localLog = null;
            GCHandle handle;
            if(log == null)
            {
                log = localLog = new StringWriter();
            }
            try
            {
                configuration = configuration ?? GetConfiguration();
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

                if (channelPrefix != null) config.ChannelPrefix = channelPrefix;
                if (tieBreaker != null) config.TieBreaker = tieBreaker;
                if (password != null) config.Password = string.IsNullOrEmpty(password) ? null : password;
                if (clientName != null) config.ClientName = clientName;
                else if (caller != null) config.ClientName = caller;
                if (syncTimeout != null) config.SyncTimeout = syncTimeout.Value;
                if (allowAdmin != null) config.AllowAdmin = allowAdmin.Value;
                if (keepAlive != null) config.KeepAlive = keepAlive.Value;
                if (connectTimeout != null) config.ConnectTimeout = connectTimeout.Value;
                if (proxy != null) config.Proxy = proxy.Value;
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
                        catch
                        { }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("Connect timeout");
                }
                watch.Stop();
                if (Output == null)
                {
                    Assert.True(false, "Failure: Be sure to call the TestBase constuctor like this: BasicOpsTests(ITestOutputHelper output) : base(output) { }");
                }
                Log("Connect took: " + watch.ElapsedMilliseconds + "ms");
                var muxer = task.Result;
                if (checkConnect && (muxer == null || !muxer.IsConnected))
                {
                    // If fail is true, we throw.
                    Assert.False(fail, failMessage + "Server is not available");
                    Skip.Inconclusive(failMessage + "Server is not available");
                }

                handle = GCHandle.Alloc(muxer);
                lock (ActiveMultiplexers)
                {
                    ActiveMultiplexers.Add(handle);
                }

                muxer.InternalError += OnInternalError;
                muxer.ConnectionFailed += OnConnectionFailed;
                muxer.MessageFaulted += (msg, ex, origin) =>
                {
                    Writer?.WriteLine($"Faulted from '{origin}': '{msg}' - '{(ex == null ? "(null)" : ex.Message)}'");
                    if (ex != null && ex.Data.Contains("got"))
                    {
                        Writer?.WriteLine($"Got: '{ex.Data["got"]}'");
                    }
                };
                muxer.Connecting += (e, t) => Writer.WriteLine($"Connecting to {Format.ToString(e)} as {t}");
                if (logTransactionData)
                {
                    muxer.TransactionLog += msg => Writer.WriteLine("tran: " + msg);
                }
                muxer.InfoMessage += msg => Writer.WriteLine(msg);
                muxer.Resurrecting += (e, t) => Writer.WriteLine($"Resurrecting {Format.ToString(e)} as {t}");
                muxer.Closing += complete =>
                {

                    int count;
                    lock(ActiveMultiplexers)
                    {
                        count = ActiveMultiplexers.Count;
                        if (complete)
                        {
                            ActiveMultiplexers.Remove(handle);
                        }                        
                    }
                    Writer.WriteLine((complete ? "Closed (" : "Closing... (") + count.ToString() + " remaining)");

                };
                return muxer;
            }
            catch
            {
                if (localLog != null) Output?.WriteLine(localLog.ToString());
                throw;
            }
        }

        public static string Me([CallerFilePath] string filePath = null, [CallerMemberName] string caller = null) =>
#if NET462
            "net462-" + Path.GetFileNameWithoutExtension(filePath) + "-" + caller;
#elif NETCOREAPP2_0
            "netcoreapp2.0-" + Path.GetFileNameWithoutExtension(filePath) + "-" + caller;
#else
            "unknown-" + Path.GetFileNameWithoutExtension(filePath) + "-" + caller;
#endif

        protected static TimeSpan RunConcurrent(Action work, int threads, int timeout = 10000, [CallerMemberName] string caller = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (threads < 1) throw new ArgumentOutOfRangeException(nameof(threads));
            if (string.IsNullOrWhiteSpace(caller)) caller = Me();
            Stopwatch watch = null;
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
                    watch.Stop();
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
                    if (thd.IsAlive) thd.Abort();
                }
                throw new TimeoutException();
            }

            return watch.Elapsed;
        }

        protected async Task UntilCondition(int maxMilliseconds, Func<bool> predicate, int perLoop = 100)
        {
            var spent = 0;
            while (spent < maxMilliseconds && !predicate())
            {
                await Task.Delay(perLoop).ForAwait();
                spent += perLoop;
            }
        }
    }
}
